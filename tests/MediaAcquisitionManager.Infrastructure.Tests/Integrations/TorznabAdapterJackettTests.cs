using System.Net;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Search;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Search;
using MediaAcquisitionManager.Infrastructure.Integrations.Torznab;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-30 — what only Jackett answers, through the same Torznab adapter (Vasyl's decision 5), from its recorded answers
// (VendorSamples/Torznab/jackett-*, MAM-29): the address its Copy Torznab Feed gives, its download links at its host's
// root with jackett_apikey= and a path= token, a wrong key refused with HTTP 200 and error 100 on caps and searches and a
// bare 401 on a download, a bad token a bare 404, an indexer it doesn't have error 201 — and the sentences that say
// how to fix each (D16). What both gateways answer alike is TorznabAdapterGatewaySamplesTests'.
public sealed class TorznabAdapterJackettTests : IDisposable
{
    /// <summary>The address Jackett's Copy Torznab Feed gives, its trailing slash included.</summary>
    private const string BaseUrl = "http://jackett.test:9117/api/v2.0/indexers/toloka/results/torznab/";

    private const string ApiKey = "0f9e8d7c6b5a49382716a5b4c3d2e1f0";

    private const string RefusedKeyError =
        "The Torznab feed refused the API key (error 100: Invalid API Key). Copy the API key from the dashboard of the service that serves this feed, such as Jackett or Prowlarr, into this integration under Integrations.";

    private const string RefusedDownloadKeyError =
        "The Torznab feed refused the API key (401 Unauthorized). Copy the API key from the dashboard of the service that serves this feed, such as Jackett or Prowlarr, into this integration under Integrations.";

    private const string DownloadNotFoundError =
        "The Torznab feed couldn't hand over this release (404 Not Found): it may be gone, or the feed failed to fetch it; its own log says which.";

    private static readonly IntegrationConnectionSettings Settings = new(null, BaseUrl, null, null, ApiKey);

    /// <summary>The caps sample's movie categories with TV/Anime and TV/Documentary, as a movie search asks.</summary>
    private static readonly string[] MovieCategories = ["2000", "2020", "2040", "2070", "5070", "5080"];

    /// <summary>The caps sample's TV categories, as a TV search asks.</summary>
    private static readonly string[] TvCategories = ["5000", "5040", "5050", "5060", "5070", "5080"];

    private readonly IntegrationAdapterHarness _harness = new();

    private IIntegrationAdapter Adapter => _harness.Adapter(TorznabAdapter.TypeId);

    private IReleaseSearchProvider Search => _harness.Adapter<IReleaseSearchProvider>(TorznabAdapter.TypeId);

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task TestConnection_reads_jacketts_caps_as_movie_search_by_title_and_tv_search_by_title_season_and_episode()
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => VendorResponses.Sample(TorznabSamples.JackettCapsFile, "application/xml; charset=utf-8");

        //Act
        var result = await Adapter.TestConnectionAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal("Connected to Jackett over Torznab; movie search by title, TV search by title, season and episode.", result.Message);
        // The copied address ends in /results/torznab/: the endpoint is that with /api (ADR 0004, VendorSamples README).
        var request = Assert.Single(_harness.TorznabHttp.Requests);
        Assert.Equal($"http://jackett.test:9117/api/v2.0/indexers/toloka/results/torznab/api?t=caps&apikey={ApiKey}", request.Uri.AbsoluteUri);
    }

    [Fact]
    public async Task TestConnection_of_jacketts_recorded_answer_to_a_wrong_key_says_the_key_was_refused_and_where_to_copy_it()
    {
        //Arrange
        // Jackett's real answer to a wrong apikey on caps and on a search alike: HTTP 200 and Torznab's error 100, not
        // Prowlarr's bare 401.
        _harness.TorznabHttp.Handler = (_, _) => VendorResponses.Recorded(TorznabSamples.JackettWrongKeyResponseFile);

        //Act
        var result = await Adapter.TestConnectionAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.AuthenticationFailed, result.Status);
        Assert.Equal(RefusedKeyError, result.Message);
    }

    [Fact]
    public async Task GetCapabilities_of_jacketts_recorded_answer_to_a_wrong_key_says_the_key_was_refused_and_where_to_copy_it()
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => VendorResponses.Recorded(TorznabSamples.JackettWrongKeyResponseFile);

        //Act
        var result = await Search.GetCapabilitiesAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.AuthenticationFailed, result.Status);
        Assert.Equal(RefusedKeyError, result.Error);
    }

    [Fact]
    public async Task Search_of_jacketts_recorded_answer_to_a_wrong_key_says_the_key_was_refused_and_where_to_copy_it()
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => VendorResponses.Recorded(TorznabSamples.JackettWrongKeyResponseFile);

        //Act
        var result = await Search.SearchAsync(Settings, Query(MovieCategories), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.AuthenticationFailed, result.Status);
        Assert.Equal(RefusedKeyError, result.Error);
        Assert.Null(result.Value);
    }

    [Theory]
    // Jackett's answers for an indexer id it doesn't serve, on its Torznab route always with HTTP 200 (its
    // ResultsController; not recorded): the caps request sends nothing but the key, so the address is what's wrong.
    [InlineData("Indexer is not supported")]
    [InlineData("Indexer is not configured")]
    public async Task TestConnection_of_an_indexer_jackett_does_not_serve_says_to_check_the_indexer_in_the_address(string description)
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) =>
            Xml($"""<?xml version="1.0" encoding="UTF-8"?><error code="201" description="{description}" />""");

        //Act
        var result = await Adapter.TestConnectionAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(
            $"The Torznab feed doesn't serve what this address names (error 201: {description}). Check the indexer in the feed's address, such as …/indexers/toloka/results/torznab/ for Jackett or …/1/api for Prowlarr.",
            result.Message);
    }

    [Fact]
    public async Task GetCapabilities_of_an_indexer_jackett_does_not_serve_says_it_as_the_connection_test_does()
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) =>
            Xml("""<?xml version="1.0" encoding="UTF-8"?><error code="201" description="Indexer is not supported" />""");

        //Act
        var result = await Search.GetCapabilitiesAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(
            "The Torznab feed doesn't serve what this address names (error 201: Indexer is not supported). Check the indexer in the feed's address, such as …/indexers/toloka/results/torznab/ for Jackett or …/1/api for Prowlarr.",
            result.Error);
    }

    [Fact]
    public async Task Search_with_an_error_201_keeps_its_own_words_about_the_search()
    {
        //Arrange
        // A search sends parameters of its own, so a 2xx error there can be about one of them: not the address.
        _harness.TorznabHttp.Handler = (_, _) =>
            Xml("""<?xml version="1.0" encoding="UTF-8"?><error code="201" description="Incorrect parameter: invalid imdbid format" />""", HttpStatusCode.BadRequest);

        //Act
        var result = await Search.SearchAsync(Settings, Query(MovieCategories), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal("The Torznab feed didn't understand the search (error 201: Incorrect parameter: invalid imdbid format).", result.Error);
    }

    [Fact]
    public async Task TestConnection_of_nothing_at_the_address_names_both_gateways_address_shapes()
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => VendorResponses.Recorded(TorznabSamples.JackettDownloadBadPathResponseFile);

        //Act
        var result = await Adapter.TestConnectionAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(
            "Nothing answers Torznab at this address (404 Not Found). Use the feed's Torznab address: Prowlarr's ends in /api, and Jackett's Copy Torznab Feed gives one ending in /results/torznab/.",
            result.Message);
    }

    [Fact]
    public async Task Search_of_jacketts_answer_keeps_each_download_link_at_its_hosts_root_with_its_token_and_without_the_key()
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => RssXml(TorznabSamples.JackettMovieSearch(BaseUrl, ApiKey));

        //Act
        var result = await Search.SearchAsync(Settings, Query(MovieCategories), TestContext.Current.CancellationToken);

        //Assert
        // Not under the feed's address, and still the feed's origin, which is all the fetch requires (ADR 0007).
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        var first = result.Value![0];
        Assert.Equal(
            "http://jackett.test:9117/dl/toloka/?jackett_apikey=%7Bapi-key%7D&path=planted-jackett-path-0001&file=Interstellar+(2014)+BDRemux+1080p+Ukr%2FEng+%7C+Sub+Eng+%5BIMAX+Edition%5D",
            first.Locator.Value);
        Assert.Equal("https://toloka.to/t699400", first.SourceItemId);
        Assert.Equal("https://toloka.to/t699400", first.SourcePageUrl);
        Assert.Equal(36711232962, first.SizeBytes);
        Assert.Equal(["2040", "100096"], first.CategoryIds);
    }

    [Fact]
    public async Task Search_of_jacketts_newest_movies_reads_a_ukrainian_only_film_by_its_punctuation_title_and_its_whole_description()
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => RssXml(TorznabSamples.JackettLatestMovies(BaseUrl, ApiKey));

        //Act
        var result = await Search.SearchAsync(Settings, Latest(MovieCategories), TestContext.Current.CancellationToken);

        //Assert
        // "Strip Cyrillic Letters", Jackett's default, left the title nothing but its quotes; the description keeps it
        // (decision 7). The Search page and Recommendations match the film by the description.
        var film = Assert.Single(result.Value!, hit => hit.SourceItemId == "https://toloka.to/t700794");
        Assert.Equal("« » (2026) WEB-DL-AVC Ukr | Sub Ukr/Eng", film.Title);
        Assert.Equal("Потяг «Червона Рута» (2026) WEB-DLRip-AVC Ukr | Sub Ukr/Eng", film.Description);
        Assert.Equal(ReleaseKind.Movie, film.Kind);
        Assert.Equal(["2000", "100042"], film.CategoryIds);
        Assert.Equal(
            "http://jackett.test:9117/dl/toloka/?jackett_apikey=%7Bapi-key%7D&path=planted-jackett-path-0001&file=%C2%AB+%C2%BB+(2026)+WEB-DL-AVC+Ukr+%7C+Sub+Ukr%2FEng",
            film.Locator.Value);
        Assert.Equal(2, result.Value!.Count(hit => hit.Title.StartsWith("« » (2026)", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Search_of_jacketts_newest_in_every_section_keeps_only_the_asked_categories()
    {
        //Arrange
        // Toloka's newest 90 of every section, books (7000) and software (4000) among them: MAM always asks
        // categories, and the adapter keeps only those (both gateways expand a parent so).
        _harness.TorznabHttp.Handler = (_, _) => RssXml(TorznabSamples.JackettSearch(TorznabSamples.JackettLatestFile, BaseUrl, ApiKey));
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var movies = await Search.SearchAsync(Settings, Latest(MovieCategories), cancellationToken);
        var tv = await Search.SearchAsync(Settings, Latest(TvCategories), cancellationToken);

        //Assert
        Assert.Equal(51, movies.Value!.Count);
        Assert.Equal(38, tv.Value!.Count);
        Assert.All(movies.Value.Concat(tv.Value), hit => Assert.DoesNotContain(hit.CategoryIds, id => id.StartsWith('4') && id.Length == 4 || id.StartsWith('7') && id.Length == 4));
        Assert.All(movies.Value, hit => Assert.Contains(hit.CategoryIds, id => id.StartsWith('2') && id.Length == 4 || id is "5070" or "5080"));
    }

    [Fact]
    public async Task Search_of_a_cartoon_series_by_its_ukrainian_title_keeps_its_seasons_and_leaves_the_book_and_the_scan_out()
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) =>
            RssXml(TorznabSamples.JackettSearch(TorznabSamples.JackettTvCartoonUkrainianTitleSearchFile, BaseUrl, ApiKey));

        //Act
        var result = await Search.SearchAsync(Settings, Query(TvCategories, ReleaseSearchMode.Tv, "Клуб Міккі Мауса"), TestContext.Current.CancellationToken);

        //Assert
        // The book (7000) and the scan (8000), whose titles kept nothing but "(2010) [djvu]", are no TV.
        var hits = result.Value!;
        Assert.Equal(TorznabSamples.JackettTvCartoonUkrainianTitleItemCount - 2, hits.Count);
        Assert.All(hits, hit => Assert.Equal(ReleaseKind.Tv, hit.Kind));
        Assert.All(hits, hit => Assert.StartsWith("Mickey Mouse Clubhouse (S", hit.Title, StringComparison.Ordinal));
        Assert.Equal(
            "Клуб Міккі Мауса (Сезон 1-4) / Mickey Mouse Clubhouse (Season 1-4) (2006-2016) WEB-DL 1080p/DVDRip-AVC Ukr/Eng | sub Ukr",
            hits[0].Description);
        // The description names the show's Ukrainian title, its seasons and its years: what the Search page matches a
        // library series titled only "Клуб Міккі Мауса" by.
        var claim = ReleaseWorkClaims.ReadTv(hits[0].Description!);
        Assert.Equal(["Клуб Міккі Мауса", "Mickey Mouse Clubhouse"], claim.Titles);
        Assert.Equal((2006, (int?)2016), (claim.Year!.Value, claim.LastYear));
        Assert.Equal((1, 4), (claim.Episodes!.FirstSeason, claim.Episodes.LastSeason));
    }

    [Fact]
    public async Task Search_of_a_ukrainian_only_show_jackett_found_nothing_for_lists_nothing()
    {
        //Arrange
        // An empty channel: the reason is upstream (VendorSamples README); the newest releases still list the show.
        _harness.TorznabHttp.Handler = (_, _) =>
            RssXml(TorznabSamples.JackettSearch(TorznabSamples.JackettTvUkrainianOnlySearchFile, BaseUrl, ApiKey));

        //Act
        var result = await Search.SearchAsync(
            Settings, Query(TvCategories, ReleaseSearchMode.Tv, "Розслідування з Андрієм Данілевичем"), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task Search_masks_a_download_token_a_search_error_quotes()
    {
        //Arrange
        // MAM-30 — Jackett's error 900 quotes an exception's text; a token quoted in it without its address is masked
        // by its name, as the key is, though it isn't a secret of the search (TorznabLinks.TextOnlyMaskedNames).
        _harness.TorznabHttp.Handler = (_, _) => Xml(
            """<error code="900" description="Download of /dl/toloka/?jackett_apikey=planted-key-0001&amp;path=planted-path-0001&amp;file=x failed" />""",
            HttpStatusCode.BadRequest);

        //Act
        var result = await Search.SearchAsync(Settings, Query(MovieCategories), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.ServiceError, result.Status);
        Assert.Equal("The Torznab feed failed (error 900: Download of /dl/toloka/?jackett_apikey=***&path=***&file=x failed).", result.Error);
    }

    [Fact]
    public async Task FetchPayload_of_jacketts_recorded_answer_to_a_wrong_key_says_the_key_was_refused_and_where_to_copy_it()
    {
        //Arrange
        // A download link refuses a wrong jackett_apikey with a bare 401, not error 100.
        _harness.TorznabHttp.Handler = (_, _) => VendorResponses.Recorded(TorznabSamples.JackettWrongKeyDownloadResponseFile);

        //Act
        var result = await Search.FetchPayloadAsync(Settings, Locator(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.AuthenticationFailed, result.Status);
        Assert.Equal(RefusedDownloadKeyError, result.Error);
    }

    [Fact]
    public async Task FetchPayload_of_jacketts_recorded_answer_to_a_bad_token_says_the_release_couldnt_be_handed_over()
    {
        //Arrange
        // Jackett answers every failed download with a bare 404, a token that doesn't unprotect included.
        _harness.TorznabHttp.Handler = (_, _) => VendorResponses.Recorded(TorznabSamples.JackettDownloadBadPathResponseFile);

        //Act
        var result = await Search.FetchPayloadAsync(Settings, Locator(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.NotFound, result.Status);
        Assert.Equal(DownloadNotFoundError, result.Error);
    }

    [Fact]
    public async Task FetchPayload_of_jacketts_recorded_download_hands_the_torrent_over()
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => FakeTorznabServer.JackettDownload(TorznabSamples.TorrentFile());

        //Act
        var result = await Search.FetchPayloadAsync(Settings, Locator(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(ReleasePayloadKind.TorrentFile, result.Value!.Kind);
        Assert.Equal(TorznabSamples.TorrentFile(), result.Value.TorrentFile);
        // At the host's root, the key put back in jackett_apikey, the token and the file name as listed.
        var request = Assert.Single(_harness.TorznabHttp.Requests);
        Assert.Equal(
            $"http://jackett.test:9117/dl/toloka/?jackett_apikey={ApiKey}&path=planted-jackett-path-0001&file=Interstellar%20%282014%29%20BDRip%201080p",
            request.Uri.AbsoluteUri);
    }

    [Fact]
    public async Task FetchPayload_masks_the_download_token_a_failed_download_echoes()
    {
        //Arrange
        _harness.TorznabHttp.Handler = (request, _) => Xml(
            $"""<error code="900" description="Token planted-jackett-path-0001 of {System.Security.SecurityElement.Escape(request.RequestUri!.Query)} failed" />""",
            HttpStatusCode.InternalServerError);

        //Act
        var result = await Search.FetchPayloadAsync(Settings, Locator(), TestContext.Current.CancellationToken);

        //Assert
        // The token and the key wherever the feed's text repeats them: a secret of this call, and named path=.
        Assert.Equal(IntegrationCallStatus.ServiceError, result.Status);
        Assert.Equal("The Torznab feed failed to hand over the release (error 900: Token *** of ?jackett_apikey=***&path=***&file=*** failed).", result.Error);
    }

    [Fact]
    public async Task FetchPayload_of_a_release_jackett_listed_through_the_fake_fetches_it_and_a_wrong_key_is_refused()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        FakeTorznabServer.Jackett(BaseUrl, ApiKey).Arm(_harness.TorznabHttp);
        var search = await Search.SearchAsync(Settings, Query(MovieCategories), cancellationToken);
        var locator = search.Value![0].Locator;

        //Act
        var withTheKey = await Search.FetchPayloadAsync(Settings, locator, cancellationToken);
        var withAnother = await Search.FetchPayloadAsync(Settings with { ApiKey = "a-key-jackett-refuses" }, locator, cancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, withTheKey.Status);
        Assert.Equal(TorznabSamples.TorrentFile(), withTheKey.Value!.TorrentFile);
        // The stored link holds no key, so a key changed under Integrations is the one the next fetch sends.
        Assert.Equal(IntegrationCallStatus.AuthenticationFailed, withAnother.Status);
        Assert.Equal(RefusedDownloadKeyError, withAnother.Error);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>A Jackett download link as the adapter stores it.</summary>
    private static ReleasePayloadLocator Locator() => new(TorznabSamples.JackettLocator(BaseUrl));

    private static ReleaseSearchQuery Query(IReadOnlyList<string> categories, ReleaseSearchMode mode = ReleaseSearchMode.Movie, string text = "Interstellar") =>
        new(mode, text, null, [], categories, ReleaseSearchPlanning.MaximumResults);

    private static ReleaseSearchQuery Latest(IReadOnlyList<string> categories) =>
        new(ReleaseSearchMode.Latest, null, null, [], categories, ReleaseSearchPlanning.MaximumResults);

    private static HttpResponseMessage Xml(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        VendorResponses.Text(body, status, "application/xml");

    private static HttpResponseMessage RssXml(string body) => VendorResponses.Text(body, HttpStatusCode.OK, "application/rss+xml");
}
