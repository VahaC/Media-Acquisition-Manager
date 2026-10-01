using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Integrations;
using MediaAcquisitionManager.Infrastructure.Integrations.Jellyfin;
using MediaAcquisitionManager.Infrastructure.Integrations.QBittorrent;
using MediaAcquisitionManager.Infrastructure.Integrations.Torznab;
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
    /// MAM-10 revisits it for its own calls.
    /// </summary>
    public const long MaximumResponseBytes = 4 * 1024 * 1024;

    /// <summary>
    /// MAM-7 — a connection test is answered from the service's memory on a LAN in milliseconds; 15 s
    /// covers a NAS waking its disks or a service still starting, without leaving the owner staring at
    /// "Testing…". MAM-8 — the timeout is per request, and a library sync sends one request per page of
    /// 50 movies, so it bounds reading one page, never the whole library. MAM-10 and MAM-11 re-justify
    /// it for their own calls (a Toloka search through Prowlarr took 2.7 s, ADR 0004).
    /// </summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// A LAN service accepts a TCP connection in milliseconds; without this, an address where nothing
    /// answers holds the dialog for the operating system's SYN retries.
    /// </summary>
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    public static IServiceCollection AddIntegrationsInfrastructure(this IServiceCollection services)
    {
        AddIntegrationClient(services, JellyfinAdapter.HttpClientName);
        AddIntegrationClient(services, TorznabAdapter.HttpClientName);
        AddIntegrationClient(services, QBittorrentAdapter.HttpClientName);
        // Stateless apart from the client factory: one instance per type serves every request.
        services.AddSingleton<IIntegrationAdapter, JellyfinAdapter>();
        services.AddSingleton<IIntegrationAdapter, TorznabAdapter>();
        services.AddSingleton<IIntegrationAdapter, QBittorrentAdapter>();
        // Strategy over every IIntegrationAdapter registered, the test host's fake included.
        services.AddSingleton<IIntegrationAdapterRegistry, IntegrationAdapterRegistry>();
        return services;
    }

    // No retry or resilience handler (house rule 8): a test is one attempt, and a failed qBittorrent
    // sign-in repeated counts towards its ban.
    private static void AddIntegrationClient(IServiceCollection services, string name) =>
        services.AddHttpClient(name, client =>
            {
                client.Timeout = RequestTimeout;
                client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
                client.MaxResponseContentBufferSize = MaximumResponseBytes;
                // No BaseAddress (a house deviation): one client serves every connection of its type,
                // each with its own owner-entered address, so the adapters build absolute URIs per call.
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // Every request here carries a credential; a redirect would carry it wherever the
                // Location points. A 3xx is reported to the owner instead (ADR 0007).
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
