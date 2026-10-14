using System.Globalization;
using System.Net;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Search;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Integrations.Torznab;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-10 — a feed's caps as the release search contract reads them: the real answer of Toloka through
// Prowlarr (VendorSamples/Torznab/prowlarr-toloka-caps.xml), hand-written variants for what it doesn't
// show, and every failure answered like the connection test (TorznabAdapterTests) answers it.
public sealed class TorznabAdapterCapabilitiesTests : IDisposable
{
    private const string BaseUrl = "http://prowlarr.test:9696/1/api";
    private const string ApiKey = "0f9e8d7c6b5a49382716a5b4c3d2e1f0";

    private const string NotTorznabError =
        "The service at this address answered, but not with Torznab capabilities. Check that the address is the feed's Torznab URL.";

    /// <summary>The caps sample's distinct category ids: 117 categories and 18 subcategories, 100137
    /// listed twice.</summary>
    private const int SampleCategoryCount = 134;

    private static readonly IntegrationConnectionSettings Settings = new(null, BaseUrl, null, null, ApiKey);

    private readonly IntegrationAdapterHarness _harness = new();

    private IReleaseSearchProvider Search => _harness.Adapter<IReleaseSearchProvider>(TorznabAdapter.TypeId);

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task GetCapabilities_reads_the_caps_sample()
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => VendorResponses.Sample(TorznabSamples.CapsFile, "application/xml; charset=utf-8");

