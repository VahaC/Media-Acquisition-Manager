using System.Net;
using System.Net.Http.Headers;
using System.Text;
using MediaAcquisitionManager.Infrastructure.Integrations.Torznab;
using MediaAcquisitionManager.TestInfrastructure.Http;

namespace MediaAcquisitionManager.TestInfrastructure.Integrations;

/// <summary>
/// One Torznab feed behind a <see cref="RecordingHttpHandler"/> (MAM-10): it answers <c>t=caps</c>,
/// <c>t=search</c>, <c>t=movie</c> and (MAM-32) <c>t=tvsearch</c> at the feed's endpoint and the download
/// links beside it, from the real samples (<see cref="TorznabSamples"/>) unless a test changes them.
/// </summary>
/// <remarks>
/// <para>
/// One server per feed: <see cref="Arm(RecordingHttpHandler, FakeTorznabServer[])"/> gives one handler
/// several, each answering the requests to its own origin and indexer path, so two Prowlarr indexers on
/// one host are two feeds. A request to none of them answers 503, like an unarmed handler.
/// </para>
/// <para>
/// MAM-30 — <see cref="Jackett"/> makes one that answers as Jackett v0.24.2830 did (MAM-29's recordings): its own
/// samples, its downloads at <c>/dl/{indexer}/</c> at the root of its host, and its key checked the way Jackett
/// checks it — a wrong <c>apikey</c> on caps or a search gets the recorded HTTP 200 with error 100, a wrong
/// <c>jackett_apikey</c> on a download the recorded bare 401, and a download token other than
/// <see cref="TorznabSamples.JackettPathToken"/> the recorded bare 404. A Prowlarr-shaped one checks no key.
/// </para>
/// <para>
/// Thread safe: a release search asks every feed at once. Each answer is built fresh from the
/// properties, which a test sets before the calls; an <see cref="Intercept"/> it sets must be safe to
/// call concurrently itself.
/// </para>
/// </remarks>
public sealed class FakeTorznabServer
{
    private readonly Uri _endpoint;

    /// <summary>The paths this feed answers below, on its origin: a Prowlarr indexer's own (<c>/1/</c>, its endpoint and
    /// its downloads both); MAM-30 — a Jackett indexer's feed address and its downloads at the root.</summary>
    private readonly string[] _servedPaths;

    /// <summary>MAM-30 — the path a download link asks: Prowlarr's <c>/{indexer}/download</c>, Jackett's
    /// <c>/dl/{indexer}/</c>.</summary>
    private readonly string _downloadPath;

    /// <summary>MAM-30 — the key a Jackett feed checks; <c>null</c> for a Prowlarr-shaped feed, which checks none.</summary>
    private readonly string? _jackettApiKey;

    /// <param name="feedAddress">The address an integration stores for the feed, such as
    /// <c>http://prowlarr.test:9696/1/api</c>.</param>
    /// <param name="apiKey">The key the search answer's download links carry, as Prowlarr's do; the
    /// sample's <c>REDACTED</c> when <c>null</c>.</param>
    public FakeTorznabServer(string feedAddress, string? apiKey = null)
    {
        FeedAddress = feedAddress;
        _endpoint = new Uri(TorznabAdapter.EndpointFor(feedAddress));
        DownloadBase = TorznabSamples.DownloadBaseFor(feedAddress);
        var indexerPath = new Uri(DownloadBase).AbsolutePath;
        _servedPaths = [indexerPath];
        _downloadPath = indexerPath + "download";
        CapsXml = TorznabSamples.Caps();
        SearchXml = TorznabSamples.Search(feedAddress, apiKey);
    }

    /// <summary>MAM-30 — a Jackett feed (<see cref="Jackett"/>), whose key is <paramref name="jackettApiKey"/>.</summary>
    private FakeTorznabServer(string feedAddress, string jackettApiKey, string downloadBase)
    {
        FeedAddress = feedAddress;
        _jackettApiKey = jackettApiKey;
        _endpoint = new Uri(TorznabAdapter.EndpointFor(feedAddress));
        DownloadBase = downloadBase;
        _downloadPath = new Uri(downloadBase).AbsolutePath;
        // The feed's own address (…/indexers/toloka/results/torznab/) and its downloads at the host's root.
        _servedPaths = [_endpoint.AbsolutePath[..(_endpoint.AbsolutePath.LastIndexOf('/') + 1)], _downloadPath];
        CapsXml = TorznabSamples.JackettCaps();
        SearchXml = TorznabSamples.JackettMovieSearch(feedAddress, jackettApiKey);
    }

    /// <summary>The address this feed answers at.</summary>
    public string FeedAddress { get; }

    /// <summary>MAM-30 — where this feed's download links live: <c>http://prowlarr.test:9696/1/</c> beside a Prowlarr
    /// feed (the links ask <c>download</c> below it), <c>http://jackett.test:9117/dl/toloka/</c> for a Jackett one.</summary>
    public string DownloadBase { get; }

    /// <summary>What <c>t=caps</c> answers: the caps sample (MAM-30: Jackett's for a Jackett feed).</summary>
    public string CapsXml { get; set; }

    /// <summary>What <c>t=search</c> and <c>t=movie</c> answer: the <c>t=search</c> sample, its links on
    /// this feed — MAM-30, Jackett's <c>t=movie</c> sample for a Jackett feed.</summary>
    public string SearchXml { get; set; }

    /// <summary>
    /// What <c>t=tvsearch</c> answers (MAM-32): an empty feed, so a search asks the sample's TV categories
    /// and finds nothing there, and every movie a test expects stays the whole answer; a TV test sets one
    /// of the TV samples.
    /// </summary>
    public string TvSearchXml { get; set; } = TorznabSamples.Feed();

