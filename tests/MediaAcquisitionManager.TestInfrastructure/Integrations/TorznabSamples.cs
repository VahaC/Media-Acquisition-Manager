using System.Security;
using System.Text;
using MediaAcquisitionManager.Infrastructure.Integrations.Torznab;

namespace MediaAcquisitionManager.TestInfrastructure.Integrations;

/// <summary>
/// The real Torznab answers of <c>VendorSamples/Torznab</c> (MAM-6) as text, their download links moved
/// to a feed of the test's own (MAM-10); hand-written answers for what they don't show
/// (<see cref="Feed"/>, <see cref="Item"/>); and a small .torrent file to download.
/// </summary>
/// <remarks>
/// The samples' download links point at the reference installation's Prowlarr indexer,
/// <c>http://192.168.1.195:9696/1/</c>; a test never seeds a real LAN host, and a feed's links are on
/// its own origin, so <see cref="Search"/> and <see cref="MovieSearch"/> move them to the feed they are
/// given, and can put that feed's key where the sample has <c>apikey=REDACTED</c>.
/// </remarks>
public static class TorznabSamples
{
    /// <summary><c>GET /1/api?t=caps</c>: Toloka's categories and search modes.</summary>
    public const string CapsFile = "Torznab/prowlarr-toloka-caps.xml";

    /// <summary><c>GET /1/api?t=search&amp;q=Interstellar</c>: its links carry <c>sid=REDACTED</c>.</summary>
    public const string SearchFile = "Torznab/prowlarr-toloka-search.xml";

    /// <summary><c>GET /1/api?t=movie&amp;q=Interstellar</c>: the same releases, links without <c>sid</c>.</summary>
    public const string MovieSearchFile = "Torznab/prowlarr-toloka-movie-search.xml";

    /// <summary>MAM-32 — <c>GET /1/api?t=tvsearch&amp;q=Yellowstone</c>: the show's seasons in TV and TV/HD,
    /// and documentaries of the same name in TV/Documentary; its links carry <c>sid=REDACTED</c>.</summary>
    public const string TvSearchFile = "Torznab/prowlarr-toloka-tv-search.xml";

    /// <summary>MAM-32 — <c>GET /1/api?t=tvsearch&amp;q=Yellowstone&amp;season=1</c>: Toloka barely narrows
    /// by the season, so the first six of <see cref="TvSearchFile"/>'s releases.</summary>
    public const string TvSeasonSearchFile = "Torznab/prowlarr-toloka-tv-season-search.xml";

    /// <summary>MAM-32 — <c>GET /1/api?t=tvsearch&amp;q=Mickey Mouse Clubhouse</c>: a cartoon series' seasons,
    /// in TV and TV/HD and in Toloka's two «Мультсеріали» sections.</summary>
    public const string TvCartoonSearchFile = "Torznab/prowlarr-toloka-tv-cartoon-search.xml";

    /// <summary>
    /// What Prowlarr answered <c>t=caps</c>, <c>t=search</c> and a download link alike when asked with a
    /// wrong API key (recorded 2026-10-01): a bare 401 with no body, read by
    /// <c>VendorResponses.Recorded</c>.
    /// </summary>
    public const string WrongKeyResponseFile = "Torznab/prowlarr-wrong-key-response.txt";

    /// <summary>The releases each search sample lists.</summary>
    public const int ItemCount = 17;

    /// <summary>The distinct titles among them: two titles are listed twice, as two releases.</summary>
    public const int DistinctTitleCount = 15;

    /// <summary>The releases listed in no movie category: the audio-only "Interstellar (2014) [DTS, AC3]".</summary>
    public const int NonMovieItemCount = 1;

    /// <summary>The category of that audio-only release (Audio/Video).</summary>
    public const string NonMovieCategoryId = "3020";

    /// <summary>The releases <see cref="TvSearchFile"/> lists (MAM-32).</summary>
    public const int TvItemCount = 9;

    /// <summary>The releases <see cref="TvSeasonSearchFile"/> lists (MAM-32).</summary>
    public const int TvSeasonItemCount = 6;

    /// <summary>The releases <see cref="TvCartoonSearchFile"/> lists (MAM-32).</summary>
    public const int TvCartoonItemCount = 4;

    /// <summary>The indexer the samples' download links point at; only ever replaced, never requested.</summary>
    private const string SampleIndexerBase = "http://192.168.1.195:9696/1/";

    private const string RedactedApiKey = "apikey=REDACTED";

    private static readonly Lazy<string> CapsText = new(() => Read(CapsFile));
    private static readonly Lazy<string> SearchText = new(() => Read(SearchFile));
    private static readonly Lazy<string> MovieSearchText = new(() => Read(MovieSearchFile));
    private static readonly Lazy<string> TvSearchText = new(() => Read(TvSearchFile));
    private static readonly Lazy<string> TvSeasonSearchText = new(() => Read(TvSeasonSearchFile));
    private static readonly Lazy<string> TvCartoonSearchText = new(() => Read(TvCartoonSearchFile));

