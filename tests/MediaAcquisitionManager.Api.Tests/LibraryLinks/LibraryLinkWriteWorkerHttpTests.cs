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
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.LibraryLinks;

// MAM-34, decision 6, R13 — "Accept all confident"'s background writer (the click itself: LibraryLinkReviewHttpTests): it
// claims the oldest queued link until none is left, each written with its own audit row in the name of whoever queued
// it, a library switched off left not written with its sentence (R12), every link counted by where its write left it; a
// database that fails before a link is claimed ends the pass with the links still queued, never the app; a series' link
// is kept in the app, never sent (Jellyfin's save of a series rewrites all its seasons and episodes); the reconciler turns
// a write a restart cut into NotWritten — an episode link's cut restore into KeptInApp, to be removed, and a series link a
// cut removal held back into KeptInApp with no write recorded — and wakes the writer for the queued ones; the click's
// results polled by its id (GET /api/library/tmdb/batches/{id}).
public sealed class LibraryLinkWriteWorkerHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string ShortId = JellyfinSamples.LandingStriplingId;
    private const string OtherShortId = "b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2";
    private const string PenguinsId = "d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4";
    private const string FilmId = JellyfinSamples.LandingStriplingTmdbId;
    private const string OtherFilmId = "223022";
    private const string RestartedError = "The app restarted while writing the ids into the library. Retry.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private ILibraryItemLinkSeeder _links = null!;
    private AuthorizedUser _owner = null!;
    private FakeJellyfinServer _jellyfin = null!;
    private IntegrationConnectionEntity _jellyfinIntegration = null!;

    public LibraryLinkWriteWorkerHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _links = new LibraryItemLinkSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        _jellyfin = new FakeJellyfinServer { ApiKey = ApiKey };
        _jellyfin.Movies.Clear();
        _jellyfin.Arm(_factory.JellyfinHttp);
        var tmdb = FakeTmdbServer.Arm(_factory.TmdbHttp);
        tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, int.Parse(FilmId), "Посадкова смуга"));
        tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, int.Parse(OtherFilmId), "Калипсо"));
        tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Series, int.Parse(JellyfinSamples.PenguinsTmdbTvId), "Пінгвіни з Мадагаскару", ImdbId: "tt0892700"));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task RunQueued_writes_every_queued_link_with_its_own_audit_row_in_the_name_of_whoever_queued_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (shortWorkId, otherWorkId) = await SyncShortsAsync(cancellationToken);
        var batchId = Guid.NewGuid();
        await QueueAsync(ShortId, shortWorkId, FilmId, batchId);
        await QueueAsync(OtherShortId, otherWorkId, OtherFilmId, batchId);

        //Act
        await _factory.Resolve<LibraryLinkWriteWorker>().RunQueuedAsync(cancellationToken);
        var polled = await _factory.GetHttpRequest($"{BaseUrl}/tmdb/batches/{batchId}", _owner.AccessToken);

        //Assert
        Assert.Equal((FilmId, OtherFilmId), (TmdbOf(ShortId), TmdbOf(OtherShortId)));
        Assert.Equal(HttpStatusCode.OK, polled.StatusCode);
        var results = await polled.Content.ReadFromJsonAsync<LibraryLinkBatchResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(batchId, results!.BatchId);
        Assert.Equal(
            [(shortWorkId, LibraryLinkWriteState.Written), (otherWorkId, LibraryLinkWriteState.Written)],
            results.Items.Select(item => (item.WorkId, Assert.Single(item.Links).WriteState)).OrderBy(item => item.WorkId == otherWorkId));
        await _factory.WithDbAsync(async db =>
        {
            var written = await db.AuditEvents.AsNoTracking().Where(auditEvent => auditEvent.Action == AuditAction.LibraryIdsWritten).ToListAsync(cancellationToken);
            Assert.Equal(2, written.Count);
            Assert.All(written, auditEvent => Assert.Equal((_owner.User.Id, "owner"), (auditEvent.ActorUserId!.Value, auditEvent.ActorUserName!)));
        });
        Assert.False(_factory.Resolve<LibraryLinkWriteQueue>().Reader.TryPeek(out _));
        // Counted by where each write left its link.
        Assert.Contains(
            _factory.Logs.Entries,
            entry => entry.Category == typeof(LibraryLinkWriteWorker).FullName
                && entry.Message == "The background writer settled 2 queued TMDb links: 2 written, 0 not written, 0 kept in the app or taken over meanwhile.");
    }

    [Fact]
    public async Task RunQueued_leaves_a_link_of_a_switched_off_library_not_written_with_its_sentence_and_sends_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (shortWorkId, _) = await SyncShortsAsync(cancellationToken);
        await QueueAsync(ShortId, shortWorkId, FilmId, Guid.NewGuid());
        await _factory.WithDbAsync(db => db.IntegrationConnections
            .Where(integration => integration.Id == _jellyfinIntegration.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(integration => integration.IsEnabled, false), cancellationToken));
        var sent = _factory.JellyfinHttp.Requests.Count;

        //Act
        await _factory.Resolve<LibraryLinkWriteWorker>().RunQueuedAsync(cancellationToken);

        //Assert
        var link = await SingleLinkAsync(cancellationToken);
        Assert.Equal(
            (LibraryLinkWriteState.NotWritten, "“Living room” is switched off. Switch it on under Integrations, then retry."),
            (link.WriteState, link.WriteError));
        Assert.Equal(sent, _factory.JellyfinHttp.Requests.Count);
        // Not counted as written: the log says what became of each link.
        Assert.Contains(
            _factory.Logs.Entries,
            entry => entry.Category == typeof(LibraryLinkWriteWorker).FullName
                && entry.Message == "The background writer settled 1 queued TMDb links: 0 written, 1 not written, 0 kept in the app or taken over meanwhile.");
    }

    [Fact]
    public async Task RunQueued_keeps_a_queued_series_link_in_the_app_and_sends_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var seriesWorkId = await SyncPenguinsAsync(cancellationToken);
        await _links.Create(
            _jellyfinIntegration, PenguinsId, seriesWorkId, MediaIdentifierScheme.TmdbTv, JellyfinSamples.PenguinsTmdbTvId,
            writeState: LibraryLinkWriteState.Queued, setBy: _owner.User);
        var sent = _factory.JellyfinHttp.Requests.Count;

        //Act
        await _factory.Resolve<LibraryLinkWriteWorker>().RunQueuedAsync(cancellationToken);

        //Assert
        // Jellyfin's save of a series rewrites all its seasons and episodes: the worker never sends one either.
        var link = await SingleLinkAsync(cancellationToken);
        Assert.Equal((LibraryLinkWriteState.KeptInApp, (string?)null), (link.WriteState, link.WriteError));
        Assert.Equal(sent, _factory.JellyfinHttp.Requests.Count);
        Assert.Contains(
            _factory.Logs.Entries,
            entry => entry.Category == typeof(LibraryLinkWriteWorker).FullName
                && entry.Message == "The background writer settled 1 queued TMDb links: 0 written, 0 not written, 1 kept in the app or taken over meanwhile.");
    }

    [Fact]
    public async Task RunQueued_that_cannot_read_the_next_link_logs_the_error_returns_and_leaves_the_links_queued()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (shortWorkId, _) = await SyncShortsAsync(cancellationToken);
        var queued = await QueueAsync(ShortId, shortWorkId, FilmId, Guid.NewGuid());
        // A long sync holds the database's write lock past the command timeout.
        _factory.DbCommandHook.BeforeNextCommand(
            command => command.CommandText.Contains("\"LibraryItemLinks\"", StringComparison.Ordinal),
            _ => throw new SqliteException("database is locked", 5));

        //Act
        var thrown = await Record.ExceptionAsync(() => _factory.Resolve<LibraryLinkWriteWorker>().RunQueuedAsync(cancellationToken));

        //Assert
        // Out of ExecuteAsync, it would stop the whole app (the host's default); the next signal tries again.
        Assert.Null(thrown);
        var error = Assert.Single(_factory.Logs.Entries, entry => entry.Category == typeof(LibraryLinkWriteWorker).FullName && entry.Level == LogLevel.Error);
        Assert.Equal(
            ("The background writer couldn't take the next queued TMDb link.", "database is locked"),
            (error.Message, error.Exception?.Message));
        var link = await SingleLinkAsync(cancellationToken);
        Assert.Equal((LibraryLinkWriteState.Queued, queued.WriteAttemptId), (link.WriteState, link.WriteAttemptId));
    }

    [Fact]
    public async Task Reconcile_turns_a_write_a_restart_cut_into_not_written_and_wakes_the_writer_for_the_queued_links()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (shortWorkId, otherWorkId) = await SyncShortsAsync(cancellationToken);
        var cut = await _links.Create(_jellyfinIntegration, ShortId, shortWorkId, MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.Writing);
        var queued = await _links.Create(_jellyfinIntegration, OtherShortId, otherWorkId, MediaIdentifierScheme.Tmdb, OtherFilmId, writeState: LibraryLinkWriteState.Queued);

        //Act
        await _factory.Resolve<LibraryLinkWriteReconciler>().ReconcileAsync(cancellationToken);
        var woken = _factory.Resolve<LibraryLinkWriteQueue>().Reader.TryPeek(out _);
        await _factory.Resolve<LibraryLinkWriteWorker>().RunQueuedAsync(cancellationToken);

        //Assert
        Assert.True(woken);
        await _factory.WithDbAsync(async db =>
        {
            var links = await db.LibraryItemLinks.AsNoTracking().ToDictionaryAsync(link => link.Id, cancellationToken);
            Assert.Equal((LibraryLinkWriteState.NotWritten, RestartedError), (links[cut.Id].WriteState, links[cut.Id].WriteError));
            Assert.Equal(LibraryLinkWriteState.Written, links[queued.Id].WriteState);
            // No save of it was recorded, so there is nothing a write could have sent for it to record (R10).
            Assert.DoesNotContain(await db.AuditEvents.AsNoTracking().ToListAsync(cancellationToken), auditEvent => auditEvent.TargetId == cut.Id);
        });
        // The cut write is the owner's to retry: the writer never sends it again by itself (house rule 8).
        Assert.Null(TmdbOf(ShortId));
        Assert.Equal(OtherFilmId, TmdbOf(OtherShortId));
    }

    [Fact]
    public async Task Reconcile_keeps_an_episode_link_whose_restore_a_restart_cut_in_the_app_saying_to_remove_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (shortWorkId, otherWorkId) = await SyncShortsAsync(cancellationToken);
        // An episode's link to its show is Writing only while the film ids the app wrote are put back.
        var restoring = await _links.Create(
            _jellyfinIntegration, ShortId, shortWorkId, MediaIdentifierScheme.TmdbTv, "1044", LibraryLinkTarget.ShowEpisode, LibraryLinkWriteState.Writing);
        await _links.Create(_jellyfinIntegration, OtherShortId, otherWorkId, MediaIdentifierScheme.Tmdb, OtherFilmId, writeState: LibraryLinkWriteState.Writing);

        //Act
        await _factory.Resolve<LibraryLinkWriteReconciler>().ReconcileAsync(cancellationToken);

        //Assert
        // Retry has nothing to write for it; removing it puts the item back.
        LibraryItemLinkEntity? link = null;
        await _factory.WithDbAsync(async db => link = await db.LibraryItemLinks.AsNoTracking().SingleAsync(row => row.Id == restoring.Id, cancellationToken));
        Assert.Equal(
            (LibraryLinkWriteState.KeptInApp,
                "The app restarted while putting back the ids it had written into the library. Remove the link to put them back."),
            (link!.WriteState, link.WriteError));
        Assert.Contains(
            _factory.Logs.Entries,
            entry => entry.Category == typeof(LibraryLinkWriteReconciler).FullName
                && entry.Message == "TMDb links at startup: 1 writes a restart cut marked not written (0 recorded as possibly sent), 0 links never written kept in the app, 1 restores a restart cut kept in the app, 0 queued links left to write.");
    }

    [Fact]
    public async Task Reconcile_gives_a_series_link_a_cut_removal_held_back_to_the_app_and_records_no_write_for_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var seriesWorkId = await SyncPenguinsAsync(cancellationToken);
        var saved = await _factory.PutHttpRequest(
            $"{BaseUrl}/works/{seriesWorkId}/tmdb", _owner.AccessToken, new LibraryLinkRequest("Series", JellyfinSamples.PenguinsTmdbTvId));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        // A removal claimed it (R9) — Writing, though it sends nothing for a series — and the app stopped before it deleted it.
        await _factory.WithDbAsync(db => db.LibraryItemLinks.ExecuteUpdateAsync(
            setters => setters.SetProperty(row => row.WriteState, LibraryLinkWriteState.Writing), cancellationToken));
        var sent = _factory.JellyfinHttp.Requests.Count;

        //Act
        await _factory.Resolve<LibraryLinkWriteReconciler>().ReconcileAsync(cancellationToken);

        //Assert
        // Never written: nothing to retry, and no write a later removal would think it must put back.
        var link = await SingleLinkAsync(cancellationToken);
        Assert.Equal((LibraryLinkWriteState.KeptInApp, (string?)null), (link.WriteState, link.WriteError));
        await _factory.WithDbAsync(async db => Assert.Equal(
            [AuditAction.LibraryLinkSet],
            await db.AuditEvents.AsNoTracking().Select(auditEvent => auditEvent.Action).ToListAsync(cancellationToken)));
        Assert.Equal(sent, _factory.JellyfinHttp.Requests.Count);
        Assert.Contains(
            _factory.Logs.Entries,
            entry => entry.Category == typeof(LibraryLinkWriteReconciler).FullName
                && entry.Message == "TMDb links at startup: 0 writes a restart cut marked not written (0 recorded as possibly sent), 1 links never written kept in the app, 0 restores a restart cut kept in the app, 0 queued links left to write.");
    }

    [Fact]
    public async Task Batch_of_a_click_no_link_has_is_a_bare_404()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/tmdb/batches/{Guid.NewGuid()}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    // A link "Accept all confident" saved under the click's batch, waiting for the writer, set by the owner.
    private Task<LibraryItemLinkEntity> QueueAsync(string itemId, Guid workId, string filmId, Guid batchId) =>
        _links.Create(
            _jellyfinIntegration, itemId, workId, MediaIdentifierScheme.Tmdb, filmId, writeState: LibraryLinkWriteState.Queued, setBy: _owner.User,
            configure: link => link.BatchId = batchId);

    // Two shorts of their own, with no ids: two works.
    private async Task<(Guid ShortWorkId, Guid OtherWorkId)> SyncShortsAsync(CancellationToken cancellationToken)
    {
        _jellyfinIntegration = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        _jellyfin.Movies.Add(JellyfinSamples.MovieWith(ShortId, "Landing Stripling", null, 1962, "/media/cartoons/Tom & Jerry/120.mkv"));
        _jellyfin.Movies.Add(JellyfinSamples.MovieWith(OtherShortId, "Calypso Cat", null, 1962, "/media/cartoons/Tom & Jerry/121.mkv"));
        await _factory.SyncLibraryAsync(_jellyfinIntegration.Id, _owner.AccessToken, cancellationToken);
        await _integrations.Create("tmdb", "TMDb", FakeTmdbServer.BaseUrl, apiKey: FakeTmdbServer.Token);
        return (await WorkOfAsync(ShortId, cancellationToken), await WorkOfAsync(OtherShortId, cancellationToken));
    }

    // The Penguins series with one episode, no ids: one series work.
    private async Task<Guid> SyncPenguinsAsync(CancellationToken cancellationToken)
    {
        _jellyfinIntegration = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        _jellyfin.Series.Add(JellyfinSamples.SeriesWith(PenguinsId, "The Penguins Of Madagascar", 2008, "/media/tv/Penguins"));
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith("e5e5e5e5e5e5e5e5e5e5e5e5e5e5e5e5", "Gone in a Flash", PenguinsId, 1, 1, "/media/tv/Penguins/S01E01.mkv"));
        await _factory.SyncLibraryAsync(_jellyfinIntegration.Id, _owner.AccessToken, cancellationToken);
        await _integrations.Create("tmdb", "TMDb", FakeTmdbServer.BaseUrl, apiKey: FakeTmdbServer.Token);
        return await WorkOfAsync(PenguinsId, cancellationToken);
    }

    private string? TmdbOf(string itemId) =>
        (string?)_jellyfin.Movies.Single(movie => (string?)movie["Id"] == itemId)["ProviderIds"]!.AsObject()["Tmdb"];

    private async Task<Guid> WorkOfAsync(string itemId, CancellationToken cancellationToken)
    {
        var workId = Guid.Empty;
        await _factory.WithDbAsync(async db => workId = await db.MediaWorkTitles
            .Where(title => title.SourceItemId == itemId)
            .Select(title => title.MediaWorkId)
            .FirstAsync(cancellationToken));
        return workId;
    }

    private async Task<LibraryItemLinkEntity> SingleLinkAsync(CancellationToken cancellationToken)
    {
        LibraryItemLinkEntity? link = null;
        await _factory.WithDbAsync(async db => link = await db.LibraryItemLinks.AsNoTracking().SingleAsync(cancellationToken));
        return link!;
    }
}
