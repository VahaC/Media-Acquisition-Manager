using System.Net;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.LibraryLinks;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.LibraryLinks;

// MAM-34 (0.2.1, ADR 0026), R6 — replacing a work's link (the dialog's Save over a link): the same row, recorded as replaced
// and queued again with its identified time cleared, the id it replaces being what Jellyfin holds — the old link's value
// once Jellyfin confirmed it, else the library's own. A replace is a new Identify on the new id, in the background, and
// the owner's own click: Jellyfin's Identify replaces the ids the first one left (a guard that refused them would refuse
// every replace). Nothing is put back: a film link replaced by an episode's link to its show is identified by the episode's
// IMDb id through the Open Movie Database when TMDb gives one, and otherwise kept in the app, Jellyfin keeping what the
// first Identify set. A library switched off saves the new link not written and changes nothing in Jellyfin.
public sealed class LibraryLinkReplaceHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string ShortId = JellyfinSamples.LandingStriplingId;
    private const string PenguinsId = "d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4";
    private const string FilmId = JellyfinSamples.LandingStriplingTmdbId;
    private const string FilmImdbId = JellyfinSamples.LandingStriplingImdbId;
    private const string OtherFilmId = "223022";
    private const string OtherFilmImdbId = "tt0056165";
    private const string FilmWithoutImdbId = "223023";
    private const string PlanetEarthId = "1044";
    private const string WwiiShowId = TmdbSamples.TvEpisodeWwiiShowId;
    private const string WwiiEpisodeImdbId = TmdbSamples.TvEpisodeWwiiImdbId;
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
        _jellyfin.AddFilm(FilmId, "Landing Stripling", 1962, FilmImdbId);
        _jellyfin.AddFilm(OtherFilmId, "Calypso Cat", 1962, OtherFilmImdbId);
        _jellyfin.AddFilm(FilmWithoutImdbId, "Film Without IMDb", 1962);
        _jellyfin.AddShow(PlanetEarthId, "Planet Earth", 2006, "tt0795176");
        _jellyfin.AddShow(JellyfinSamples.PenguinsTmdbTvId, "The Penguins of Madagascar", 2008, "tt0892700");
        _jellyfin.AddEpisodeMovie(WwiiEpisodeImdbId, "Blitzkrieg", 2019);
        _jellyfin.Arm(_factory.JellyfinHttp);
        var tmdb = FakeTmdbServer.Arm(_factory.TmdbHttp);
        tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, int.Parse(FilmId), "Посадкова смуга", ImdbId: FilmImdbId));
        tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, int.Parse(OtherFilmId), "Інший фільм", ImdbId: OtherFilmImdbId));
        tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, int.Parse(FilmWithoutImdbId), "Фільм без IMDb"));
        tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Series, int.Parse(PlanetEarthId), "Планета Земля", ImdbId: "tt0795176"));
        tmdb.AddEpisode(new FakeTmdbEpisode(int.Parse(PlanetEarthId), 1, 8, "Джунглі"));
        tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Series, int.Parse(JellyfinSamples.PenguinsTmdbTvId), "Пінгвіни з Мадагаскару", ImdbId: "tt0892700"));
        tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Series, int.Parse(WwiiShowId), "Найвизначніші події Другої світової у кольорі", "Greatest Events of WWII in Colour"));
        tmdb.AddEpisode(new FakeTmdbEpisode(int.Parse(WwiiShowId), 1, 1, "Серія 1", WwiiEpisodeImdbId, "2019-07-12", 50));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Replace_of_an_identified_link_keeps_its_row_records_it_replaced_clears_identified_and_identifies_the_new_id()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync(tmdbId: null, imdbId: null);
        await SetAndIdentifyAsync(workId, new LibraryLinkRequest("Movie", FilmId));
        var first = await _factory.SingleLinkAsync(cancellationToken);

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Movie", OtherFilmId));
        var queued = await _factory.SingleLinkAsync(cancellationToken);
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = Assert.Single((await response.ReadDialogAsync(cancellationToken)).Items);
        // A replaced link is not identified as its new id: queued, with nothing confirmed.
        Assert.Equal(
            ((LibraryLinkWriteState?)LibraryLinkWriteState.Queued, (DateTime?)null, OtherFilmId),
            (item.WriteState, item.IdentifiedUtc, item.CatalogId));
        Assert.Equal((first.Id, LibraryLinkWriteState.Queued, (DateTime?)null), (queued.Id, queued.WriteState, queued.IdentifiedUtc));
        Assert.NotNull(first.IdentifiedUtc);
        var link = await _factory.SingleLinkAsync(cancellationToken);
        // R6 — the value it replaces is the old link's, which Jellyfin holds until it confirms the new one.
        Assert.Equal(
            (first.Id, OtherFilmId, OtherFilmImdbId, (string?)FilmId, LibraryLinkWriteState.Written),
            (link.Id, link.Value, link.ImdbId, link.SupersededValue, link.WriteState));
        Assert.NotNull(link.IdentifiedUtc);
        Assert.Equal(
            [
                AuditAction.LibraryLinkSet, AuditAction.LibraryItemIdentified,
                AuditAction.LibraryLinkReplaced, AuditAction.LibraryItemIdentified,
            ],
            await _factory.AuditActionsAsync(cancellationToken));
        Assert.Equal(["Movie", "Movie"], _jellyfin.RemoteSearches.Select(search => search.Endpoint));
        Assert.Equal(2, _jellyfin.Applied.Count);
    }

    [Fact]
    public async Task Replace_of_an_identified_film_identifies_again_and_the_item_ends_with_only_the_new_ids_and_no_item_is_posted()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync(tmdbId: null, imdbId: null);
        await SetAndIdentifyAsync(workId, new LibraryLinkRequest("Movie", FilmId));
        var asFirst = ProviderIdsOf(ShortId);

        //Act
        // TMDb gives the second film no IMDb id: Jellyfin's Identify replaces the item's ids with the result's, so the IMDb
        // id the first Identify left goes with it — and nothing is put back by the app.
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Movie", FilmWithoutImdbId));
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new Dictionary<string, string> { ["Tmdb"] = FilmId, ["Imdb"] = FilmImdbId }, asFirst);
        Assert.Equal(new Dictionary<string, string> { ["Tmdb"] = FilmWithoutImdbId }, ProviderIdsOf(ShortId));
        Assert.Equal(LibraryLinkWriteState.Written, (await _factory.SingleLinkAsync(cancellationToken)).WriteState);
        // Only the search and Apply ever change anything in Jellyfin: no POST of the item itself, no refresh.
        Assert.All(
            _factory.JellyfinHttp.Requests.Where(request => request.Method == HttpMethod.Post),
            request => Assert.StartsWith("/Items/RemoteSearch/", request.Uri.AbsolutePath, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Replace_of_a_link_a_bulk_click_queued_clears_its_batch_so_the_guard_does_not_refuse_the_ids_the_first_identify_left()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync(tmdbId: null, imdbId: null);
        await SetAndIdentifyAsync(workId, new LibraryLinkRequest("Movie", FilmId));
        // The first identification came from a bulk click: its batch protects the library's own ids.
        await _factory.WithDbAsync(db => db.LibraryItemLinks.ExecuteUpdateAsync(
            setters => setters.SetProperty(row => row.BatchId, (Guid?)Guid.NewGuid()), cancellationToken));

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Movie", OtherFilmId));
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // The item held the first film's Tmdb and Imdb ids, which a protected identification would have refused to discard:
        // the owner's own click is his word.
        var link = await _factory.SingleLinkAsync(cancellationToken);
        Assert.Equal((LibraryLinkWriteState.Written, (string?)null, (Guid?)null), (link.WriteState, link.WriteError, link.BatchId));
        Assert.Equal(new Dictionary<string, string> { ["Tmdb"] = OtherFilmId, ["Imdb"] = OtherFilmImdbId }, ProviderIdsOf(ShortId));
    }

    [Fact]
    public async Task Replace_of_a_written_series_link_identifies_the_new_show_in_the_background_and_the_replaced_link_does_not_wait_for_the_old_attempt()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncPenguinsAsync(cancellationToken);
        await SetAndIdentifyAsync(workId, new LibraryLinkRequest("Series", PlanetEarthId));
        var first = await _factory.SingleLinkAsync(cancellationToken);
        var asFirst = _jellyfin.Series.Single()["ProviderIds"]!.ToJsonString();

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Series", JellyfinSamples.PenguinsTmdbTvId));
        var queued = await _factory.SingleLinkAsync(cancellationToken);
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(LibraryLinkWriteState.Queued, queued.WriteState);
        var link = await _factory.SingleLinkAsync(cancellationToken);
        // R6 — the id it replaces is the one Jellyfin confirmed, the first link's. An Apply for the earlier link, minutes ago
        // or not, doesn't hold the new one back: the series wait counts only attempts since the link was set.
        Assert.Equal(
            (first.Id, JellyfinSamples.PenguinsTmdbTvId, (string?)PlanetEarthId, LibraryLinkWriteState.Written, (string?)null),
            (link.Id, link.Value, link.SupersededValue, link.WriteState, link.WriteError));
        Assert.Equal("""{"Tmdb":"1044","Imdb":"tt0795176"}""", asFirst);
        Assert.Equal("""{"Tmdb":"7869","Imdb":"tt0892700"}""", _jellyfin.Series.Single()["ProviderIds"]!.ToJsonString());
        Assert.Equal(
            [
                AuditAction.LibraryLinkSet, AuditAction.LibraryItemIdentified,
                AuditAction.LibraryLinkReplaced, AuditAction.LibraryItemIdentified,
            ],
            await _factory.AuditActionsAsync(cancellationToken));
        Assert.Equal(["Series", "Series"], _jellyfin.RemoteSearches.Select(search => search.Endpoint));
    }

    [Fact]
    public async Task Replace_of_a_link_jellyfin_never_confirmed_takes_the_id_it_replaces_from_the_library()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync(tmdbId: WrongTmdbId, imdbId: null);
        // An episode's link first, kept in the app: Jellyfin still holds its own wrong film id.
        await SetAsync(workId, new LibraryLinkRequest("Series", PlanetEarthId, SeasonNumber: "1"));

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Movie", FilmId));
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var link = await _factory.SingleLinkAsync(cancellationToken);
        Assert.Equal(
            (LibraryLinkTarget.Work, FilmId, (string?)WrongTmdbId, LibraryLinkWriteState.Written),
            (link.Target, link.Value, link.SupersededValue, link.WriteState));
    }

    [Fact]
    public async Task Replace_of_an_identified_film_with_an_episode_link_identifies_through_the_open_movie_database_when_tmdb_gives_the_imdb_id()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync(tmdbId: null, imdbId: null);
        await SetAndIdentifyAsync(workId, new LibraryLinkRequest("Movie", FilmId));

        //Act
        var response = await _factory.PutHttpRequest(
            LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Series", WwiiShowId, SeasonNumber: "1", EpisodeNumber: "1"));
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var link = await _factory.SingleLinkAsync(cancellationToken);
        // The film id the first Identify set is Jellyfin's to drop, not the library's own as of the last sync, which read none.
        Assert.Equal(
            (LibraryLinkTarget.ShowEpisode, LibraryLinkWriteState.Written, (string?)WwiiEpisodeImdbId, (string?)null),
            (link.Target, link.WriteState, link.EpisodeImdbId, link.SupersededValue));
        // The episode's own IMDb id through the Open Movie Database; Jellyfin's Identify drops the film id the first one set.
        var search = _jellyfin.RemoteSearches.Last();
        Assert.Equal("Movie", search.Endpoint);
        Assert.Contains("\"ProviderIds\":{\"Imdb\":\"tt9103946\"}", search.Body, StringComparison.Ordinal);
        Assert.Contains("\"SearchProviderName\":\"The Open Movie Database\"", search.Body, StringComparison.Ordinal);
        Assert.Equal(new Dictionary<string, string> { ["Imdb"] = WwiiEpisodeImdbId }, ProviderIdsOf(ShortId));
    }

    [Fact]
    public async Task Replace_of_an_identified_film_with_an_episode_link_tmdb_gives_no_imdb_id_keeps_the_link_in_the_app_and_sends_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync(tmdbId: null, imdbId: null);
        await SetAndIdentifyAsync(workId, new LibraryLinkRequest("Movie", FilmId));
        var asFilm = ProviderIdsOf(ShortId);
        var sent = _factory.JellyfinHttp.Requests.Count;

        //Act
        var response = await _factory.PutHttpRequest(
            LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Series", PlanetEarthId, SeasonNumber: "1", EpisodeNumber: "8"));
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var link = await _factory.SingleLinkAsync(cancellationToken);
        // Kept in the app as TMDb gives the episode no IMDb id — said on the link, as only asking TMDb tells it.
        Assert.Equal(
            (LibraryLinkTarget.ShowEpisode, MediaIdentifierScheme.TmdbTv, PlanetEarthId, LibraryLinkWriteState.KeptInApp, (string?)LibraryLinkWritability.NoEpisodeImdbReason),
            (link.Target, link.Scheme, link.Value, link.WriteState, link.WriteError));
        // Nothing is put back: Jellyfin keeps what the first Identify set, and nothing was sent for the replace.
        Assert.Equal(asFilm, ProviderIdsOf(ShortId));
        Assert.Equal(sent, _factory.JellyfinHttp.Requests.Count);
        Assert.Equal(
            [AuditAction.LibraryLinkSet, AuditAction.LibraryItemIdentified, AuditAction.LibraryLinkReplaced],
            await _factory.AuditActionsAsync(cancellationToken));
    }

    [Fact]
    public async Task Replace_while_jellyfin_is_switched_off_saves_the_link_not_written_and_changes_nothing_in_jellyfin()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync(tmdbId: null, imdbId: null);
        await SetAndIdentifyAsync(workId, new LibraryLinkRequest("Movie", FilmId));
        var asFirst = ProviderIdsOf(ShortId);
        await _factory.WithDbAsync(db => db.IntegrationConnections
            .Where(integration => integration.AdapterType == "jellyfin")
            .ExecuteUpdateAsync(setters => setters.SetProperty(integration => integration.IsEnabled, false), cancellationToken));
        var sent = _factory.JellyfinHttp.Requests.Count;

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Movie", OtherFilmId));

        //Assert
        // No longer a 409: nothing is put back, so a library that is off doesn't stand in the way of changing the link.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var link = await _factory.SingleLinkAsync(cancellationToken);
        Assert.Equal(
            (OtherFilmId, LibraryLinkWriteState.NotWritten, (string?)"“Living room” is switched off. Switch it on under Integrations, then retry."),
            (link.Value, link.WriteState, link.WriteError));
        Assert.Equal(asFirst, ProviderIdsOf(ShortId));
        Assert.Equal(sent, _factory.JellyfinHttp.Requests.Count);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static string LinkUrl(Guid workId) => $"{BaseUrl}/works/{workId}/tmdb";

    // The owner's Save, with a minute passing after it so the next change is a later instant.
    private async Task SetAsync(Guid workId, LibraryLinkRequest request)
    {
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
    }

    // The Save and the identification it queued.
    private async Task SetAndIdentifyAsync(Guid workId, LibraryLinkRequest request)
    {
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _factory.IdentifyQueuedLinksAsync(TestContext.Current.CancellationToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
    }

    private async Task<Guid> SyncShortAsync(string? tmdbId, string? imdbId)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        _jellyfinIntegration = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        _jellyfin.Movies.Add(JellyfinSamples.MovieWith(
            ShortId, "Landing Stripling", null, 1962, $"{JellyfinSamples.CartoonsRoot}/Tom & Jerry/120.mkv", imdbId, tmdbId));
        await _factory.SyncLibraryAsync(_jellyfinIntegration.Id, _owner.AccessToken, cancellationToken);
        await _integrations.Create("tmdb", "TMDb", FakeTmdbServer.BaseUrl, apiKey: FakeTmdbServer.Token);
        return await _factory.WorkOfItemAsync(ShortId, cancellationToken);
    }

    // The Penguins series with one episode, given a wrong show id by Jellyfin, synced, with TMDb connected.
    private async Task<Guid> SyncPenguinsAsync(CancellationToken cancellationToken)
    {
        _jellyfinIntegration = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        _jellyfin.Series.Add(JellyfinSamples.SeriesWith(PenguinsId, "The Penguins Of Madagascar", 2008, $"{JellyfinSamples.TvShowsRoot}/Penguins", tmdbId: WrongTmdbId));
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith("e5e5e5e5e5e5e5e5e5e5e5e5e5e5e5e5", "Gone in a Flash", PenguinsId, 1, 1, $"{JellyfinSamples.TvShowsRoot}/Penguins/S01E01.mkv"));
        await _factory.SyncLibraryAsync(_jellyfinIntegration.Id, _owner.AccessToken, cancellationToken);
        await _integrations.Create("tmdb", "TMDb", FakeTmdbServer.BaseUrl, apiKey: FakeTmdbServer.Token);
        return await _factory.WorkOfItemAsync(PenguinsId, cancellationToken);
    }

    private Dictionary<string, string> ProviderIdsOf(string itemId) =>
        _jellyfin.Movies.Single(movie => (string?)movie["Id"] == itemId)["ProviderIds"]!.AsObject()
            .ToDictionary(pair => pair.Key, pair => (string)pair.Value!);
}