    /// <summary>
    /// A bencoded single-file torrent the size of a byte: the shape a payload fetch accepts (a dictionary
    /// with an <c>info</c> key), and nothing that names a real tracker.
    /// </summary>
    private static readonly byte[] TorrentBytes = Encoding.ASCII.GetBytes(
        "d8:announce19:http://tracker.test4:infod6:lengthi1e4:name5:x.mkv12:piece lengthi16384e6:pieces20:aaaaaaaaaaaaaaaaaaaaee");

    /// <summary>The caps sample, as Prowlarr sent it.</summary>
    public static string Caps() => CapsText.Value;

    /// <summary>The <c>t=search</c> sample with its download links on <paramref name="feedAddress"/>'s
    /// indexer (<see cref="DownloadBaseFor"/>), carrying <paramref name="apiKey"/> when given.</summary>
    public static string Search(string feedAddress, string? apiKey = null) => Rewrite(SearchText.Value, feedAddress, apiKey);

    /// <summary>The <c>t=movie</c> sample, moved the same way as <see cref="Search"/>.</summary>
    public static string MovieSearch(string feedAddress, string? apiKey = null) => Rewrite(MovieSearchText.Value, feedAddress, apiKey);

    /// <summary>The <c>t=tvsearch</c> sample (MAM-32), moved the same way as <see cref="Search"/>.</summary>
    public static string TvSearch(string feedAddress, string? apiKey = null) => Rewrite(TvSearchText.Value, feedAddress, apiKey);

    /// <summary>The <c>t=tvsearch</c> sample of one season (MAM-32), moved the same way as
    /// <see cref="Search"/>.</summary>
    public static string TvSeasonSearch(string feedAddress, string? apiKey = null) => Rewrite(TvSeasonSearchText.Value, feedAddress, apiKey);

    /// <summary>The <c>t=tvsearch</c> sample of a cartoon series (MAM-32), moved the same way as
    /// <see cref="Search"/>.</summary>
    public static string TvCartoonSearch(string feedAddress, string? apiKey = null) => Rewrite(TvCartoonSearchText.Value, feedAddress, apiKey);

    /// <summary>A fresh copy of a small valid .torrent file.</summary>
    public static byte[] TorrentFile() => [.. TorrentBytes];

    /// <summary>A search answer of <paramref name="items"/> (<see cref="Item"/>), shaped like the
    /// samples: RSS with the <c>torznab</c> namespace.</summary>
    public static string Feed(params string[] items) =>
        $"""<?xml version="1.0" encoding="UTF-8"?><rss version="1.0" xmlns:torznab="http://torznab.com/schemas/2015/feed"><channel><title>Feed</title>{string.Concat(items)}</channel></rss>""";

    /// <summary>
    /// One <c>&lt;item&gt;</c>, each value XML-escaped: a title, a <c>&lt;link&gt;</c>, a <c>&lt;guid&gt;</c>
    /// and <c>&lt;comments&gt;</c> when given, and <paramref name="extra"/> as written (more elements,
    /// <c>torznab:attr</c>s).
    /// </summary>
    public static string Item(string? title, string? link, string? guid = null, string? comments = null, string extra = "") =>
        "<item>"
        + Element("title", title)
        + Element("guid", guid)
        + Element("comments", comments)
        + Element("link", link)
        + extra
        + "</item>";

    private static string Element(string name, string? value) =>
        value is null ? "" : $"<{name}>{SecurityElement.Escape(value)}</{name}>";

    /// <summary>
    /// Where a feed's download links live: its Torznab endpoint with the last segment cut, as Prowlarr
    /// builds them — <c>http://prowlarr.test:9696/1/api</c> downloads from <c>http://prowlarr.test:9696/1/download</c>.
    /// </summary>
    public static string DownloadBaseFor(string feedAddress)
    {
        var endpoint = TorznabAdapter.EndpointFor(feedAddress);
        return endpoint[..(endpoint.LastIndexOf('/') + 1)];
    }

    private static string Rewrite(string sample, string feedAddress, string? apiKey)
    {
        var moved = sample.Replace(SampleIndexerBase, DownloadBaseFor(feedAddress), StringComparison.Ordinal);
        // Escaped as a link carries it, which leaves nothing XML would need to escape.
        return apiKey is null ? moved : moved.Replace(RedactedApiKey, $"apikey={Uri.EscapeDataString(apiKey)}", StringComparison.Ordinal);
    }

    private static string Read(string relativePath) => File.ReadAllText(Path.Combine(RepositoryPaths.VendorSamples, relativePath));
}
