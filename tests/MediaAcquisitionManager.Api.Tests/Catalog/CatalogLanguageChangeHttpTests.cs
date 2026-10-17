using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.LibraryLinks;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Catalog;

// MAM-46, ADR 0025 — the catalog read again after the owner changes TMDb's language (PUT /api/settings/integrations):
// every cached row says which language it was read in, so the computation the change queues reads the details, the
// collections and the genre lists again in the new language — once, through the cache, after which they are fresh in it —
// and the recommendations show the new titles, a work only a release source's newest releases name included; the TMDb
// suggestions keep their searches but read their candidates' details again, so the TMDb dialog shows them in the new
// language, as it does after TMDb is removed and added again in another language — and a candidate kept with no details
// stored, as a run under way during the change leaves one, is read too. A newest release the old language matched to no
// work is resolved again in the new one. TMDb is faked at the network; each answer's title is the one it has in the
// language asked.
public sealed class CatalogLanguageChangeHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string IntegrationsUrl = "/api/settings/integrations";
    private const string RecommendationsUrl = "/api/recommendations";
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";
    private const string TolokaAddress = "http://prowlarr.test:9696/1/api";
    private const string TolokaApiKey = "planted-torznab-key-0001";
    private const string LandingId = "a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1";
    private const string TomAndJerryFolder = "/media/cartoons/Tom & Jerry e001-e161 (1940-1967)";
    private const int Action = 28;
    private const int LandingFilm = 223021;
    private const int LandingRemake = 5000;
    private const int TomAndJerryShow = 300;
    private const int SpiritedAway = 129;

    /// <summary>Each work's title by the language TMDb is asked in.</summary>
    private static readonly Dictionary<string, (string Ukrainian, string English)> Titles = new(StringComparer.Ordinal)
    {
        ["348"] = ("Чужий", "Alien"),
        ["8077"] = ("Чужий 3", "Alien³"),
        ["218"] = ("Термінатор", "The Terminator"),
        [$"{LandingFilm}"] = ("Посадка салаги", "Landing Stripling"),
        [$"{LandingRemake}"] = ("Посадка салаги (1999)", "Landing Stripling (1999)"),
        [$"{TomAndJerryShow}"] = ("Том і Джеррі", "Tom & Jerry"),
        [$"{SpiritedAway}"] = ("Віднесені привидами", "Spirited Away"),
    };

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private AuthorizedUser _owner = null!;
    private FakeJellyfinServer _jellyfin = null!;
    private FakeTmdbServer _tmdb = null!;

    public CatalogLanguageChangeHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        _jellyfin = new FakeJellyfinServer { ApiKey = JellyfinApiKey };
        _jellyfin.Movies.Clear();
        _jellyfin.Arm(_factory.JellyfinHttp);
        _tmdb = FakeTmdbServer.Arm(_factory.TmdbHttp);
        _tmdb.AddWork(Film(348, "1979-05-25", collectionId: 8091, recommendations: [218]));
        _tmdb.AddWork(Film(8077, "1992-05-22"));
        _tmdb.AddWork(Film(218, "1984-10-26"));
        _tmdb.AddCollection(8091, "Alien Collection", 348, 8077);
        _tmdb.AddWork(Film(LandingFilm, "1962-04-20") with { OriginalTitle = "Landing Stripling", GenreIds = [16, 35], RuntimeMinutes = 6 });
        _tmdb.AddWork(Film(LandingRemake, "1999-01-01") with { OriginalTitle = "Landing Stripling", GenreIds = [], RuntimeMinutes = 95 });
        _tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Series, TomAndJerryShow, "Том і Джеррі", "Tom & Jerry", "1975-09-06", GenreIds: [16])
        {
            TitlesByLanguage = ByLanguage($"{TomAndJerryShow}"),
        });
        _tmdb.AddSearch(MediaWorkKind.Movie, "Landing Stripling", 1962, LandingFilm);
        _tmdb.AddSearch(MediaWorkKind.Movie, "Landing Stripling", null, LandingFilm, LandingRemake);
        _tmdb.AddSearch(MediaWorkKind.Series, "Tom & Jerry", null, TomAndJerryShow);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Language_change_reads_the_recommendations_catalog_again_in_it_once_and_shows_its_titles()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var library = await _integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey);
        await new MediaWorkSeeder(_factory.Scope).CreateMovie(library, "Чужий", 1979, tmdbId: "348");
        var catalog = await ConnectCatalogAsync("uk-UA");
        await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Act
        var change = await ChangeLanguageAsync(catalog, "en-US");
        await _factory.RunQueuedRecommendationsAsync(cancellationToken);
        var list = await _factory.GetHttpRequest($"{RecommendationsUrl}?tab=Movies", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        // Read once in Ukrainian, then once again in English: the details, the collection and the genre list alike.
        Assert.Equal(["uk-UA", "en-US"], LanguagesOf("/3/movie/348"));
        Assert.Equal(["uk-UA", "en-US"], LanguagesOf("/3/collection/8091"));
        Assert.Equal(["uk-UA", "en-US"], LanguagesOf("/3/genre/movie/list"));
        await _factory.WithDbAsync(async db =>
        {
            var alien = await db.CatalogWorks.AsNoTracking().SingleAsync(work => work.CatalogId == "348", cancellationToken);
            Assert.Equal(("Alien", "en-US"), (alien.Title, alien.DetailsLanguage));
            Assert.Equal("en-US", (await db.CatalogCollections.AsNoTracking().SingleAsync(cancellationToken)).Language);
            Assert.All(await db.CatalogGenres.AsNoTracking().Where(genre => genre.Kind == MediaWorkKind.Movie).ToListAsync(cancellationToken),
                genre => Assert.Equal("en-US", genre.Language));
            var run = await db.RecommendationRuns.AsNoTracking()
                .SingleAsync(row => row.Trigger == RecommendationRunTrigger.CatalogLanguageChanged, cancellationToken);
            Assert.Equal(RecommendationRunStatus.Succeeded, run.Status);
        });
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var items = (await list.Content.ReadFromJsonAsync<RecommendationListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!.Items;
        Assert.NotEmpty(items);
        Assert.All(items, item => Assert.Equal(Titles[item.CatalogId].English, item.Title));
    }

    [Fact]
    public async Task Computation_after_the_language_change_finds_the_catalog_fresh_in_it_and_asks_nothing_again()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var library = await _integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey);
        await new MediaWorkSeeder(_factory.Scope).CreateMovie(library, "Чужий", 1979, tmdbId: "348");
        var catalog = await ConnectCatalogAsync("uk-UA");
        await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);
        await ChangeLanguageAsync(catalog, "en-US");
        await _factory.RunQueuedRecommendationsAsync(cancellationToken);

        //Act
        await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal(2, Count("/3/movie/348"));
        Assert.Equal(2, Count("/3/collection/8091"));
        Assert.Equal(2, Count("/3/genre/movie/list"));
    }

    [Fact]
    public async Task Language_change_reads_the_suggestions_candidates_again_in_it_without_searching_again()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncLandingAsync(cancellationToken);
        var catalog = await ConnectCatalogAsync("uk-UA");
        await _factory.PrepareLinkSuggestionsAsync(_owner.AccessToken, cancellationToken);
        var searched = Searches();
        var landingWorkId = await WorkOfAsync(LandingId, cancellationToken);

        //Act
        var change = await ChangeLanguageAsync(catalog, "en-US");
        await _factory.RunQueuedLinkSuggestionsAsync(cancellationToken);
        var dialog = await _factory.GetHttpRequest($"/api/library/works/{landingWorkId}/tmdb", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        // The search is a month fresh and kept: TMDb would list the same works.
        Assert.Equal(searched, Searches());
        // Each stored candidate's details read once more, in English.
        Assert.Equal(["uk-UA", "en-US"], LanguagesOf($"/3/movie/{LandingFilm}"));
        Assert.Equal(["uk-UA", "en-US"], LanguagesOf($"/3/movie/{LandingRemake}"));
        Assert.Equal(["uk-UA", "en-US"], LanguagesOf($"/3/tv/{TomAndJerryShow}"));
        await _factory.WithDbAsync(async db =>
        {
            var run = await db.LibraryLinkSuggestionRuns.AsNoTracking().SingleAsync(row => row.Trigger == LibraryLinkSuggestionRunTrigger.CatalogLanguageChanged, cancellationToken);
            Assert.Equal((LibraryLinkSuggestionRunStatus.Succeeded, 1), (run.Status, run.WorksDone));
            var landing = await db.CatalogWorks.AsNoTracking().SingleAsync(work => work.CatalogId == $"{LandingFilm}", cancellationToken);
            Assert.Equal(("Landing Stripling", "en-US"), (landing.Title, landing.DetailsLanguage));
        });
        Assert.Equal(HttpStatusCode.OK, dialog.StatusCode);
        var suggestions = (await dialog.Content.ReadFromJsonAsync<LibraryLinkDialogResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!.Suggestions;
        Assert.NotEmpty(suggestions);
        Assert.All(suggestions, suggestion => Assert.Equal(Titles[suggestion.CatalogId].English, suggestion.Title));
    }

    [Fact]
    public async Task Suggestions_run_after_the_language_change_reads_no_candidate_again()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncLandingAsync(cancellationToken);
        var catalog = await ConnectCatalogAsync("uk-UA");
        await _factory.PrepareLinkSuggestionsAsync(_owner.AccessToken, cancellationToken);
        await ChangeLanguageAsync(catalog, "en-US");
        await _factory.RunQueuedLinkSuggestionsAsync(cancellationToken);
        var sent = _factory.TmdbHttp.Requests.Count;
        var queued = await new LibraryLinkSuggestionRunSeeder(_factory.Scope).Create(LibraryLinkSuggestionRunStatus.Queued, LibraryLinkSuggestionRunTrigger.LibrarySynced);
        _factory.Resolve<LibraryLinkSuggestionRunQueue>().TryEnqueue(queued.Id);

        //Act
        await _factory.RunQueuedLinkSuggestionsAsync(cancellationToken);

        //Assert
        // The gate's connection test, and nothing else: every candidate was read in English already.
        Assert.Equal(["/3/authentication"], _factory.TmdbHttp.Requests.Skip(sent).Select(request => request.Uri.AbsolutePath));
    }

    [Fact]
    public async Task Catalog_added_again_in_another_language_reads_the_kept_suggestions_candidates_again_in_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncLandingAsync(cancellationToken);
        var catalog = await ConnectCatalogAsync("uk-UA");
        await _factory.PrepareLinkSuggestionsAsync(_owner.AccessToken, cancellationToken);
        await _factory.DeleteHttpRequest($"{IntegrationsUrl}/{catalog.Id}", _owner.AccessToken);

        //Act
        var created = await _factory.PostHttpRequest(IntegrationsUrl, _owner.AccessToken, new CreateIntegrationRequest(
            "tmdb", "TMDb", FakeTmdbServer.BaseUrl, ApiKey: new SecretValueUpsert(SecretUpsertMode.Set, FakeTmdbServer.Token), CatalogLanguage: "en-US"));
        await _factory.RunQueuedLinkSuggestionsAsync(cancellationToken);

        //Assert
        // Queued as a catalog connected: the rows' own language says what to read again, whatever queued the run.
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(["uk-UA", "en-US"], LanguagesOf($"/3/movie/{LandingFilm}"));
        await _factory.WithDbAsync(async db =>
        {
            var run = await db.LibraryLinkSuggestionRuns.AsNoTracking().SingleAsync(row => row.Trigger == LibraryLinkSuggestionRunTrigger.CatalogConnected, cancellationToken);
            Assert.Equal(LibraryLinkSuggestionRunStatus.Succeeded, run.Status);
            Assert.Equal(
                "Landing Stripling",
                (await db.CatalogWorks.AsNoTracking().SingleAsync(work => work.CatalogId == $"{LandingFilm}", cancellationToken)).Title);
        });
    }

    [Fact]
    public async Task Language_change_reads_again_once_a_work_only_a_release_sources_newest_releases_name_and_shows_its_title()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var catalog = await ConnectCatalogAsync("uk-UA");
        var toloka = await _integrations.Create("torznab", "Toloka", apiKey: JellyfinApiKey);
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        // Resolved, and its details read, in Ukrainian two days ago; no seed recommends it.
        var terminator = await new CatalogWorkSeeder(_factory.Scope).Create(
            MediaWorkKind.Movie, "218", Titles["218"].Ukrainian, new DateOnly(1984, 10, 26), [Action], detailsUtc: now.AddDays(-2), catalog: catalog);
        await new SourceFeedEntrySeeder(_factory.Scope).Create(
            toloka, "Термінатор / The Terminator (1984) BDRip 1080p", terminator, seenUtc: now.AddDays(-2), publishedUtc: now.AddDays(-2));
        await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Act
        await ChangeLanguageAsync(catalog, "en-US");
        await _factory.RunQueuedRecommendationsAsync(cancellationToken);
        var list = await _factory.GetHttpRequest($"{RecommendationsUrl}?tab=Movies", _owner.AccessToken);
        await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // Its row served the computation in Ukrainian; after the change it is read once, in English, and is fresh in it.
        Assert.Equal(["en-US"], LanguagesOf("/3/movie/218"));
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.CatalogWorks.AsNoTracking().SingleAsync(work => work.CatalogId == "218", cancellationToken);
            Assert.Equal(("The Terminator", "en-US"), (row.Title, row.DetailsLanguage));
        });
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var item = Assert.Single((await list.Content.ReadFromJsonAsync<RecommendationListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!.Items);
        Assert.Equal(("218", "The Terminator"), (item.CatalogId, item.Title));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Suggestions_run_reads_a_kept_candidate_with_no_details_stored_and_the_dialog_lists_it_again(bool rowKept)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncLandingAsync(cancellationToken);
        await ConnectCatalogAsync("uk-UA");
        await _factory.PrepareLinkSuggestionsAsync(_owner.AccessToken, cancellationToken);
        var searched = Searches();
        var landingWorkId = await WorkOfAsync(LandingId, cancellationToken);
        // What a run under way during a change of language leaves of a candidate it kept: the row a list stored, with no
        // details, or no row at all — it stores no details over a row, and a row another run read meanwhile is no answer.
        await _factory.WithDbAsync(db => rowKept
            ? db.CatalogWorks.Where(work => work.CatalogId == $"{LandingRemake}")
                .ExecuteUpdateAsync(setters => setters.SetProperty(work => work.DetailsUtc, (DateTime?)null), cancellationToken)
            : db.CatalogWorks.Where(work => work.CatalogId == $"{LandingRemake}").ExecuteDeleteAsync(cancellationToken));
        var queued = await new LibraryLinkSuggestionRunSeeder(_factory.Scope).Create(LibraryLinkSuggestionRunStatus.Queued, LibraryLinkSuggestionRunTrigger.LibrarySynced);
        _factory.Resolve<LibraryLinkSuggestionRunQueue>().TryEnqueue(queued.Id);

        //Act
        await _factory.RunQueuedLinkSuggestionsAsync(cancellationToken);
        var dialog = await _factory.GetHttpRequest($"/api/library/works/{landingWorkId}/tmdb", _owner.AccessToken);

        //Assert
        // The search is kept; that candidate's details are read again, the other candidate's are fresh.
        Assert.Equal(searched, Searches());
        Assert.Equal(["uk-UA", "uk-UA"], LanguagesOf($"/3/movie/{LandingRemake}"));
        Assert.Equal(["uk-UA"], LanguagesOf($"/3/movie/{LandingFilm}"));
        Assert.Equal(HttpStatusCode.OK, dialog.StatusCode);
        var suggestions = (await dialog.Content.ReadFromJsonAsync<LibraryLinkDialogResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!.Suggestions;
        Assert.Contains(suggestions, suggestion => suggestion.CatalogId == $"{LandingRemake}" && suggestion.Title == Titles[$"{LandingRemake}"].Ukrainian);
    }

    [Fact]
    public async Task Language_change_resolves_again_a_newest_release_the_old_language_matched_to_no_work()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var catalog = await ConnectCatalogAsync("en-US");
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: TolokaApiKey);
        // Toloka's own topic title in the description, "Ukrainian / Original (year)": the original a romanisation of the
        // Japanese one, which TMDb lists under neither.
        new FakeTorznabServer(TolokaAddress, TolokaApiKey)
        {
            SearchXml = TorznabSamples.Feed(TorznabSamples.Item(
                "(2001) BDRip 1080p Ukr/Jpn",
                $"{TorznabSamples.DownloadBaseFor(TolokaAddress)}download?file=1",
                "toloka-1",
                extra: "<category>2040</category><description>Віднесені привидами / Sen to Chihiro no kamikakushi (2001) BDRip 1080p Ukr/Jpn</description>")),
        }.Arm(_factory.TorznabHttp);
        _tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, SpiritedAway, Titles[$"{SpiritedAway}"].Ukrainian, "千と千尋の神隠し", "2001-07-20")
        {
            TitlesByLanguage = ByLanguage($"{SpiritedAway}"),
        });
        // TMDb's search finds the film by either title in any language; a lookup keeps it only by its title in the
        // language asked, or by its original one.
        _tmdb.AddSearch(MediaWorkKind.Movie, "Віднесені привидами", 2001, SpiritedAway);
        _tmdb.AddSearch(MediaWorkKind.Movie, "Sen to Chihiro no kamikakushi", 2001, SpiritedAway);
        await _factory.ReadRecommendationFeedAsync(cancellationToken);

        //Act
        var change = await ChangeLanguageAsync(catalog, "uk-UA");
        await _factory.ReadRecommendationFeedAsync(cancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        // Both titles asked in English, matched to nothing; then the Ukrainian title asked again in Ukrainian, and found.
        Assert.Equal(["en-US", "en-US", "uk-UA"], LanguagesOf("/3/search/movie"));
        await _factory.WithDbAsync(async db =>
        {
            var work = await db.CatalogWorks.AsNoTracking().SingleAsync(row => row.CatalogId == $"{SpiritedAway}", cancellationToken);
            var entry = await db.SourceFeedEntries.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(((Guid?)work.Id, "Віднесені привидами"), (entry.CatalogWorkId, work.Title));
        });
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static FakeTmdbWork Film(int id, string releaseDate, int? collectionId = null, int[]? recommendations = null) =>
        new(
            MediaWorkKind.Movie,
            id,
            Titles[$"{id}"].Ukrainian,
            ReleaseDate: releaseDate,
            GenreIds: [Action],
            CollectionId: collectionId,
            RecommendationIds: recommendations,
            TitlesByLanguage: ByLanguage($"{id}"));

    private static Dictionary<string, string> ByLanguage(string catalogId) =>
        new(StringComparer.Ordinal) { ["uk-UA"] = Titles[catalogId].Ukrainian, ["en-US"] = Titles[catalogId].English };

    private Task<IntegrationConnectionEntity> ConnectCatalogAsync(string catalogLanguage) =>
        _integrations.Create("tmdb", "TMDb", FakeTmdbServer.BaseUrl, apiKey: FakeTmdbServer.Token, catalogLanguage: catalogLanguage);

    private Task<HttpResponseMessage> ChangeLanguageAsync(IntegrationConnectionEntity catalog, string catalogLanguage) =>
        _factory.PutHttpRequest(
            $"{IntegrationsUrl}/{catalog.Id}", _owner.AccessToken, new UpdateIntegrationRequest("TMDb", FakeTmdbServer.BaseUrl, CatalogLanguage: catalogLanguage));

    /// <summary>"120. Landing Stripling" (1962) of the Tom &amp; Jerry folder, no ids, synced from Jellyfin before a catalog
    /// is connected, so the sync queues no run of its own.</summary>
    private async Task SyncLandingAsync(CancellationToken cancellationToken)
    {
        var jellyfin = await _integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey);
        _jellyfin.Movies.Add(JellyfinSamples.MovieWith(LandingId, "120. Landing Stripling", null, 1962, $"{TomAndJerryFolder}/120. Landing Stripling (1962).mkv"));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
    }

    private async Task<Guid> WorkOfAsync(string sourceItemId, CancellationToken cancellationToken)
    {
        var workId = Guid.Empty;
        await _factory.WithDbAsync(async db => workId = await db.MediaWorkTitles
            .Where(title => title.SourceItemId == sourceItemId)
            .Select(title => title.MediaWorkId)
            .FirstAsync(cancellationToken));
        return workId;
    }

    private int Count(string path) => _factory.TmdbHttp.Requests.Count(request => request.Uri.AbsolutePath == path);

    /// <summary>The <c>language</c> of every request to <paramref name="path"/>, in the order they were sent.</summary>
    private List<string> LanguagesOf(string path) =>
    [
        .. _factory.TmdbHttp.Requests
            .Where(request => request.Uri.AbsolutePath == path)
            .Select(request => QueryHelpers.ParseQuery(request.Uri.Query)["language"].ToString()),
    ];

    /// <summary>The catalog searches sent, in order, by their path and query.</summary>
    private List<string> Searches() =>
        [.. _factory.TmdbHttp.Requests.Where(request => request.Uri.AbsolutePath.StartsWith("/3/search/", StringComparison.Ordinal)).Select(request => request.Uri.PathAndQuery)];
}