        //Act
        var result = await Search.GetCapabilitiesAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        var caps = result.Value!;
        // Toloka's movie search takes only q (ADR 0004): no identifier, no year.
        Assert.True(caps.HasTextSearch);
        Assert.True(caps.HasMovieSearch);
        Assert.True(caps.MovieSearchTakesText);
        Assert.Empty(caps.MovieSearchIdentifierSchemes);
        Assert.False(caps.MovieSearchTakesYear);
        Assert.Equal(100, caps.ResultLimit);
        Assert.Equal(SampleCategoryCount, caps.Categories.Count);
        Assert.Equal(new ReleaseCategory("2000", "Movies", null, true), caps.Categories[0]);
        Assert.Equal(new ReleaseCategory("2040", "Movies/HD", "2000", true), caps.Categories[2]);
        Assert.Equal(new ReleaseCategory("3020", "Audio/Video", "3000", false), caps.Categories[5]);
        Assert.Contains(new ReleaseCategory("100096", "|-Фільми в HD", null, false, IsSourceSpecific: true), caps.Categories);
        // Torznab's Movies range: a gateway's own ids say nothing the core could rely on. MAM-33 — and TV/Anime
        // and TV/Documentary, where Toloka files films beside series (ADR 0020).
        Assert.Equal(
            ["2000", "2020", "2040", "2070", "5070", "5080"],
            caps.Categories.Where(category => category.HoldsMovies).Select(category => category.Id));
        // MAM-32 — and its TV range, where Prowlarr files Toloka's series and cartoon series.
        Assert.Equal(["5000", "5040", "5050", "5060", "5070", "5080"], caps.Categories.Where(category => category.HoldsTv).Select(category => category.Id));
        Assert.Equal(["5070", "5080"], caps.Categories.Where(category => category is { HoldsMovies: true, HoldsTv: true }).Select(category => category.Id));
        Assert.Equal(new ReleaseCategory("5070", "TV/Anime", "5000", true, HoldsTv: true), Assert.Single(caps.Categories, category => category.Id == "5070"));
        Assert.Equal(new ReleaseCategory("5080", "TV/Documentary", "5000", true, HoldsTv: true), Assert.Single(caps.Categories, category => category.Id == "5080"));
        Assert.Contains(new ReleaseCategory("100125", "|-Мультсеріали", null, false, IsSourceSpecific: true), caps.Categories);
        Assert.All(caps.Categories, category => Assert.Equal(int.Parse(category.Id, CultureInfo.InvariantCulture) >= 100000, category.IsSourceSpecific));
        // Toloka's TV search takes q, season and ep: no show's id.
        Assert.NotNull(caps.TvSearch);
        Assert.True(caps.TvSearch.TakesText);
        Assert.True(caps.TvSearch.TakesSeason);
        Assert.Empty(caps.TvSearch.IdentifierSchemes);
        // The real caps list 100137 twice; the first entry is kept.
        Assert.Single(caps.Categories, category => category.Id == "100137");
        Assert.Equal(caps.Categories.Count, caps.Categories.Select(category => category.Id).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task GetCapabilities_asks_for_caps_exactly_as_the_connection_test_does()
    {
        //Arrange
        var settings = Settings with { ApiKey = "key+/=&?%~!12" };
        _harness.TorznabHttp.Handler = (_, _) => VendorResponses.Sample(TorznabSamples.CapsFile, "application/xml");

        //Act
        await Search.GetCapabilitiesAsync(settings, TestContext.Current.CancellationToken);
        await _harness.Adapter(TorznabAdapter.TypeId).TestConnectionAsync(settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(2, _harness.TorznabHttp.Requests.Count);
        var call = _harness.TorznabHttp.Requests[0];
        var test = _harness.TorznabHttp.Requests[1];
        Assert.Equal(HttpMethod.Get, call.Method);
        Assert.Equal("http://prowlarr.test:9696/1/api?t=caps&apikey=key%2B%2F%3D%26%3F%25~%2112", call.Uri.AbsoluteUri);
        Assert.Equal(test.Uri, call.Uri);
        Assert.Equal(["application/xml", "text/xml"], call.HeaderValues("Accept"));
    }

    [Theory]
    [InlineData("q,imdbid,tmdbid,year", true, new[] { MediaIdentifierScheme.Imdb, MediaIdentifierScheme.Tmdb }, true)]
    // Any order, any letter case, with spaces and parameters this app doesn't use.
    [InlineData(" YEAR , genre,TmdbId, Q ", true, new[] { MediaIdentifierScheme.Tmdb }, true)]
    [InlineData("imdbid", false, new[] { MediaIdentifierScheme.Imdb }, false)]
    // No parameter named, or the attribute missing: Newznab's default, q alone.
    [InlineData("", true, new MediaIdentifierScheme[0], false)]
    [InlineData(null, true, new MediaIdentifierScheme[0], false)]
    [InlineData("genre", false, new MediaIdentifierScheme[0], false)]
    public async Task GetCapabilities_reads_what_the_movie_search_takes(
        string? supportedParams, bool expectedText, MediaIdentifierScheme[] expectedSchemes, bool expectedYear)
    {
        //Arrange
        var parameters = supportedParams is null ? "" : $" supportedParams=\"{supportedParams}\"";
        _harness.TorznabHttp.Handler = (_, _) => Xml(
            $"""
             <caps>
               <searching>
                 <search available="no" supportedParams="q" />
                 <movie-search available=" Yes "{parameters} />
               </searching>
             </caps>
             """);

        //Act
        var result = await Search.GetCapabilitiesAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        var caps = result.Value!;
        Assert.False(caps.HasTextSearch);
        Assert.True(caps.HasMovieSearch);
        Assert.Equal(expectedText, caps.MovieSearchTakesText);
        Assert.Equal(expectedSchemes, caps.MovieSearchIdentifierSchemes);
        Assert.Equal(expectedYear, caps.MovieSearchTakesYear);
    }

    [Theory]
    [InlineData("""<caps><searching><movie-search available="no" supportedParams="q,imdbid,year" /></searching></caps>""")]
    [InlineData("""<caps><searching><search available="yes" supportedParams="q" /></searching></caps>""")]
    [InlineData("<caps />")]
    public async Task GetCapabilities_without_movie_search_says_it_takes_nothing(string body)
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => Xml(body);

        //Act
        var result = await Search.GetCapabilitiesAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        var caps = result.Value!;
        Assert.False(caps.HasMovieSearch);
        Assert.False(caps.MovieSearchTakesText);
        Assert.Empty(caps.MovieSearchIdentifierSchemes);
        Assert.False(caps.MovieSearchTakesYear);
        Assert.Null(caps.ResultLimit);
        Assert.Empty(caps.Categories);
    }

    [Theory]
    [InlineData("""<limits max="50" default="20" />""", 50)]
    [InlineData("""<limits default="20" />""", 20)]
    [InlineData("""<limits max=" 75 " />""", 75)]
    // A limit that isn't a positive whole number says nothing; the default stands in.
    [InlineData("""<limits max="lots" default="20" />""", 20)]
    [InlineData("""<limits max="0" default="-5" />""", null)]
    [InlineData("""<limits max="1e3" default="+10" />""", null)]
    [InlineData("""<limits max="99999999999" />""", null)]
    [InlineData("<limits />", null)]
    public async Task GetCapabilities_reads_the_result_limit(string limits, int? expectedLimit)
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => Xml($"<caps>{limits}</caps>");

        //Act
        var result = await Search.GetCapabilitiesAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(expectedLimit, result.Value!.ResultLimit);
    }

