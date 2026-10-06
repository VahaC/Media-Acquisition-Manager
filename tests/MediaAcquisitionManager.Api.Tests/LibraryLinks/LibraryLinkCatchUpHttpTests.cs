using System.Net;
using System.Net.Http.Json;
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

// MAM-34 follow-up (Vasyl's decision of 2026-10-06) — the catch-up of the links set under MAM-34's first rules, which
// kept every series' and episode's link in the app and asked Jellyfin for no lookup (GET/POST
// /api/library/tmdb/catch-up): it counts exactly the links that write ids of their own and Jellyfin doesn't hold yet — a
// series' link and a numbered episode's on a film item kept in the app, any link not written — and the written links
// never looked up, a series' among them, attached, in a library switched on (series written like films: his final
// decision of the same day); nothing happens without the click, which queues them (202) for the background writer,
// which writes each — nothing posted where the ids are in place —, fetches an episode's IMDb id from TMDb, and asks
// Jellyfin to look each item up, with its own audit rows. A catch-up Jellyfin couldn't take leaves the links Jellyfin
// holds in it, their lookup missed, and counts every link again; an episode TMDb gives no IMDb id stays in the app and is counted no more, and one whose IMDb id is still to
// be asked isn't counted while no catalog is connected; a sync between the click and the writer leaves the queued links
// to the writer. Owner-only by its namespace (LibraryLinksAreOwnerOnlyHttpTests).
public sealed class LibraryLinkCatchUpHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string CatchUpUrl = "/api/library/tmdb/catch-up";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string FilmItemId = JellyfinSamples.LandingStriplingId;
    private const string OtherFilmItemId = "b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2";
    private const string EpisodeItemId = "c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3";
    private const string PenguinsId = "d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4";
    private const string FilmId = JellyfinSamples.LandingStriplingTmdbId;
    private const string FilmImdbId = JellyfinSamples.LandingStriplingImdbId;
    private const string OtherFilmId = "223022";
    private const string ShowId = TmdbSamples.TvEpisodeWwiiShowId;
    private const string EpisodeImdbId = TmdbSamples.TvEpisodeWwiiImdbId;

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private ILibraryItemLinkSeeder _links = null!;
    private AuthorizedUser _owner = null!;
    private FakeJellyfinServer _jellyfin = null!;
    private FakeTmdbServer _tmdb = null!;
    private IntegrationConnectionEntity _jellyfinIntegration = null!;

    public LibraryLinkCatchUpHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _links = new LibraryItemLinkSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        _jellyfin = new FakeJellyfinServer { ApiKey = ApiKey };
        _jellyfin.Movies.Clear();
        _jellyfin.Arm(_factory.JellyfinHttp);
        _tmdb = FakeTmdbServer.Arm(_factory.TmdbHttp);
        _tmdb.AddEpisode(new FakeTmdbEpisode(int.Parse(ShowId), 1, 1, "Серія 1", EpisodeImdbId, "2019-07-12", 50));
        _tmdb.AddEpisode(new FakeTmdbEpisode(int.Parse(ShowId), 1, 2, "Битва за Британію"));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task CatchUp_counts_the_kept_and_not_written_links_that_write_ids_and_the_written_links_never_looked_up_and_nothing_else()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var works = await SyncLibraryAsync(cancellationToken);
        // To write: a series' link and a numbered episode's link on a film item, kept in the app by MAM-34's first rules,
        // and a film link whose write failed.
        await _links.Create(_jellyfinIntegration, PenguinsId, works[PenguinsId], MediaIdentifierScheme.TmdbTv, JellyfinSamples.PenguinsTmdbTvId);
        await _links.Create(_jellyfinIntegration, EpisodeItemId, works[EpisodeItemId], MediaIdentifierScheme.TmdbTv, ShowId, LibraryLinkTarget.ShowEpisode,
            configure: row => (row.SeasonNumber, row.EpisodeNumber) = (1, 1));
        await _links.Create(_jellyfinIntegration, "e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2", works[FilmItemId], MediaIdentifierScheme.Tmdb, "2",
            writeState: LibraryLinkWriteState.NotWritten, configure: row => row.WriteError = "Couldn't reach Jellyfin.");
        // To look up: film links written, or read in Jellyfin, and a series' read there, never looked up.
        await _links.Create(_jellyfinIntegration, FilmItemId, works[FilmItemId], MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.Written);
        await _links.Create(_jellyfinIntegration, OtherFilmItemId, works[OtherFilmItemId], MediaIdentifierScheme.Tmdb, OtherFilmId, writeState: LibraryLinkWriteState.InLibrary);
        await _links.Create(_jellyfinIntegration, "e6e6e6e6e6e6e6e6e6e6e6e6e6e6e6e6", works[PenguinsId], MediaIdentifierScheme.TmdbTv, JellyfinSamples.PenguinsTmdbTvId,
            writeState: LibraryLinkWriteState.InLibrary);
        // Neither: one looked up already, an episode's link without its numbers or whose episode TMDb gives no IMDb id, an
        // item gone, a library switched off, and a library type that can't set ids.
        await _links.Create(_jellyfinIntegration, "e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1", works[FilmItemId], MediaIdentifierScheme.Tmdb, "1",
            writeState: LibraryLinkWriteState.Written, configure: row => row.LookupRequestedUtc = _factory.Time.GetUtcNow().UtcDateTime);
        await _links.Create(_jellyfinIntegration, "e3e3e3e3e3e3e3e3e3e3e3e3e3e3e3e3", works[EpisodeItemId], MediaIdentifierScheme.TmdbTv, ShowId, LibraryLinkTarget.ShowEpisode,
            configure: row => row.SeasonNumber = 1);
        await _links.Create(_jellyfinIntegration, "e4e4e4e4e4e4e4e4e4e4e4e4e4e4e4e4", works[EpisodeItemId], MediaIdentifierScheme.TmdbTv, ShowId, LibraryLinkTarget.ShowEpisode,
            configure: row => (row.SeasonNumber, row.EpisodeNumber, row.WriteError) = (1, 2, LibraryLinkWritability.NoEpisodeImdbReason));
        await _links.Create(_jellyfinIntegration, "e5e5e5e5e5e5e5e5e5e5e5e5e5e5e5e5", null, MediaIdentifierScheme.Tmdb, "5", writeState: LibraryLinkWriteState.NotWritten);
        var bedroom = await _integrations.Create("jellyfin", "Bedroom", "http://127.0.0.1:9/bedroom", apiKey: ApiKey);
        await _factory.WithDbAsync(db => db.IntegrationConnections
            .Where(integration => integration.Id == bedroom.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(integration => integration.IsEnabled, false), cancellationToken));
        await _links.Create(bedroom, FilmItemId, works[FilmItemId], MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.NotWritten);
        var oldLibrary = await _integrations.Create(name: "Old library");
        await _links.Create(oldLibrary, FilmItemId, works[FilmItemId], MediaIdentifierScheme.Tmdb, FilmId);

        //Act
        var response = await _factory.GetHttpRequest(CatchUpUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new LibraryLinkCatchUpResponse(3, 3), await ReadAsync<LibraryLinkCatchUpResponse>(response, cancellationToken));
        // Counting sends nothing and changes no link.
        Assert.DoesNotContain(_factory.JellyfinHttp.Requests, request => request.Method == HttpMethod.Post);
        await _factory.WithDbAsync(async db => Assert.DoesNotContain(
            await db.LibraryItemLinks.AsNoTracking().ToListAsync(cancellationToken), link => link.WriteState == LibraryLinkWriteState.Queued));
    }

    [Fact]
    public async Task CatchUp_queues_them_and_the_writer_writes_each_fetches_the_episodes_imdb_id_and_asks_jellyfin_to_look_each_up()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var works = await SyncLibraryAsync(cancellationToken);
        var episode = await _links.Create(
            _jellyfinIntegration, EpisodeItemId, works[EpisodeItemId], MediaIdentifierScheme.TmdbTv, ShowId, LibraryLinkTarget.ShowEpisode, setBy: _owner.User,
            configure: row => (row.SeasonNumber, row.EpisodeNumber) = (1, 1));
        // A film link whose write failed: Jellyfin doesn't hold it yet.
        var notWritten = await _links.Create(
            _jellyfinIntegration, OtherFilmItemId, works[OtherFilmItemId], MediaIdentifierScheme.Tmdb, OtherFilmId, writeState: LibraryLinkWriteState.NotWritten,
            setBy: _owner.User, configure: row => row.WriteError = "Couldn't reach Jellyfin.");
        // The film link MAM-34 wrote, its ids in Jellyfin, never looked up.
        Movie(FilmItemId)["ProviderIds"] = new System.Text.Json.Nodes.JsonObject { ["Tmdb"] = FilmId, ["Imdb"] = FilmImdbId };
        var film = await _links.Create(
            _jellyfinIntegration, FilmItemId, works[FilmItemId], MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.InLibrary, setBy: _owner.User,
            configure: row => row.ImdbId = FilmImdbId);
        // A series' link the first rules kept in the app: written like a film's now (Vasyl's final decision of 2026-10-06).
        var series = await _links.Create(
            _jellyfinIntegration, PenguinsId, works[PenguinsId], MediaIdentifierScheme.TmdbTv, JellyfinSamples.PenguinsTmdbTvId, setBy: _owner.User,
            configure: row => row.ImdbId = "tt0892700");
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var response = await _factory.PostHttpRequest(CatchUpUrl, _owner.AccessToken);
        var queued = await LinkStatesAsync(cancellationToken);
        var sentByTheClick = _factory.JellyfinHttp.Requests.Count(request => request.Method == HttpMethod.Post);
        var woken = _factory.Resolve<LibraryLinkWriteQueue>().Reader.TryPeek(out _);
        await _factory.Resolve<LibraryLinkWriteWorker>().RunQueuedAsync(cancellationToken);
        var after = await _factory.GetHttpRequest(CatchUpUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(new LibraryLinkCatchUpResponse(3, 1), await ReadAsync<LibraryLinkCatchUpResponse>(response, cancellationToken));
        // The click queues and wakes the writer; it sends nothing itself.
        Assert.Equal(
            [(episode.Id, LibraryLinkWriteState.Queued), (notWritten.Id, LibraryLinkWriteState.Queued), (film.Id, LibraryLinkWriteState.Queued), (series.Id, LibraryLinkWriteState.Queued)],
            new[] { episode.Id, notWritten.Id, film.Id, series.Id }.Select(id => (id, queued[id])));
        Assert.Equal(0, sentByTheClick);
        Assert.True(woken);

        var links = await LinksAsync(cancellationToken);
        Assert.All(links, link => Assert.Equal(
            (LibraryLinkWriteState.Written, (string?)null, (DateTime?)_factory.Time.GetUtcNow().UtcDateTime, (string?)null),
            (link.WriteState, link.WriteError, link.LookupRequestedUtc, link.LookupError)));
        // The episode's own IMDb id, asked of TMDb by the writer; the other film's id; the series' show id and its IMDb id;
        // the film's ids in place, so nothing posted for it — each item then looked up.
        Assert.Equal(EpisodeImdbId, links.Single(link => link.Id == episode.Id).EpisodeImdbId);
        Assert.Equal(EpisodeImdbId, (string?)Movie(EpisodeItemId)["ProviderIds"]!["Imdb"]);
        Assert.Equal(OtherFilmId, (string?)Movie(OtherFilmItemId)["ProviderIds"]!["Tmdb"]);
        Assert.Equal("""{"Tmdb":"7869","Imdb":"tt0892700"}""", _jellyfin.Series.Single()["ProviderIds"]!.ToJsonString());
        Assert.Single(_factory.TmdbHttp.Requests, request => request.Uri.AbsolutePath == $"/3/tv/{ShowId}/season/1/episode/1");
        Assert.DoesNotContain(
            _factory.JellyfinHttp.Requests,
            request => request.Method == HttpMethod.Post && request.Uri.AbsolutePath == $"/Items/{FilmItemId}");
        Assert.Equal([FilmItemId, OtherFilmItemId, EpisodeItemId, PenguinsId], _jellyfin.Lookups.Order(StringComparer.Ordinal));
        await _factory.WithDbAsync(async db =>
        {
            var audit = await db.AuditEvents.AsNoTracking().ToListAsync(cancellationToken);
            Assert.Equal(
                new[] { episode.Id, notWritten.Id, series.Id }.Order(),
                audit.Where(row => row.Action == AuditAction.LibraryIdsWritten).Select(row => row.TargetId!.Value).Order());
            Assert.Equal(
                new[] { episode.Id, notWritten.Id, film.Id, series.Id }.Order(),
                audit.Where(row => row.Action == AuditAction.LibraryMetadataLookupRequested).Select(row => row.TargetId!.Value).Order());
            // R13 — in the name of whoever set the links.
            Assert.All(audit, row => Assert.Equal("owner", row.ActorUserName));
        });
        Assert.Equal(new LibraryLinkCatchUpResponse(0, 0), await ReadAsync<LibraryLinkCatchUpResponse>(after, cancellationToken));
    }

    [Fact]
    public async Task CatchUp_jellyfin_cannot_take_leaves_the_links_it_holds_in_it_with_the_lookup_missed_and_counts_every_link_again()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var works = await SyncLibraryAsync(cancellationToken);
        // The film link MAM-34 wrote: Jellyfin holds its id, and the last sync read it there.
        Movie(FilmItemId)["ProviderIds"] = new System.Text.Json.Nodes.JsonObject { ["Tmdb"] = FilmId };
        var film = await _links.Create(_jellyfinIntegration, FilmItemId, works[FilmItemId], MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.InLibrary);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        await _factory.SyncLibraryAsync(_jellyfinIntegration.Id, _owner.AccessToken, cancellationToken);
        var episode = await _links.Create(
            _jellyfinIntegration, EpisodeItemId, works[EpisodeItemId], MediaIdentifierScheme.TmdbTv, ShowId, LibraryLinkTarget.ShowEpisode,
            configure: row => (row.SeasonNumber, row.EpisodeNumber) = (1, 1));
        // Jellyfin restarting: its connection test fails, so nothing is sent.
        _jellyfin.Intercept = request => request.RequestUri!.AbsolutePath.EndsWith("/System/Info", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : null;
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        await _factory.PostHttpRequest(CatchUpUrl, _owner.AccessToken);
        await _factory.Resolve<LibraryLinkWriteWorker>().RunQueuedAsync(cancellationToken);
        var after = await _factory.GetHttpRequest(CatchUpUrl, _owner.AccessToken);

        //Assert
        var links = await LinksAsync(cancellationToken);
        var episodeAfter = links.Single(link => link.Id == episode.Id);
        var filmAfter = links.Single(link => link.Id == film.Id);
        // Not in Jellyfin yet, with why; its IMDb id from TMDb kept for the next write.
        Assert.Equal((LibraryLinkWriteState.NotWritten, EpisodeImdbId), (episodeAfter.WriteState, episodeAfter.EpisodeImdbId));
        Assert.StartsWith("Jellyfin", episodeAfter.WriteError, StringComparison.Ordinal);
        // Jellyfin holds the film's id: still in Jellyfin, only its lookup missed — with the same sentence.
        Assert.Equal(
            (LibraryLinkWriteState.InLibrary, (string?)null, (DateTime?)null, episodeAfter.WriteError),
            (filmAfter.WriteState, filmAfter.WriteError, filmAfter.LookupRequestedUtc, filmAfter.LookupError));
        Assert.DoesNotContain(_factory.JellyfinHttp.Requests, request => request.Method == HttpMethod.Post);
        Assert.Empty(_jellyfin.Lookups);
        // The next click takes both back.
        Assert.Equal(new LibraryLinkCatchUpResponse(1, 1), await ReadAsync<LibraryLinkCatchUpResponse>(after, cancellationToken));
    }

    [Fact]
    public async Task CatchUp_counts_no_episode_link_whose_imdb_id_is_still_to_be_asked_while_no_catalog_is_connected()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var works = await SyncLibraryAsync(cancellationToken);
        await _factory.WithDbAsync(db => db.IntegrationConnections
            .Where(integration => integration.Kind == IntegrationKind.Metadata)
            .ExecuteUpdateAsync(setters => setters.SetProperty(integration => integration.IsEnabled, false), cancellationToken));
        // TMDb would have to be asked for this one's IMDb id; the other holds it already (not written: its item held another).
        await _links.Create(_jellyfinIntegration, EpisodeItemId, works[EpisodeItemId], MediaIdentifierScheme.TmdbTv, ShowId, LibraryLinkTarget.ShowEpisode,
            configure: row => (row.SeasonNumber, row.EpisodeNumber) = (1, 1));
        await _links.Create(_jellyfinIntegration, OtherFilmItemId, works[OtherFilmItemId], MediaIdentifierScheme.TmdbTv, ShowId, LibraryLinkTarget.ShowEpisode,
            writeState: LibraryLinkWriteState.NotWritten, configure: row => (row.SeasonNumber, row.EpisodeNumber, row.EpisodeImdbId) = (1, 1, EpisodeImdbId));

        //Act
        var response = await _factory.GetHttpRequest(CatchUpUrl, _owner.AccessToken);

        //Assert
        // Without a catalog the first one's write could only fail: it waits for one.
        Assert.Equal(new LibraryLinkCatchUpResponse(1, 0), await ReadAsync<LibraryLinkCatchUpResponse>(response, cancellationToken));
        Assert.Empty(_factory.TmdbHttp.Requests);
    }

    [Fact]
    public async Task CatchUp_of_an_episode_tmdb_gives_no_imdb_id_keeps_it_in_the_app_saying_why_and_counts_it_no_more()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var works = await SyncLibraryAsync(cancellationToken);
        await _links.Create(_jellyfinIntegration, EpisodeItemId, works[EpisodeItemId], MediaIdentifierScheme.TmdbTv, ShowId, LibraryLinkTarget.ShowEpisode,
            configure: row => (row.SeasonNumber, row.EpisodeNumber) = (1, 2));

        //Act
        var response = await _factory.PostHttpRequest(CatchUpUrl, _owner.AccessToken);
        await _factory.Resolve<LibraryLinkWriteWorker>().RunQueuedAsync(cancellationToken);
        var after = await _factory.GetHttpRequest(CatchUpUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(new LibraryLinkCatchUpResponse(1, 0), await ReadAsync<LibraryLinkCatchUpResponse>(response, cancellationToken));
        var link = Assert.Single(await LinksAsync(cancellationToken));
        Assert.Equal(
            (LibraryLinkWriteState.KeptInApp, (string?)"TMDb knows no IMDb id of this episode, so its link stays in the app.", (string?)null),
            (link.WriteState, link.WriteError, link.EpisodeImdbId));
        Assert.DoesNotContain(_factory.JellyfinHttp.Requests, request => request.Method == HttpMethod.Post);
        // Asked once: a second click would keep it in the app all the same.
        Assert.Equal(new LibraryLinkCatchUpResponse(0, 0), await ReadAsync<LibraryLinkCatchUpResponse>(after, cancellationToken));
    }

    [Fact]
    public async Task CatchUp_left_waiting_through_a_sync_is_still_the_writers_and_its_lookup_still_asked()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var works = await SyncLibraryAsync(cancellationToken);
        Movie(FilmItemId)["ProviderIds"] = new System.Text.Json.Nodes.JsonObject { ["Tmdb"] = FilmId };
        await _links.Create(_jellyfinIntegration, FilmItemId, works[FilmItemId], MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.Written);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        await _factory.PostHttpRequest(CatchUpUrl, _owner.AccessToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        // Jellyfin reports the link's id: a sync would mark it in Jellyfin, were it not waiting for its lookup.
        await _factory.SyncLibraryAsync(_jellyfinIntegration.Id, _owner.AccessToken, cancellationToken);
        var throughTheSync = Assert.Single(await LinksAsync(cancellationToken)).WriteState;
        await _factory.Resolve<LibraryLinkWriteWorker>().RunQueuedAsync(cancellationToken);

        //Assert
        Assert.Equal(LibraryLinkWriteState.Queued, throughTheSync);
        Assert.Equal(LibraryLinkWriteState.Written, Assert.Single(await LinksAsync(cancellationToken)).WriteState);
        Assert.Equal([FilmItemId], _jellyfin.Lookups);
    }

    [Fact]
    public async Task CatchUp_with_nothing_to_catch_up_is_202_with_nothing_queued_and_wakes_no_writer()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var works = await SyncLibraryAsync(cancellationToken);
        await _links.Create(_jellyfinIntegration, FilmItemId, works[FilmItemId], MediaIdentifierScheme.Tmdb, FilmId,
            writeState: LibraryLinkWriteState.InLibrary, configure: row => row.LookupRequestedUtc = _factory.Time.GetUtcNow().UtcDateTime);

        //Act
        var response = await _factory.PostHttpRequest(CatchUpUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(new LibraryLinkCatchUpResponse(0, 0), await ReadAsync<LibraryLinkCatchUpResponse>(response, cancellationToken));
        Assert.Equal(LibraryLinkWriteState.InLibrary, Assert.Single(await LinksAsync(cancellationToken)).WriteState);
        Assert.False(_factory.Resolve<LibraryLinkWriteQueue>().Reader.TryPeek(out _));
    }

    [Fact]
    public async Task CatchUp_without_a_token_is_401()
    {
        //Arrange

        //Act
        var read = await _factory.GetHttpRequest(CatchUpUrl);
        var start = await _factory.PostHttpRequest(CatchUpUrl);

        //Assert
        Assert.Equal((HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized), (read.StatusCode, start.StatusCode));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    // Two shorts, a documentary episode Jellyfin lists as a film and the Penguins series with one episode, none with ids,
    // synced, with TMDb connected: each item's work by its id.
    private async Task<Dictionary<string, Guid>> SyncLibraryAsync(CancellationToken cancellationToken)
    {
        _jellyfinIntegration = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        _jellyfin.Movies.Add(JellyfinSamples.MovieWith(FilmItemId, "Landing Stripling", null, 1962, "/media/cartoons/Tom & Jerry/120.mkv"));
        _jellyfin.Movies.Add(JellyfinSamples.MovieWith(OtherFilmItemId, "Calypso Cat", null, 1962, "/media/cartoons/Tom & Jerry/121.mkv"));
        _jellyfin.Movies.Add(JellyfinSamples.MovieWith(
            EpisodeItemId, "Greatest Events of World War II in Colour.S01E01.Blitzkrieg", null, 2019, "/media/documentary/WWII/S01E01.mkv"));
        _jellyfin.Series.Add(JellyfinSamples.SeriesWith(PenguinsId, "The Penguins Of Madagascar", 2008, "/media/tv/Penguins"));
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith("f5f5f5f5f5f5f5f5f5f5f5f5f5f5f5f5", "Gone in a Flash", PenguinsId, 1, 1, "/media/tv/Penguins/S01E01.mkv"));
        await _factory.SyncLibraryAsync(_jellyfinIntegration.Id, _owner.AccessToken, cancellationToken);
        await _integrations.Create("tmdb", "TMDb", FakeTmdbServer.BaseUrl, apiKey: FakeTmdbServer.Token);

        var works = new Dictionary<string, Guid>(StringComparer.Ordinal);
        await _factory.WithDbAsync(async db =>
        {
            foreach (var itemId in (string[])[FilmItemId, OtherFilmItemId, EpisodeItemId, PenguinsId])
            {
                works[itemId] = await db.MediaWorkTitles
                    .Where(title => title.SourceItemId == itemId)
                    .Select(title => title.MediaWorkId)
                    .FirstAsync(cancellationToken);
            }
        });
        return works;
    }

    private System.Text.Json.Nodes.JsonObject Movie(string itemId) => _jellyfin.Movies.Single(movie => (string?)movie["Id"] == itemId);

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<T>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;

    private async Task<List<LibraryItemLinkEntity>> LinksAsync(CancellationToken cancellationToken)
    {
        var links = new List<LibraryItemLinkEntity>();
        await _factory.WithDbAsync(async db => links.AddRange(await db.LibraryItemLinks.AsNoTracking().ToListAsync(cancellationToken)));
        return links;
    }

    private async Task<Dictionary<Guid, LibraryLinkWriteState>> LinkStatesAsync(CancellationToken cancellationToken) =>
        (await LinksAsync(cancellationToken)).ToDictionary(link => link.Id, link => link.WriteState);
}
