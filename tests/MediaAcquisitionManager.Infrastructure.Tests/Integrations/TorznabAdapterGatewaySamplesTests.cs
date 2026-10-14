using System.Net;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Search;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Search;
using MediaAcquisitionManager.Infrastructure.Integrations.Torznab;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-30 — Jackett instead of Prowlarr, through the same adapter (Vasyl's decision 5): the real answers of Toloka through
// both gateways (VendorSamples/Torznab, MAM-6 and MAM-29), read alike — the same releases, kinds, descriptions and
// categories, every download link kept without the key and on the feed's own origin, every page link Toloka's topic.
// What only Jackett answers (its refusals, its links at the host's root) is TorznabAdapterJackettTests'.
public sealed class TorznabAdapterGatewaySamplesTests : IDisposable
{
    private const string Prowlarr = "Prowlarr";
    private const string Jackett = "Jackett";

    private const string ProwlarrAddress = "http://prowlarr.test:9696/1/api";

    /// <summary>The address Jackett's Copy Torznab Feed gives, its trailing slash included.</summary>
    private const string JackettAddress = "http://jackett.test:9117/api/v2.0/indexers/toloka/results/torznab/";

    private const string ApiKey = "0f9e8d7c6b5a49382716a5b4c3d2e1f0";

    /// <summary>The caps samples' movie categories with TV/Anime and TV/Documentary (ADR 0020), as a movie search asks.</summary>
    private static readonly string[] MovieCategories = ["2000", "2020", "2040", "2070", "5070", "5080"];

    /// <summary>The caps samples' TV categories, as a TV search asks.</summary>
    private static readonly string[] TvCategories = ["5000", "5040", "5050", "5060", "5070", "5080"];

    private readonly IntegrationAdapterHarness _harness = new();

    private IReleaseSearchProvider Search => _harness.Adapter<IReleaseSearchProvider>(TorznabAdapter.TypeId);

    public static TheoryData<string> Gateways => new() { Prowlarr, Jackett };

    public void Dispose() => _harness.Dispose();

    [Theory]
    [MemberData(nameof(Gateways))]
    public async Task GetCapabilities_reads_the_gateways_caps_as_the_same_search_modes_and_categories(string gateway)
    {
        //Arrange
        var settings = SettingsOf(gateway);
        _harness.TorznabHttp.Handler = (_, _) => Xml(gateway == Jackett ? TorznabSamples.JackettCaps() : TorznabSamples.Caps());

        //Act
        var result = await Search.GetCapabilitiesAsync(settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        var caps = result.Value!;
        // The same search modes, so the planning sends either gateway the same t=, q= and cat=.
        Assert.True(caps is { HasTextSearch: true, HasMovieSearch: true, MovieSearchTakesText: true, MovieSearchTakesYear: false });
        Assert.Empty(caps.MovieSearchIdentifierSchemes);
        Assert.Equal(100, caps.ResultLimit);
        Assert.True(caps.TvSearch is { TakesText: true, TakesSeason: true, IdentifierSchemes.Count: 0 });
        Assert.Equal(MovieCategories, caps.Categories.Where(category => category.HoldsMovies).Select(category => category.Id));
        Assert.Equal(TvCategories, caps.Categories.Where(category => category.HoldsTv).Select(category => category.Id));
        // Toloka's «Мультсеріали» sections keep the |- mark of a subsection on the same ids (ADR 0020's names).
        Assert.Equal(
            ["100125 |-Мультсеріали", "100044 |-Мультсеріали", "100195 |-Мультсеріали", "100174 |-Мультсеріали в HD"],
            caps.Categories.Where(category => category.Name.StartsWith("|-Мультсеріали", StringComparison.Ordinal)).Select(category => $"{category.Id} {category.Name}"));
    }

    [Fact]
    public async Task GetCapabilities_reads_every_category_of_prowlarrs_caps_from_jacketts_alike()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _harness.TorznabHttp.Handler = (request, _) =>
            Xml(request.RequestUri!.Host == "jackett.test" ? TorznabSamples.JackettCaps() : TorznabSamples.Caps());

        //Act
        var prowlarr = await Search.GetCapabilitiesAsync(SettingsOf(Prowlarr), cancellationToken);
        var jackett = await Search.GetCapabilitiesAsync(SettingsOf(Jackett), cancellationToken);

        //Assert
        // Every one with the same id, name, parent and kind (VendorSamples README), and 20 more in Jackett's, 8000
        // "Other" among them, which no search asks.
        Assert.Empty(prowlarr.Value!.Categories.Except(jackett.Value!.Categories));
        Assert.Equal(20, jackett.Value.Categories.Count - prowlarr.Value.Categories.Count);
        Assert.Contains(new ReleaseCategory("8000", "Other", null, false), jackett.Value.Categories);
    }

    [Theory]
    [MemberData(nameof(Gateways))]
    public async Task Search_of_the_gateways_movie_sample_in_the_movie_categories_keeps_its_films_and_leaves_the_rest_out(string gateway)
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => RssXml(gateway == Jackett
            ? TorznabSamples.JackettMovieSearch(JackettAddress, ApiKey)
            : TorznabSamples.MovieSearch(ProwlarrAddress, ApiKey));

        //Act
        var result = await Search.SearchAsync(SettingsOf(gateway), Query(ReleaseSearchMode.Movie, MovieCategories), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        var hits = result.Value!;
        // Prowlarr's 17 less the audio release; Jackett's 18 less it and the trailer in 8000.
        Assert.Equal(
            gateway == Jackett
                ? TorznabSamples.JackettItemCount - TorznabSamples.JackettNonMovieItemCount
                : TorznabSamples.ItemCount - TorznabSamples.NonMovieItemCount,
            hits.Count);
        Assert.DoesNotContain(hits, hit => hit.CategoryIds.Contains("3020") || hit.CategoryIds.Contains("8000"));
        Assert.All(hits, hit => Assert.Equal(ReleaseKind.Movie, hit.Kind));
        // Toloka's own topic title, Ukrainian title first, which both gateways' titles cut to the English part.
        Assert.All(hits, hit => Assert.StartsWith("Інтерстеллар / Interstellar (2014)", hit.Description, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Search_reads_the_same_releases_from_both_gateways_movie_samples()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _harness.TorznabHttp.Handler = (request, _) => RssXml(request.RequestUri!.Host == "jackett.test"
            ? TorznabSamples.JackettMovieSearch(JackettAddress, ApiKey)
            : TorznabSamples.MovieSearch(ProwlarrAddress, ApiKey));
        var query = Query(ReleaseSearchMode.Movie, MovieCategories);

        //Act
        var prowlarr = await Search.SearchAsync(SettingsOf(Prowlarr), query, cancellationToken);
        var jackett = await Search.SearchAsync(SettingsOf(Jackett), query, cancellationToken);

        //Assert
        // One release is one id, title, description, kind and category list through either gateway (VendorSamples
        // README, "Jackett's Toloka answers read like Prowlarr's").
        Assert.Equal(
            prowlarr.Value!.Select(hit => (hit.SourceItemId, hit.SourcePageUrl, hit.Title, hit.Description, hit.Kind, string.Join(',', hit.CategoryIds))),
            jackett.Value!.Select(hit => (hit.SourceItemId, hit.SourcePageUrl, hit.Title, hit.Description, hit.Kind, string.Join(',', hit.CategoryIds))));
    }

    [Theory]
    [MemberData(nameof(Gateways))]
    public async Task Search_keeps_each_download_link_on_the_feeds_origin_without_the_key_and_each_page_link_tolokas(string gateway)
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => RssXml(gateway == Jackett
            ? TorznabSamples.JackettMovieSearch(JackettAddress, ApiKey)
            : TorznabSamples.MovieSearch(ProwlarrAddress, ApiKey));

        //Act
        var result = await Search.SearchAsync(SettingsOf(gateway), Query(ReleaseSearchMode.Movie, []), TestContext.Current.CancellationToken);

        //Assert
        var hits = result.Value!;
        Assert.Equal(gateway == Jackett ? TorznabSamples.JackettItemCount : TorznabSamples.ItemCount, hits.Count);
        // Prowlarr's links below its indexer, Jackett's at its host's root; the key's place kept by its placeholder.
        var linkStart = gateway == Jackett
            ? "http://jackett.test:9117/dl/toloka/?jackett_apikey=%7Bapi-key%7D&path=planted-jackett-path-0001&file=Interstellar+(2014)"
            : "http://prowlarr.test:9696/1/download?apikey=%7Bapi-key%7D&link=REDACTED&file=Interstellar+(2014)";
        Assert.All(hits, hit =>
        {
            Assert.StartsWith(linkStart, hit.Locator.Value, StringComparison.Ordinal);
            Assert.DoesNotContain(ApiKey, hit.Locator.Value, StringComparison.Ordinal);
            Assert.Null(hit.PayloadKind);
            Assert.StartsWith("https://toloka.to/t", hit.SourcePageUrl, StringComparison.Ordinal);
            Assert.Equal(hit.SourcePageUrl, hit.SourceItemId);
        });
    }

    [Theory]
    [MemberData(nameof(Gateways))]
    public async Task Search_of_the_gateways_tv_sample_in_the_tv_categories_reads_the_seasons_as_tv_and_the_documentaries_as_films(string gateway)
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => RssXml(gateway == Jackett
            ? TorznabSamples.JackettTvSearch(JackettAddress, ApiKey)
            : TorznabSamples.TvSearch(ProwlarrAddress, ApiKey));

        //Act
        var result = await Search.SearchAsync(SettingsOf(gateway), Query(ReleaseSearchMode.Tv, TvCategories), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        var hits = result.Value!;
        // Jackett's 12 less the subtitles and the two archive sections: Prowlarr's 9.
        Assert.Equal(
            gateway == Jackett ? TorznabSamples.JackettTvItemCount - TorznabSamples.JackettNonTvItemCount : TorznabSamples.TvItemCount,
            hits.Count);
        // ADR 0020 — the films of TV/Documentary that name no season ("Wild Yellowstone She Wolf (2013) HDTV").
        Assert.Equal(TorznabSamples.TvFilmItemCount, hits.Count(hit => hit.Kind == ReleaseKind.Movie));
        Assert.Equal("Yellowstone (S1-5) (2018-2024) WEB-DL-AVC Ukr/Eng | Sub Eng", hits[0].Title);
        Assert.Equal("Єллоустоун (Сезони 1-5) / Yellowstone (Seasons 1-5) (2018-2024) WEB-DLRip-AVC Ukr/Eng | Sub Eng", hits[0].Description);
        Assert.Equal(ReleaseKind.Tv, hits[0].Kind);
    }

    [Theory]
    [MemberData(nameof(Gateways))]
    public async Task Search_of_the_gateways_season_sample_in_the_tv_categories_leaves_the_subtitles_out(string gateway)
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => RssXml(gateway == Jackett
            ? TorznabSamples.JackettSearch(TorznabSamples.JackettTvSeasonSearchFile, JackettAddress, ApiKey)
            : TorznabSamples.TvSeasonSearch(ProwlarrAddress, ApiKey));

        //Act
        var result = await Search.SearchAsync(
            SettingsOf(gateway), Query(ReleaseSearchMode.Tv, TvCategories) with { Season = 1 }, TestContext.Current.CancellationToken);

        //Assert
        // Toloka barely narrows by the season through either gateway; Jackett's one more is the subtitles (3020).
        Assert.Equal(gateway == Jackett ? TorznabSamples.JackettTvSeasonItemCount - 1 : TorznabSamples.TvSeasonItemCount, result.Value!.Count);
        Assert.DoesNotContain(result.Value, hit => hit.CategoryIds.Contains("3020"));
    }

    [Theory]
    [MemberData(nameof(Gateways))]
    public async Task Search_of_the_gateways_cartoon_sample_reads_every_season_as_tv_with_its_ukrainian_title_in_the_description(string gateway)
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => RssXml(gateway == Jackett
            ? TorznabSamples.JackettSearch(TorznabSamples.JackettTvCartoonSearchFile, JackettAddress, ApiKey)
            : TorznabSamples.TvCartoonSearch(ProwlarrAddress, ApiKey));

        //Act
        var result = await Search.SearchAsync(SettingsOf(gateway), Query(ReleaseSearchMode.Tv, TvCategories), TestContext.Current.CancellationToken);

        //Assert
        var hits = result.Value!;
        Assert.Equal(gateway == Jackett ? TorznabSamples.JackettTvCartoonItemCount : TorznabSamples.TvCartoonItemCount, hits.Count);
        Assert.All(hits, hit => Assert.Equal(ReleaseKind.Tv, hit.Kind));
        Assert.All(hits, hit => Assert.StartsWith("Клуб Міккі Мауса (Сезон", hit.Description, StringComparison.Ordinal));
        // Two of Toloka's «Мультсеріали» sections, which keep the destination a cartoon is offered (ADR 0020).
        Assert.Equal(["100044", "100174"], hits.SelectMany(hit => hit.CategoryIds).Where(id => id.Length == 6).Distinct().Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Gateways))]
    public async Task Search_of_the_gateways_film_in_tv_documentary_reads_all_five_as_the_film(string gateway)
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => RssXml(gateway == Jackett
            ? TorznabSamples.JackettSearch(TorznabSamples.JackettFilmInTvCategorySearchFile, JackettAddress, ApiKey)
            : TorznabSamples.FilmInTvCategorySearch(ProwlarrAddress, ApiKey));

        //Act
        var result = await Search.SearchAsync(SettingsOf(gateway), Query(ReleaseSearchMode.Movie, MovieCategories), TestContext.Current.CancellationToken);

        //Assert
        var hits = result.Value!;
        Assert.Equal(TorznabSamples.FilmInTvCategoryItemCount, hits.Count);
        Assert.Equal(TorznabSamples.FilmInTvCategoryDocumentaryItemCount, hits.Count(hit => hit.CategoryIds.SequenceEqual(["5080", "100140"])));
        Assert.All(hits, hit => Assert.Equal(ReleaseKind.Movie, hit.Kind));
        Assert.All(hits, hit => Assert.StartsWith("Шумахер 1994: Народження легенди / Schumacher '94", hit.Description, StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Gateways))]
    public async Task Search_of_the_gateways_newest_movies_reads_them_newest_first_each_as_a_film_with_its_description(string gateway)
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => RssXml(gateway == Jackett
            ? TorznabSamples.JackettLatestMovies(JackettAddress, ApiKey)
            : TorznabSamples.LatestMovies(ProwlarrAddress, ApiKey));

        //Act
        var result = await Search.SearchAsync(SettingsOf(gateway), Latest(MovieCategories), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        var hits = result.Value!;
        // Prowlarr's file is trimmed to 25 of its 90; Jackett's is kept whole.
        Assert.Equal(gateway == Jackett ? TorznabSamples.JackettLatestItemCount : TorznabSamples.LatestMoviesItemCount, hits.Count);
        Assert.Equal(hits.Select(hit => hit.PublishedUtc).OrderDescending(), hits.Select(hit => hit.PublishedUtc));
        Assert.All(hits, hit => Assert.False(string.IsNullOrEmpty(hit.Description)));
        Assert.All(hits, hit => Assert.Equal(ReleaseKind.Movie, hit.Kind));
    }

    [Theory]
    [MemberData(nameof(Gateways))]
    public async Task Search_of_the_gateways_newest_tv_reads_as_tv_all_but_what_tv_anime_and_documentary_file_without_a_season(string gateway)
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => RssXml(gateway == Jackett
            ? TorznabSamples.JackettLatestTv(JackettAddress, ApiKey)
            : TorznabSamples.LatestTv(ProwlarrAddress, ApiKey));

        //Act
        var result = await Search.SearchAsync(SettingsOf(gateway), Latest(TvCategories), TestContext.Current.CancellationToken);

        //Assert
        var hits = result.Value!;
        Assert.Equal(gateway == Jackett ? TorznabSamples.JackettLatestItemCount : TorznabSamples.LatestTvItemCount, hits.Count);
        // ADR 0020 — TV by a TV category other than TV/Anime and TV/Documentary, and in those two by the seasons or
        // episodes the title or the description names: a film there ("Akira (1988)") stays a film.
        Assert.All(hits, hit => Assert.Equal(
            hit.CategoryIds.Any(id => id is not ("5070" or "5080") && id.StartsWith('5') && id.Length == 4)
            || ReleaseWorkClaims.ClaimsSeasonsOrEpisodes(hit.Title)
            || hit.Description is { } description && ReleaseWorkClaims.ClaimsSeasonsOrEpisodes(description)
                ? ReleaseKind.Tv
                : ReleaseKind.Movie,
            hit.Kind));
        Assert.Contains(hits, hit => hit.Kind == ReleaseKind.Tv && hit.CategoryIds.Contains("5070"));
    }

    [Theory]
    [MemberData(nameof(Gateways))]
    public async Task FetchPayload_of_a_release_the_gateway_listed_fetches_it_from_its_download_link_with_the_key_put_back(string gateway)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var feed = gateway == Jackett ? FakeTorznabServer.Jackett(JackettAddress, ApiKey) : new FakeTorznabServer(ProwlarrAddress, ApiKey);
        feed.Arm(_harness.TorznabHttp);
        var settings = SettingsOf(gateway);
        var search = await Search.SearchAsync(settings, Query(ReleaseSearchMode.Movie, MovieCategories), cancellationToken);
        var locator = search.Value![0].Locator;

        //Act
        var result = await Search.FetchPayloadAsync(settings, locator, cancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(ReleasePayloadKind.TorrentFile, result.Value!.Kind);
        Assert.Equal(feed.TorrentFile, result.Value.TorrentFile);
        var download = _harness.TorznabHttp.Requests[^1];
        Assert.StartsWith(
            gateway == Jackett
                ? $"http://jackett.test:9117/dl/toloka/?jackett_apikey={ApiKey}&path=planted-jackett-path-0001&file=Interstellar"
                : $"http://prowlarr.test:9696/1/download?apikey={ApiKey}&link=REDACTED&file=Interstellar",
            download.Uri.AbsoluteUri,
            StringComparison.Ordinal);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static IntegrationConnectionSettings SettingsOf(string gateway) =>
        new(null, gateway == Jackett ? JackettAddress : ProwlarrAddress, null, null, ApiKey);

    private static ReleaseSearchQuery Query(ReleaseSearchMode mode, IReadOnlyList<string> categories) =>
        new(mode, mode == ReleaseSearchMode.Tv ? "Yellowstone" : "Interstellar", null, [], categories, ReleaseSearchPlanning.MaximumResults);

    private static ReleaseSearchQuery Latest(IReadOnlyList<string> categories) =>
        new(ReleaseSearchMode.Latest, null, null, [], categories, ReleaseSearchPlanning.MaximumResults);

    private static HttpResponseMessage Xml(string body) => VendorResponses.Text(body, HttpStatusCode.OK, "application/xml");

    private static HttpResponseMessage RssXml(string body) => VendorResponses.Text(body, HttpStatusCode.OK, "application/rss+xml");
}