    /// <summary>What a download link answers, as <c>application/x-bittorrent</c>.</summary>
    public byte[] TorrentFile { get; set; } = TorznabSamples.TorrentFile();

    /// <summary>Answers a request before the feed does — a failure, a redirect, a slow answer — or
    /// returns <c>null</c> to let the feed answer.</summary>
    public Func<HttpRequestMessage, HttpResponseMessage?>? Intercept { get; set; }

    /// <summary>
    /// MAM-30 — a Jackett feed at <paramref name="feedAddress"/> (<c>http://jackett.test:9117/api/v2.0/indexers/toloka/results/torznab/</c>,
    /// as Jackett's Copy Torznab Feed gives it) whose key is <paramref name="apiKey"/>: its caps and its <c>t=movie</c>
    /// sample, links rewritten onto it with that key (<see cref="TorznabSamples.JackettSearch"/>).
    /// </summary>
    public static FakeTorznabServer Jackett(string feedAddress, string apiKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(apiKey);
        return new FakeTorznabServer(feedAddress, apiKey, TorznabSamples.JackettDownloadBaseFor(feedAddress));
    }

    /// <summary>Makes <paramref name="handler"/> answer as this feed.</summary>
    public void Arm(RecordingHttpHandler handler) => Arm(handler, this);

    /// <summary>Makes <paramref name="handler"/> answer as <paramref name="servers"/>, each at its own
    /// address; a request to none of them answers 503.</summary>
    public static void Arm(RecordingHttpHandler handler, params FakeTorznabServer[] servers)
    {
        ArgumentNullException.ThrowIfNull(handler);
        handler.Handler = (request, _) =>
            Array.Find(servers, server => server.Serves(request.RequestUri!)) is { } server
                ? server.Answer(request)
                : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
    }

    /// <summary>
    /// MAM-30 — Jackett's answer to a download it hands over: the recorded status line and headers
    /// (<see cref="TorznabSamples.JackettDownloadResponseFile"/>, its <c>Content-Disposition</c> included) with
    /// <paramref name="torrentFile"/> as the body, which the recording didn't keep — and so without the recorded
    /// <c>Content-Length</c>, which was that other file's.
    /// </summary>
    public static HttpResponseMessage JackettDownload(byte[] torrentFile)
    {
        var response = VendorResponses.Recorded(TorznabSamples.JackettDownloadResponseFile);
        using var recorded = response.Content;
        var content = new ByteArrayContent(torrentFile);
        foreach (var header in recorded.Headers.NonValidated.Where(header => !string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase)))
            content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        response.Content = content;
        return response;
    }

    /// <summary>What this feed answers <paramref name="request"/>.</summary>
    public HttpResponseMessage Answer(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Intercept?.Invoke(request) is { } intercepted)
            return intercepted;

        var uri = request.RequestUri!;
        if (string.Equals(uri.AbsolutePath, _endpoint.AbsolutePath, StringComparison.Ordinal))
        {
            // MAM-30 — Jackett checks the key before anything else, and answers a wrong one in Torznab's words with
            // HTTP 200 (VendorSamples).
            if (_jackettApiKey is not null && !string.Equals(QueryValue(uri, "apikey"), _jackettApiKey, StringComparison.Ordinal))
                return VendorResponses.Recorded(TorznabSamples.JackettWrongKeyResponseFile);

            return QueryValue(uri, "t") switch
            {
                "caps" => Xml(CapsXml, "application/xml"),
                "search" or "movie" => Xml(SearchXml, "application/rss+xml"),
                "tvsearch" => Xml(TvSearchXml, "application/rss+xml"),
                // What a Torznab feed answers a function it doesn't have.
                _ => Xml("""<?xml version="1.0" encoding="UTF-8"?><error code="202" description="No such function" />""", "application/xml"),
            };
        }

        if (string.Equals(uri.AbsolutePath, _downloadPath, StringComparison.Ordinal))
        {
            if (_jackettApiKey is null)
            {
                var content = new ByteArrayContent(TorrentFile);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/x-bittorrent");
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            }

            // MAM-30 — Jackett's three recorded answers to a download link.
            if (!string.Equals(QueryValue(uri, "jackett_apikey"), _jackettApiKey, StringComparison.Ordinal))
                return VendorResponses.Recorded(TorznabSamples.JackettWrongKeyDownloadResponseFile);
            if (!string.Equals(QueryValue(uri, "path"), TorznabSamples.JackettPathToken, StringComparison.Ordinal))
                return VendorResponses.Recorded(TorznabSamples.JackettDownloadBadPathResponseFile);
            return JackettDownload(TorrentFile);
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private bool Serves(Uri uri) =>
        Uri.Compare(uri, _endpoint, UriComponents.SchemeAndServer, UriFormat.SafeUnescaped, StringComparison.OrdinalIgnoreCase) == 0
        && Array.Exists(_servedPaths, path => uri.AbsolutePath.StartsWith(path, StringComparison.Ordinal));

    /// <summary>The first value of <paramref name="name"/> in a request's query, decoded, or "" without one.</summary>
    private static string QueryValue(Uri uri, string name) =>
        uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Where(pair => string.Equals(pair[0], name, StringComparison.Ordinal))
            .Select(pair => pair.Length > 1 ? Uri.UnescapeDataString(pair[1]) : "")
            .FirstOrDefault() ?? "";

    private static HttpResponseMessage Xml(string body, string mediaType) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, mediaType) };
}
