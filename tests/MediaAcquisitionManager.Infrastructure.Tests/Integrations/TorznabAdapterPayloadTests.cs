using System.IO.Pipelines;
using System.Net;
using System.Net.Http.Headers;
using System.Security;
using System.Text;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Search;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Integrations;
using MediaAcquisitionManager.Infrastructure.Integrations.Torznab;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-10 — a release's payload fetched on the server: a magnet as it is, a download link from the feed's
// own origin only, once, with the connection's key put back and no redirect followed (ADR 0007); and
// what Prowlarr (an <error> with 410, 429 or 500) and Jackett (a bare 404) answer a failed download.
public sealed class TorznabAdapterPayloadTests : IDisposable
{
    private const string BaseUrl = "http://prowlarr.test:9696/1/api";
    private const string ApiKey = "0f9e8d7c6b5a49382716a5b4c3d2e1f0";

    /// <summary>Prowlarr's protected download link: base64 with + and =, percent-encoded in the link.</summary>
    private const string LinkToken = "Q2ZESjhQcm90+ZWN0ZWQ=";

    private const string Locator =
        "http://prowlarr.test:9696/1/download?apikey=%7Bapi-key%7D&link=Q2ZESjhQcm90%2BZWN0ZWQ%3D&file=Interstellar+(2014)";

    private const string ElsewhereError =
        "The release's download link points somewhere other than the Torznab feed's address, so the app didn't fetch it.";

    private const string NotATorrentError = "The Torznab feed answered the download with something that isn't a .torrent file.";

    private const string RedirectWithoutTargetError =
        "The Torznab feed answered the download with a redirect that doesn't say where to, and the app doesn't follow redirects.";

    private static readonly IntegrationConnectionSettings Settings = new(null, BaseUrl, null, null, ApiKey);

    private readonly IntegrationAdapterHarness _harness = new();

    private IReleaseSearchProvider Search => _harness.Adapter<IReleaseSearchProvider>(TorznabAdapter.TypeId);

    public void Dispose() => _harness.Dispose();

