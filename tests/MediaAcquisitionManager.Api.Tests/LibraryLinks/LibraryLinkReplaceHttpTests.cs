using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Api.Services.LibraryLinks;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.LibraryLinks;

// MAM-34, R6, R11 — replacing a work's link (the dialog's Save over a link): the same row, recorded as replaced; the
// id it replaces is what Jellyfin holds — the old link's value once Jellyfin confirmed it, else the library's own; the
// new TMDb id written over the old one; the IMDb id the app added swapped or cleared, one Jellyfin held left alone; a
// film link the app wrote, replaced by an episode's link to its show that writes nothing of its own (TMDb gives the
// fake's Planet Earth episodes no IMDb id), first put back in Jellyfin; and a series' link replaced like a film's, the
// new show's id written over the one the app wrote (Vasyl's final decision of 2026-10-06). Every write that leaves the
// ids in is followed by Jellyfin's lookup, and a replaced link's lookup is not the new link's (LibraryLinkLookupHttpTests).
// A film link replaced by an episode's link that writes the episode's IMDb id: LibraryLinkEpisodeHttpTests.
public sealed class LibraryLinkReplaceHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string ShortId = JellyfinSamples.LandingStriplingId;
    private const string FilmId = JellyfinSamples.LandingStriplingTmdbId;
    private const string FilmImdbId = JellyfinSamples.LandingStriplingImdbId;
    private const string OtherFilmId = "223022";
    private const string OtherFilmImdbId = "tt0056165";
    private const string FilmWithoutImdbId = "223023";
    private const string PlanetEarthId = "1044";
    private const string WrongTmdbId = "100";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private AuthorizedUser _owner = null!;
    private FakeJellyfinServer _jellyfin = null!;
    private IntegrationConnectionEntity _jellyfinIntegration = null!;

    public LibraryLinkReplaceHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        _jellyfin = new FakeJellyfinServer { ApiKey = ApiKey };
        _jellyfin.Movies.Clear();
        _jellyfin.Arm(_factory.JellyfinHttp);
        var tmdb = FakeTmdbServer.Arm(_factory.TmdbHttp);
        tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, int.Parse(FilmId), "Посадкова смуга", ImdbId: FilmImdbId));
        tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, int.Parse(OtherFilmId), "Інший фільм", ImdbId: OtherFilmImdbId));
        tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, int.Parse(FilmWithoutImdbId), "Фільм без IMDb"));
        tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Series, int.Parse(PlanetEarthId), "Планета Земля", ImdbId: "tt0795176"));
        tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Series, int.Parse(JellyfinSamples.PenguinsTmdbTvId), "Пінгвіни з Мадагаскару", ImdbId: "tt0892700"));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Replace_of_a_written_link_keeps_its_row_records_it_replaced_and_writes_the_new_ids_over_the_ones_the_app_wrote()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync(tmdbId: null, imdbId: null);
        await SetAsync(workId, new LibraryLinkRequest("Movie", FilmId));
        var first = await SingleLinkAsync(cancellationToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Movie", OtherFilmId));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var link = await SingleLinkAsync(cancellationToken);
        // R6 — the value it replaces is the old link's, which Jellyfin holds until it confirms the new one.
        Assert.Equal(
            (first.Id, OtherFilmId, OtherFilmImdbId, (string?)FilmId, LibraryLinkWriteState.Written),
            (link.Id, link.Value, link.ImdbId, link.SupersededValue, link.WriteState));
        // The IMDb id the app added is swapped for the new film's.
        Assert.Equal(new Dictionary<string, string> { ["Imdb"] = OtherFilmImdbId, ["Tmdb"] = OtherFilmId }, ProviderIdsOf(ShortId));
        Assert.Equal(
            [
                AuditAction.LibraryLinkSet, AuditAction.LibraryIdsWritten, AuditAction.LibraryMetadataLookupRequested,
                AuditAction.LibraryLinkReplaced, AuditAction.LibraryIdsWritten, AuditAction.LibraryMetadataLookupRequested,
            ],
            await AuditActionsAsync(cancellationToken));
        Assert.Equal([ShortId, ShortId], _jellyfin.Lookups);
    }

    [Fact]
    public async Task Replace_with_a_film_tmdb_gives_no_imdb_id_clears_the_one_the_app_added()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync(tmdbId: null, imdbId: null);
        await SetAsync(workId, new LibraryLinkRequest("Movie", FilmId));
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Movie", FilmWithoutImdbId));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new Dictionary<string, string> { ["Tmdb"] = FilmWithoutImdbId }, ProviderIdsOf(ShortId));
        Assert.Equal(LibraryLinkWriteState.Written, (await SingleLinkAsync(cancellationToken)).WriteState);
    }

    [Fact]
    public async Task Replace_leaves_an_imdb_id_jellyfin_held_before_the_app_wrote()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync(tmdbId: null, imdbId: "tt9999999");
        await SetAsync(workId, new LibraryLinkRequest("Movie", FilmId));
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Movie", OtherFilmId));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new Dictionary<string, string> { ["Imdb"] = "tt9999999", ["Tmdb"] = OtherFilmId }, ProviderIdsOf(ShortId));
        Assert.Equal(LibraryLinkWriteState.Written, (await SingleLinkAsync(cancellationToken)).WriteState);
    }

    [Fact]
    public async Task Replace_of_a_written_film_link_with_an_episode_link_puts_jellyfins_own_ids_back_and_keeps_the_link_in_the_app()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync(tmdbId: WrongTmdbId, imdbId: null);
        await SetAsync(workId, new LibraryLinkRequest("Movie", FilmId));
        var written = ProviderIdsOf(ShortId);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var response = await _factory.PutHttpRequest(
            LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Series", PlanetEarthId, SeasonNumber: "1", EpisodeNumber: "8"));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new Dictionary<string, string> { ["Imdb"] = FilmImdbId, ["Tmdb"] = FilmId }, written);
        // R11 — the film id Jellyfin held is back, and the IMDb id the app added is gone.
        Assert.Equal(new Dictionary<string, string> { ["Tmdb"] = WrongTmdbId }, ProviderIdsOf(ShortId));
        var link = await SingleLinkAsync(cancellationToken);
        // Kept in the app as TMDb gives the episode no IMDb id — said on the link, as only asking TMDb tells it.
        Assert.Equal(
            (LibraryLinkTarget.ShowEpisode, MediaIdentifierScheme.TmdbTv, PlanetEarthId, LibraryLinkWriteState.KeptInApp, (string?)LibraryLinkWritability.NoEpisodeImdbReason),
            (link.Target, link.Scheme, link.Value, link.WriteState, link.WriteError));
        Assert.Equal(
            [
                AuditAction.LibraryLinkSet, AuditAction.LibraryIdsWritten, AuditAction.LibraryMetadataLookupRequested,
                AuditAction.LibraryLinkReplaced, AuditAction.LibraryIdsRestored,
            ],
            await AuditActionsAsync(cancellationToken));
    }

    [Fact]
    public async Task Replace_of_a_link_jellyfin_never_confirmed_takes_the_id_it_replaces_from_the_library()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync(tmdbId: WrongTmdbId, imdbId: null);
        // An episode's link first, kept in the app: Jellyfin still holds its own wrong film id.
        await SetAsync(workId, new LibraryLinkRequest("Series", PlanetEarthId, SeasonNumber: "1"));
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Movie", FilmId));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var link = await SingleLinkAsync(cancellationToken);
        Assert.Equal(
            (LibraryLinkTarget.Work, FilmId, (string?)WrongTmdbId, LibraryLinkWriteState.Written),
            (link.Target, link.Value, link.SupersededValue, link.WriteState));
    }

    [Fact]
    public async Task Replace_of_a_written_series_link_writes_the_new_shows_id_over_the_one_the_app_wrote_and_swaps_the_imdb_id_it_added()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        const string PenguinsId = "d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4";
        _jellyfinIntegration = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        // Jellyfin gave the series a wrong show id; the owner first linked it to another wrong one, then to the right show.
        _jellyfin.Series.Add(JellyfinSamples.SeriesWith(PenguinsId, "The Penguins Of Madagascar", 2008, "/media/tv/Penguins", tmdbId: WrongTmdbId));
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith("e5e5e5e5e5e5e5e5e5e5e5e5e5e5e5e5", "Gone in a Flash", PenguinsId, 1, 1, "/media/tv/Penguins/S01E01.mkv"));
        await _factory.SyncLibraryAsync(_jellyfinIntegration.Id, _owner.AccessToken, cancellationToken);
        await _integrations.Create("tmdb", "TMDb", FakeTmdbServer.BaseUrl, apiKey: FakeTmdbServer.Token);
        var workId = Guid.Empty;
        await _factory.WithDbAsync(async db => workId = await db.MediaWorkTitles
            .Where(title => title.SourceItemId == PenguinsId)
            .Select(title => title.MediaWorkId)
            .FirstAsync(cancellationToken));
        await SetAsync(workId, new LibraryLinkRequest("Series", PlanetEarthId));
        var first = await SingleLinkAsync(cancellationToken);
        var written = _jellyfin.Series.Single()["ProviderIds"]!.ToJsonString();
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Series", JellyfinSamples.PenguinsTmdbTvId));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var link = await SingleLinkAsync(cancellationToken);
        // R6 — the id it replaces is the one Jellyfin confirmed, the first link's.
        Assert.Equal(
            (first.Id, JellyfinSamples.PenguinsTmdbTvId, (string?)PlanetEarthId, LibraryLinkWriteState.Written, (string?)null),
            (link.Id, link.Value, link.SupersededValue, link.WriteState, link.WriteError));
        // The first write gave the series the wrong show and its IMDb id; the second put the right show over it and swapped
        // the IMDb id the app had added.
        Assert.Equal("""{"Tmdb":"1044","Imdb":"tt0795176"}""", written);
        Assert.Equal("""{"Tmdb":"7869","Imdb":"tt0892700"}""", _jellyfin.Series.Single()["ProviderIds"]!.ToJsonString());
        Assert.Equal(
            [
                AuditAction.LibraryLinkSet, AuditAction.LibraryIdsWritten, AuditAction.LibraryMetadataLookupRequested,
                AuditAction.LibraryLinkReplaced, AuditAction.LibraryIdsWritten, AuditAction.LibraryMetadataLookupRequested,
            ],
            await AuditActionsAsync(cancellationToken));
        Assert.Equal([PenguinsId, PenguinsId], _jellyfin.Lookups);
    }

    [Fact]
    public async Task Replace_that_needs_jellyfin_put_back_while_it_is_switched_off_is_409_and_changes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync(tmdbId: null, imdbId: null);
        await SetAsync(workId, new LibraryLinkRequest("Movie", FilmId));
        await _factory.WithDbAsync(db => db.IntegrationConnections
            .Where(integration => integration.AdapterType == "jellyfin")
            .ExecuteUpdateAsync(setters => setters.SetProperty(integration => integration.IsEnabled, false), cancellationToken));

        //Act
        var response = await _factory.PutHttpRequest(
            LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Series", PlanetEarthId, SeasonNumber: "1"));

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "“Living room” is switched off. Switch it on under Integrations to change the link.",
            await response.ReadErrorAsync(cancellationToken));
        var link = await SingleLinkAsync(cancellationToken);
        Assert.Equal((FilmId, LibraryLinkWriteState.Written), (link.Value, link.WriteState));
    }

    [Fact]
    public async Task Replace_leaves_an_imdb_id_someone_set_after_the_app_added_its_own_and_so_does_a_removal_after_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync(tmdbId: null, imdbId: null);
        await SetAsync(workId, new LibraryLinkRequest("Movie", FilmId));
        // Someone set another IMDb id in Jellyfin over the one the app added.
        Movie(ShortId)["ProviderIds"]!["Imdb"] = "tt7777777";
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var replaced = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Movie", OtherFilmId));
        var afterReplace = ProviderIdsOf(ShortId);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var removed = await _factory.DeleteHttpRequest(LinkUrl(workId), _owner.AccessToken);

        //Assert
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (replaced.StatusCode, removed.StatusCode));
        // Decision 13 — the replace's read again found the changed IMDb id, which the app never wrote: neither the
        // replace nor the removal treats it as the app's.
        Assert.Equal(new Dictionary<string, string> { ["Imdb"] = "tt7777777", ["Tmdb"] = OtherFilmId }, afterReplace);
        Assert.Equal(new Dictionary<string, string> { ["Imdb"] = "tt7777777" }, ProviderIdsOf(ShortId));
    }

    [Fact]
    public async Task Replace_of_a_film_link_a_sync_read_with_an_episode_link_drops_the_film_id_the_restore_put_back()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync(tmdbId: WrongTmdbId, imdbId: null);
        await SetAsync(workId, new LibraryLinkRequest("Movie", FilmId));
        // A sync read the app's film id as Jellyfin's own: the link is in Jellyfin.
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        await _factory.SyncLibraryAsync(_jellyfinIntegration.Id, _owner.AccessToken, cancellationToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var response = await _factory.PutHttpRequest(
            LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Series", PlanetEarthId, SeasonNumber: "1", EpisodeNumber: "8"));
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        await _factory.SyncLibraryAsync(_jellyfinIntegration.Id, _owner.AccessToken, cancellationToken);
        var byWrongFilm = await _factory.GetHttpRequest($"{BaseUrl}/works?search={WrongTmdbId}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new Dictionary<string, string> { ["Tmdb"] = WrongTmdbId }, ProviderIdsOf(ShortId));
        // R7 — the episode's link replaces the wrong film id Jellyfin holds again, not the app's that the sync had read.
        var link = await SingleLinkAsync(cancellationToken);
        Assert.Equal((LibraryLinkTarget.ShowEpisode, (string?)WrongTmdbId), (link.Target, link.SupersededValue));
        var list = await byWrongFilm.Content.ReadFromJsonAsync<LibraryWorkListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(0, list!.TotalCount);
    }

    [Fact]
    public async Task Replace_of_a_film_link_whose_write_a_restart_cut_swaps_the_imdb_id_it_added()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync(tmdbId: WrongTmdbId, imdbId: null);
        await CutWriteAsync(workId);
        await _factory.Resolve<LibraryLinkWriteReconciler>().ReconcileAsync(cancellationToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Movie", OtherFilmId));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // R10 — the reconciler recorded the cut write: the IMDb id it added is the app's, and is swapped.
        Assert.Equal(new Dictionary<string, string> { ["Imdb"] = OtherFilmImdbId, ["Tmdb"] = OtherFilmId }, ProviderIdsOf(ShortId));
    }

    [Fact]
    public async Task Replace_of_a_film_link_whose_write_a_restart_cut_with_an_episode_link_puts_jellyfins_own_ids_back()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync(tmdbId: WrongTmdbId, imdbId: null);
        await CutWriteAsync(workId);
        await _factory.Resolve<LibraryLinkWriteReconciler>().ReconcileAsync(cancellationToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var response = await _factory.PutHttpRequest(
            LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Series", PlanetEarthId, SeasonNumber: "1", EpisodeNumber: "8"));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new Dictionary<string, string> { ["Tmdb"] = WrongTmdbId }, ProviderIdsOf(ShortId));
        var link = await SingleLinkAsync(cancellationToken);
        Assert.Equal(
            (LibraryLinkTarget.ShowEpisode, LibraryLinkWriteState.KeptInApp, (string?)LibraryLinkWritability.NoEpisodeImdbReason),
            (link.Target, link.WriteState, link.WriteError));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static string LinkUrl(Guid workId) => $"{BaseUrl}/works/{workId}/tmdb";

    private async Task SetAsync(Guid workId, LibraryLinkRequest request)
    {
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<Guid> SyncShortAsync(string? tmdbId, string? imdbId)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        _jellyfinIntegration = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        _jellyfin.Movies.Add(JellyfinSamples.MovieWith(ShortId, "Landing Stripling", null, 1962, "/media/cartoons/Tom & Jerry/120.mkv", imdbId, tmdbId));
        await _factory.SyncLibraryAsync(_jellyfinIntegration.Id, _owner.AccessToken, cancellationToken);
        await _integrations.Create("tmdb", "TMDb", FakeTmdbServer.BaseUrl, apiKey: FakeTmdbServer.Token);
        var workId = Guid.Empty;
        await _factory.WithDbAsync(async db => workId = await db.MediaWorkTitles
            .Where(title => title.SourceItemId == ShortId)
            .Select(title => title.MediaWorkId)
            .FirstAsync(cancellationToken));
        return workId;
    }

    // A film link whose write landed in Jellyfin — its TMDb id and the IMDb id it added — before a restart cut it: only its
    // set row was recorded, and the link is still Writing for the reconciler.
    private async Task CutWriteAsync(Guid workId)
    {
        Movie(ShortId)["ProviderIds"] = new JsonObject { ["Tmdb"] = FilmId, ["Imdb"] = FilmImdbId };
        var link = await new LibraryItemLinkSeeder(_factory.Scope).Create(
            _jellyfinIntegration, ShortId, workId, MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.Writing, setBy: _owner.User,
            configure: row => row.ImdbId = FilmImdbId);
        await new AuditEventSeeder(_factory.Scope).CreateForLink(
            AuditAction.LibraryLinkSet, link, "Landing Stripling",
            [
                new AuditIdentifier(AuditIdentifierRole.Before, MediaIdentifierScheme.Tmdb, WrongTmdbId),
                new AuditIdentifier(AuditIdentifierRole.After, MediaIdentifierScheme.Tmdb, FilmId),
                new AuditIdentifier(AuditIdentifierRole.After, MediaIdentifierScheme.Imdb, FilmImdbId),
            ],
            _owner.User);
    }

    private JsonObject Movie(string itemId) => _jellyfin.Movies.Single(movie => (string?)movie["Id"] == itemId);

    private Dictionary<string, string> ProviderIdsOf(string itemId) =>
        _jellyfin.Movies.Single(movie => (string?)movie["Id"] == itemId)["ProviderIds"]!.AsObject()
            .ToDictionary(pair => pair.Key, pair => (string)pair.Value!);

    private async Task<LibraryItemLinkEntity> SingleLinkAsync(CancellationToken cancellationToken)
    {
        LibraryItemLinkEntity? link = null;
        await _factory.WithDbAsync(async db => link = await db.LibraryItemLinks.AsNoTracking().SingleAsync(cancellationToken));
        return link!;
    }

    private async Task<List<AuditAction>> AuditActionsAsync(CancellationToken cancellationToken)
    {
        var actions = new List<AuditAction>();
        await _factory.WithDbAsync(async db => actions.AddRange((await db.AuditEvents.AsNoTracking().ToListAsync(cancellationToken))
            .OrderBy(auditEvent => auditEvent.CreatedUtc)
            .ThenBy(auditEvent => auditEvent.Action)
            .Select(auditEvent => auditEvent.Action)));
        return actions;
    }
}
