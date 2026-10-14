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
/// <para>
/// The samples' download links point at the reference installation's Prowlarr indexer,
/// <c>http://192.168.1.195:9696/1/</c>; a test never seeds a real LAN host, and a feed's links are on
/// its own origin, so <see cref="Search"/> and <see cref="MovieSearch"/> move them to the feed they are
/// given, and can put that feed's key where the sample has <c>apikey=REDACTED</c>.
/// </para>
/// <para>
/// MAM-30 — Jackett v0.24.2830's answers through its Toloka.to indexer (MAM-29, the <c>jackett-*</c> files) the
/// same way: their links point at <c>http://192.168.1.195:9117/dl/toloka/</c>, at the host's root rather than under
/// the feed's address, and carry <c>jackett_apikey=REDACTED&amp;path=REDACTED</c>. <see cref="JackettMovieSearch"/>
/// and the rest move them to the Jackett feed they are given (<see cref="JackettDownloadBaseFor"/>), put that feed's
/// key in <c>jackett_apikey</c> and <see cref="JackettPathToken"/> in <c>path</c>.
/// </para>
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
    /// MAM-33 — <c>GET /1/api?t=search&amp;cat=2000&amp;limit=100</c>, no <c>q</c>: Toloka's newest releases
    /// in the movie categories, newest first, each with its own topic title in <c>&lt;description&gt;</c>.
    /// It answered its latest 90, trimmed here to the first 25; asked with <c>offset=50</c> or
    /// <c>offset=100</c> it answered none at all (recorded 2026-10-05).
    /// </summary>
    public const string LatestMoviesFile = "Torznab/prowlarr-toloka-latest-movies.xml";

    /// <summary>
    /// MAM-33 — the same with <c>cat=5000</c>: the newest TV releases, trimmed to 25 of 90 — anime in
    /// TV/Anime with season or episode markers, a Ukrainian-only documentary series in TV/Documentary whose
    /// title Prowlarr cut to "(2026) WEB-DL 1080p (S1E1-25 of ?)", sport in TV/Sport.
    /// </summary>
    public const string LatestTvFile = "Torznab/prowlarr-toloka-latest-tv.xml";

    /// <summary>
    /// MAM-33 — <c>GET /1/api?t=movie&amp;q=Schumacher '94 - Die Geburt einer Legende 2026&amp;cat=2000,2020,2040,2070,5070,5080</c>:
    /// a documentary film Toloka files in Movies once and in its «Документальні фільми в HD», which Prowlarr
    /// lists as TV/Documentary, four times.
    /// </summary>
    public const string FilmInTvCategorySearchFile = "Torznab/prowlarr-toloka-movie-search-film-in-tv-category.xml";

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

    /// <summary>
    /// MAM-33 — the releases of <see cref="TvSearchFile"/> that are films: its last three, in TV/Documentary
    /// and naming no season ("Wild Yellowstone She Wolf (2013) HDTV"), where the BBC's "Yellowstone (S1)
    /// (2009)" beside them is a series (ADR 0020).
    /// </summary>
    public const int TvFilmItemCount = 3;

    /// <summary>MAM-33 — the releases <see cref="LatestMoviesFile"/> lists.</summary>
    public const int LatestMoviesItemCount = 25;

    /// <summary>MAM-33 — the releases <see cref="LatestTvFile"/> lists.</summary>
    public const int LatestTvItemCount = 25;

    /// <summary>MAM-33 — the releases <see cref="FilmInTvCategorySearchFile"/> lists.</summary>
    public const int FilmInTvCategoryItemCount = 5;

    /// <summary>MAM-33 — those of them in TV/Documentary (5080) alone: all but the first.</summary>
    public const int FilmInTvCategoryDocumentaryItemCount = 4;

    // ── MAM-30: Jackett's answers ───────────────────────────────────────────────────────────────

    /// <summary>MAM-30 — <c>GET …/indexers/toloka/results/torznab/api?t=caps</c> on Jackett: Prowlarr's categories with
    /// the same ids, names and <c>|-</c> marks, and 20 more; the same search modes; <c>&lt;server title="Jackett" /&gt;</c>.</summary>
    public const string JackettCapsFile = "Torznab/jackett-toloka-caps.xml";

    /// <summary>MAM-30 — <c>t=movie&amp;q=Interstellar</c>: <see cref="MovieSearchFile"/>'s 17 releases and a trailer in
    /// 8000 (Other), each with Toloka's whole topic title as its description and its link at the host's root.</summary>
    public const string JackettMovieSearchFile = "Torznab/jackett-toloka-movie-search.xml";

    /// <summary>MAM-30 — <c>t=tvsearch&amp;q=Yellowstone</c>: <see cref="TvSearchFile"/>'s 9, subtitles in Audio/Video (3020)
    /// and two archive sections in 8000.</summary>
    public const string JackettTvSearchFile = "Torznab/jackett-toloka-tv-search.xml";

    /// <summary>MAM-30 — the same with <c>&amp;season=1</c>: Toloka barely narrows by the season here either.</summary>
    public const string JackettTvSeasonSearchFile = "Torznab/jackett-toloka-tv-season-search.xml";

    /// <summary>MAM-30 — <c>t=tvsearch&amp;q=Mickey Mouse Clubhouse</c>: <see cref="TvCartoonSearchFile"/>'s 4 releases.</summary>
    public const string JackettTvCartoonSearchFile = "Torznab/jackett-toloka-tv-cartoon-search.xml";

    /// <summary>MAM-30 — <c>t=tvsearch&amp;q=Клуб Міккі Мауса</c>: the same 4, a book (7000) and a scan (8000) whose titles
    /// kept nothing but their year and format ("(2010) [djvu]"). No title holds Cyrillic; every description does.</summary>
    public const string JackettTvCartoonUkrainianTitleSearchFile = "Torznab/jackett-toloka-tv-cartoon-search-ukrainian-title.xml";

    /// <summary>MAM-30 — <c>t=tvsearch&amp;q=Розслідування з Андрієм Данілевичем</c>, a Ukrainian-only show: an empty
    /// channel, for a reason upstream that isn't known (VendorSamples README).</summary>
    public const string JackettTvUkrainianOnlySearchFile = "Torznab/jackett-toloka-tv-search-ukrainian-only.xml";

    /// <summary>MAM-30 — <c>t=movie&amp;q=Schumacher '94 - Die Geburt einer Legende 2026&amp;cat=…5070,5080</c>: the 5
    /// releases of <see cref="FilmInTvCategorySearchFile"/>, once in Movies and four times in TV/Documentary.</summary>
    public const string JackettFilmInTvCategorySearchFile = "Torznab/jackett-toloka-movie-search-film-in-tv-category.xml";

    /// <summary>MAM-30 — <c>t=search</c> with no <c>q</c> and no <c>cat</c>: Toloka's newest 90 in every section, books
    /// (7000) and software (4000) among them.</summary>
    public const string JackettLatestFile = "Torznab/jackett-toloka-latest.xml";

    /// <summary>
    /// MAM-30 — <c>t=search&amp;cat=2000&amp;limit=100</c>: the newest 90 in the movie categories, kept whole — among them
    /// two Ukrainian-only films whose titles "Strip Cyrillic Letters" left as "« » (2026) …" and whose descriptions keep
    /// the whole title, "Потяг «Червона Рута» (2026) …" (<see cref="JackettUkrainianOnlyFilmTitle"/>).
    /// </summary>
    public const string JackettLatestMoviesFile = "Torznab/jackett-toloka-latest-movies.xml";

    /// <summary>MAM-30 — the same with <c>cat=5000</c>: the newest 90 in the TV categories, 61 of them in TV/Anime.</summary>
    public const string JackettLatestTvFile = "Torznab/jackett-toloka-latest-tv.xml";

    /// <summary>MAM-30 — what Jackett answered <c>t=caps</c> and <c>t=search</c> alike with a wrong <c>apikey</c>: HTTP 200,
    /// <c>application/xml</c>, <c>&lt;error code="100" description="Invalid API Key" /&gt;</c>.</summary>
    public const string JackettWrongKeyResponseFile = "Torznab/jackett-wrong-key-response.txt";

    /// <summary>MAM-30 — what a download link answered with a wrong <c>jackett_apikey</c>: a bare 401.</summary>
    public const string JackettWrongKeyDownloadResponseFile = "Torznab/jackett-wrong-key-download-response.txt";

    /// <summary>MAM-30 — what a download link answered with the right key and a mangled <c>path</c>: a bare 404.</summary>
    public const string JackettDownloadBadPathResponseFile = "Torznab/jackett-download-bad-path-response.txt";

    /// <summary>MAM-30 — what a download link answered: 200, <c>application/x-bittorrent</c> and a
    /// <c>Content-Disposition</c>; the status line and headers only, the .torrent wasn't kept.</summary>
    public const string JackettDownloadResponseFile = "Torznab/jackett-download-response.txt";

    /// <summary>MAM-30 — what the rewritten Jackett links carry as their download token (<c>path=</c>), where the samples
    /// have <c>REDACTED</c>: a credential of Jackett's own, which opens the download with the key, so no answer, log
    /// line or job may repeat it.</summary>
    public const string JackettPathToken = "planted-jackett-path-0001";

    /// <summary>MAM-30 — the Toloka title of the two Ukrainian-only films of <see cref="JackettLatestMoviesFile"/>, as
    /// their descriptions give it: "Потяг «Червона Рута» (2026)".</summary>
    public const string JackettUkrainianOnlyFilmTitle = "Потяг «Червона Рута»";

    /// <summary>MAM-30 — the releases <see cref="JackettMovieSearchFile"/> lists.</summary>
    public const int JackettItemCount = 18;

    /// <summary>MAM-30 — those of them in no movie category: the audio-only release (3020) and the trailer (8000).</summary>
    public const int JackettNonMovieItemCount = 2;

    /// <summary>MAM-30 — the releases <see cref="JackettTvSearchFile"/> lists.</summary>
    public const int JackettTvItemCount = 12;

    /// <summary>MAM-30 — those of them in no TV category: the subtitles (3020) and the two archive sections (8000).</summary>
    public const int JackettNonTvItemCount = 3;

    /// <summary>MAM-30 — the releases <see cref="JackettTvSeasonSearchFile"/> lists, the subtitles (3020) among them.</summary>
    public const int JackettTvSeasonItemCount = 7;

    /// <summary>MAM-30 — the releases <see cref="JackettTvCartoonSearchFile"/> lists.</summary>
    public const int JackettTvCartoonItemCount = 4;

    /// <summary>MAM-30 — the releases <see cref="JackettTvCartoonUkrainianTitleSearchFile"/> lists, the book and the scan
    /// among them.</summary>
    public const int JackettTvCartoonUkrainianTitleItemCount = 6;

    /// <summary>MAM-30 — the releases each of <see cref="JackettLatestFile"/>, <see cref="JackettLatestMoviesFile"/> and
    /// <see cref="JackettLatestTvFile"/> lists.</summary>
    public const int JackettLatestItemCount = 90;

    /// <summary>The indexer the samples' download links point at; only ever replaced, never requested.</summary>
    private const string SampleIndexerBase = "http://192.168.1.195:9696/1/";

    private const string RedactedApiKey = "apikey=REDACTED";

    /// <summary>MAM-30 — where Jackett's samples were recorded: its download links at <c>dl/toloka/</c> below it, and the
    /// channel's own <c>atom:link</c>. Only ever replaced, never requested.</summary>
    private const string SampleJackettRoot = "http://192.168.1.195:9117/";

    /// <summary>MAM-30 — the indexer of Jackett's samples, whose download links live at <c>/dl/toloka/</c>.</summary>
    private const string SampleJackettDownloadBase = SampleJackettRoot + "dl/toloka/";

    private const string RedactedJackettApiKey = "jackett_apikey=REDACTED";

    private const string RedactedJackettPath = "path=REDACTED";

    /// <summary>MAM-30 — what a Jackett feed's address holds before its indexer's id: <c>…/api/v2.0/indexers/toloka/results/torznab/</c>.</summary>
    private const string JackettIndexersPath = "/api/v2.0/indexers/";

    private static readonly Lazy<string> CapsText = new(() => Read(CapsFile));
    private static readonly Lazy<string> SearchText = new(() => Read(SearchFile));
    private static readonly Lazy<string> MovieSearchText = new(() => Read(MovieSearchFile));
    private static readonly Lazy<string> TvSearchText = new(() => Read(TvSearchFile));
    private static readonly Lazy<string> TvSeasonSearchText = new(() => Read(TvSeasonSearchFile));
    private static readonly Lazy<string> TvCartoonSearchText = new(() => Read(TvCartoonSearchFile));
    private static readonly Lazy<string> LatestMoviesText = new(() => Read(LatestMoviesFile));
    private static readonly Lazy<string> LatestTvText = new(() => Read(LatestTvFile));
    private static readonly Lazy<string> FilmInTvCategorySearchText = new(() => Read(FilmInTvCategorySearchFile));
    private static readonly Lazy<string> JackettCapsText = new(() => Read(JackettCapsFile));

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

    /// <summary>The newest releases in the movie categories (MAM-33), moved the same way as
    /// <see cref="Search"/>.</summary>
    public static string LatestMovies(string feedAddress, string? apiKey = null) => Rewrite(LatestMoviesText.Value, feedAddress, apiKey);

    /// <summary>The newest releases in the TV categories (MAM-33), moved the same way as
    /// <see cref="Search"/>.</summary>
    public static string LatestTv(string feedAddress, string? apiKey = null) => Rewrite(LatestTvText.Value, feedAddress, apiKey);

    /// <summary>The <c>t=movie</c> sample of a documentary film in TV/Documentary (MAM-33), moved the same way
    /// as <see cref="Search"/>.</summary>
    public static string FilmInTvCategorySearch(string feedAddress, string? apiKey = null) =>
        Rewrite(FilmInTvCategorySearchText.Value, feedAddress, apiKey);

    /// <summary>MAM-30 — Jackett's caps sample, as it sent it.</summary>
    public static string JackettCaps() => JackettCapsText.Value;

    /// <summary>
    /// MAM-30 — the Jackett answer <paramref name="file"/> (one of the <c>Jackett…File</c> searches) with its download
    /// links on <paramref name="feedAddress"/>'s indexer (<see cref="JackettDownloadBaseFor"/>), carrying
    /// <paramref name="apiKey"/> when given and <see cref="JackettPathToken"/> as each one's token.
    /// </summary>
    public static string JackettSearch(string file, string feedAddress, string? apiKey = null) => RewriteJackett(Read(file), feedAddress, apiKey);

    /// <summary>MAM-30 — <see cref="JackettMovieSearchFile"/>, moved as <see cref="JackettSearch"/> moves it.</summary>
    public static string JackettMovieSearch(string feedAddress, string? apiKey = null) => JackettSearch(JackettMovieSearchFile, feedAddress, apiKey);

    /// <summary>MAM-30 — <see cref="JackettTvSearchFile"/>, moved as <see cref="JackettSearch"/> moves it.</summary>
    public static string JackettTvSearch(string feedAddress, string? apiKey = null) => JackettSearch(JackettTvSearchFile, feedAddress, apiKey);

    /// <summary>MAM-30 — <see cref="JackettLatestMoviesFile"/>, moved as <see cref="JackettSearch"/> moves it.</summary>
    public static string JackettLatestMovies(string feedAddress, string? apiKey = null) => JackettSearch(JackettLatestMoviesFile, feedAddress, apiKey);

    /// <summary>MAM-30 — <see cref="JackettLatestTvFile"/>, moved as <see cref="JackettSearch"/> moves it.</summary>
    public static string JackettLatestTv(string feedAddress, string? apiKey = null) => JackettSearch(JackettLatestTvFile, feedAddress, apiKey);

    /// <summary>
    /// MAM-30 — where a Jackett feed's download links live: <c>dl/</c> and the indexer's id at the root of its host (or of
    /// the base path it is served under), not under the feed's address — <c>http://jackett.test:9117/api/v2.0/indexers/toloka/results/torznab/</c>
    /// downloads from <c>http://jackett.test:9117/dl/toloka/</c>. Throws for an address that isn't Jackett's shape.
    /// </summary>
    public static string JackettDownloadBaseFor(string feedAddress)
    {
        var uri = new Uri(feedAddress);
        var path = uri.AbsolutePath;
        var at = path.IndexOf(JackettIndexersPath, StringComparison.Ordinal);
        if (at < 0)
            throw new ArgumentException($"“{feedAddress}” is not a Jackett feed's address (…{JackettIndexersPath}<id>/results/torznab/).", nameof(feedAddress));

        var indexer = path[(at + JackettIndexersPath.Length)..].Split('/')[0];
        return $"{JackettRootFor(feedAddress)}dl/{indexer}/";
    }

    /// <summary>
    /// MAM-30 — a Jackett download link as the Torznab adapter stores it: on <paramref name="feedAddress"/>'s indexer, the
    /// key replaced by <see cref="TorznabLinks.ApiKeyPlaceholder"/>, <see cref="JackettPathToken"/> as its token and
    /// <paramref name="file"/> as Jackett names the file — what a <see cref="FakeTorznabServer.Jackett"/> at that address
    /// answers with its .torrent once the key is put back.
    /// </summary>
    public static string JackettLocator(string feedAddress, string file = "Interstellar (2014) BDRip 1080p") =>
        $"{JackettDownloadBaseFor(feedAddress)}?jackett_apikey={TorznabLinks.ApiKeyPlaceholder}&path={Uri.EscapeDataString(JackettPathToken)}&file={Uri.EscapeDataString(file)}";

    /// <summary>MAM-30 — the root a Jackett feed is served under: its origin, and the base path before <c>api/v2.0/</c>
    /// when Jackett has one (<c>http://jackett.test:9117/</c>).</summary>
    public static string JackettRootFor(string feedAddress)
    {
        var uri = new Uri(feedAddress);
        var at = uri.AbsolutePath.IndexOf(JackettIndexersPath, StringComparison.Ordinal);
        if (at < 0)
            throw new ArgumentException($"“{feedAddress}” is not a Jackett feed's address (…{JackettIndexersPath}<id>/results/torznab/).", nameof(feedAddress));
        return $"{uri.GetLeftPart(UriPartial.Authority)}{uri.AbsolutePath[..at]}/";
    }

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

    /// <summary>
    /// MAM-30 — a Jackett sample with its download links on <paramref name="feedAddress"/>'s indexer and every other
    /// address of the recording host (the channel's <c>atom:link</c>) on its root, so nothing in it names a LAN host;
    /// <c>jackett_apikey</c> and <c>path</c> replaced by name, never as a part of another name.
    /// </summary>
    private static string RewriteJackett(string sample, string feedAddress, string? apiKey)
    {
        var moved = sample
            .Replace(SampleJackettDownloadBase, JackettDownloadBaseFor(feedAddress), StringComparison.Ordinal)
            .Replace(SampleJackettRoot, JackettRootFor(feedAddress), StringComparison.Ordinal)
            .Replace("&amp;" + RedactedJackettPath, $"&amp;path={Uri.EscapeDataString(JackettPathToken)}", StringComparison.Ordinal);
        return apiKey is null
            ? moved
            : moved.Replace("?" + RedactedJackettApiKey, $"?jackett_apikey={Uri.EscapeDataString(apiKey)}", StringComparison.Ordinal);
    }

    private static string Read(string relativePath) => File.ReadAllText(Path.Combine(RepositoryPaths.VendorSamples, relativePath));
}
