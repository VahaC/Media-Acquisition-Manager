using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Integrations;
using MediaAcquisitionManager.Infrastructure.Integrations.Jellyfin;
using MediaAcquisitionManager.Infrastructure.Integrations.QBittorrent;
using MediaAcquisitionManager.Infrastructure.Integrations.Tmdb;
using MediaAcquisitionManager.Infrastructure.Integrations.Torznab;
using MediaAcquisitionManager.Infrastructure.Integrations.Transmission;
using Microsoft.Extensions.DependencyInjection;

namespace MediaAcquisitionManager.Infrastructure.Integrations;

/// <summary>
/// The integration adapters behind <see cref="DependencyInjection.AddMediaAcquisitionManagerInfrastructure"/>:
/// one named HTTP client and one adapter per type, and the registry that picks between them (MAM-7,
/// ADR 0005).
/// </summary>
internal static class IntegrationsInfrastructureExtensions
{
    /// <summary>Names the app and nothing about the installation.</summary>
    public const string UserAgent = "MediaAcquisitionManager/1.0 (+integrations)";

    /// <summary>
    /// 4 MiB: far above anything a connection test reads (Jellyfin's <c>/System/Info</c> is about
    /// 1 KB, Toloka's Torznab caps about 25 KB), while an address that turns out to stream a media
    /// file is never buffered into memory. MAM-8 — it also holds a page of a library sync with room to
    /// spare: the heaviest sample movie, streams included, is about 12 KB as Jellyfin sends it, so a
    /// page of 50 is well under 1 MB (and <c>JellyfinAdapter.MaximumPageSize</c>'s 200 under 3 MB).
    /// MAM-10 — a release search asks for at most 100 releases (<c>ReleaseSearchPlanning.MaximumResults</c>),
    /// about 150 KB of RSS (the sample's 17 are 25 KB), and the .torrent of a movie, which the payload
    /// fetch buffers whole, is well under 1 MiB: 4 MiB holds either with room to spare. MAM-14 — a torrent's
    /// files take about 300 bytes each as qBittorrent lists them (the sample pack's three are under 1 KB), so a
    /// season pack of hundreds is a few dozen KB, and only a torrent of over 12,000 files would not fit.
    /// MAM-33 — TMDb's heaviest answers are a film's details with its credits and recommendations (about
    /// 170 KB for Guardians of the Galaxy, VendorSamples) and a person's combined credits (about 160 KB for
    /// Robert Zemeckis); a poster at w342 is tens of kilobytes. All far under 4 MiB. MAM-34 — one Jellyfin item read
    /// whole, streams and people included, is about 6 to 15 KB (VendorSamples' <c>item.json</c> and
    /// <c>episode-movie-after-identify.json</c>), and a search answers a few hundred bytes a result. MAM-35 — a library
    /// poster is asked for as a JPEG at most 342 pixels wide, about 35 KB (recorded live), and a page of the sync carries one
    /// image tag more per item. MAM-24 — qBittorrent's whole list with every torrent's files is about 2 KB a torrent plus
    /// about 300 bytes a file: the reference installation's 352 torrents (ADR 0003) with a few thousand files come to about
    /// 2 MB, so only one several times bigger outgrows it — a failed read in its own words, which the Download dialog reports
    /// as copies it couldn't check (ADR 0031), never a list cut short.
    /// </summary>
    public const long MaximumResponseBytes = 4 * 1024 * 1024;

    /// <summary>
    /// MAM-7 — a connection test is answered from the service's memory on a LAN in milliseconds; 15 s
    /// covers a NAS waking its disks or a service still starting, without leaving the owner staring at
    /// "Testing…". MAM-8 — the timeout is per request, and a library sync sends one request per page of
    /// 50 movies, so it bounds reading one page, never the whole library. MAM-10 — a Toloka search
    /// through Prowlarr took 2.7 s (ADR 0004) and a feed answers its caps from memory, so 15 s leaves a
    /// slow tracker five times that; a payload fetch, where the gateway fetches the .torrent from the
    /// tracker, is one request of the same kind. MAM-11 — listing a download client's destinations is
    /// three reads (the version, the categories, the default save path), each answered from qBittorrent's
    /// memory on a LAN like a connection test, plus a sign-in when no session is kept, or a sign-in and one
    /// read sent again when qBittorrent has forgotten the kept one: at most five requests of a few
    /// milliseconds, each bounded on its own. MAM-13 — a submission is the same sign-in and version read
    /// and then one <c>torrents/add</c>, and a job lookup the version read and one <c>torrents/info</c>; a
    /// submission whose answer doesn't come in time is reported as timed out and never sent again, because
    /// qBittorrent may have added the release all the same — the caller looks for its info hash instead.
    /// MAM-14 — following a job is the same version read and one <c>torrents/info</c> (by hashes, or by
    /// category for a job they no longer find), plus one <c>torrents/files</c> of a torrent, each answered
    /// from qBittorrent's memory. MAM-33 — TMDb answers over the internet rather than the LAN, in well under
    /// a second; each request is bounded on its own, a 429's wait (at most 10 s) included in none of them.
    /// MAM-34 — an identification is reads (the users, the item, the libraries), one search and the item read again, each
    /// bounded on its own and answered from Jellyfin's memory or one provider's answer in well under a second; the
    /// one call that is not is Apply, which has a client of its own (<see cref="IdentifyTimeout"/>). MAM-24 — the whole list
    /// with every torrent's files is the same version read and one <c>torrents/info</c>, answered from qBittorrent's memory:
    /// a couple of megabytes on a LAN, well within 15 s. MAM-30 — a Transmission call is one <c>session-get</c> and then one
    /// <c>torrent-get</c> or <c>torrent-add</c>, or one <c>free-space</c> per folder for the destinations, each answered from
    /// Transmission's memory, plus the same request sent once more after a 409 that hands out its session id; a
    /// <c>torrent-add</c> whose answer doesn't come in time is never sent again, as qBittorrent's isn't.
    /// </summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// MAM-34 — the timeout of the <c>jellyfin-identify</c> client, which carries Jellyfin's Apply and nothing else. Apply
    /// is synchronous and fetches the item's metadata and images again, and for a series every episode's too: recorded live
    /// on Jellyfin 12.1.0 a film took 1.8 s, an episode through the Open Movie Database 21.3 s and the Penguins of
    /// Madagascar (149 episodes) 159.3 s, about a second per episode. 20 minutes covers a series of about 1,100 episodes. A
    /// timeout of this client is never "failed": Jellyfin carries on after a client gives up (its Apply runs with no
    /// cancellation), so the item is read again to see what became of it, and Apply is never sent twice.
    /// </summary>
    public static readonly TimeSpan IdentifyTimeout = TimeSpan.FromMinutes(20);

