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
/// Thread safe: a release search asks every feed at once. Each answer is built fresh from the
/// properties, which a test sets before the calls; an <see cref="Intercept"/> it sets must be safe to
/// call concurrently itself.
/// </para>
/// </remarks>
public sealed class FakeTorznabServer
{
    private readonly Uri _endpoint;
    private readonly string _indexerPath;

    /// <param name="feedAddress">The address an integration stores for the feed, such as
    /// <c>http://prowlarr.test:9696/1/api</c>.</param>
    /// <param name="apiKey">The key the search answer's download links carry, as Prowlarr's do; the
    /// sample's <c>REDACTED</c> when <c>null</c>.</param>
    public FakeTorznabServer(string feedAddress, string? apiKey = null)
    {
        FeedAddress = feedAddress;
        _endpoint = new Uri(TorznabAdapter.EndpointFor(feedAddress));
        _indexerPath = new Uri(TorznabSamples.DownloadBaseFor(feedAddress)).AbsolutePath;
        SearchXml = TorznabSamples.Search(feedAddress, apiKey);
    }

    /// <summary>The address this feed answers at.</summary>
    public string FeedAddress { get; }

    /// <summary>What <c>t=caps</c> answers: the caps sample.</summary>
    public string CapsXml { get; set; } = TorznabSamples.Caps();

    /// <summary>What <c>t=search</c> and <c>t=movie</c> answer: the <c>t=search</c> sample, its links on
    /// this feed.</summary>
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

    /// <summary>What this feed answers <paramref name="request"/>.</summary>
    public HttpResponseMessage Answer(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Intercept?.Invoke(request) is { } intercepted)
            return intercepted;

        var uri = request.RequestUri!;
        if (string.Equals(uri.AbsolutePath, _endpoint.AbsolutePath, StringComparison.Ordinal))
        {
            return Function(uri) switch
            {
                "caps" => Xml(CapsXml, "application/xml"),
                "search" or "movie" => Xml(SearchXml, "application/rss+xml"),
                "tvsearch" => Xml(TvSearchXml, "application/rss+xml"),
                // What a Torznab feed answers a function it doesn't have.
                _ => Xml("""<?xml version="1.0" encoding="UTF-8"?><error code="202" description="No such function" />""", "application/xml"),
            };
        }

        if (string.Equals(uri.AbsolutePath, _indexerPath + "download", StringComparison.Ordinal))
        {
            var content = new ByteArrayContent(TorrentFile);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/x-bittorrent");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private bool Serves(Uri uri) =>
        Uri.Compare(uri, _endpoint, UriComponents.SchemeAndServer, UriFormat.SafeUnescaped, StringComparison.OrdinalIgnoreCase) == 0
        && uri.AbsolutePath.StartsWith(_indexerPath, StringComparison.Ordinal);

    /// <summary>The <c>t=</c> of a request, or "" without one.</summary>
    private static string Function(Uri uri) =>
        uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Where(pair => string.Equals(pair[0], "t", StringComparison.Ordinal))
            .Select(pair => pair.Length > 1 ? Uri.UnescapeDataString(pair[1]) : "")
            .FirstOrDefault() ?? "";

    private static HttpResponseMessage Xml(string body, string mediaType) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, mediaType) };
}
