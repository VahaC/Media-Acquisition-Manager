using System.Net;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Infrastructure.Integrations.Torznab;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Texts;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

public sealed class TorznabAdapterTests : IDisposable
{
    // Prowlarr's per-indexer feed, the reference installation's shape (ADR 0004).
    private const string BaseUrl = "http://prowlarr.test:9696/1/api";
    private const string ApiKey = "0f9e8d7c6b5a49382716a5b4c3d2e1f0";

    private const string NotTorznabError =
        "The service at this address answered, but not with Torznab capabilities. Check that the address is the feed's Torznab URL.";

    private static readonly IntegrationConnectionSettings Settings = new(null, BaseUrl, null, null, ApiKey);

    private readonly IntegrationAdapterHarness _harness = new();

    // A file of this test's own for the external-entity test to point at; deleted after every test.
    private readonly string _entityFile = Path.Combine(Path.GetTempPath(), $"mam-torznab-entity-{Guid.NewGuid():N}.xml");

    private IIntegrationAdapter Adapter => _harness.Adapter(TorznabAdapter.TypeId);

    public void Dispose()
    {
        _harness.Dispose();
        File.Delete(_entityFile);
    }

    [Fact]
    public async Task TestConnection_reads_the_caps_sample_as_movie_search_by_title_and_tv_search_by_title_season_and_episode()
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => VendorResponses.Sample("Torznab/prowlarr-toloka-caps.xml", "application/xml; charset=utf-8");