    /// <summary>
    /// A LAN service accepts a TCP connection in milliseconds; without this, an address where nothing
    /// answers holds the dialog for the operating system's SYN retries.
    /// </summary>
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    public static IServiceCollection AddIntegrationsInfrastructure(this IServiceCollection services)
    {
        AddIntegrationClient(services, JellyfinAdapter.HttpClientName);
        // MAM-34 — Jellyfin's Apply alone, with its own timeout and the same handler settings: a redirect never carries the
        // key, and nothing logs a URI.
        AddIntegrationClient(services, JellyfinAdapter.IdentifyHttpClientName, IdentifyTimeout);
        AddIntegrationClient(services, TorznabAdapter.HttpClientName);
        AddIntegrationClient(services, QBittorrentAdapter.HttpClientName);
        AddIntegrationClient(services, TransmissionAdapter.HttpClientName);
        // MAM-33 — the posters from TMDb's image host go through this client too, so the test host's handler
        // answers them and nothing reaches the real host.
        AddIntegrationClient(services, TmdbAdapter.HttpClientName);
        // One instance per type serves every request. Jellyfin's and Torznab's are stateless apart from the
        // client factory; MAM-11 — qBittorrent's keeps its sessions and the sign-ins qBittorrent refused
        // (QBittorrentSessions), so it must stay a singleton: a scoped or transient one would sign in on
        // every call and send refused credentials again until qBittorrent banned the app's address.
        services.AddSingleton<IIntegrationAdapter, JellyfinAdapter>();
        services.AddSingleton<IIntegrationAdapter, TorznabAdapter>();
        services.AddSingleton<IIntegrationAdapter, QBittorrentAdapter>();
        // MAM-30 — Transmission's keeps its session ids and the 401s and 403s Transmission answered (TransmissionSessions),
        // so it must stay a singleton too: one per scope would start every call with a 409, and would send credentials
        // Transmission refused again, each refusal counting towards locking its RPC when its anti-brute-force setting is on.
        services.AddSingleton<IIntegrationAdapter, TransmissionAdapter>();
        // MAM-33 — stateless apart from its pacing gate (TmdbRequestPacer), which must be one for every
        // caller: TMDb limits requests per address, and an instance per scope would pace each request alone.
        services.AddSingleton<IIntegrationAdapter, TmdbAdapter>();
        // Strategy over every IIntegrationAdapter registered, the test host's fake included.
        services.AddSingleton<IIntegrationAdapterRegistry, IntegrationAdapterRegistry>();
        return services;
    }

    // No retry or resilience handler (house rule 8): a test is one attempt, a failed qBittorrent sign-in
    // repeated counts towards its ban, and — MAM-13 — a torrents/add whose answer was lost may have added the
    // release already. MAM-33 — the TMDb adapter sends a GET again itself, only after a 429 and after the
    // wait TMDb asked for: a read, and an answer that says it was not served. MAM-34 — a Jellyfin Apply whose answer
    // was lost may have replaced the item's title, overview and images already, and one sent twice is two identifies:
    // each deletes the item's images first, and a series' repeats for every episode.
    private static void AddIntegrationClient(IServiceCollection services, string name, TimeSpan? timeout = null) =>
        services.AddHttpClient(name, client =>
            {
                client.Timeout = timeout ?? RequestTimeout;
                client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
                client.MaxResponseContentBufferSize = MaximumResponseBytes;
                // No BaseAddress (a house deviation): one client serves every connection of its type,
                // each with its own owner-entered address, so the adapters build absolute URIs per call.
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // Every request here carries a credential; a redirect would carry it wherever the
                // Location points. A 3xx is reported to the owner instead (ADR 0007). MAM-10 — the
                // payload fetch reads a 3xx too but follows none: a magnet: Location is the payload,
                // anything else is refused.
                AllowAutoRedirect = false,
                // The factory pools handlers across connections: a cookie container would share one
                // qBittorrent's session with every other. The adapter sends the one cookie it means to.
                UseCookies = false,
                ConnectTimeout = ConnectTimeout,
            })
            // The factory's logging handlers write each request URI at Information, and Torznab's API
            // key travels in the query (ADR 0004). .NET masks the query as * unless
            // DOTNET_SYSTEM_NET_HTTP_DISABLEURIREDACTION switches that off, and one switch an operator
            // flips to debug is not a guard (ADR 0007). The API logs the outcomes itself.
            .RemoveAllLoggers();
}