    [Theory]
    [InlineData("magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=Interstellar")]
    [InlineData("MAGNET:?dn=Interstellar%20(2014)&xt=urn:btmh:12200123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    [InlineData("magnet:?xt.1=urn:btih:0123456789abcdef0123456789abcdef01234567&xt.2=urn:btih:76543210fedcba9876543210fedcba9876543210")]
    public async Task FetchPayload_of_a_magnet_hands_it_over_without_a_request(string magnet)
    {
        //Arrange
        var locator = new ReleasePayloadLocator(magnet);

        //Act
        var result = await Search.FetchPayloadAsync(Settings, locator, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(ReleasePayloadKind.Magnet, result.Value!.Kind);
        Assert.Equal(magnet, result.Value.MagnetUri);
        Assert.Null(result.Value.TorrentFile);
        Assert.Empty(_harness.TorznabHttp.Requests);
    }

    [Fact]
    public async Task FetchPayload_fetches_the_torrent_from_the_feed_with_the_key_put_back()
    {
        //Arrange
        var settings = Settings with { ApiKey = "key+/=&?%~!12" };
        _harness.TorznabHttp.Handler = (_, _) => Torrent(TorznabSamples.TorrentFile());

        //Act
        var result = await Search.FetchPayloadAsync(settings, new ReleasePayloadLocator(Locator), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(ReleasePayloadKind.TorrentFile, result.Value!.Kind);
        Assert.Equal(TorznabSamples.TorrentFile(), result.Value.TorrentFile);
        Assert.Null(result.Value.MagnetUri);
        var request = Assert.Single(_harness.TorznabHttp.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        // The connection's current key, escaped, where the placeholder was; the rest as listed.
        Assert.Equal(
            "http://prowlarr.test:9696/1/download?apikey=key%2B%2F%3D%26%3F%25~%2112&link=Q2ZESjhQcm90%2BZWN0ZWQ%3D&file=Interstellar+(2014)",
            request.Uri.AbsoluteUri);
        Assert.Equal(["application/x-bittorrent", "*/*"], request.HeaderValues("Accept"));
    }

    [Fact]
    public async Task FetchPayload_of_a_release_the_search_listed_fetches_it_from_the_feed()
    {
        //Arrange
        var feed = new FakeTorznabServer(BaseUrl, ApiKey);
        feed.Arm(_harness.TorznabHttp);
        var cancellationToken = TestContext.Current.CancellationToken;
        var search = await Search.SearchAsync(Settings, new ReleaseSearchQuery(ReleaseSearchMode.General, "Interstellar", null, [], [], 100), cancellationToken);
        var locator = search.Value![0].Locator;

        //Act
        var result = await Search.FetchPayloadAsync(Settings, locator, cancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(feed.TorrentFile, result.Value!.TorrentFile);
        var download = _harness.TorznabHttp.Requests[^1];
        Assert.StartsWith($"http://prowlarr.test:9696/1/download?apikey={ApiKey}&link=REDACTED&file=Interstellar", download.Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Theory]
    // User info that reads like the feed's host and port, before the real host.
    [InlineData(BaseUrl, "http://prowlarr.test:9696@evil.test/1/download?link=x")]
    [InlineData(BaseUrl, "http://prowlarr.test:9696.evil.test/1/download?link=x")]
    [InlineData(BaseUrl, "http://prowlarr.test.evil.test:9696/1/download?link=x")]
    [InlineData(BaseUrl, "https://prowlarr.test:9696/1/download?link=x")]
    [InlineData(BaseUrl, "http://prowlarr.test:9697/1/download?link=x")]
    [InlineData(BaseUrl, "http://prowlarr.test/1/download?link=x")]
    [InlineData(BaseUrl, "http://evil.test:9696/1/download?link=x")]
    [InlineData("http://[::1]:9696/1/api", "http://[::2]:9696/1/download?link=x")]
    [InlineData("http://[::1]:9696/1/api", "http://127.0.0.1:9696/1/download?link=x")]
    [InlineData(BaseUrl, "file://prowlarr.test/1/download")]
    [InlineData(BaseUrl, "file:///etc/passwd")]
    [InlineData(BaseUrl, "data:application/x-bittorrent;base64,ZDQ6aW5mb2RlZQ==")]
    [InlineData(BaseUrl, "ftp://prowlarr.test:9696/1/download")]
    [InlineData(BaseUrl, "/1/download?link=x")]
    [InlineData(BaseUrl, "prowlarr.test:9696/1/download")]
    [InlineData(BaseUrl, "")]
    [InlineData(BaseUrl, "magnet:?dn=Interstellar")]
    public async Task FetchPayload_refuses_a_link_off_the_feeds_origin_without_sending_anything(string baseUrl, string locator)
    {
        //Arrange
        var settings = Settings with { BaseUrl = baseUrl };
        _harness.TorznabHttp.Handler = (_, _) => Torrent(TorznabSamples.TorrentFile());

        //Act
        var result = await Search.FetchPayloadAsync(settings, new ReleasePayloadLocator(locator), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(ElsewhereError, result.Error);
        Assert.Empty(_harness.TorznabHttp.Requests);
    }

    [Theory]
    // The default port written out, the host in another letter case, with a trailing dot, in Unicode
    // or in punycode, an IPv6 address written long: one origin each time.
    [InlineData("http://prowlarr.test/1/api", "http://prowlarr.test:80/1/download?link=x")]
    [InlineData("https://prowlarr.test/1/api", "https://prowlarr.test:443/1/download?link=x")]
    [InlineData(BaseUrl, "HTTP://PROWLARR.Test:9696/1/download?link=x")]
    [InlineData(BaseUrl, "http://prowlarr.test.:9696/1/download?link=x")]
    [InlineData("http://пример.test:9696/1/api", "http://xn--e1afmkfd.test:9696/1/download?link=x")]
    [InlineData("http://xn--e1afmkfd.test:9696/1/api", "http://ПРИМЕР.test:9696/1/download?link=x")]
    [InlineData("http://[::1]:9696/1/api", "http://[0:0:0:0:0:0:0:1]:9696/1/download?link=x")]
    public async Task FetchPayload_sends_a_link_on_the_feeds_origin_however_it_is_written(string baseUrl, string locator)
    {
        //Arrange
        var settings = Settings with { BaseUrl = baseUrl };
        _harness.TorznabHttp.Handler = (_, _) => Torrent(TorznabSamples.TorrentFile());

        //Act
        var result = await Search.FetchPayloadAsync(settings, new ReleasePayloadLocator(locator), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Single(_harness.TorznabHttp.Requests);
    }

    [Fact]
    public async Task FetchPayload_that_needs_a_key_the_feed_has_none_of_refuses_without_sending_anything()
    {
        //Arrange
        var settings = Settings with { ApiKey = null };

        //Act
        var result = await Search.FetchPayloadAsync(settings, new ReleasePayloadLocator(Locator), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.AuthenticationFailed, result.Status);
        Assert.Equal(
            "The release's download link needs the Torznab feed's API key, and none is saved for the feed. Enter its API key.",
            result.Error);
        Assert.Empty(_harness.TorznabHttp.Requests);
    }

    [Fact]
    public async Task FetchPayload_of_a_link_without_the_placeholder_sends_it_as_listed()
    {
        //Arrange
        const string locator = "http://prowlarr.test:9696/1/download?link=Q2ZESjhQcm90%2BZWN0ZWQ%3D;apikey=%7Bother%7D";
        _harness.TorznabHttp.Handler = (_, _) => Torrent(TorznabSamples.TorrentFile());

        //Act
        var result = await Search.FetchPayloadAsync(Settings with { ApiKey = null }, new ReleasePayloadLocator(locator), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(locator, Assert.Single(_harness.TorznabHttp.Requests).Uri.AbsoluteUri);
    }

    [Theory]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.MovedPermanently)]
    [InlineData(HttpStatusCode.TemporaryRedirect)]
    public async Task FetchPayload_redirected_to_a_magnet_hands_the_magnet_over(HttpStatusCode status)
    {
        //Arrange
        // Prowlarr and Jackett redirect a download to the magnet for an indexer that gives magnets only.
        const string magnet = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=Interstellar%20(2014)";
        _harness.TorznabHttp.Handler = (_, _) => VendorResponses.Redirect(magnet, status);

        //Act
        var result = await Search.FetchPayloadAsync(Settings, new ReleasePayloadLocator(Locator), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(ReleasePayloadKind.Magnet, result.Value!.Kind);
        Assert.Equal(magnet, result.Value.MagnetUri);
        Assert.Single(_harness.TorznabHttp.Requests);
    }

    [Fact]
    public async Task FetchPayload_redirected_to_a_magnet_keeps_it_as_sent()
    {
        //Arrange
        // As the network hands it over, raw: once parsed, the header's own view of it reads %20.
        const string magnet = "Magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=Interstellar (2014)";
        _harness.TorznabHttp.Handler = (_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.TryAddWithoutValidation("Location", magnet);
            return response;
        };

        //Act
        var result = await Search.FetchPayloadAsync(Settings, new ReleasePayloadLocator(Locator), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(magnet, result.Value!.MagnetUri);
    }

    [Fact]
    public async Task FetchPayload_redirected_to_an_address_that_does_not_parse_names_only_its_scheme()
    {
        //Arrange
        // Only the raw header holds it: the typed view can't parse a host with a space.
        _harness.TorznabHttp.Handler = (_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.TryAddWithoutValidation("Location", "http://tracker test/dl/planted-passkey-0001");
            return response;
        };

        //Act
        var result = await Search.FetchPayloadAsync(Settings, new ReleasePayloadLocator(Locator), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(
            "The Torznab feed sends this download on to a “http” link instead of handing it over, and the app doesn't follow it.",
            result.Error);
    }

    [Fact]
    public async Task FetchPayload_redirected_to_the_tracker_refuses_and_names_only_its_host()
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) =>
            VendorResponses.Redirect("https://tracker.test/dl/planted-passkey-0001/x.torrent?passkey=planted-passkey-0002");

        //Act
        var result = await Search.FetchPayloadAsync(Settings, new ReleasePayloadLocator(Locator), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(
            "The Torznab feed sends this download on to https://tracker.test instead of handing it over, and the app doesn't follow it. Switch off the feed's option to redirect downloads to the tracker.",
            result.Error);
        // Not followed: the key would go along.
        Assert.Single(_harness.TorznabHttp.Requests);
    }

    [Theory]
    [InlineData("/1/download?link=another")]
    [InlineData("http://prowlarr.test:9696/login?returnUrl=%2F1%2Fdownload")]
    public async Task FetchPayload_redirected_within_the_feed_refuses_too(string location)
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => VendorResponses.Redirect(location);

        //Act
        var result = await Search.FetchPayloadAsync(Settings, new ReleasePayloadLocator(Locator), TestContext.Current.CancellationToken);

        //Assert
        // ADR 0007 rejects following a redirect to the same host for a request that carries the key.
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(
            "The Torznab feed sends this download on to another address of its own at http://prowlarr.test:9696 instead of handing it over, and the app doesn't follow redirects.",
            result.Error);
        Assert.Single(_harness.TorznabHttp.Requests);
    }

    [Theory]
    [InlineData("ftp://tracker.test/x.torrent", "The Torznab feed sends this download on to a “ftp” link instead of handing it over, and the app doesn't follow it.")]
    [InlineData("bc://bitcoin.test/pay", "The Torznab feed sends this download on to a “bc” link instead of handing it over, and the app doesn't follow it.")]
    // A magnet without a BitTorrent hash is no payload.
    [InlineData("magnet:?dn=Interstellar", "The Torznab feed sends this download on to a “magnet” link instead of handing it over, and the app doesn't follow it.")]
    public async Task FetchPayload_redirected_elsewhere_names_only_the_scheme(string location, string expectedMessage)
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => VendorResponses.Redirect(location);

        //Act
        var result = await Search.FetchPayloadAsync(Settings, new ReleasePayloadLocator(Locator), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(expectedMessage, result.Error);
        Assert.Single(_harness.TorznabHttp.Requests);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task FetchPayload_redirected_without_one_target_says_so(int locations)
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            for (var index = 0; index < locations; index++)
                response.Headers.TryAddWithoutValidation("Location", $"magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef0123456{index}");
            return response;
        };

        //Act
        var result = await Search.FetchPayloadAsync(Settings, new ReleasePayloadLocator(Locator), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(RedirectWithoutTargetError, result.Error);
    }

    [Theory]
    // Prowlarr: a release its indexer no longer has, or an indexer switched off in Prowlarr.
    [InlineData(410, "410", "Release not available", IntegrationCallStatus.NotFound,
        "The Torznab feed can't hand over this release any more (error 410: Release not available). Search again.")]
    [InlineData(410, "900", "Indexer disabled", IntegrationCallStatus.NotFound,
        "The Torznab feed can't hand over this release any more (error 900: Indexer disabled). Search again.")]
    // Prowlarr: a grab limit, or the indexer backing off.
    [InlineData(429, "429", "Grab limit reached", IntegrationCallStatus.Refused,
        "The Torznab feed is refusing requests for now (error 429: Grab limit reached). Try again in a few minutes.")]
    // Prowlarr: anything else, with its exception's message.
    [InlineData(500, "500", "Unable to fetch the torrent", IntegrationCallStatus.ServiceError,
        "The Torznab feed failed to hand over the release (error 500: Unable to fetch the torrent).")]
    [InlineData(401, "100", "Invalid API Key", IntegrationCallStatus.AuthenticationFailed,
        "The Torznab feed refused the API key (error 100: Invalid API Key).")]
    [InlineData(400, "201", "Incorrect parameter", IntegrationCallStatus.UnexpectedResponse,
        "The Torznab feed didn't understand the download request (error 201: Incorrect parameter).")]
    // The body speaks first, whatever the status: a redirect included.
    [InlineData(302, "500", "Tracker is down", IntegrationCallStatus.ServiceError,
        "The Torznab feed failed to hand over the release (error 500: Tracker is down).")]
    [InlineData(500, "none", "Not a code", IntegrationCallStatus.UnexpectedResponse, NotATorrentError)]
    public async Task FetchPayload_maps_each_error_the_feed_answers_a_download_with(
        int statusCode, string code, string description, IntegrationCallStatus expectedStatus, string expectedMessage)
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) =>
            Xml($"""<?xml version="1.0" encoding="UTF-8"?><error code="{code}" description="{description}" />""", (HttpStatusCode)statusCode);

        //Act
        var result = await Search.FetchPayloadAsync(Settings, new ReleasePayloadLocator(Locator), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedMessage, result.Error);
        Assert.Null(result.Value);
    }

    [Theory]
    // Jackett answers every failed download with a bare 404.
    [InlineData(404, IntegrationCallStatus.NotFound,
        "The Torznab feed couldn't hand over this release (404 Not Found): it may be gone, or the feed failed to fetch it; its own log says which.")]
    [InlineData(410, IntegrationCallStatus.NotFound, "The Torznab feed can't hand over this release any more (410 Gone). Search again.")]
    [InlineData(401, IntegrationCallStatus.AuthenticationFailed, "The Torznab feed refused the API key (401 Unauthorized).")]
    [InlineData(403, IntegrationCallStatus.AuthenticationFailed, "The Torznab feed refused the API key (403 Forbidden).")]
    [InlineData(429, IntegrationCallStatus.Refused, "The Torznab feed is refusing requests for now (429 Too Many Requests). Try again in a few minutes.")]
    [InlineData(503, IntegrationCallStatus.ServiceError, "Torznab answered 503 Service Unavailable. Its own log says why.")]
    [InlineData(204, IntegrationCallStatus.UnexpectedResponse, "Torznab answered 204 No Content, which it doesn't send for this request. Check the address.")]
    public async Task FetchPayload_answers_each_bare_http_failure_with_its_sentence(int statusCode, IntegrationCallStatus expectedStatus, string expectedMessage)
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => new HttpResponseMessage((HttpStatusCode)statusCode);

        //Act
        var result = await Search.FetchPayloadAsync(Settings, new ReleasePayloadLocator(Locator), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedMessage, result.Error);
    }

    [Theory]
    [InlineData("<!DOCTYPE html><html><head><title>Login</title></head><body></body></html>")]
    [InlineData("""{"error":"no"}""")]
    [InlineData("")]
    [InlineData("de")]
    [InlineData("d4:infodee junk")]
    [InlineData("l4:infoe")]
    [InlineData("<caps />")]
    public async Task FetchPayload_of_something_other_than_a_torrent_is_an_unexpected_response(string body)
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => Torrent(Encoding.UTF8.GetBytes(body));

        //Act
        var result = await Search.FetchPayloadAsync(Settings, new ReleasePayloadLocator(Locator), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(NotATorrentError, result.Error);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n \t")]
    public async Task FetchPayload_takes_a_torrent_with_trailing_whitespace_as_sent(string trailing)
    {
        //Arrange
        // Prowlarr passes the tracker's bytes through as they are.
        byte[] body = [.. TorznabSamples.TorrentFile(), .. Encoding.ASCII.GetBytes(trailing)];
        _harness.TorznabHttp.Handler = (_, _) => Torrent(body);

        //Act
        var result = await Search.FetchPayloadAsync(Settings, new ReleasePayloadLocator(Locator), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(body, result.Value!.TorrentFile);
    }

    [Fact]
    public async Task FetchPayload_masks_every_part_of_the_link_an_error_echoes()
    {
        //Arrange
        // Prowlarr's 500 carries its exception's message, which can quote the decoded link= token, the
        // request it answered, or the key.
        _harness.TorznabHttp.Handler = (request, _) => Xml(
            $"""<error code="500" description="Token {LinkToken} (Q2ZESjhQcm90%2BZWN0ZWQ%3D) of {Escape(request.RequestUri!.PathAndQuery)} with {ApiKey} failed" />""",
            HttpStatusCode.InternalServerError);

        //Act
        var result = await Search.FetchPayloadAsync(Settings, new ReleasePayloadLocator(Locator), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.ServiceError, result.Status);
        // Each query value, as sent and decoded, and the long path segment ("download") within the
        // feed's own words — but not in the app's sentence around them.
        Assert.Equal(
            "The Torznab feed failed to hand over the release (error 500: Token *** (***) of /1/***?apikey=***&link=***&file=*** with *** failed).",
            result.Error);
    }

    [Fact]
    public async Task FetchPayload_keeps_its_own_words_when_the_links_path_is_one_of_them()
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => VendorResponses.Text("<html>Login</html>", HttpStatusCode.OK, "text/html");

        //Act
        var result = await Search.FetchPayloadAsync(Settings, new ReleasePayloadLocator(Locator), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(NotATorrentError, result.Error);
    }

    [Fact]
    public async Task FetchPayload_of_an_answer_nested_too_deep_is_refused_in_moments()
    {
        //Arrange
        const int levels = 40_000;
        var nested = string.Concat(Enumerable.Repeat("<a>", levels)) + string.Concat(Enumerable.Repeat("</a>", levels));
        _harness.TorznabHttp.Handler = (_, _) => Xml($"<error code=\"500\" description=\"x\">{nested}</error>", HttpStatusCode.InternalServerError);
        var started = TimeProvider.System.GetTimestamp();

        //Act
        var result = await Search.FetchPayloadAsync(Settings, new ReleasePayloadLocator(Locator), TestContext.Current.CancellationToken);

        //Assert
        // Not read as the feed's <error>: answered by its status alone.
        Assert.Equal(IntegrationCallStatus.ServiceError, result.Status);
        Assert.Equal("Torznab answered 500 Internal Server Error. Its own log says why.", result.Error);
        Assert.True(TimeProvider.System.GetElapsedTime(started) < TimeSpan.FromSeconds(5), "Reading a deeply nested answer took seconds.");
    }

    [Fact]
    public async Task FetchPayload_of_a_link_whose_host_has_no_ascii_form_refuses_without_sending_anything()
    {
        //Arrange
        // A zero-width joiner in the host: Uri takes it, but its ASCII form can't be made.
        var locator = new ReleasePayloadLocator("http://prowlarr‍.test:9696/1/download?link=x");

        //Act
        var result = await Search.FetchPayloadAsync(Settings, locator, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(ElsewhereError, result.Error);
        Assert.Empty(_harness.TorznabHttp.Requests);
    }

    [Fact]
    public async Task FetchPayload_of_an_unreachable_feed_answers_the_transport_sentence()
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => throw new HttpRequestException(HttpRequestError.ConnectionError, $"Connection refused {Locator}");

        //Act
        var result = await Search.FetchPayloadAsync(Settings, new ReleasePayloadLocator(Locator), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Unreachable, result.Status);
        Assert.Equal(
            "Couldn't reach Torznab at http://prowlarr.test:9696/1/api: nothing accepted the connection. Check the address and port, and that Torznab is running.",
            result.Error);
    }

    [Fact]
    public async Task FetchPayload_of_a_huge_answer_is_an_unexpected_response()
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => Torrent(new byte[IntegrationsInfrastructureExtensions.MaximumResponseBytes + 1]);

        //Act
        var result = await Search.FetchPayloadAsync(Settings, new ReleasePayloadLocator(Locator), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal("The answer from http://prowlarr.test:9696/1/api is far larger than anything Torznab sends. Check the address.", result.Error);
    }

    [Fact]
    public async Task FetchPayload_that_outlives_the_timeout_is_timed_out()
    {
        //Arrange
        using var harness = new IntegrationAdapterHarness(timeout: TimeSpan.FromMilliseconds(100));
        harness.TorznabHttp.Handler = (_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new Pipe().Reader.AsStream()) };

        //Act
        var result = await harness.Adapter<IReleaseSearchProvider>(TorznabAdapter.TypeId)
            .FetchPayloadAsync(Settings, new ReleasePayloadLocator(Locator), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.TimedOut, result.Status);
        Assert.Equal("Torznab at http://prowlarr.test:9696/1/api didn't answer within 0.1 seconds.", result.Error);
    }

    [Fact]
    public async Task FetchPayload_cancelled_by_the_caller_throws()
    {
        //Arrange
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        _harness.TorznabHttp.Handler = (_, _) =>
        {
            cancellation.Cancel();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new Pipe().Reader.AsStream()) };
        };

        //Act
        var exception = await Record.ExceptionAsync(() => Search.FetchPayloadAsync(Settings, new ReleasePayloadLocator(Locator), cancellation.Token));

        //Assert
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
    }

    [Fact]
    public async Task FetchPayload_of_prowlarrs_recorded_answer_to_a_wrong_key_says_the_key_was_refused()
    {
        //Arrange
        // Prowlarr's real answer to a wrong key: a bare 401, no body, not Torznab's <error code="100">.
        _harness.TorznabHttp.Handler = (_, _) => VendorResponses.Recorded(TorznabSamples.WrongKeyResponseFile);

        //Act
        var result = await Search.FetchPayloadAsync(Settings, new ReleasePayloadLocator(Locator), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.AuthenticationFailed, result.Status);
        Assert.Equal("The Torznab feed refused the API key (401 Unauthorized).", result.Error);
    }

    private static HttpResponseMessage Torrent(byte[] body)
    {
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-bittorrent");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static HttpResponseMessage Xml(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        VendorResponses.Text(body, status, "application/xml");

    private static string Escape(string value) => SecurityElement.Escape(value);
}