    [Fact]
    public async Task GetCapabilities_keeps_only_categories_with_numeric_ids_and_names_each()
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => Xml(
            """
            <caps>
              <categories>
                <category id="2000" name="Movies">
                  <subcat id="2045" name="Movies/UHD" />
                  <subcat id="x2050" name="Not a number" />
                  <subcat id=" 2060 " name="  " />
                </category>
                <category id="movies" name="Named, not numbered">
                  <subcat id="2080" name="Under a parent that can't be listed" />
                </category>
                <category id="1234567890" name="Ten digits" />
                <category id="-3000" name="Signed" />
                <category name="No id" />
                <category id="5000" name="TV&#x202E;&#10;HD" />
                <category id="2000" name="Movies again" />
              </categories>
            </caps>
            """);

        //Act
        var result = await Search.GetCapabilitiesAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(
            [
                new ReleaseCategory("2000", "Movies", null, true),
                new ReleaseCategory("2045", "Movies/UHD", "2000", true),
                // A blank name is the id.
                new ReleaseCategory("2060", "2060", "2000", true),
                // Cleaned like any remote text: no bidi override, no line break. MAM-32: TV's range.
                new ReleaseCategory("5000", "TV HD", null, false, HoldsTv: true),
            ],
            result.Value!.Categories);
    }

    [Fact]
    public async Task GetCapabilities_masks_an_api_key_a_category_name_echoes()
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => Xml($"""<caps><categories><category id="2000" name="Movies {ApiKey}" /></categories></caps>""");

        //Act
        var result = await Search.GetCapabilitiesAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal("Movies ***", Assert.Single(result.Value!.Categories).Name);
    }

    [Theory]
    [InlineData("100", 401, IntegrationCallStatus.AuthenticationFailed, "The Torznab feed refused the API key (error 100: Incorrect user credentials). Copy the API key from the dashboard of the service that serves this feed, such as Jackett or Prowlarr, into this integration under Integrations.")]
    [InlineData("910", 200, IntegrationCallStatus.Refused, "The Torznab feed has its API switched off (error 910: Incorrect user credentials).")]
    [InlineData("429", 429, IntegrationCallStatus.Refused,
        "The Torznab feed is refusing requests for now (error 429: Incorrect user credentials). Try again in a few minutes.")]
    [InlineData("201", 400, IntegrationCallStatus.UnexpectedResponse,
        "The Torznab feed doesn't serve what this address names (error 201: Incorrect user credentials). Check the indexer in the feed's address, such as …/indexers/toloka/results/torznab/ for Jackett or …/1/api for Prowlarr.")]
    [InlineData("900", 500, IntegrationCallStatus.ServiceError, "The Torznab feed failed (error 900: Incorrect user credentials).")]
    // A 410 is a download's answer only; for caps it is the feed's failure.
    [InlineData("410", 410, IntegrationCallStatus.ServiceError, "The Torznab feed failed (error 410: Incorrect user credentials).")]
    [InlineData("x", 200, IntegrationCallStatus.UnexpectedResponse, NotTorznabError)]
    public async Task GetCapabilities_maps_each_torznab_error_code_like_the_connection_test(
        string code, int statusCode, IntegrationCallStatus expectedStatus, string expectedMessage)
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) =>
            Xml($"""<error code="{code}" description="Incorrect user credentials" />""", (HttpStatusCode)statusCode);

        //Act
        var result = await Search.GetCapabilitiesAsync(Settings, TestContext.Current.CancellationToken);
        var test = await _harness.Adapter(TorznabAdapter.TypeId).TestConnectionAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedMessage, result.Error);
        Assert.Null(result.Value);
        Assert.Equal((test.Status, test.Message), (result.Status, result.Error!));
    }

    [Theory]
    [InlineData(401, IntegrationCallStatus.AuthenticationFailed, "The Torznab feed refused the API key (401 Unauthorized). Copy the API key from the dashboard of the service that serves this feed, such as Jackett or Prowlarr, into this integration under Integrations.")]
    [InlineData(403, IntegrationCallStatus.AuthenticationFailed, "The Torznab feed refused the API key (403 Forbidden). Copy the API key from the dashboard of the service that serves this feed, such as Jackett or Prowlarr, into this integration under Integrations.")]
    [InlineData(404, IntegrationCallStatus.UnexpectedResponse,
        "Nothing answers Torznab at this address (404 Not Found). Use the feed's Torznab address: Prowlarr's ends in /api, and Jackett's Copy Torznab Feed gives one ending in /results/torznab/.")]
    [InlineData(429, IntegrationCallStatus.Refused,
        "The Torznab feed is refusing requests for now (429 Too Many Requests). Try again in a few minutes.")]
    [InlineData(503, IntegrationCallStatus.ServiceError, "Torznab answered 503 Service Unavailable. Its own log says why.")]
    [InlineData(200, IntegrationCallStatus.UnexpectedResponse, NotTorznabError)]
    public async Task GetCapabilities_of_an_html_answer_says_why_with_its_status(int statusCode, IntegrationCallStatus expectedStatus, string expectedMessage)
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) =>
            VendorResponses.Text("<!DOCTYPE html><html><body>Error</body></html>", (HttpStatusCode)statusCode, "text/html");

        //Act
        var result = await Search.GetCapabilitiesAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedMessage, result.Error);
    }

    [Fact]
    public async Task GetCapabilities_of_the_search_sample_is_an_unexpected_response()
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => VendorResponses.Sample(TorznabSamples.SearchFile, "application/rss+xml");

        //Act
        var result = await Search.GetCapabilitiesAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(NotTorznabError, result.Error);
    }

    [Fact]
    public async Task GetCapabilities_of_an_unreachable_feed_answers_the_transport_sentence()
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => throw new HttpRequestException(HttpRequestError.ConnectionError);

        //Act
        var result = await Search.GetCapabilitiesAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Unreachable, result.Status);
        Assert.Equal(
            "Couldn't reach Torznab at http://prowlarr.test:9696/1/api: nothing accepted the connection. Check the address and port, and that Torznab is running.",
            result.Error);
    }

    [Fact]
    public async Task GetCapabilities_of_prowlarrs_recorded_answer_to_a_wrong_key_says_the_key_was_refused()
    {
        //Arrange
        // Prowlarr's real answer to a wrong key: a bare 401, no body, not Torznab's <error code="100">.
        _harness.TorznabHttp.Handler = (_, _) => VendorResponses.Recorded(TorznabSamples.WrongKeyResponseFile);

        //Act
        var result = await Search.GetCapabilitiesAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.AuthenticationFailed, result.Status);
        Assert.Equal("The Torznab feed refused the API key (401 Unauthorized). Copy the API key from the dashboard of the service that serves this feed, such as Jackett or Prowlarr, into this integration under Integrations.", result.Error);
    }

    private static HttpResponseMessage Xml(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        VendorResponses.Text(body, status, "application/xml");
}