        //Act
        var result = await Adapter.TestConnectionAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        // Toloka's movie search takes only q, its TV search q, season and ep (VendorSamples README, MAM-32).
        Assert.Equal("Connected to Prowlarr over Torznab; movie search by title, TV search by title, season and episode.", TestTexts.En(result.Message));
    }

    [Theory]
    [InlineData("q,tvdbid,imdbid,tmdbid,season,ep", "title, TVDB id, IMDb id, TMDb id, season and episode")]
    [InlineData("", "title")]
    public async Task TestConnection_names_the_parameters_of_the_tv_search(string parameters, string expectedParameters)
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => Xml(
            $"""<caps><server title="Jackett" /><searching><movie-search available="yes" supportedParams="q" /><tv-search available="yes" supportedParams="{parameters}" /></searching></caps>""");

        //Act
        var result = await Adapter.TestConnectionAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal($"Connected to Jackett over Torznab; movie search by title, TV search by {expectedParameters}.", TestTexts.En(result.Message));
    }

    [Fact]
    public async Task TestConnection_with_a_tv_search_and_no_movie_search_says_both()
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => Xml(
            """<caps><searching><movie-search available="no" /><tv-search available="yes" supportedParams="q,season" /></searching></caps>""");

        //Act
        var result = await Adapter.TestConnectionAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal("Connected to the feed over Torznab, but it offers no movie search; TV search by title and season.", TestTexts.En(result.Message));
    }

    [Theory]
    [InlineData("q,imdbid,tmdbid,year", "title, IMDb id, TMDb id and year")]
    [InlineData("year, genre ,q", "title and year")]
    [InlineData("imdbid", "IMDb id")]
    [InlineData("Q,IMDBID", "title and IMDb id")]
    [InlineData("genre", "title")]
    [InlineData("", "title")]
    public async Task TestConnection_names_every_movie_search_parameter_it_knows(string supportedParams, string expectedParameters)
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => Xml(
            $"""
             <caps>
               <server title="Jackett" />
               <searching>
                 <search available="yes" supportedParams="q" />
                 <movie-search available="yes" supportedParams="{supportedParams}" />
               </searching>
             </caps>
             """);

        //Act
        var result = await Adapter.TestConnectionAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal($"Connected to Jackett over Torznab; movie search by {expectedParameters}.", TestTexts.En(result.Message));
    }

    [Theory]
    [InlineData("""<caps><searching><movie-search available="no" supportedParams="q,imdbid" /></searching></caps>""")]
    [InlineData("""<caps><searching><search available="yes" supportedParams="q" /></searching></caps>""")]
    [InlineData("""<caps><server title="  " /></caps>""")]
    public async Task TestConnection_without_movie_or_tv_search_says_so(string caps)
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => Xml(caps);

        //Act
        var result = await Adapter.TestConnectionAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        // Still a working feed: the owner learns what it can't do before a search finds nothing.
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal("Connected to the feed over Torznab, but it offers no movie or TV search.", TestTexts.En(result.Message));
    }

    [Theory]
    // MAM-47 — each shape is a sentence of its own (the server the caps name or "the feed", times what it searches by),
    // so each is pinned: a typo in one would change English with no other test failing.
    [InlineData(
        """<caps><server title="Jackett" /></caps>""",
        "Connected to Jackett over Torznab, but it offers no movie or TV search.")]
    [InlineData(
        """<caps><server title="Jackett" /><searching><tv-search available="yes" supportedParams="q,season" /></searching></caps>""",
        "Connected to Jackett over Torznab, but it offers no movie search; TV search by title and season.")]
    [InlineData(
        """<caps><searching><movie-search available="yes" supportedParams="q,year" /></searching></caps>""",
        "Connected to the feed over Torznab; movie search by title and year.")]
    [InlineData(
        """<caps><searching><movie-search available="yes" supportedParams="q" /><tv-search available="yes" supportedParams="q,ep" /></searching></caps>""",
        "Connected to the feed over Torznab; movie search by title, TV search by title and episode.")]
    public async Task TestConnection_names_the_server_or_the_feed_with_each_search_it_offers(string caps, string expected)
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => Xml(caps);

        //Act
        var result = await Adapter.TestConnectionAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(expected, TestTexts.En(result.Message));
    }

    [Fact]
    public async Task TestConnection_asks_for_caps_with_the_escaped_api_key()
    {
        //Arrange
        var settings = Settings with { ApiKey = "key+/=&?%~!12" };
        _harness.TorznabHttp.Handler = (_, _) => VendorResponses.Sample("Torznab/prowlarr-toloka-caps.xml", "application/xml");

        //Act
        await Adapter.TestConnectionAsync(settings, TestContext.Current.CancellationToken);

        //Assert
        var request = Assert.Single(_harness.TorznabHttp.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/1/api", request.Uri.AbsolutePath);
        // Every character that means something in a query is escaped, so the key can't end early or
        // add a parameter of its own.
        Assert.Equal("?t=caps&apikey=key%2B%2F%3D%26%3F%25~%2112", request.Uri.Query);
        Assert.Equal(["application/xml", "text/xml"], request.HeaderValues("Accept"));
    }

    [Theory]
    // Prowlarr's per-indexer feed, in any letter case: used as it is.
    [InlineData("http://prowlarr.test:9696/1/api", "http://prowlarr.test:9696/1/api")]
    [InlineData("http://prowlarr.test:9696/1/API", "http://prowlarr.test:9696/1/API")]
    // Anything else gets /api: a Prowlarr indexer without it, and Jackett's copied Torznab address.
    [InlineData("http://prowlarr.test:9696/1", "http://prowlarr.test:9696/1/api")]
    [InlineData("http://jackett.test:9117/api/v2.0/indexers/x/results/torznab", "http://jackett.test:9117/api/v2.0/indexers/x/results/torznab/api")]
    // MAM-30 — as Jackett's Copy Torznab Feed gives it, with its trailing slash.
    [InlineData("http://jackett.test:9117/api/v2.0/indexers/toloka/results/torznab/", "http://jackett.test:9117/api/v2.0/indexers/toloka/results/torznab/api")]
    // A host called "api" is not a path segment called "api".
    [InlineData("http://api", "http://api/api")]
    [InlineData("http://feed.test/rapi", "http://feed.test/rapi/api")]
    public void EndpointFor_uses_an_address_ending_in_api_and_appends_api_otherwise(string baseUrl, string expectedEndpoint)
    {
        //Arrange

        //Act
        var endpoint = TorznabAdapter.EndpointFor(baseUrl);

        //Assert
        Assert.Equal(expectedEndpoint, endpoint);
    }

    [Theory]
    // A feed reports its failures in the body, with whatever HTTP status it likes.
    [InlineData("100", "Incorrect user credentials", 401, IntegrationCallStatus.AuthenticationFailed,
        "The Torznab feed refused the API key (error 100: Incorrect user credentials). Copy the API key from the dashboard of the service that serves this feed, such as Jackett or Prowlarr, into this integration under Integrations.")]
    [InlineData("101", "Account suspended", 200, IntegrationCallStatus.AuthenticationFailed,
        "The Torznab feed refused the API key (error 101: Account suspended). Copy the API key from the dashboard of the service that serves this feed, such as Jackett or Prowlarr, into this integration under Integrations.")]
    [InlineData("102", "Insufficient privileges/not authorized", 403, IntegrationCallStatus.AuthenticationFailed,
        "The Torznab feed refused the API key (error 102: Insufficient privileges/not authorized). Copy the API key from the dashboard of the service that serves this feed, such as Jackett or Prowlarr, into this integration under Integrations.")]
    [InlineData("910", "API disabled", 200, IntegrationCallStatus.Refused,
        "The Torznab feed has its API switched off (error 910: API disabled).")]
    // Prowlarr's answer for an indexer it paused after failures (ADR 0004): HTTP 429 and error 429.
    [InlineData("429", "Indexer is disabled till 29/09/2026 14:05:00 due to recent failures.", 429, IntegrationCallStatus.Refused,
        "The Torznab feed is refusing requests for now (error 429: Indexer is disabled till 29/09/2026 14:05:00 due to recent failures.). Try again in a few minutes.")]
    // Either one is enough: the code without the HTTP status, and the HTTP status whatever the code.
    [InlineData("429", "Request limit reached", 200, IntegrationCallStatus.Refused,
        "The Torznab feed is refusing requests for now (error 429: Request limit reached). Try again in a few minutes.")]
    [InlineData("100", "Incorrect user credentials", 429, IntegrationCallStatus.Refused,
        "The Torznab feed is refusing requests for now (error 100: Incorrect user credentials). Try again in a few minutes.")]
    [InlineData("201", "Incorrect parameter", 400, IntegrationCallStatus.UnexpectedResponse,
        "The Torznab feed doesn't serve what this address names (error 201: Incorrect parameter). Check the indexer in the feed's address, such as …/indexers/toloka/results/torznab/ for Jackett or …/1/api for Prowlarr.")]
    [InlineData(" 900 ", "Unknown error", 500, IntegrationCallStatus.ServiceError,
        "The Torznab feed failed (error 900: Unknown error).")]
    [InlineData("100", "", 200, IntegrationCallStatus.AuthenticationFailed,
        "The Torznab feed refused the API key (error 100). Copy the API key from the dashboard of the service that serves this feed, such as Jackett or Prowlarr, into this integration under Integrations.")]
    [InlineData("x", "Not a number", 200, IntegrationCallStatus.UnexpectedResponse, NotTorznabError)]
    [InlineData("-100", "Signed", 200, IntegrationCallStatus.UnexpectedResponse, NotTorznabError)]
    public async Task TestConnection_maps_each_torznab_error_code(
        string code, string description, int statusCode, IntegrationCallStatus expectedStatus, string expectedMessage)
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) =>
            Xml($"""<?xml version="1.0" encoding="UTF-8"?><error code="{code}" description="{description}" />""", (HttpStatusCode)statusCode);

        //Act
        var result = await Adapter.TestConnectionAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedMessage, TestTexts.En(result.Message));
    }

    [Fact]
    public async Task TestConnection_masks_an_api_key_the_error_description_echoes()
    {
        //Arrange
        // The key twice: on its own, and inside the request URI the feed repeats (XML-escaped).
        _harness.TorznabHttp.Handler = (request, _) => Xml(
            $"""<error code="100" description="Invalid API key {ApiKey} for {request.RequestUri!.AbsoluteUri.Replace("&", "&amp;", StringComparison.Ordinal)}" />""",
            HttpStatusCode.Unauthorized);

        //Act
        var result = await Adapter.TestConnectionAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.AuthenticationFailed, result.Status);
        // MAM-10 — the echoed address is cut to its scheme, host and port before anything else.
        Assert.Equal(
            "The Torznab feed refused the API key (error 100: Invalid API key *** for http://prowlarr.test:9696). Copy the API key from the dashboard of the service that serves this feed, such as Jackett or Prowlarr, into this integration under Integrations.",
            TestTexts.En(result.Message));
    }

    [Theory]
    [InlineData(0)]
    // The address starts 180 characters in, so the 200-character cap falls inside it as written.
    [InlineData(180)]
    public async Task TestConnection_cuts_every_address_the_error_description_quotes_to_its_host(int padding)
    {
        //Arrange
        // MAM-10 — Jackett's error 900 quotes the tracker's URL, passkey and all; the app doesn't hold
        // the passkey, so masking can't find it (VendorSamples README, "Cut to the host").
        const string trackerUrl = "https://tracker.test/rss/planted-passkey-0001/x?passkey=planted-passkey-0002&amp;sid=planted-sid-0001";
        var description = $"{new string('d', padding)} Request to {trackerUrl} failed (magnet:?xt=urn:btih:planted-passkey-0003&amp;tr=x)";
        _harness.TorznabHttp.Handler = (_, _) => Xml($"""<error code="900" description="{description}" />""");

        //Act
        var result = await Adapter.TestConnectionAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.ServiceError, result.Status);
        Assert.All(
            ["planted-passkey-0001", "planted-passkey-0002", "planted-sid-0001", "planted-passkey-0003", "/rss", "passkey="],
            planted => Assert.DoesNotContain(planted, TestTexts.En(result.Message), StringComparison.Ordinal));
        Assert.StartsWith($"The Torznab feed failed (error 900: {new string('d', padding)}", TestTexts.En(result.Message), StringComparison.Ordinal);
    }

    [Theory]
    // A .NET exception prints a URL decoded: the space before "(2014)" ends the address, and the query
    // reads as words after it.
    [InlineData("https://tracker.test/download/123/Interstellar (2014) 1080p.torrent?torrent_pass=planted-passkey-0001")]
    [InlineData("https://tracker.test/download/123/Interstellar(2014).torrent?torrent_pass=planted-passkey-0001")]
    [InlineData("http://[2001:db8::1]:8080/rss/planted-passkey-0001/x.torrent?passkey=planted-passkey-0002")]
    [InlineData("magnet:?xt=urn:btih:0123456789abcdef&amp;dn=Interstellar+(2014)&amp;tr=https%3A%2F%2Ftracker.test%2Fannounce%2Fplanted-passkey-0001")]
    [InlineData("the tracker refused passkey%3Dplanted-passkey-0001 and sid=planted-sid-0001")]
    [InlineData("Request failed: [GET] at [https://tracker.test/x?passkey=planted-passkey-0001]")]
    public async Task TestConnection_masks_a_credential_the_error_description_quotes_wherever_its_address_ends(string quoted)
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => Xml($"""<error code="900" description="Exception (tracker): {quoted}" />""");

        //Act
        var result = await Adapter.TestConnectionAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.ServiceError, result.Status);
        Assert.All(
            ["planted-passkey-0001", "planted-passkey-0002", "planted-sid-0001", "/rss", "/download"],
            planted => Assert.DoesNotContain(planted, TestTexts.En(result.Message), StringComparison.Ordinal));
    }

    [Theory]
    // RFC 3986 allows parentheses, brackets and an apostrophe in a password.
    [InlineData("https://planted-user-0001:pa(ss-planted-0001@tracker.test/x", "https://tracker.test")]
    [InlineData("https://planted-user-0001:se[cret-planted-0001]@tracker.test/x", "https://tracker.test")]
    [InlineData("https://planted-user-0001:pa'ss-planted-0001@tracker.test/x", "https://tracker.test")]
    // An address quoted inside another, percent-encoded.
    [InlineData("go to url=https%3A%2F%2Ftracker.test%2Fplanted-passkey-0001%2Fx", "go to url=***")]
    // A magnet whose name holds a space: the trackers after it are masked by name.
    [InlineData("magnet:?xt=urn:btih:0123456789abcdef&amp;dn=Interstellar (2014)&amp;tr=https%3A%2F%2Ftracker.test%2Fplanted-passkey-0001", "magnet: (2014)&tr=***")]
    public async Task TestConnection_shows_no_more_of_a_quoted_address_than_its_host(string quoted, string expectedShown)
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => Xml($"""<error code="900" description="{quoted}" />""");

        //Act
        var result = await Adapter.TestConnectionAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal($"The Torznab feed failed (error 900: {expectedShown}).", TestTexts.En(result.Message));
        Assert.DoesNotContain("planted-0001", TestTexts.En(result.Message), StringComparison.Ordinal);
        Assert.DoesNotContain("planted-passkey", TestTexts.En(result.Message), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TestConnection_keeps_a_bracketed_ipv6_host_of_a_quoted_address()
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) =>
            Xml("""<error code="900" description="Request to http://[2001:db8::1]:8080/rss/planted-passkey-0001/x failed" />""");

        //Act
        var result = await Adapter.TestConnectionAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal("The Torznab feed failed (error 900: Request to http://[2001:db8::1]:8080 failed).", TestTexts.En(result.Message));
    }

    [Fact(Timeout = 30_000)]
    public async Task TestConnection_reads_a_huge_error_description_in_moments()
    {
        //Arrange
        // MAM-10 — the addresses are cut out of the whole description before it is cut to length, so the
        // scan must stay linear: two million letters with no address took minutes with a scheme pattern
        // tried from every letter.
        _harness.TorznabHttp.Handler = (_, _) => Xml($"""<error code="900" description="{new string('a', 2_000_000)} https://tracker.test/x" />""");

        //Act
        var result = await Adapter.TestConnectionAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.ServiceError, result.Status);
        Assert.Equal($"The Torznab feed failed (error 900: {new string('a', 199)}…).", TestTexts.En(result.Message));
    }

    [Fact]
    public async Task TestConnection_keeps_the_host_of_a_quoted_address_and_drops_its_user_info()
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => Xml(
            """<error code="900" description="Could not reach https://user:planted-password-0001@tracker.test:8443/announce?uk=planted-uk-0001 (timeout)" />""");

        //Act
        var result = await Adapter.TestConnectionAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.ServiceError, result.Status);
        Assert.Equal("The Torznab feed failed (error 900: Could not reach https://tracker.test:8443 (timeout)).", TestTexts.En(result.Message));
    }

    [Theory]
    [InlineData("")]
    // A zero-width space splits the key, and the cleaning joins it back together.
    [InlineData("\u200B")]
    public async Task TestConnection_masks_a_key_the_error_description_echoes_across_its_length_cap(string split)
    {
        //Arrange
        // The key starts 190 characters in, so the 200-character cap falls inside it.
        var echoed = ApiKey[..16] + split + ApiKey[16..];
        _harness.TorznabHttp.Handler = (_, _) => Xml($"""<error code="900" description="{new string('d', 190)}{echoed}" />""");

        //Act
        var result = await Adapter.TestConnectionAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.ServiceError, result.Status);
        Assert.Equal($"The Torznab feed failed (error 900: {new string('d', 190)}***).", TestTexts.En(result.Message));
        // Not 8 characters of the key in a row anywhere, the shortest value masking knows as a secret.
        Assert.All(
            Enumerable.Range(0, ApiKey.Length - 7),
            start => Assert.DoesNotContain(ApiKey.Substring(start, 8), TestTexts.En(result.Message), StringComparison.Ordinal));
    }

    [Fact]
    public async Task TestConnection_caps_a_long_error_description()
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => Xml($"""<error code="900" description="{new string('d', 500)}" />""");

        //Act
        var result = await Adapter.TestConnectionAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.ServiceError, result.Status);
        Assert.Equal($"The Torznab feed failed (error 900: {new string('d', 199)}…).", TestTexts.En(result.Message));
    }

    [Theory]
    [InlineData(401, IntegrationCallStatus.AuthenticationFailed, "The Torznab feed refused the API key (401 Unauthorized). Copy the API key from the dashboard of the service that serves this feed, such as Jackett or Prowlarr, into this integration under Integrations.")]
    [InlineData(403, IntegrationCallStatus.AuthenticationFailed, "The Torznab feed refused the API key (403 Forbidden). Copy the API key from the dashboard of the service that serves this feed, such as Jackett or Prowlarr, into this integration under Integrations.")]
    [InlineData(404, IntegrationCallStatus.UnexpectedResponse,
        "Nothing answers Torznab at this address (404 Not Found). Use the feed's Torznab address: Prowlarr's ends in /api, and Jackett's Copy Torznab Feed gives one ending in /results/torznab/.")]
    // A bare 429 from something in front of the feed; Prowlarr's own carries an <error> (above).
    [InlineData(429, IntegrationCallStatus.Refused,
        "The Torznab feed is refusing requests for now (429 Too Many Requests). Try again in a few minutes.")]
    [InlineData(503, IntegrationCallStatus.ServiceError, "Torznab answered 503 Service Unavailable. Its own log says why.")]
    [InlineData(400, IntegrationCallStatus.UnexpectedResponse,
        "Torznab answered 400 Bad Request, which it doesn't send for this request. Check the address.")]
    public async Task TestConnection_answers_each_http_failure_with_its_status_and_sentence(
        int statusCode, IntegrationCallStatus expectedStatus, string expectedMessage)
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) =>
            VendorResponses.Text("<!DOCTYPE html><html><body>Error</body></html>", (HttpStatusCode)statusCode, "text/html");

        //Act
        var result = await Adapter.TestConnectionAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedMessage, TestTexts.En(result.Message));
    }

    [Fact]
    public async Task TestConnection_of_the_search_sample_instead_of_caps_is_an_unexpected_response()
    {
        //Arrange
        // Well-formed XML from the right kind of service, but an <rss> feed rather than <caps>.
        _harness.TorznabHttp.Handler = (_, _) => VendorResponses.Sample("Torznab/prowlarr-toloka-search.xml", "application/rss+xml; charset=utf-8");

        //Act
        var result = await Adapter.TestConnectionAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(NotTorznabError, TestTexts.En(result.Message));
    }

    [Theory]
    [InlineData("<!DOCTYPE html><html><head><title>Login</title></head><body></body></html>")]
    [InlineData("<html><body><p>Prowlarr</p></body></html>")]
    [InlineData("""{"caps":true}""")]
    [InlineData("")]
    public async Task TestConnection_of_html_is_an_unexpected_response(string body)
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => VendorResponses.Text(body, HttpStatusCode.OK, "text/html");

        //Act
        var result = await Adapter.TestConnectionAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(NotTorznabError, TestTexts.En(result.Message));
    }

    [Fact]
    public async Task TestConnection_reads_no_file_an_external_entity_points_to()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Were the entity read, the caps would offer movie search and the test would succeed; left
        // unread, with or without the DTD refused, they can't.
        await File.WriteAllTextAsync(_entityFile, """<movie-search available="yes" supportedParams="q" />""", cancellationToken);
        var caps = $"""
                    <?xml version="1.0"?>
                    <!DOCTYPE caps [<!ENTITY search SYSTEM "{new Uri(_entityFile).AbsoluteUri}">]>
                    <caps><searching>&search;</searching></caps>
                    """;
        _harness.TorznabHttp.Handler = (_, _) => Xml(caps);

        //Act
        var result = await Adapter.TestConnectionAsync(Settings, cancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(NotTorznabError, TestTexts.En(result.Message));
    }

    [Fact]
    public async Task TestConnection_expands_no_entity_a_dtd_declares()
    {
        //Arrange
        // Nested internal entities: expanded, ten levels of them take memory by the gigabyte. Three
        // levels already make a title, so a parser that expands them answers Succeeded here.
        const string caps = """
                            <?xml version="1.0"?>
                            <!DOCTYPE caps [<!ENTITY a "aaaaaaaaaa"><!ENTITY b "&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;"><!ENTITY c "&b;&b;&b;&b;&b;&b;&b;&b;&b;&b;">]>
                            <caps><server title="&c;" /></caps>
                            """;
        _harness.TorznabHttp.Handler = (_, _) => Xml(caps);

        //Act
        var result = await Adapter.TestConnectionAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(NotTorznabError, TestTexts.En(result.Message));
    }

    [Fact]
    public async Task TestConnection_reports_a_redirect_and_sends_one_request()
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) =>
            VendorResponses.Redirect($"http://prowlarr.test:9696/login?returnUrl=%2F1%2Fapi%3Fapikey%3D{ApiKey}");

        //Act
        var result = await Adapter.TestConnectionAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(
            "The address answered with a redirect to http://prowlarr.test:9696/login. Enter the address the service is really at: the app doesn't follow redirects, so your credentials never go anywhere else.",
            TestTexts.En(result.Message));
        Assert.Single(_harness.TorznabHttp.Requests);
    }

    [Fact]
    public async Task TestConnection_of_prowlarrs_recorded_answer_to_a_wrong_key_says_the_key_was_refused()
    {
        //Arrange
        // Prowlarr's real answer to a wrong key: a bare 401, no body, not Torznab's <error code="100">.
        _harness.TorznabHttp.Handler = (_, _) => VendorResponses.Recorded(TorznabSamples.WrongKeyResponseFile);

        //Act
        var result = await Adapter.TestConnectionAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.AuthenticationFailed, result.Status);
        Assert.Equal("The Torznab feed refused the API key (401 Unauthorized). Copy the API key from the dashboard of the service that serves this feed, such as Jackett or Prowlarr, into this integration under Integrations.", TestTexts.En(result.Message));
    }

    private static HttpResponseMessage Xml(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        VendorResponses.Text(body, status, "application/xml");
}
