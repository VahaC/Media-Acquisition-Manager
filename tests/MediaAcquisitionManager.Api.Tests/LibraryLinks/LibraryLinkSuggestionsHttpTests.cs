using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.LibraryLinks;

// MAM-34, decisions 3, 7 and 8 — a work's TMDb suggestions as its dialog shows them: each with its evidence (title,
// year, the runtime against the duration the app measured, the kind, the library's genre derived from its identified
// works, a linked neighbour), best first, at most five with the confident one always among them. Computed when read from
// the stored candidates, so a duration measured later changes them without asking TMDb again. Confident only for the
// single exact title of the work's year whose runtime agrees with a measured duration — never two, never without a
// runtime, never a show for a film.
public sealed class LibraryLinkSuggestionsHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library/works";
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";
    private const string LandingId = "a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1";
    private const string CalypsoId = "b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2";
    private const string JunglesId = "c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3";
    private const string TomAndJerryFolder = "/media/cartoons/Tom & Jerry e001-e161 (1940-1967)";
    private const string LandingPath = $"{TomAndJerryFolder}/120. Landing Stripling (1962).mkv";
    private const string JunglesPath = "/media/documentary/Planet.Earth.2006.BDRip.1080p[Hurtom]/Planet_Earth_E08 - Jungles.mkv";
    private const int LandingFilm = 223021;
    private const int LandingRemake = 5000;
    private const int PlanetEarthShow = 1044;
    private const int GeneDeitch = 7;

    /// <summary>The Jellyfin item's 3 730 210 000 ticks (§1): 6.2 minutes.</summary>
    private static readonly TimeSpan LandingDuration = TimeSpan.FromMilliseconds(373_021);

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private AuthorizedUser _owner = null!;
    private FakeJellyfinServer _jellyfin = null!;
    private FakeTmdbServer _tmdb = null!;
    private IntegrationConnectionEntity _library = null!;

    public LibraryLinkSuggestionsHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        _jellyfin = new FakeJellyfinServer { ApiKey = JellyfinApiKey };
        _jellyfin.Movies.Clear();
        _jellyfin.Arm(_factory.JellyfinHttp);
        _tmdb = FakeTmdbServer.Arm(_factory.TmdbHttp);
        _tmdb.AddPerson(GeneDeitch, "Gene Deitch");
        _tmdb.AddWork(new FakeTmdbWork(
            MediaWorkKind.Movie, LandingFilm, "Посадка салаги", "Landing Stripling", "1962-04-20", GenreIds: [16, 35], PosterPath: "/landing.jpg",
            ImdbId: "tt0056164", DirectorIds: [GeneDeitch], RuntimeMinutes: 6, Overview: "Том і Джеррі на аеродромі."));
        _tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, LandingRemake, "Landing Stripling", ReleaseDate: "1999-01-01", GenreIds: [18], RuntimeMinutes: 95));
        _tmdb.AddSearch(MediaWorkKind.Movie, "Landing Stripling", 1962, LandingFilm);
        _tmdb.AddSearch(MediaWorkKind.Movie, "Landing Stripling", null, LandingFilm, LandingRemake);
        _library = await _integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Dialog_shows_the_suggestions_best_first_with_their_evidence_and_the_confident_one()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncAsync(cancellationToken, [Landing(), .. IdentifiedCartoons()]);
        await new MediaInspectionSeeder(_factory.Scope).Measure(LandingPath, LandingDuration);
        await ConnectCatalogAsync();
        var run = await _factory.PrepareLinkSuggestionsAsync(_owner.AccessToken, cancellationToken);
        var workId = await WorkOfAsync(LandingId, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(DialogUrl(workId), _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dialog = await ReadAsync<LibraryLinkDialogResponse>(response, cancellationToken);
        Assert.Equal(new LibraryLinkSearchStateDto(run.FinishedUtc, null, false, true), dialog.Search);
        Assert.Equal(2, dialog.Suggestions.Count);
        var confident = dialog.Suggestions[0];
        Assert.Equal(
            new LinkCandidateDto(
                MediaWorkKind.Movie,
                "223021",
                "Посадка салаги",
                "Landing Stripling",
                1962,
                "Том і Джеррі на аеродромі.",
                6,
                "/api/recommendations/posters/landing.jpg",
                "tt0056164",
                true,
                new LinkEvidenceDto(
                    new TitleEvidenceDto(1, true, LinkTitleKind.OriginalTitle, "Landing Stripling"),
                    new YearEvidenceDto(1962, 1962, 0),
                    new RuntimeEvidenceDto(6, 6, true),
                    true,
                    // The cartoons library mostly holds animation: three of its identified works are (decision 3).
                    new GenreEvidenceDto(CatalogGenreRole.Animation, LinkGenreAgreement.Agrees),
                    null),
                null,
                null),
            confident);
        var remake = dialog.Suggestions[1];
        Assert.Equal(
            ("5000", false, (int?)37, (bool?)false, LinkGenreAgreement.Disagrees),
            (remake.CatalogId, remake.IsConfident, remake.Evidence.Year.Difference, remake.Evidence.Runtime.Agrees, remake.Evidence.Genre.Agreement));
    }

    [Fact]
    public async Task Dialog_without_a_measured_duration_has_no_confident_suggestion_until_one_is_measured_and_asks_tmdb_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncAsync(cancellationToken, Landing());
        await ConnectCatalogAsync();
        await _factory.PrepareLinkSuggestionsAsync(_owner.AccessToken, cancellationToken);
        var workId = await WorkOfAsync(LandingId, cancellationToken);
        var before = await ReadAsync<LibraryLinkDialogResponse>(await _factory.GetHttpRequest(DialogUrl(workId), _owner.AccessToken), cancellationToken);
        var sent = _factory.TmdbHttp.Requests.Count;
        await new MediaInspectionSeeder(_factory.Scope).Measure(LandingPath, LandingDuration);

        //Act
        var response = await _factory.GetHttpRequest(DialogUrl(workId), _owner.AccessToken);

        //Assert
        // Decision 7: the evidence is worked out when read — an inspection measuring the file later changes it.
        Assert.Equal(new RuntimeEvidenceDto(null, 6, null), before.Suggestions[0].Evidence.Runtime);
        Assert.DoesNotContain(before.Suggestions, suggestion => suggestion.IsConfident);
        var after = await ReadAsync<LibraryLinkDialogResponse>(response, cancellationToken);
        Assert.Equal((new RuntimeEvidenceDto(6, 6, true), true), (after.Suggestions[0].Evidence.Runtime, after.Suggestions[0].IsConfident));
        Assert.Equal(sent, _factory.TmdbHttp.Requests.Count);
    }

    [Fact]
    public async Task Dialog_with_two_films_of_the_title_and_year_has_no_confident_suggestion()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, 6000, "Landing Stripling", ReleaseDate: "1962-09-01", RuntimeMinutes: 7));
        _tmdb.AddSearch(MediaWorkKind.Movie, "Landing Stripling", 1962, LandingFilm, 6000);
        await SyncAsync(cancellationToken, Landing());
        await new MediaInspectionSeeder(_factory.Scope).Measure(LandingPath, LandingDuration);
        await ConnectCatalogAsync();
        await _factory.PrepareLinkSuggestionsAsync(_owner.AccessToken, cancellationToken);
        var workId = await WorkOfAsync(LandingId, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(DialogUrl(workId), _owner.AccessToken);

        //Assert
        // Two answers of one title and year are a doubt the owner settles, however well the runtimes agree.
        var dialog = await ReadAsync<LibraryLinkDialogResponse>(response, cancellationToken);
        Assert.Equal(["223021", "6000", "5000"], dialog.Suggestions.Select(suggestion => suggestion.CatalogId));
        Assert.DoesNotContain(dialog.Suggestions, suggestion => suggestion.IsConfident);
    }

    [Fact]
    public async Task Dialog_of_a_film_never_has_a_show_confident_and_prefills_its_episode()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _tmdb.AddWork(new FakeTmdbWork(
            MediaWorkKind.Series, PlanetEarthShow, "Планета Земля", "Planet Earth", "2006-03-05", GenreIds: [99],
            Seasons: [new FakeTmdbSeason(0, EpisodeCount: 24), new FakeTmdbSeason(1, "2006-03-05", 11)], RuntimeMinutes: 53));
        _tmdb.AddSearch(MediaWorkKind.Series, "Planet Earth", 2006, PlanetEarthShow);
        await SyncAsync(cancellationToken, JellyfinSamples.MovieWith(JunglesId, "Planet_Earth_E08 - Jungles", null, 2006, JunglesPath));
        await new MediaInspectionSeeder(_factory.Scope).Measure(JunglesPath, TimeSpan.FromMinutes(51));
        await ConnectCatalogAsync();
        await _factory.PrepareLinkSuggestionsAsync(_owner.AccessToken, cancellationToken);
        var workId = await WorkOfAsync(JunglesId, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(DialogUrl(workId), _owner.AccessToken);

        //Assert
        // Decision 8: a show for a film is an episode-to-show link — never confident, whatever else agrees. Its season and
        // episode come from "_E08" and the show's one regular season (its specials aside).
        var show = Assert.Single((await ReadAsync<LibraryLinkDialogResponse>(response, cancellationToken)).Suggestions);
        Assert.Equal(
            ("1044", false, true, 0, true, false, (int?)1, (int?)8),
            (show.CatalogId, show.IsConfident, show.Evidence.Title.Exact, show.Evidence.Year.Difference, show.Evidence.Runtime.Agrees == true,
                show.Evidence.KindAgrees, show.SeasonNumber, show.EpisodeNumber));
    }

    [Fact]
    public async Task Dialog_ranks_first_the_candidate_of_a_linked_neighbours_director()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, 7000, "Calypso Cat", ReleaseDate: "1962-06-22", DirectorIds: [99]));
        _tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, 7001, "Calypso Cat", ReleaseDate: "1962-06-22", DirectorIds: [GeneDeitch]));
        _tmdb.AddSearch(MediaWorkKind.Movie, "Calypso Cat", 1962, 7000, 7001);
        await SyncAsync(cancellationToken, Landing(), Calypso());
        var landingWorkId = await WorkOfAsync(LandingId, cancellationToken);
        // The Tom & Jerry short beside it is linked to Gene Deitch's film.
        await new LibraryItemLinkSeeder(_factory.Scope).Create(_library, LandingId, landingWorkId, MediaIdentifierScheme.Tmdb, "223021");
        await ConnectCatalogAsync();
        await _factory.PrepareLinkSuggestionsAsync(_owner.AccessToken, cancellationToken);
        var workId = await WorkOfAsync(CalypsoId, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(DialogUrl(workId), _owner.AccessToken);

        //Assert
        // TMDb listed the other director's first; the neighbour's director lifts the second above it.
        var suggestions = (await ReadAsync<LibraryLinkDialogResponse>(response, cancellationToken)).Suggestions;
        Assert.Equal(["7001", "7000"], suggestions.Select(suggestion => suggestion.CatalogId));
        Assert.Equal(new SiblingEvidenceDto(LinkSiblingReason.SameDirector, "120. Landing Stripling"), suggestions[0].Evidence.Sibling);
        Assert.Null(suggestions[1].Evidence.Sibling);
    }

    [Fact]
    public async Task Dialog_shows_five_and_puts_the_confident_one_last_when_five_rank_above_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _tmdb.AddCollection(77, "Tom and Jerry Collection", 500);
        _tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, 500, "Calypso Cat", ReleaseDate: "1962-06-22", CollectionId: 77));
        int[] shorts = [801, 802, 803, 804, 805, 806];
        foreach (var id in shorts)
            _tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, id, $"Tom and Jerry short {id}", ReleaseDate: "1962-01-01", CollectionId: 77));
        _tmdb.AddSearch(MediaWorkKind.Movie, "Landing Stripling", 1962, [.. shorts, LandingFilm]);
        await SyncAsync(cancellationToken, Landing(), Calypso());
        await new MediaInspectionSeeder(_factory.Scope).Measure(LandingPath, LandingDuration);
        var calypsoWorkId = await WorkOfAsync(CalypsoId, cancellationToken);
        await new LibraryItemLinkSeeder(_factory.Scope).Create(_library, CalypsoId, calypsoWorkId, MediaIdentifierScheme.Tmdb, "500");
        await ConnectCatalogAsync();
        await _factory.PrepareLinkSuggestionsAsync(_owner.AccessToken, cancellationToken);
        var workId = await WorkOfAsync(LandingId, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(DialogUrl(workId), _owner.AccessToken);

        //Assert
        // Six shorts of the neighbour's collection rank first; the confident film, sixth, takes the fifth place (R21).
        var suggestions = (await ReadAsync<LibraryLinkDialogResponse>(response, cancellationToken)).Suggestions;
        Assert.Equal(["801", "802", "803", "804", "223021"], suggestions.Select(suggestion => suggestion.CatalogId));
        Assert.Equal([false, false, false, false, true], suggestions.Select(suggestion => suggestion.IsConfident));
        Assert.All(suggestions.Take(4), suggestion => Assert.Equal(LinkSiblingReason.SameCollection, suggestion.Evidence.Sibling?.Reason));
    }

    [Fact]
    public async Task Dialog_of_a_work_never_searched_has_no_suggestions_and_says_a_preparation_is_pending()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncAsync(cancellationToken, Landing());
        await ConnectCatalogAsync();
        await new LibraryLinkSuggestionRunSeeder(_factory.Scope).Create(LibraryLinkSuggestionRunStatus.Queued, LibraryLinkSuggestionRunTrigger.LibrarySynced);
        var workId = await WorkOfAsync(LandingId, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(DialogUrl(workId), _owner.AccessToken);

        //Assert
        var dialog = await ReadAsync<LibraryLinkDialogResponse>(response, cancellationToken);
        Assert.Empty(dialog.Suggestions);
        Assert.Equal(new LibraryLinkSearchStateDto(null, null, true, true), dialog.Search);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static JsonObject Landing() =>
        JellyfinSamples.MovieWith(LandingId, "120. Landing Stripling", null, 1962, LandingPath);

    private static JsonObject Calypso() =>
        JellyfinSamples.MovieWith(CalypsoId, "119. Calypso Cat", null, 1962, $"{TomAndJerryFolder}/119. Calypso Cat (1962).mkv");

    /// <summary>Three cartoons Jellyfin knows the TMDb ids of, each an animation: what the cartoons library holds.</summary>
    private JsonObject[] IdentifiedCartoons()
    {
        int[] ids = [11134, 11135, 11136];
        foreach (var id in ids)
            _tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, id, $"Cartoon {id}", GenreIds: [16]));
        return
        [
            .. ids.Select(id => JellyfinSamples.MovieWith(
                $"{id:D32}", $"Cartoon {id}", null, 2000, $"/media/cartoons/Cartoon {id}/Cartoon {id}.mkv", tmdbId: id.ToString(System.Globalization.CultureInfo.InvariantCulture))),
        ];
    }

    private async Task SyncAsync(CancellationToken cancellationToken, params JsonObject[] movies)
    {
        _jellyfin.Movies.AddRange(movies);
        await _factory.SyncLibraryAsync(_library.Id, _owner.AccessToken, cancellationToken);
    }

    private Task<IntegrationConnectionEntity> ConnectCatalogAsync() =>
        _integrations.Create("tmdb", "TMDb", FakeTmdbServer.BaseUrl, apiKey: FakeTmdbServer.Token);

    private async Task<Guid> WorkOfAsync(string sourceItemId, CancellationToken cancellationToken)
    {
        var workId = Guid.Empty;
        await _factory.WithDbAsync(async db => workId = await db.MediaWorkTitles
            .Where(title => title.SourceItemId == sourceItemId)
            .Select(title => title.MediaWorkId)
            .FirstAsync(cancellationToken));
        return workId;
    }

    private static string DialogUrl(Guid workId) => $"{BaseUrl}/{workId}/tmdb";

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<T>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
}
