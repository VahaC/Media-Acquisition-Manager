using System.Net;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.LibraryLinks;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using MediaAcquisitionManager.TestInfrastructure.Texts;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.LibraryLinks;

// MAM-34 (0.2.1, ADR 0026), R13 — the background writer, the only caller of Jellyfin's Identify: every flow queues and
// it takes the oldest queued link of its kind — the owner's own clicks before a bulk click's — claims it and identifies it
// with its own audit row in the name of whoever queued it. Bounded parallelism: two film lanes and one series lane, so at
// most three Applies are in flight and one series at a time, each lane long-lived and woken on its own (a film saved
// while a series is inside a long Apply doesn't wait for it). A circuit breaker stops a batch whose Applies keep not
// confirming: two NotConfirmed in a row settle its remaining queued links not written with nothing sent. A library
// switched off is left not written with its sentence (R12); a database that fails before a link is claimed ends that
// lane's run with the links still queued, never the app; a link that throws is settled not written and the next goes on.
// The reconciler turns an identification a restart cut into NotWritten and wakes the writer for the queued ones; the
// click's results are polled by its id (GET /api/library/tmdb/batches/{id}). The repair queues for the same writer:
// LibraryLinkCatchUpHttpTests.
public sealed class LibraryLinkWriteWorkerHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string ShortId = JellyfinSamples.LandingStriplingId;
    private const string OtherShortId = "b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2";
    private const string PenguinsId = "d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4";
    private const string FilmId = JellyfinSamples.LandingStriplingTmdbId;
    private const string OtherFilmId = "223022";
    private const string RestartedError =
        "The app restarted while identifying the item in Jellyfin. Jellyfin may still be working on it: Retry in a few minutes checks what it holds before anything is sent.";
    private const string NotConfirmedSentence = "Jellyfin took the match, but the item read again doesn't show it.";

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
        _jellyfin.AddFilm(FilmId, "Landing Stripling", 1962);
        _jellyfin.AddFilm(OtherFilmId, "Calypso Cat", 1962);
        _jellyfin.AddShow(JellyfinSamples.PenguinsTmdbTvId, "The Penguins of Madagascar", 2008, "tt0892700");
        _jellyfin.Arm(_factory.JellyfinHttp);
        var tmdb = FakeTmdbServer.Arm(_factory.TmdbHttp);
        tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, int.Parse(FilmId), "Посадкова смуга"));
        tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, int.Parse(OtherFilmId), "Калипсо"));
        tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Series, int.Parse(JellyfinSamples.PenguinsTmdbTvId), "Пінгвіни з Мадагаскару", ImdbId: "tt0892700"));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task RunQueued_identifies_every_queued_link_with_its_own_audit_row_in_the_name_of_whoever_queued_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (shortWorkId, otherWorkId) = await SyncShortsAsync(cancellationToken);
        var batchId = Guid.NewGuid();
        await QueueAsync(ShortId, shortWorkId, FilmId, batchId);
        await QueueAsync(OtherShortId, otherWorkId, OtherFilmId, batchId);

        //Act
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);
        var polled = await _factory.GetHttpRequest($"{BaseUrl}/tmdb/batches/{batchId}", _owner.AccessToken);

        //Assert
        Assert.Equal((FilmId, OtherFilmId), (TmdbOf(ShortId), TmdbOf(OtherShortId)));
        Assert.Equal(HttpStatusCode.OK, polled.StatusCode);
        var results = await polled.ReadAsync<LibraryLinkBatchResponse>(cancellationToken);
        Assert.Equal(batchId, results.BatchId);
        Assert.Equal(
            [(shortWorkId, LibraryLinkWriteState.Written), (otherWorkId, LibraryLinkWriteState.Written)],
            results.Items.Select(item => (item.WorkId, Assert.Single(item.Links).WriteState)).OrderBy(item => item.WorkId == otherWorkId));
        Assert.All(results.Items.SelectMany(item => item.Links), row => Assert.NotNull(row.IdentifiedUtc));
        await _factory.WithDbAsync(async db =>
        {
            var identified = await db.AuditEvents.AsNoTracking().Where(auditEvent => auditEvent.Action == AuditAction.LibraryItemIdentified).ToListAsync(cancellationToken);
            Assert.Equal(2, identified.Count);
            Assert.All(identified, auditEvent => Assert.Equal((_owner.User.Id, "owner"), (auditEvent.ActorUserId!.Value, auditEvent.ActorUserName!)));
        });
        Assert.False(_factory.Resolve<LibraryLinkWriteQueue>().Reader.TryPeek(out _));
        // Counted by where each identification left its link.
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
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);

        //Assert
        var link = await _factory.SingleLinkAsync(cancellationToken);
        Assert.Equal(
            (LibraryLinkWriteState.NotWritten, "“Living room” is switched off. Switch it on under Integrations, then retry."),
            (link.WriteState, TestTexts.En(link.WriteError)));
        Assert.Equal(sent, _factory.JellyfinHttp.Requests.Count);
        // Not counted as written: the log says what became of each link.
        Assert.Contains(
            _factory.Logs.Entries,
            entry => entry.Category == typeof(LibraryLinkWriteWorker).FullName
                && entry.Message == "The background writer settled 1 queued TMDb links: 0 written, 1 not written, 0 kept in the app or taken over meanwhile.");
    }

    [Fact]
    public async Task RunQueued_keeps_a_link_of_a_library_type_that_cannot_identify_in_the_app_without_an_error()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The fake type is a media library without the identify capability (ADR 0005).
        var library = await _integrations.Create(name: "Old library");
        await _links.Create(library, "a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1", null, MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.Queued);

        //Act
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);

        //Assert
        var link = await _factory.SingleLinkAsync(cancellationToken);
        Assert.Equal((LibraryLinkWriteState.KeptInApp, (string?)null), (link.WriteState, TestTexts.En(link.WriteError)));
        Assert.Contains(
            _factory.Logs.Entries,
            entry => entry.Category == typeof(LibraryLinkWriteWorker).FullName
                && entry.Message == "The background writer settled 1 queued TMDb links: 0 written, 0 not written, 1 kept in the app or taken over meanwhile.");
    }

    [Fact]
    public async Task RunQueued_identifies_a_queued_series_in_its_own_lane_while_films_run_beside_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectJellyfinAsync();
        AddFilmItem(ShortId, FilmId, "Landing Stripling");
        AddSeriesItem(PenguinsId, "The Penguins Of Madagascar");
        await QueueFilmLinkAsync(ShortId, FilmId, batchId: null);
        await QueueSeriesLinkAsync(PenguinsId, batchId: null);
        _jellyfin.ApplyDelay = TimeSpan.FromMilliseconds(400);

        //Act
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);

        //Assert
        var links = await _factory.LinksAsync(cancellationToken);
        // A link left not written names why: an exception the writer logged first, then each link's sentence beside its state.
        // This test failed once in a full run (2026-10-07) with a bare "Written ≠ NotWritten", and never again in forty runs.
        Assert.DoesNotContain(_factory.Logs.Entries, entry => entry.Level >= LogLevel.Error);
        Assert.All(links, link => Assert.Equal((LibraryLinkWriteState.Written, (string?)null), (link.WriteState, TestTexts.En(link.WriteError))));
        // The two Applies ran at the same time: the series didn't hold the film back, nor the film the series.
        var film = _jellyfin.Applied.Single(applied => applied.ItemId == ShortId);
        var series = _jellyfin.Applied.Single(applied => applied.ItemId == PenguinsId);
        Assert.True(film.StartedAt < series.EndedAt && series.StartedAt < film.EndedAt, "The film's and the series' Applies didn't overlap.");
        Assert.Equal(2, _jellyfin.PeakAppliesInFlight);
    }

    [Fact]
    public async Task RunQueued_runs_at_most_two_film_identifications_at_once_and_one_series_at_a_time()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectJellyfinAsync();
        for (var number = 1; number <= 5; number++)
        {
            AddFilmItem(ItemId(number), $"4000{number}", $"Film {number}");
            await QueueFilmLinkAsync(ItemId(number), $"4000{number}", batchId: null);
        }

        for (var number = 1; number <= 3; number++)
        {
            AddSeriesItem(ItemId(100 + number), $"Show {number}");
            await QueueSeriesLinkAsync(ItemId(100 + number), batchId: null);
        }

        _jellyfin.ApplyDelay = TimeSpan.FromMilliseconds(150);

        //Act
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);

        //Assert
        Assert.All(await _factory.LinksAsync(cancellationToken), link => Assert.Equal(LibraryLinkWriteState.Written, link.WriteState));
        Assert.Equal(8, _jellyfin.Applied.Count);
        // The lane counts are the two constants' own numbers: films two at once, series one.
        Assert.Equal(
            (LibraryLinkWriteWorker.FilmLaneCount, LibraryLinkWriteWorker.SeriesLaneCount),
            (_jellyfin.PeakAppliesInFlightByType["Movie"], _jellyfin.PeakAppliesInFlightByType["Series"]));
        Assert.Equal(LibraryLinkWriteWorker.FilmLaneCount + LibraryLinkWriteWorker.SeriesLaneCount, _jellyfin.PeakAppliesInFlight);
    }

    [Fact]
    public async Task RunQueued_run_twice_at_once_identifies_each_link_once_and_applies_each_item_once()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectJellyfinAsync();
        for (var number = 1; number <= 6; number++)
        {
            AddFilmItem(ItemId(number), $"4000{number}", $"Film {number}");
            await QueueFilmLinkAsync(ItemId(number), $"4000{number}", batchId: null);
        }

        _jellyfin.ApplyDelay = TimeSpan.FromMilliseconds(40);
        var worker = _factory.Resolve<LibraryLinkWriteWorker>();

        //Act
        await Task.WhenAll(worker.RunQueuedAsync(cancellationToken), worker.RunQueuedAsync(cancellationToken));

        //Assert
        // The atomic claim is what keeps one item from two Applies: a lost race passes over the link.
        Assert.All(await _factory.LinksAsync(cancellationToken), link => Assert.Equal(LibraryLinkWriteState.Written, link.WriteState));
        Assert.Equal(
            Enumerable.Range(1, 6).Select(ItemId).Order(StringComparer.Ordinal),
            _jellyfin.Applied.Select(applied => applied.ItemId).Order(StringComparer.Ordinal));
        Assert.Equal(6, (await _factory.AuditActionsAsync(cancellationToken)).Count(action => action == AuditAction.LibraryItemIdentified));
    }

    [Fact]
    public async Task ExecuteAsync_identifies_a_film_queued_while_a_series_is_being_identified_without_waiting_for_the_series()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectJellyfinAsync();
        AddSeriesItem(PenguinsId, "The Penguins Of Madagascar");
        AddFilmItem(ShortId, FilmId, "Landing Stripling");
        var seriesLink = await QueueSeriesLinkAsync(PenguinsId, batchId: null);
        // A series' Apply runs for minutes in Jellyfin; here for a few seconds.
        _jellyfin.ApplyDelay = TimeSpan.FromSeconds(4);
        var worker = _factory.Resolve<LibraryLinkWriteWorker>();
        var queue = _factory.Resolve<LibraryLinkWriteQueue>();
        await worker.StartAsync(cancellationToken);

        try
        {
            queue.Signal();
            await WaitForAsync(() => Task.FromResult(_jellyfin.PeakAppliesInFlightByType.GetValueOrDefault("Series") == 1), cancellationToken);
            // The series is inside its Apply: a film saved now must not wait for it.
            _jellyfin.ApplyDelay = TimeSpan.Zero;
            var filmLink = await QueueFilmLinkAsync(ShortId, FilmId, batchId: null);
            queue.Signal();

            //Act
            await WaitForAsync(async () => (await LinkAsync(filmLink.Id, cancellationToken)).WriteState == LibraryLinkWriteState.Written, cancellationToken);
            var seriesWhileFilmIsDone = (await LinkAsync(seriesLink.Id, cancellationToken)).WriteState;
            await WaitForAsync(async () => (await LinkAsync(seriesLink.Id, cancellationToken)).WriteState == LibraryLinkWriteState.Written, cancellationToken);

            //Assert
            Assert.Equal(LibraryLinkWriteState.Writing, seriesWhileFilmIsDone);
            var film = _jellyfin.Applied.Single(applied => applied.ItemId == ShortId);
            var series = _jellyfin.Applied.Single(applied => applied.ItemId == PenguinsId);
            Assert.True(film.EndedAt < series.EndedAt, "The film was identified only after the series.");
            Assert.True(series.StartedAt < film.StartedAt, "The series' Apply hadn't started when the film's did.");
        }
        finally
        {
            await worker.StopAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task RunQueued_identifies_a_dialog_queued_link_before_the_rest_of_a_running_batch()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectJellyfinAsync();
        var batchId = Guid.NewGuid();
        // The repair's links, queued at one instant...
        for (var number = 1; number <= 6; number++)
        {
            AddFilmItem(ItemId(number), $"4000{number}", $"Film {number}");
            await QueueFilmLinkAsync(ItemId(number), $"4000{number}", batchId);
        }

        // ...and the owner's own Save a minute later, from the dialog (no batch): he is waiting at the screen.
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        AddFilmItem(ItemId(99), "49999", "Film 99");
        await QueueFilmLinkAsync(ItemId(99), "49999", batchId: null);
        _jellyfin.ApplyDelay = TimeSpan.FromMilliseconds(80);

        //Act
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);

        //Assert
        var order = _jellyfin.Applied.OrderBy(applied => applied.StartedAt).Select(applied => applied.ItemId).ToList();
        Assert.Equal(7, order.Count);
        Assert.True(order.IndexOf(ItemId(99)) < LibraryLinkWriteWorker.FilmLaneCount, $"The dialog's link was identified at position {order.IndexOf(ItemId(99))}.");
    }

    [Fact]
    public async Task Batch_that_fails_to_confirm_twice_in_a_row_is_stopped_and_the_rest_is_not_sent()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var library = await _integrations.Create(FakeLibraryItemIdentifier.TypeId, "Fake library");
        var work = await new MediaWorkSeeder(_factory.Scope).CreateMovie(library, "Landing Stripling", 1962);
        var stoppable = Guid.NewGuid();
        var otherBatch = Guid.NewGuid();
        HashSet<string> failing = [.. Enumerable.Range(1, 20).Select(ItemId)];
        foreach (var number in Enumerable.Range(1, 20))
            await QueueFakeLinkAsync(library, work.Id, ItemId(number), stoppable);
        // Another batch, and the owner's own link, are not the stopped batch's: they identify.
        await QueueFakeLinkAsync(library, work.Id, ItemId(50), otherBatch);
        await QueueFakeLinkAsync(library, work.Id, ItemId(51), batchId: null);
        _factory.FakeLibraryIdentifier.Identification = request => failing.Contains(request.SourceItemId)
            ? new LibraryIdentification(LibraryIdentifyOutcome.NotConfirmed, null, Sent: true, [], [request.Identifier], Message.Legacy(NotConfirmedSentence))
            : FakeLibraryItemIdentifier.Identified(request);

        //Act
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);

        //Assert
        var links = (await _factory.LinksAsync(cancellationToken)).ToDictionary(link => link.SourceItemId);
        var asked = _factory.FakeLibraryIdentifier.Requests.Select(request => request.SourceItemId).Where(failing.Contains).ToList();
        // Two in a row tripped it: at least two were sent, and the rest of the batch was never started — but for the link a
        // lane was already on its way to when the stop came, however long the update that settles the batch waits for SQLite's
        // write lock (a lane that finds a link of a stopped batch settles it itself).
        Assert.True(asked.Count >= LibraryLinkWriteWorker.BatchStopAfterNotConfirmed, "Fewer than two links were sent.");
        Assert.True(
            asked.Count <= LibraryLinkWriteWorker.BatchStopAfterNotConfirmed + LibraryLinkWriteWorker.FilmLaneCount,
            $"The batch was stopped only after {asked.Count} were sent.");
        foreach (var number in Enumerable.Range(1, 20))
        {
            var link = links[ItemId(number)];
            if (asked.Contains(link.SourceItemId))
            {
                Assert.Equal((LibraryLinkWriteState.NotWritten, NotConfirmedSentence, (DateTime?)null), (link.WriteState, TestTexts.En(link.WriteError), link.IdentifiedUtc));
            }
            else
            {
                Assert.Equal((LibraryLinkWriteState.NotWritten, LibraryLinkWriteWorker.BatchStoppedError), (link.WriteState, link.WriteError));
            }
        }

        // The stopped links sent nothing, so they have no audit row; the ones that were sent do.
        var audited = (await _factory.AuditAsync(cancellationToken)).Where(row => row.Action == AuditAction.LibraryItemIdentified).Select(row => row.TargetId).ToList();
        Assert.Equal(
            asked.Select(item => (Guid?)links[item].Id).Concat([links[ItemId(50)].Id, links[ItemId(51)].Id]).Order(),
            audited.Order());
        // Another batch's link and the owner's own link are untouched by the stop.
        Assert.Equal((LibraryLinkWriteState.Written, LibraryLinkWriteState.Written), (links[ItemId(50)].WriteState, links[ItemId(51)].WriteState));
        // One Warning for the stop — the links already inside a call when it came finish unwatched, and never stop it twice.
        Assert.Single(_factory.Logs.Entries, entry => entry.Category == typeof(LibraryLinkWriteWorker).FullName && entry.Level == LogLevel.Warning);
        // Nothing was sent for the stopped links, so the next repair click takes them again.
        var catchUp = await (await _factory.GetHttpRequest("/api/library/tmdb/catch-up", _owner.AccessToken)).ReadAsync<LibraryLinkCatchUpResponse>(cancellationToken);
        Assert.Equal(20 - asked.Count, catchUp.ToIdentify);
    }

    [Fact]
    public async Task Batch_whose_failures_are_separated_by_confirmed_identifications_is_never_stopped()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var library = await _integrations.Create(FakeLibraryItemIdentifier.TypeId, "Fake library");
        var work = await new MediaWorkSeeder(_factory.Scope).CreateMovie(library, "Landing Stripling", 1962);
        var batchId = Guid.NewGuid();
        // Series links, so the one series lane takes them one after another: with two film lanes the order in which two
        // answers are settled — a lane waiting for SQLite's write lock while the other settles three — can put the 1st and
        // 5th failure next to each other, which is a different thing from what this test says (it flaked once in five runs).
        foreach (var number in Enumerable.Range(1, 12))
            await QueueFakeLinkAsync(library, work.Id, ItemId(number), batchId, MediaWorkKind.Series);
        // The 1st, 5th and 9th answers don't confirm: never two in a row, so a count that reset on every confirmation
        // stays below the stop, while one that didn't would have stopped the batch at the second failure.
        var calls = 0;
        _factory.FakeLibraryIdentifier.Identification = request => Interlocked.Increment(ref calls) % 4 == 1
            ? new LibraryIdentification(LibraryIdentifyOutcome.NotConfirmed, null, Sent: true, [], [request.Identifier], Message.Legacy(NotConfirmedSentence))
            : FakeLibraryItemIdentifier.Identified(request);

        //Act
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);

        //Assert
        var links = await _factory.LinksAsync(cancellationToken);
        Assert.Equal(12, _factory.FakeLibraryIdentifier.Requests.Count);
        Assert.Equal(3, links.Count(link => TestTexts.En(link.WriteError) == NotConfirmedSentence));
        Assert.Equal(9, links.Count(link => link.WriteState == LibraryLinkWriteState.Written));
        Assert.DoesNotContain(links, link => link.WriteError == LibraryLinkWriteWorker.BatchStoppedError);
        Assert.DoesNotContain(_factory.Logs.Entries, entry => entry.Category == typeof(LibraryLinkWriteWorker).FullName && entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task RunQueued_settles_a_link_whose_identify_throws_not_written_and_goes_on()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var library = await _integrations.Create(FakeLibraryItemIdentifier.TypeId, "Fake library");
        var work = await new MediaWorkSeeder(_factory.Scope).CreateMovie(library, "Landing Stripling", 1962);
        var batchId = Guid.NewGuid();
        await QueueFakeLinkAsync(library, work.Id, ItemId(1), batchId);
        await QueueFakeLinkAsync(library, work.Id, ItemId(2), batchId);
        // A bug in an adapter: one link's identification throws.
        _factory.FakeLibraryIdentifier.Identification = request => request.SourceItemId == ItemId(1)
            ? throw new InvalidOperationException("the adapter broke")
            : FakeLibraryItemIdentifier.Identified(request);

        //Act
        var thrown = await Record.ExceptionAsync(() => _factory.IdentifyQueuedLinksAsync(cancellationToken));

        //Assert
        Assert.Null(thrown);
        var links = (await _factory.LinksAsync(cancellationToken)).ToDictionary(link => link.SourceItemId);
        Assert.Equal((LibraryLinkWriteState.NotWritten, LibraryLinkWriter.AppErrorMessage), (links[ItemId(1)].WriteState, links[ItemId(1)].WriteError));
        Assert.Equal(LibraryLinkWriteState.Written, links[ItemId(2)].WriteState);
        var error = Assert.Single(_factory.Logs.Entries, entry => entry.Category == typeof(LibraryLinkWriter).FullName && entry.Level == LogLevel.Error);
        Assert.Equal("the adapter broke", error.Exception?.Message);
        Assert.Contains(
            _factory.Logs.Entries,
            entry => entry.Category == typeof(LibraryLinkWriteWorker).FullName
                && entry.Message == "The background writer settled 2 queued TMDb links: 1 written, 1 not written, 0 kept in the app or taken over meanwhile.");
    }

    [Fact]
    public async Task RunQueued_that_cannot_read_the_next_link_logs_the_error_per_lane_returns_and_leaves_the_links_queued()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (shortWorkId, _) = await SyncShortsAsync(cancellationToken);
        var queued = await QueueAsync(ShortId, shortWorkId, FilmId, Guid.NewGuid());
        // A long sync holds the database's write lock past the command timeout. An arming runs once, and the writer reads
        // for the next link in each of its lanes, so one is armed per lane.
        for (var lane = 0; lane < LibraryLinkWriteWorker.FilmLaneCount + LibraryLinkWriteWorker.SeriesLaneCount; lane++)
        {
            _factory.DbCommandHook.BeforeNextCommand(
                command => command.CommandText.Contains("\"LibraryItemLinks\"", StringComparison.Ordinal),
                _ => throw new SqliteException("database is locked", 5));
        }

        //Act
        var thrown = await Record.ExceptionAsync(() => _factory.IdentifyQueuedLinksAsync(cancellationToken));

        //Assert
        // Out of ExecuteAsync, it would stop the whole app (the host's default); the next signal tries again.
        Assert.Null(thrown);
        var errors = _factory.Logs.Entries.Where(entry => entry.Category == typeof(LibraryLinkWriteWorker).FullName && entry.Level == LogLevel.Error).ToList();
        Assert.Equal(LibraryLinkWriteWorker.FilmLaneCount + LibraryLinkWriteWorker.SeriesLaneCount, errors.Count);
        Assert.All(errors, error => Assert.Equal(
            ("The background writer couldn't take the next queued TMDb link.", "database is locked"),
            (error.Message, error.Exception?.Message)));
        var link = await _factory.SingleLinkAsync(cancellationToken);
        Assert.Equal((LibraryLinkWriteState.Queued, queued.WriteAttemptId), (link.WriteState, link.WriteAttemptId));
    }

    [Fact]
    public async Task Reconcile_turns_an_identify_a_restart_cut_into_not_written_with_the_retry_sentence_and_wakes_the_writer_for_the_queued_links()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (shortWorkId, otherWorkId) = await SyncShortsAsync(cancellationToken);
        var cut = await _links.Create(_jellyfinIntegration, ShortId, shortWorkId, MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.Writing);
        var queued = await _links.Create(_jellyfinIntegration, OtherShortId, otherWorkId, MediaIdentifierScheme.Tmdb, OtherFilmId, writeState: LibraryLinkWriteState.Queued);

        //Act
        await _factory.Resolve<LibraryLinkWriteReconciler>().ReconcileAsync(cancellationToken);
        var woken = _factory.Resolve<LibraryLinkWriteQueue>().Reader.TryPeek(out _);
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);

        //Assert
        Assert.True(woken);
        await _factory.WithDbAsync(async db =>
        {
            var links = await db.LibraryItemLinks.AsNoTracking().ToDictionaryAsync(link => link.Id, cancellationToken);
            Assert.Equal((LibraryLinkWriteState.NotWritten, RestartedError), (links[cut.Id].WriteState, TestTexts.En(links[cut.Id].WriteError)));
            Assert.Equal(LibraryLinkWriteState.Written, links[queued.Id].WriteState);
        });
        // The cut identification is the owner's to retry: the writer never sends it again by itself (house rule 8).
        Assert.Null(TmdbOf(ShortId));
        Assert.Equal(OtherFilmId, TmdbOf(OtherShortId));
        Assert.Contains(
            _factory.Logs.Entries,
            entry => entry.Category == typeof(LibraryLinkWriteReconciler).FullName
                && entry.Message == "TMDb links at startup: 1 identifications a restart cut marked not written, 0 links never identified by rule kept in the app, 1 queued links left to identify.");
    }

    [Fact]
    public async Task Reconcile_gives_a_link_that_is_never_identified_by_rule_back_to_the_app_without_an_error()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (shortWorkId, _) = await SyncShortsAsync(cancellationToken);
        // An episode's link without its numbers has no id of its own to identify by; one claim a restart cut leaves it Writing.
        var episode = await _links.Create(
            _jellyfinIntegration, ShortId, shortWorkId, MediaIdentifierScheme.TmdbTv, "1044", LibraryLinkTarget.ShowEpisode, LibraryLinkWriteState.Writing,
            configure: row => row.WriteError = Message.Legacy("left over"));
        // And one of a library type that can't identify.
        var library = await _integrations.Create(name: "Old library");
        var work = await new MediaWorkSeeder(_factory.Scope).Create(library);
        var unsupported = await _links.Create(library, MediaWorkSeeder.SourceItemId, work.Id, MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.Writing);

        //Act
        await _factory.Resolve<LibraryLinkWriteReconciler>().ReconcileAsync(cancellationToken);

        //Assert
        var links = (await _factory.LinksAsync(cancellationToken)).ToDictionary(link => link.Id);
        Assert.Equal((LibraryLinkWriteState.KeptInApp, (string?)null), (links[episode.Id].WriteState, TestTexts.En(links[episode.Id].WriteError)));
        Assert.Equal((LibraryLinkWriteState.KeptInApp, (string?)null), (links[unsupported.Id].WriteState, TestTexts.En(links[unsupported.Id].WriteError)));
        Assert.Empty(_factory.FakeAdapter.Calls);
        Assert.Contains(
            _factory.Logs.Entries,
            entry => entry.Category == typeof(LibraryLinkWriteReconciler).FullName
                && entry.Message == "TMDb links at startup: 0 identifications a restart cut marked not written, 2 links never identified by rule kept in the app, 0 queued links left to identify.");
    }

    [Fact]
    public async Task Reconcile_records_no_audit_row_for_a_cut_identify_and_keeps_its_identified_time()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (shortWorkId, _) = await SyncShortsAsync(cancellationToken);
        var identifiedAt = _factory.Time.GetUtcNow().UtcDateTime.AddDays(-1);
        var cut = await _links.Create(
            _jellyfinIntegration, ShortId, shortWorkId, MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.Writing,
            configure: row => row.IdentifiedUtc = identifiedAt);

        //Act
        await _factory.Resolve<LibraryLinkWriteReconciler>().ReconcileAsync(cancellationToken);

        //Assert
        // Nothing is put back and nothing recorded: a restart proves nothing about the item, and Retry reads it first.
        var link = await _factory.SingleLinkAsync(cancellationToken);
        Assert.Equal((cut.Id, LibraryLinkWriteState.NotWritten, (DateTime?)identifiedAt), (link.Id, link.WriteState, link.IdentifiedUtc));
        Assert.Empty(await _factory.AuditAsync(cancellationToken));
    }

    [Fact]
    public async Task ExecuteAsync_never_runs_two_series_identifications_at_once_however_the_signals_come_in()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var library = await _integrations.Create(FakeLibraryItemIdentifier.TypeId, "Fake library");
        var work = await new MediaWorkSeeder(_factory.Scope).CreateMovie(library, "Landing Stripling", 1962);
        // Each identification takes a moment, so a second run of the one series lane would overlap the first.
        _factory.FakeLibraryIdentifier.Identification = request =>
        {
            Thread.Sleep(1);
            return FakeLibraryItemIdentifier.Identified(request);
        };
        var worker = _factory.Resolve<LibraryLinkWriteWorker>();
        var queue = _factory.Resolve<LibraryLinkWriteQueue>();
        await worker.StartAsync(cancellationToken);

        //Act
        try
        {
            // A signal with every link, as the dialogs and the clicks give them: the lane keeps going idle and being woken, which
            // is where a lane that forgot it had gone idle would be started twice.
            for (var number = 1; number <= 120; number++)
            {
                await QueueFakeLinkAsync(library, work.Id, ItemId(number), batchId: null, MediaWorkKind.Series);
                queue.Signal();
            }

            await WaitForAsync(async () => (await _factory.LinksAsync(cancellationToken)).All(link => link.WriteState == LibraryLinkWriteState.Written), cancellationToken);
        }
        finally
        {
            await worker.StopAsync(cancellationToken);
        }

        //Assert
        Assert.Equal(120, _factory.FakeLibraryIdentifier.Requests.Select(request => request.SourceItemId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(120, _factory.FakeLibraryIdentifier.Requests.Count);
        Assert.True(
            _factory.FakeLibraryIdentifier.PeakIdentificationsInFlight <= LibraryLinkWriteWorker.SeriesLaneCount,
            $"{_factory.FakeLibraryIdentifier.PeakIdentificationsInFlight} series were identified at once.");
    }

    [Fact]
    public async Task ExecuteAsync_stopped_while_a_batch_runs_finishes_the_applies_it_began_and_leaves_the_rest_queued_with_nothing_marked_failed()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectJellyfinAsync();
        var batchId = Guid.NewGuid();
        for (var number = 1; number <= 10; number++)
        {
            AddFilmItem(ItemId(number), $"4000{number}", $"Film {number}");
            await QueueFilmLinkAsync(ItemId(number), $"4000{number}", batchId);
        }

        _jellyfin.ApplyDelay = TimeSpan.FromMilliseconds(300);
        var worker = _factory.Resolve<LibraryLinkWriteWorker>();
        var queue = _factory.Resolve<LibraryLinkWriteQueue>();
        await worker.StartAsync(cancellationToken);
        queue.Signal();
        await WaitForAsync(() => Task.FromResult(_jellyfin.PeakAppliesInFlight >= 1), cancellationToken);

        //Act
        // The host stops while Applies are inside Jellyfin: the stop waits for each lane's link to settle.
        await worker.StopAsync(cancellationToken);

        //Assert
        var links = await _factory.LinksAsync(cancellationToken);
        Assert.DoesNotContain(links, link => link.WriteState == LibraryLinkWriteState.Writing);
        // An Apply that left the app is Written with its audit row; none is marked failed because the host stopped.
        var sent = _jellyfin.Applied.Select(applied => applied.ItemId).ToList();
        Assert.NotEmpty(sent);
        Assert.Equal(
            sent.Order(StringComparer.Ordinal),
            links.Where(link => link.WriteState == LibraryLinkWriteState.Written).Select(link => link.SourceItemId).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(links, link => link.WriteState == LibraryLinkWriteState.NotWritten);
        Assert.Equal(10 - sent.Count, links.Count(link => link.WriteState == LibraryLinkWriteState.Queued && link.BatchId == batchId));
        Assert.True(sent.Count < 10, "The stop didn't leave any link queued.");
        Assert.Equal(sent.Count, (await _factory.AuditActionsAsync(cancellationToken)).Count(action => action == AuditAction.LibraryItemIdentified));
    }

    [Fact]
    public async Task ExecuteAsync_keeps_the_dialog_and_the_progress_answering_while_a_series_is_inside_its_apply()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectJellyfinAsync();
        _jellyfin.Series.Add(JellyfinSamples.SeriesWith(PenguinsId, "The Penguins Of Madagascar", 2008, $"{JellyfinSamples.TvShowsRoot}/Penguins"));
        await _factory.SyncLibraryAsync(_jellyfinIntegration.Id, _owner.AccessToken, cancellationToken);
        var workId = await _factory.WorkOfItemAsync(PenguinsId, cancellationToken);
        var batchId = Guid.NewGuid();
        var link = await _links.Create(
            _jellyfinIntegration, PenguinsId, workId, MediaIdentifierScheme.TmdbTv, JellyfinSamples.PenguinsTmdbTvId, writeState: LibraryLinkWriteState.Queued,
            setBy: _owner.User, configure: row => row.BatchId = batchId);
        // An Apply of a series runs for minutes in Jellyfin; here for a few seconds.
        _jellyfin.ApplyDelay = TimeSpan.FromSeconds(3);
        var worker = _factory.Resolve<LibraryLinkWriteWorker>();
        await worker.StartAsync(cancellationToken);

        try
        {
            _factory.Resolve<LibraryLinkWriteQueue>().Signal();
            await WaitForAsync(async () => (await LinkAsync(link.Id, cancellationToken)).WriteState == LibraryLinkWriteState.Writing, cancellationToken);

            //Act
            // The series is inside its Apply: the dialog's poll and the repair's progress are plain reads and answer at once.
            var dialog = await (await _factory.GetHttpRequest($"{BaseUrl}/works/{workId}/tmdb", _owner.AccessToken)).ReadDialogAsync(cancellationToken);
            var catchUp = await (await _factory.GetHttpRequest($"{BaseUrl}/tmdb/catch-up", _owner.AccessToken)).ReadAsync<LibraryLinkCatchUpResponse>(cancellationToken);
            var appliedWhenAnswered = _jellyfin.Applied.Count;
            var pending = await (await _factory.GetHttpRequest($"{BaseUrl}/tmdb/links?filter=Pending", _owner.AccessToken)).ReadAsync<LibraryLinkListResponse>(cancellationToken);

            //Assert
            Assert.Equal(0, appliedWhenAnswered);
            var item = Assert.Single(dialog.Items);
            Assert.Equal(((LibraryLinkWriteState?)LibraryLinkWriteState.Writing, (DateTime?)null), (item.WriteState, item.IdentifiedUtc));
            Assert.True(dialog.Work.Link!.UnderWay);
            Assert.False(dialog.Work.Link.Identified);
            Assert.Equal(new LibraryLinkCatchUpResponse(0, new LibraryLinkProgressDto(batchId, 1, 0)), catchUp);
            Assert.Equal([link.Id], pending.Items.Select(row => row.LinkId));

            await WaitForAsync(async () => (await LinkAsync(link.Id, cancellationToken)).WriteState == LibraryLinkWriteState.Written, cancellationToken);
            Assert.Equal(
                new LibraryLinkCatchUpResponse(0, null),
                await (await _factory.GetHttpRequest($"{BaseUrl}/tmdb/catch-up", _owner.AccessToken)).ReadAsync<LibraryLinkCatchUpResponse>(cancellationToken));
        }
        finally
        {
            await worker.StopAsync(cancellationToken);
        }
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

    // A link "Accept all confident" or the repair saved under the click's batch, waiting for the writer, set by the owner.
    private Task<LibraryItemLinkEntity> QueueAsync(string itemId, Guid workId, string filmId, Guid batchId) =>
        _links.Create(
            _jellyfinIntegration, itemId, workId, MediaIdentifierScheme.Tmdb, filmId, writeState: LibraryLinkWriteState.Queued, setBy: _owner.User,
            configure: link => link.BatchId = batchId);

    private async Task ConnectJellyfinAsync() =>
        _jellyfinIntegration = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);

    // A film of the library, as Jellyfin has it before its Identify, and what its TMDb provider holds for the id.
    private void AddFilmItem(string itemId, string tmdbId, string name)
    {
        _jellyfin.Movies.Add(JellyfinSamples.MovieWith(itemId, $"{name} (file)", null, 1962, $"{JellyfinSamples.CartoonsRoot}/{itemId}.mkv"));
        if (_jellyfin.Matches.All(match => match.Value != tmdbId))
            _jellyfin.AddFilm(tmdbId, name, 1962);
    }

    private void AddSeriesItem(string itemId, string name)
    {
        // Every series of the test is linked to the Penguins' TMDb id, which Jellyfin's provider answers whatever the item's folder.
        _jellyfin.Series.Add(JellyfinSamples.SeriesWith(itemId, $"{name} (folder)", 2008, $"{JellyfinSamples.TvShowsRoot}/{itemId}"));
    }

    private Task<LibraryItemLinkEntity> QueueFilmLinkAsync(string itemId, string tmdbId, Guid? batchId) =>
        _links.Create(
            _jellyfinIntegration, itemId, null, MediaIdentifierScheme.Tmdb, tmdbId, writeState: LibraryLinkWriteState.Queued, setBy: _owner.User,
            configure: link => link.BatchId = batchId);

    private Task<LibraryItemLinkEntity> QueueSeriesLinkAsync(string itemId, Guid? batchId) =>
        _links.Create(
            _jellyfinIntegration, itemId, null, MediaIdentifierScheme.TmdbTv, JellyfinSamples.PenguinsTmdbTvId, writeState: LibraryLinkWriteState.Queued,
            setBy: _owner.User, configure: link => link.BatchId = batchId);

    private Task<LibraryItemLinkEntity> QueueFakeLinkAsync(
        IntegrationConnectionEntity library, Guid workId, string itemId, Guid? batchId, MediaWorkKind itemKind = MediaWorkKind.Movie) =>
        _links.Create(
            library, itemId, workId, MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.Queued, setBy: _owner.User,
            configure: link => (link.BatchId, link.ItemKind) = (batchId, itemKind));

    // Two shorts of their own, with no ids: two works.
    private async Task<(Guid ShortWorkId, Guid OtherWorkId)> SyncShortsAsync(CancellationToken cancellationToken)
    {
        _jellyfinIntegration = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        _jellyfin.Movies.Add(JellyfinSamples.MovieWith(ShortId, "Landing Stripling", null, 1962, $"{JellyfinSamples.CartoonsRoot}/Tom & Jerry/120.mkv"));
        _jellyfin.Movies.Add(JellyfinSamples.MovieWith(OtherShortId, "Calypso Cat", null, 1962, $"{JellyfinSamples.CartoonsRoot}/Tom & Jerry/121.mkv"));
        await _factory.SyncLibraryAsync(_jellyfinIntegration.Id, _owner.AccessToken, cancellationToken);
        await _integrations.Create("tmdb", "TMDb", FakeTmdbServer.BaseUrl, apiKey: FakeTmdbServer.Token);
        return (await _factory.WorkOfItemAsync(ShortId, cancellationToken), await _factory.WorkOfItemAsync(OtherShortId, cancellationToken));
    }

    private string? TmdbOf(string itemId) =>
        (string?)_jellyfin.Movies.Single(movie => (string?)movie["Id"] == itemId)["ProviderIds"]!.AsObject()["Tmdb"];

    private async Task<LibraryItemLinkEntity> LinkAsync(Guid linkId, CancellationToken cancellationToken)
    {
        LibraryItemLinkEntity? link = null;
        await _factory.WithDbAsync(async db => link = await db.LibraryItemLinks.AsNoTracking().SingleAsync(row => row.Id == linkId, cancellationToken));
        return link!;
    }

    // A 32-character item id for the n-th item of a test.
    private static string ItemId(int number) => number.ToString("x32", System.Globalization.CultureInfo.InvariantCulture);

    private static async Task WaitForAsync(Func<Task<bool>> condition, CancellationToken cancellationToken)
    {
        var until = DateTime.UtcNow.AddSeconds(30);
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < until, "The condition wasn't met in time.");
            await Task.Delay(25, cancellationToken);
        }
    }
}
