using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Replacements;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Replacements;

// MAM-27 — what the Jobs line under a job (GET /api/downloads and /api/downloads/{id}, also every replacement action's
// answer) says of its replacement for the owner's new actions: whether Keep both is offered — in every state before an old
// copy starts to move, while none of its names left its folder —, which stage stopped a review and whether Retry is
// offered there — not while a copy only a drop settles is in —, where a running check is and until when it waits, and its
// copies counted without the dropped ones, which it counts apart. Read from the rows the page already batches: one read of
// the copies and one of the names for a whole page. Writes nothing.
public sealed class ReplacementLineHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string DownloadsUrl = "/api/downloads";
    private const string AlienPath = "/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.mkv";
    private const string AliensPath = "/media/movies/Aliens (1986)/Aliens (1986).mkv";
    private const string Alien3Path = "/media/movies/Alien 3 (1992)/Alien 3 (1992).mkv";
    private const string HeatPath = "/media/movies/Heat (1995)/Heat (1995).mkv";

    // Sentences the job's page shows, spelled out rather than read from product code.
    private const string TooShortProblem =
        "“/media/movies/Alien.1979.Directors.Cut.2160p.mkv” runs 1:06:40, less than three quarters of the old copy's 1:33:24.";
    private const string NoIdProblem =
        "“Living room” gave “/media/movies/Aliens.1986.Special.Edition.2160p.mkv” no TMDb or IMDb id within an hour, so it can't be told to be Aliens (1986).";
    private const string NeedsLinkingReason =
        "qBittorrent's downloads couldn't be told apart for this release, so the app can't follow it to verify the new file.";
    private const string HeldReason = "Still held by the torrent “Alien” in “qBittorrent”: remove that torrent there without its files, then press Retry.";

    // ADR 0035 — the check waits an hour for what it can't settle yet: the files from when it started, the library from
    // when it took the request to look.
    private static readonly TimeSpan CheckWait = TimeSpan.FromHours(1);

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private ReplacementVerificationHarness _harness = null!;
    private ReplacementQuarantinePlanter _planter = null!;

    public ReplacementLineHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _harness = await ReplacementVerificationHarness.CreateAsync(_factory);
        _planter = new ReplacementQuarantinePlanter(_factory, _harness);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── per state ───────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ReplacementJobState.Scheduled, true, false, null, null)]
    [InlineData(ReplacementJobState.Submitted, true, false, null, null)]
    [InlineData(ReplacementJobState.Downloading, true, false, null, null)]
    [InlineData(ReplacementJobState.Paused, true, false, null, null)]
    [InlineData(ReplacementJobState.Downloaded, true, false, null, null)]
    [InlineData(ReplacementJobState.Inspecting, true, false, null, ReplacementCheckStage.Files)]
    [InlineData(ReplacementJobState.Verified, true, false, null, null)]
    [InlineData(ReplacementJobState.NeedsReview, true, true, ReplacementStop.Check, null)]
    // A write under way, the old copies in the quarantine — Restore is the way back —, or the job ended.
    [InlineData(ReplacementJobState.Quarantining, false, false, null, null)]
    [InlineData(ReplacementJobState.Quarantined, false, false, null, null)]
    [InlineData(ReplacementJobState.Restoring, false, false, null, null)]
    [InlineData(ReplacementJobState.Deleting, false, false, null, null)]
    [InlineData(ReplacementJobState.Completed, false, false, null, null)]
    [InlineData(ReplacementJobState.Restored, false, false, null, null)]
    [InlineData(ReplacementJobState.Failed, false, false, null, null)]
    [InlineData(ReplacementJobState.Cancelled, false, false, null, null)]
    public async Task Get_of_a_replacement_in_each_state_offers_keep_both_only_before_its_old_copies_start_to_move(
        ReplacementJobState state, bool canCancel, bool canRetry, ReplacementStop? stop, ReplacementCheckStage? checkStage)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The check's start planted on every job alike, so the state alone decides: a review is then the check's stop, and a
        // running check reads the files.
        var startedUtc = _factory.Time.GetUtcNow().UtcDateTime.AddMinutes(-10);
        var planted = await _planter.PlantAsync(
            state, [], copies: [new PlantedCopy(AlienPath, TestMedia.File(inode: 1))], inspectionStartedUtc: startedUtc);

        //Act
        var line = await _harness.LineAsync(planted.Download.Id, cancellationToken);

        //Assert
        Assert.Equal(
            new DownloadJobReplacementDto(state, 1, null, false, 0)
            {
                Id = planted.Replacement.Id,
                CanRetry = canRetry,
                CanCancel = canCancel,
                Stop = stop,
                CheckStage = checkStage,
                CheckWaitsUntilUtc = checkStage is null ? null : startedUtc + CheckWait,
            },
            line);
    }

    // ── a review ────────────────────────────────────────────────────────────────────────────────

    [Theory]
    // The download's following stopped it before any check: there is nothing the app can check again.
    [InlineData(false, false, NeedsLinkingReason, ReplacementStop.Download, false)]
    // The check of the new files stopped it: Retry checks them again once the owner fixed what it says.
    [InlineData(true, false, TooShortProblem, ReplacementStop.Check, true)]
    // The mover stopped it: Retry sends it back to the move.
    [InlineData(true, true, HeldReason, ReplacementStop.Move, true)]
    public async Task Get_of_a_replacement_stopped_for_review_says_which_stage_stopped_it_and_whether_retry_is_offered(
        bool checkStarted, bool moverChecked, string reason, ReplacementStop stop, bool canRetry)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var planted = await _planter.PlantAsync(
            ReplacementJobState.NeedsReview,
            [],
            quarantineCheckedUtc: moverChecked ? now : null,
            reason: reason,
            copies: [new PlantedCopy(AlienPath, TestMedia.File(inode: 1))],
            inspectionStartedUtc: checkStarted ? now.AddHours(-2) : null);

        //Act
        var line = await _harness.LineAsync(planted.Download.Id, cancellationToken);

        //Assert
        // Keep both in every one of them: no old copy left its folder.
        Assert.Equal(
            new DownloadJobReplacementDto(ReplacementJobState.NeedsReview, 1, reason, false, 0)
            {
                Id = planted.Replacement.Id,
                CanRetry = canRetry,
                CanCancel = true,
                Stop = stop,
            },
            line);
    }

    [Fact]
    public async Task Get_of_a_check_stop_with_a_copy_only_a_drop_settles_withholds_retry_and_counts_the_dropped_copy_apart()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        // Alien's new file runs too short, which no fix in Jellyfin changes; Aliens has no id yet, which one would; Alien 3,
        // dropped by the owner, kept its own drop-only problem as history.
        var planted = await _planter.PlantAsync(
            ReplacementJobState.NeedsReview,
            [],
            reason: TooShortProblem,
            copies:
            [
                new PlantedCopy(AlienPath, TestMedia.File(inode: 1), TooShortProblem, OnlyDropSettles: true),
                new PlantedCopy(AliensPath, TestMedia.File(inode: 2), NoIdProblem),
                new PlantedCopy(Alien3Path, TestMedia.File(inode: 3), TooShortProblem, OnlyDropSettles: true, DroppedUtc: now),
            ],
            inspectionStartedUtc: now.AddHours(-2));

        //Act
        var line = await _harness.LineAsync(planted.Download.Id, cancellationToken);

        //Assert
        // Retry would only stop on Alien again: it isn't offered while Alien is in. The two copies it would replace, the
        // dropped one apart.
        Assert.Equal(
            new DownloadJobReplacementDto(ReplacementJobState.NeedsReview, 2, TooShortProblem, false, 0)
            {
                Id = planted.Replacement.Id,
                CanRetry = false,
                DroppedCopyCount = 1,
                DropOnlyCopyCount = 1,
                CanCancel = true,
                Stop = ReplacementStop.Check,
            },
            line);
    }

    [Fact]
    public async Task Get_of_a_check_stop_whose_copy_only_a_drop_settled_was_dropped_offers_retry_again()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var planted = await _planter.PlantAsync(
            ReplacementJobState.NeedsReview,
            [],
            reason: TooShortProblem,
            copies:
            [
                new PlantedCopy(AlienPath, TestMedia.File(inode: 1), TooShortProblem, OnlyDropSettles: true, DroppedUtc: now),
                new PlantedCopy(AliensPath, TestMedia.File(inode: 2), NoIdProblem),
            ],
            inspectionStartedUtc: now.AddHours(-2));

        //Act
        var line = await _harness.LineAsync(planted.Download.Id, cancellationToken);

        //Assert
        Assert.Equal(
            new DownloadJobReplacementDto(ReplacementJobState.NeedsReview, 1, TooShortProblem, false, 0)
            {
                Id = planted.Replacement.Id,
                CanRetry = true,
                DroppedCopyCount = 1,
                CanCancel = true,
                Stop = ReplacementStop.Check,
            },
            line);
    }

    [Fact]
    public async Task Get_of_a_move_stop_counts_only_the_undropped_copies_outside_a_ticked_folder()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        // The owner dropped Alien, then unticked Фільми: a dropped copy never moves, so its folder's tick stops nothing.
        var planted = await _planter.PlantAsync(
            ReplacementJobState.NeedsReview,
            [],
            quarantineCheckedUtc: now,
            reason: HeldReason,
            copies:
            [
                new PlantedCopy(AlienPath, TestMedia.File(inode: 1), DroppedUtc: now),
                new PlantedCopy(AliensPath, TestMedia.File(inode: 2)),
            ],
            inspectionStartedUtc: now.AddHours(-2));
        await _factory.WithDbAsync(db => db.ReplacementFolders.ExecuteDeleteAsync(cancellationToken));

        //Act
        var line = await _harness.LineAsync(planted.Download.Id, cancellationToken);

        //Assert
        Assert.Equal(
            new DownloadJobReplacementDto(ReplacementJobState.NeedsReview, 1, HeldReason, false, 1)
            {
                Id = planted.Replacement.Id,
                CanRetry = true,
                DroppedCopyCount = 1,
                CanCancel = true,
                Stop = ReplacementStop.Move,
            },
            line);
    }

    [Theory]
    // A name that left its folder and hasn't come back: Keep both would forget it; Restore puts it back.
    [InlineData(ReplacementNameState.Moving, false)]
    [InlineData(ReplacementNameState.Quarantined, false)]
    [InlineData(ReplacementNameState.Restoring, false)]
    [InlineData(ReplacementNameState.Deleting, false)]
    [InlineData(ReplacementNameState.Deleted, false)]
    // Still at its old path, back there, or nowhere the app can act on: nothing holds the job.
    [InlineData(ReplacementNameState.Planned, true)]
    [InlineData(ReplacementNameState.Stayed, true)]
    [InlineData(ReplacementNameState.Restored, true)]
    [InlineData(ReplacementNameState.Lost, true)]
    public async Task Get_of_a_move_stop_offers_keep_both_only_while_none_of_its_names_left_its_folder(ReplacementNameState nameState, bool canCancel)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        // A move that stopped part-way: Alien's video in nameState, Heat's still where it was.
        var planted = await _planter.PlantAsync(
            ReplacementJobState.NeedsReview,
            [
                new PlantedName(ReplacementNameKind.Video, AlienPath, TestMedia.File(inode: 1), nameState),
                new PlantedName(ReplacementNameKind.Video, HeatPath, TestMedia.File(inode: 4), ReplacementNameState.Planned),
            ],
            quarantineCheckedUtc: now,
            reason: HeldReason,
            inspectionStartedUtc: now.AddHours(-2));

        //Act
        var line = (await _harness.LineAsync(planted.Download.Id, cancellationToken))!;

        //Assert
        Assert.Equal((canCancel, true, (ReplacementStop?)ReplacementStop.Move), (line.CanCancel, line.CanRetry, line.Stop));
    }

    // ── a running check ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_of_a_replacement_whose_check_reads_its_new_files_says_it_waits_an_hour_from_when_the_check_started()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var startedUtc = _factory.Time.GetUtcNow().UtcDateTime.AddMinutes(-20);
        var planted = await _planter.PlantAsync(
            ReplacementJobState.Inspecting, [], copies: [new PlantedCopy(AlienPath, TestMedia.File(inode: 1))], inspectionStartedUtc: startedUtc);

        //Act
        var line = await _harness.LineAsync(planted.Download.Id, cancellationToken);

        //Assert
        Assert.Equal(
            new DownloadJobReplacementDto(ReplacementJobState.Inspecting, 1, null, false, 0)
            {
                Id = planted.Replacement.Id,
                CanCancel = true,
                CheckStage = ReplacementCheckStage.Files,
                CheckWaitsUntilUtc = startedUtc + CheckWait,
            },
            line);
    }

    [Fact]
    public async Task Get_of_a_replacement_waiting_for_the_library_says_it_waits_an_hour_from_when_the_library_took_the_request()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var startedUtc = _factory.Time.GetUtcNow().UtcDateTime.AddMinutes(-40);
        // Jellyfin took the request to look late, 25 minutes into the check: its hour runs from then.
        var askedUtc = startedUtc.AddMinutes(25);
        var planted = await _planter.PlantAsync(
            ReplacementJobState.Inspecting,
            [],
            copies: [new PlantedCopy(AlienPath, TestMedia.File(inode: 1))],
            inspectionStartedUtc: startedUtc,
            libraryAskedUtc: askedUtc);

        //Act
        var line = await _harness.LineAsync(planted.Download.Id, cancellationToken);

        //Assert
        Assert.Equal(
            new DownloadJobReplacementDto(ReplacementJobState.Inspecting, 1, null, false, 0)
            {
                Id = planted.Replacement.Id,
                CanCancel = true,
                CheckStage = ReplacementCheckStage.Library,
                CheckWaitsUntilUtc = askedUtc + CheckWait,
            },
            line);
    }

    // ── the page ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_answers_the_stop_and_the_checks_stage_by_name_and_the_wait_as_a_utc_instant()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var askedUtc = now.AddMinutes(-5);
        var checking = await _planter.PlantAsync(
            ReplacementJobState.Inspecting,
            [],
            copies: [new PlantedCopy(AlienPath, TestMedia.File(inode: 1))],
            inspectionStartedUtc: now.AddMinutes(-30),
            libraryAskedUtc: askedUtc);
        var stopped = await _planter.PlantAsync(
            ReplacementJobState.NeedsReview,
            [],
            reason: TooShortProblem,
            copies: [new PlantedCopy(AliensPath, TestMedia.File(inode: 2))],
            inspectionStartedUtc: now.AddHours(-2));

        //Act
        using var response = await _factory.GetHttpRequest(DownloadsUrl, _harness.Owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var lines = document.RootElement.GetProperty("items").EnumerateArray()
            .ToDictionary(job => job.GetProperty("id").GetGuid(), job => job.GetProperty("replacement").Clone());
        var checkingLine = lines[checking.Download.Id];
        var stoppedLine = lines[stopped.Download.Id];
        // Enums by name, as every enum on the wire; the instant in UTC with its Z, to the tick.
        Assert.Equal(
            (JsonValueKind.Null, "Library", "Check", JsonValueKind.Null, JsonValueKind.Null),
            (checkingLine.GetProperty("stop").ValueKind, checkingLine.GetProperty("checkStage").GetString(), stoppedLine.GetProperty("stop").GetString(),
                stoppedLine.GetProperty("checkStage").ValueKind, stoppedLine.GetProperty("checkWaitsUntilUtc").ValueKind));
        var waitsUntil = checkingLine.GetProperty("checkWaitsUntilUtc").GetString()!;
        Assert.EndsWith("Z", waitsUntil, StringComparison.Ordinal);
        Assert.Equal(askedUtc + CheckWait, DateTime.Parse(waitsUntil, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
    }

    [Fact]
    public async Task List_of_a_page_of_replacements_reads_their_copies_and_names_once_for_the_whole_page()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var stopped = await _planter.PlantAsync(
            ReplacementJobState.NeedsReview,
            [],
            reason: TooShortProblem,
            copies:
            [
                new PlantedCopy(AlienPath, TestMedia.File(inode: 1), TooShortProblem, OnlyDropSettles: true),
                new PlantedCopy(AliensPath, TestMedia.File(inode: 2), DroppedUtc: now),
            ],
            inspectionStartedUtc: now.AddHours(-2));
        var checking = await _planter.PlantAsync(
            ReplacementJobState.Inspecting, [], copies: [new PlantedCopy(Alien3Path, TestMedia.File(inode: 3))], inspectionStartedUtc: now);
        var quarantined = await _planter.PlantAsync(
            ReplacementJobState.Quarantined, [new PlantedName(ReplacementNameKind.Video, HeatPath, TestMedia.File(inode: 4))]);
        var copiesRead = 0;
        var namesRead = 0;
        // Counts every read of the copies and of the names and lets each through: the hook matches nothing, so it never
        // disarms.
        _factory.DbCommandHook.BeforeNextCommand(
            command =>
            {
                if (command.CommandText.Contains("FROM \"ReplacementJobFiles\"", StringComparison.Ordinal))
                    Interlocked.Increment(ref copiesRead);
                if (command.CommandText.Contains("FROM \"ReplacementJobNames\"", StringComparison.Ordinal))
                    Interlocked.Increment(ref namesRead);
                return false;
            },
            _ => Task.CompletedTask);

        //Act
        var response = await _factory.GetHttpRequest(DownloadsUrl, _harness.Owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = (await response.Content.ReadFromJsonAsync<DownloadJobListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        var lines = list.Items.ToDictionary(job => job.Id, job => job.Replacement!);
        Assert.Equal(
            [
                (stopped.Replacement.Id, 1, 1, 1, false, true, (ReplacementStop?)ReplacementStop.Check, (ReplacementCheckStage?)null),
                (checking.Replacement.Id, 1, 0, 0, false, true, null, ReplacementCheckStage.Files),
                (quarantined.Replacement.Id, 1, 0, 0, false, false, null, null),
            ],
            new[] { stopped, checking, quarantined }.Select(planted => lines[planted.Download.Id])
                .Select(line => (line.Id, line.CopyCount, line.DroppedCopyCount, line.DropOnlyCopyCount, line.CanRetry, line.CanCancel, line.Stop, line.CheckStage)));
        // MAM-24, R10 — batched for the page, never one read per job: MAM-27 adds no read.
        Assert.Equal((1, 1), (copiesRead, namesRead));
    }
}
