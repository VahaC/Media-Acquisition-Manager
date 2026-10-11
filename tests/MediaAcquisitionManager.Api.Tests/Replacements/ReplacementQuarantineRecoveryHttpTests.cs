using System.Net;
using MediaAcquisitionManager.Api.Services.Replacements;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Replacements;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Replacements;

// MAM-26, ADR 0030 — a write to the media a restart or a crash cut: a job found Quarantining, Restoring or Deleting at the
// start of the mover's pass while no one in the app holds it looks each name it was writing for up by device and inode,
// where it was and where it was going, records what it found with the outcome row the write would have written, and stops
// for the owner's review saying how far the write got — renaming and deleting nothing by itself, and never taking up a job
// someone holds. Cut after a rename and before its outcome (the app gone, or the outcome's write failing), the name is found
// in the quarantine; cut between two names, the moved names are counted and nothing is renamed twice. A cut restore finds
// names back, still in the quarantine or lost; a cut deletion finds them deleted, still there or replaced by another file.
// A restore or a deletion cut after its last name ends as the write would have ended it, Restored or Completed, with nothing
// left to review. A look that can't tell — a read error, or an absence where neither of the name's folders is there, as on
// an empty mount point — decides nothing: the job stays as the cut left it until a later pass can tell. A Retry of a cut
// move takes the names left waiting up again. The move itself is ReplacementQuarantineHttpTests'.
public sealed class ReplacementQuarantineRecoveryHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string AlienImdbId = "tt0078748";
    private const string AlienRelease = "Alien (1979) Director's Cut BDRemux 2160p HDR";
    private const string TheatricalPath = "/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.mkv";
    private const string TheatricalNfoPath = "/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.nfo";
    private const string TheatricalSrtPath = "/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.srt";
    private const string NewName = "Alien.1979.Directors.Cut.2160p.mkv";
    private const string NewPath = "/media/movies/Alien.1979.Directors.Cut.2160p.mkv";
    private const int NewSize = (3 * TestTorrent.PieceLength) + 5;
    private const string PlantedHash = "0026002600260026002600260026002600260026";
    private const string PlantedExceptionMessage = "planted-exception-message-0026";

    // Sentences the job's page shows, spelled out rather than read from product code.
    private const string CutWhileMovingOneReason = "The app stopped while moving the old copies: 1 of 3 names is in the quarantine.";
    // Nothing got as far as the quarantine: no count at all, rather than "0 of 2 names are".
    private const string CutWhileMovingNoneReason = "The app stopped while moving the old copies.";
    private const string CutWhileRestoringReason = "The app stopped while putting the old copies back: 1 of 3 names is back where it was.";
    private const string CutWhileDeletingReason = "The app stopped while deleting the old copies for good: 1 of 3 names is deleted.";
    private const string CutWhileMovingTheVideoReason = "The app stopped while moving the old copies: 1 of 1 name is in the quarantine.";
    private const string LostWhileMovingProblem =
        "The app stopped while moving “Alien (1979) Theatrical Cut.nfo”, and found it neither where it was nor in the quarantine.";
    private const string LostWhileRestoringProblem =
        "The app stopped while putting “Alien (1979) Theatrical Cut.srt” back, and found it neither in the quarantine nor where it was.";
    private const string LostWhileDeletingProblem =
        "The app stopped while deleting “Alien (1979) Theatrical Cut.srt” for good, and something else is in its place in the quarantine.";

    private static readonly FileFacts VideoFacts = TestMedia.File(inode: 1);
    private static readonly FileFacts NfoFacts = TestMedia.File(inode: 2, sizeBytes: 4_567);
    private static readonly FileFacts SrtFacts = TestMedia.File(inode: 3, sizeBytes: 51_234);

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private ReplacementVerificationHarness _harness = null!;

    public ReplacementQuarantineRecoveryHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _harness = await ReplacementVerificationHarness.CreateAsync(_factory);
        // Alien's theatrical cut, inspected, in the ticked Фільми, with its .nfo and its subtitles beside it.
        await _harness.PlantFilmAsync("Alien", 1979, TheatricalPath, inode: 1, imdbId: AlienImdbId, editionLabel: "Theatrical Cut");
        _factory.Files.AddFile(TheatricalNfoPath, NfoFacts);
        _factory.Files.AddFile(TheatricalSrtPath, SrtFacts);
        await _harness.InspectAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── a move cut ──────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("the app gone")]
    [InlineData("the outcome's write failing")]
    public async Task Quarantine_cut_after_a_rename_before_its_outcome_finds_the_name_in_the_quarantine_on_the_next_pass_and_stops_for_review(string cut)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var replacement = await VerifiedAsync(cancellationToken);
        var claimedUtc = _factory.Time.GetUtcNow().UtcDateTime;
        // The library won't take the notice, so it stays owed whatever the notice does with it (its own tests are
        // ReplacementLibraryNoticeHttpTests').
        _harness.JellyfinServer.ScanRefusal = HttpStatusCode.ServiceUnavailable;
        // Right after the video's rename: the step breaks before its outcome is written, or the outcome's own write fails.
        _factory.Quarantine.AfterRename = (_, _, _) =>
        {
            _factory.Quarantine.AfterRename = null;
            if (cut == "the app gone")
                throw new InvalidOperationException(PlantedExceptionMessage);
            _factory.DbCommandHook.BeforeNextCommand(
                command => command.CommandText.StartsWith("UPDATE \"ReplacementJobNames\"", StringComparison.Ordinal),
                _ => throw new InvalidOperationException(PlantedExceptionMessage));
            return Task.CompletedTask;
        };
        _factory.Logs.Clear();
        await _factory.QuarantineReplacementsAsync(cancellationToken);
        var cutJob = await _harness.ReplacementAsync(cancellationToken);
        var cutNames = await NamesAsync(cancellationToken);
        var errors = _factory.Logs.Entries
            .Where(entry => entry.Level >= LogLevel.Error && entry.Category.StartsWith("MediaAcquisitionManager.", StringComparison.Ordinal))
            .ToList();
        var logged = _factory.Logs.Entries.ToList();
        _factory.Logs.Clear();
        _factory.Time.Advance(ReplacementQuarantineTracker.PassInterval);
        var foundUtc = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        // The cut pass left the job in the middle, the video moved and its outcome unwritten; it logged the type, never the
        // message, and let the job go.
        Assert.Equal(ReplacementJobState.Quarantining, cutJob.State);
        Assert.Equal(
            [ReplacementNameState.Moving, ReplacementNameState.Planned, ReplacementNameState.Planned],
            cutNames.Select(name => name.State));
        var error = Assert.Single(errors);
        Assert.Equal(typeof(ReplacementQuarantiner).FullName, error.Category);
        Assert.StartsWith($"Moving the old copies of replacement job {replacement.Id} threw ", error.Message, StringComparison.Ordinal);
        Assert.Null(error.Exception);
        Assert.DoesNotContain(
            logged,
            entry => entry.Category.StartsWith("MediaAcquisitionManager.", StringComparison.Ordinal)
                && entry.Message.Contains(PlantedExceptionMessage, StringComparison.Ordinal));
        Assert.False(_factory.Resolve<ReplacementJobsInHand>().Holds(replacement.Id));

        // The next pass finds the video in the quarantine by its identity, records it with the outcome row its rename would
        // have written, and stops the job — renaming nothing again, and owing the library a notice.
        var stopped = await _harness.ReplacementAsync(cancellationToken);
        Assert.Equal(
            (ReplacementJobState.NeedsReview, (string?)CutWhileMovingOneReason, (DateTime?)claimedUtc, (DateTime?)foundUtc),
            (stopped.State, stopped.Reason, stopped.QuarantineCheckedUtc, stopped.LibraryNoticeDueUtc));
        var names = await NamesAsync(cancellationToken);
        Assert.Equal(
            [
                (TheatricalPath, ReplacementNameState.Quarantined, (DateTime?)foundUtc),
                (TheatricalNfoPath, ReplacementNameState.Planned, null),
                (TheatricalSrtPath, ReplacementNameState.Planned, null),
            ],
            names.Select(name => (name.AppPath, name.State, name.MovedUtc)));
        Assert.Equal([(TheatricalPath, QuarantinePathOf(replacement, TheatricalPath))], _factory.Quarantine.Renames);
        Assert.Equal(
            [
                JobAudited(AuditAction.ReplacementQuarantineStarted, replacement.Id),
                NameAudited(AuditAction.ReplacementNameMoving, names[0]),
                NameAudited(AuditAction.ReplacementNameMoved, names[0]),
                JobAudited(AuditAction.ReplacementNeedsReview, replacement.Id),
            ],
            await _harness.QuarantineAuditsAsync(cancellationToken));

        // Once stopped it stays so: a later pass touches nothing.
        _factory.Time.Advance(ReplacementQuarantineTracker.PassInterval);
        await QuarantineAsync(cancellationToken);
        Assert.Equal(stopped.StateChangedUtc, (await _harness.ReplacementAsync(cancellationToken)).StateChangedUtc);
        Assert.Single(_factory.Quarantine.Renames);
    }

    [Fact]
    public async Task Quarantine_cut_between_two_names_counts_the_moved_ones_on_the_next_pass_and_renames_nothing_twice()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var replacement = await VerifiedAsync(cancellationToken);
        // The library won't take the notice, so it stays owed whatever the notice does with it (its own tests are
        // ReplacementLibraryNoticeHttpTests').
        _harness.JellyfinServer.ScanRefusal = HttpStatusCode.ServiceUnavailable;
        // The app shuts down right after the video's rename: its outcome is decided and written, the next name never
        // starts.
        using var shutdown = new CancellationTokenSource();
        _factory.Quarantine.AfterRename = async (_, _, _) =>
        {
            _factory.Quarantine.AfterRename = null;
            await shutdown.CancelAsync();
        };
        var stoppedByShutdown = await Record.ExceptionAsync(() => _factory.QuarantineReplacementsAsync(shutdown.Token));
        var cutJob = await _harness.ReplacementAsync(cancellationToken);
        var cutNames = await NamesAsync(cancellationToken);
        _factory.Time.Advance(ReplacementQuarantineTracker.PassInterval);

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        Assert.IsAssignableFrom<OperationCanceledException>(stoppedByShutdown);
        Assert.Equal(ReplacementJobState.Quarantining, cutJob.State);
        Assert.Equal(
            [ReplacementNameState.Quarantined, ReplacementNameState.Planned, ReplacementNameState.Planned],
            cutNames.Select(name => name.State));
        var stopped = await _harness.ReplacementAsync(cancellationToken);
        Assert.Equal((ReplacementJobState.NeedsReview, (string?)CutWhileMovingOneReason), (stopped.State, stopped.Reason));
        Assert.NotNull(stopped.LibraryNoticeDueUtc);
        Assert.Equal(cutNames.Select(name => name.State), (await NamesAsync(cancellationToken)).Select(name => name.State));
        Assert.Single(_factory.Quarantine.Renames);
        Assert.Equal((FileInventoryStatus.Found, (long?)2), await WhatIsAtAsync(TheatricalNfoPath, cancellationToken));
        // No name was in the middle of a write: only the job's stop is added.
        Assert.Equal(
            [
                JobAudited(AuditAction.ReplacementQuarantineStarted, replacement.Id),
                NameAudited(AuditAction.ReplacementNameMoving, cutNames[0]),
                NameAudited(AuditAction.ReplacementNameMoved, cutNames[0]),
                JobAudited(AuditAction.ReplacementNeedsReview, replacement.Id),
            ],
            await _harness.QuarantineAuditsAsync(cancellationToken));
    }

    [Fact]
    public async Task Quarantine_cut_between_two_names_and_retried_moves_the_names_left_waiting_on_the_next_pass()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var replacement = await VerifiedAsync(cancellationToken);
        _harness.JellyfinServer.ScanRefusal = HttpStatusCode.ServiceUnavailable;
        // The app shuts down right after the video's rename; the next start stops the job for review, and the owner
        // presses Retry.
        using var shutdown = new CancellationTokenSource();
        _factory.Quarantine.AfterRename = async (_, _, _) =>
        {
            _factory.Quarantine.AfterRename = null;
            await shutdown.CancelAsync();
        };
        await Record.ExceptionAsync(() => _factory.QuarantineReplacementsAsync(shutdown.Token));
        _factory.Time.Advance(ReplacementQuarantineTracker.PassInterval);
        await QuarantineAsync(cancellationToken);
        var stopped = await _harness.ReplacementAsync(cancellationToken);
        var retried = await _factory.PostHttpRequest($"/api/replacements/{replacement.Id}/retry", _harness.Owner.AccessToken);
        _factory.Time.Advance(ReplacementQuarantineTracker.PassInterval);

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        Assert.Equal((ReplacementJobState.NeedsReview, HttpStatusCode.OK), (stopped.State, retried.StatusCode));
        var settled = await _harness.ReplacementAsync(cancellationToken);
        Assert.Equal((ReplacementJobState.Quarantined, (string?)null), (settled.State, settled.Reason));
        var names = await NamesAsync(cancellationToken);
        Assert.All(names, name => Assert.Equal(ReplacementNameState.Quarantined, name.State));
        // Each name renamed once: the video at the cut pass, its companions after the Retry.
        Assert.Equal(names.Select(name => (name.AppPath, name.QuarantinePath)), _factory.Quarantine.Renames);
    }

    [Fact]
    public async Task Quarantine_settles_a_cut_move_whose_names_are_where_they_were_or_lost()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Cut with two renames under way: the video's never happened, and the .nfo is neither where it was nor in the
        // quarantine (the owner moved it by hand meanwhile).
        var (replacement, copy) = await PlantAsync(ReplacementJobState.Quarantining, quarantineCheckedUtc: null);
        var video = await AddNameAsync(replacement, copy, 0, ReplacementNameKind.Video, TheatricalPath, VideoFacts, ReplacementNameState.Moving);
        var nfo = await AddNameAsync(replacement, copy, 1, ReplacementNameKind.Companion, TheatricalNfoPath, NfoFacts, ReplacementNameState.Moving);
        _factory.Files.Remove(TheatricalNfoPath);
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        var stopped = await _harness.ReplacementAsync(cancellationToken);
        // Nothing moved: no notice owed. The mover's mark is set, so the owner may retry.
        Assert.Equal(
            (ReplacementJobState.NeedsReview, (string?)CutWhileMovingNoneReason, (DateTime?)now, (DateTime?)null),
            (stopped.State, stopped.Reason, stopped.QuarantineCheckedUtc, stopped.LibraryNoticeDueUtc));
        Assert.Equal(
            [
                (TheatricalPath, ReplacementNameState.Planned, (string?)null),
                (TheatricalNfoPath, ReplacementNameState.Lost, LostWhileMovingProblem),
            ],
            (await NamesAsync(cancellationToken)).Select(name => (name.AppPath, name.State, name.Problem)));
        var audits = await _harness.QuarantineAuditsAsync(cancellationToken);
        Assert.Equal(
            new HashSet<(AuditAction, Guid?, AuditTargetKind?, Guid?, string?)>
            {
                NameAudited(AuditAction.ReplacementNameNotMoved, video),
                NameAudited(AuditAction.ReplacementNameNotMoved, nfo),
            },
            audits.Take(2).ToHashSet());
        Assert.Equal([JobAudited(AuditAction.ReplacementNeedsReview, replacement.Id)], audits.Skip(2));
        Assert.Empty(_factory.Quarantine.Renames);
    }

    // ── a restore or a deletion cut ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Quarantine_settles_a_cut_restore_finding_each_name_back_in_the_quarantine_or_lost()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var movedUtc = _factory.Time.GetUtcNow().UtcDateTime;
        var (replacement, copy) = await PlantAsync(ReplacementJobState.Restoring, quarantineCheckedUtc: movedUtc);
        // The library won't take the notice, so it stays owed whatever the notice does with it (its own tests are
        // ReplacementLibraryNoticeHttpTests').
        _harness.JellyfinServer.ScanRefusal = HttpStatusCode.ServiceUnavailable;
        // The video's rename back happened, the .nfo's didn't, and the subtitles are gone from both places.
        var video = await AddNameAsync(replacement, copy, 0, ReplacementNameKind.Video, TheatricalPath, VideoFacts, ReplacementNameState.Restoring, movedUtc);
        var nfo = await AddNameAsync(replacement, copy, 1, ReplacementNameKind.Companion, TheatricalNfoPath, NfoFacts, ReplacementNameState.Restoring, movedUtc);
        var srt = await AddNameAsync(replacement, copy, 2, ReplacementNameKind.Companion, TheatricalSrtPath, SrtFacts, ReplacementNameState.Restoring, movedUtc);
        _factory.Files.Move(TheatricalNfoPath, nfo.QuarantinePath);
        _factory.Files.Remove(TheatricalSrtPath);
        _factory.Time.Advance(TimeSpan.FromMinutes(5));
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        var stopped = await _harness.ReplacementAsync(cancellationToken);
        // A name came back: the library is owed a notice. The mark the move set is kept.
        Assert.Equal(
            (ReplacementJobState.NeedsReview, (string?)CutWhileRestoringReason, (DateTime?)movedUtc, (DateTime?)now),
            (stopped.State, stopped.Reason, stopped.QuarantineCheckedUtc, stopped.LibraryNoticeDueUtc));
        Assert.Equal(
            [
                (TheatricalPath, ReplacementNameState.Restored, (string?)null, (DateTime?)now),
                (TheatricalNfoPath, ReplacementNameState.Quarantined, null, null),
                (TheatricalSrtPath, ReplacementNameState.Lost, LostWhileRestoringProblem, null),
            ],
            (await NamesAsync(cancellationToken)).Select(name => (name.AppPath, name.State, name.Problem, name.RestoredUtc)));
        var audits = await _harness.QuarantineAuditsAsync(cancellationToken);
        Assert.Equal(
            new HashSet<(AuditAction, Guid?, AuditTargetKind?, Guid?, string?)>
            {
                NameAudited(AuditAction.ReplacementNameRestored, video),
                NameAudited(AuditAction.ReplacementNameNotRestored, nfo),
                NameAudited(AuditAction.ReplacementNameNotRestored, srt),
            },
            audits.Take(3).ToHashSet());
        Assert.Equal([JobAudited(AuditAction.ReplacementNeedsReview, replacement.Id)], audits.Skip(3));
        // It renamed nothing, back or forth, and deleted nothing.
        Assert.Empty(_factory.Quarantine.Renames);
        Assert.Empty(_factory.Quarantine.Deletions);
        Assert.Equal((FileInventoryStatus.Found, (long?)2), await WhatIsAtAsync(nfo.QuarantinePath, cancellationToken));
    }

    [Fact]
    public async Task Quarantine_settles_a_cut_deletion_finding_each_name_deleted_still_there_or_replaced()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var movedUtc = _factory.Time.GetUtcNow().UtcDateTime;
        var (replacement, copy) = await PlantAsync(ReplacementJobState.Deleting, quarantineCheckedUtc: movedUtc);
        var video = await AddNameAsync(replacement, copy, 0, ReplacementNameKind.Video, TheatricalPath, VideoFacts, ReplacementNameState.Deleting, movedUtc);
        var nfo = await AddNameAsync(replacement, copy, 1, ReplacementNameKind.Companion, TheatricalNfoPath, NfoFacts, ReplacementNameState.Deleting, movedUtc);
        var srt = await AddNameAsync(replacement, copy, 2, ReplacementNameKind.Companion, TheatricalSrtPath, SrtFacts, ReplacementNameState.Deleting, movedUtc);
        // The video's deletion happened; the .nfo is still in the quarantine; another file took the subtitles' place there.
        _factory.Files.Remove(TheatricalPath);
        _factory.Files.Move(TheatricalNfoPath, nfo.QuarantinePath);
        _factory.Files.Remove(TheatricalSrtPath);
        _factory.Files.AddFile(srt.QuarantinePath, TestMedia.File(inode: 99, sizeBytes: 51_234));
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        var stopped = await _harness.ReplacementAsync(cancellationToken);
        // A deletion owes the library nothing: it was told when the names left.
        Assert.Equal(
            (ReplacementJobState.NeedsReview, (string?)CutWhileDeletingReason, (DateTime?)movedUtc, (DateTime?)null),
            (stopped.State, stopped.Reason, stopped.QuarantineCheckedUtc, stopped.LibraryNoticeDueUtc));
        Assert.Equal(
            [
                (TheatricalPath, ReplacementNameState.Deleted, (string?)null, (DateTime?)now),
                (TheatricalNfoPath, ReplacementNameState.Quarantined, null, null),
                (TheatricalSrtPath, ReplacementNameState.Lost, LostWhileDeletingProblem, null),
            ],
            (await NamesAsync(cancellationToken)).Select(name => (name.AppPath, name.State, name.Problem, name.DeletedUtc)));
        var audits = await _harness.QuarantineAuditsAsync(cancellationToken);
        Assert.Equal(
            new HashSet<(AuditAction, Guid?, AuditTargetKind?, Guid?, string?)>
            {
                NameAudited(AuditAction.ReplacementNameDeleted, video),
                NameAudited(AuditAction.ReplacementNameNotDeleted, nfo),
                NameAudited(AuditAction.ReplacementNameNotDeleted, srt),
            },
            audits.Take(3).ToHashSet());
        Assert.Equal([JobAudited(AuditAction.ReplacementNeedsReview, replacement.Id)], audits.Skip(3));
        // It deleted nothing and renamed nothing by itself.
        Assert.Empty(_factory.Quarantine.Deletions);
        Assert.Empty(_factory.Quarantine.Renames);
        Assert.Equal((FileInventoryStatus.Found, (long?)99), await WhatIsAtAsync(srt.QuarantinePath, cancellationToken));
    }

    [Fact]
    public async Task Quarantine_settles_a_cut_deletion_whose_every_name_is_deleted_as_completed()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var movedUtc = _factory.Time.GetUtcNow().UtcDateTime;
        var (replacement, copy) = await PlantAsync(ReplacementJobState.Deleting, quarantineCheckedUtc: movedUtc);
        // Cut after the last deletion and before its outcome: every name is gone from the quarantine, its folder kept.
        await AddNameAsync(replacement, copy, 0, ReplacementNameKind.Video, TheatricalPath, VideoFacts, ReplacementNameState.Deleted, movedUtc);
        await AddNameAsync(replacement, copy, 1, ReplacementNameKind.Companion, TheatricalNfoPath, NfoFacts, ReplacementNameState.Deleted, movedUtc);
        var srt = await AddNameAsync(replacement, copy, 2, ReplacementNameKind.Companion, TheatricalSrtPath, SrtFacts, ReplacementNameState.Deleting, movedUtc);
        foreach (var path in new[] { TheatricalPath, TheatricalNfoPath, TheatricalSrtPath })
            _factory.Files.Remove(path);
        _factory.Files.AddFolder(srt.QuarantinePath[..srt.QuarantinePath.LastIndexOf('/')]);
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        // Nothing is left to review, nor to retry: the deletion ends as it would have, the app its actor.
        var completed = await _harness.ReplacementAsync(cancellationToken);
        Assert.Equal(
            (ReplacementJobState.Completed, (string?)null, now, (DateTime?)movedUtc, (DateTime?)null),
            (completed.State, completed.Reason, completed.StateChangedUtc, completed.QuarantineCheckedUtc, completed.LibraryNoticeDueUtc));
        Assert.Equal(
            [ReplacementNameState.Deleted, ReplacementNameState.Deleted, ReplacementNameState.Deleted],
            (await NamesAsync(cancellationToken)).Select(name => name.State));
        Assert.Equal(
            [NameAudited(AuditAction.ReplacementNameDeleted, srt), JobAudited(AuditAction.ReplacementCompleted, replacement.Id)],
            await _harness.QuarantineAuditsAsync(cancellationToken));
        Assert.False((await _harness.LineAsync(completed.DownloadJobId, cancellationToken))!.CanRetry);
        Assert.Empty(_factory.Quarantine.Deletions);
        Assert.Empty(_factory.Quarantine.Renames);
    }

    [Fact]
    public async Task Quarantine_settles_a_cut_restore_whose_every_name_is_back_as_restored()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var movedUtc = _factory.Time.GetUtcNow().UtcDateTime;
        var (replacement, copy) = await PlantAsync(ReplacementJobState.Restoring, quarantineCheckedUtc: movedUtc);
        // The library won't take the notice, so it stays owed whatever the notice does with it.
        _harness.JellyfinServer.ScanRefusal = HttpStatusCode.ServiceUnavailable;
        // Cut after the last rename back and before its outcome: every name is where it was.
        await AddNameAsync(replacement, copy, 0, ReplacementNameKind.Video, TheatricalPath, VideoFacts, ReplacementNameState.Restored, movedUtc);
        await AddNameAsync(replacement, copy, 1, ReplacementNameKind.Companion, TheatricalNfoPath, NfoFacts, ReplacementNameState.Restored, movedUtc);
        var srt = await AddNameAsync(replacement, copy, 2, ReplacementNameKind.Companion, TheatricalSrtPath, SrtFacts, ReplacementNameState.Restoring, movedUtc);
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        // Both copies are back in the library: the restore ends as it would have, the library owed its notice, and nothing
        // is offered — a Retry would move the old copies straight back.
        var restored = await _harness.ReplacementAsync(cancellationToken);
        Assert.Equal(
            (ReplacementJobState.Restored, (string?)null, now, (DateTime?)now),
            (restored.State, restored.Reason, restored.StateChangedUtc, restored.LibraryNoticeDueUtc));
        Assert.Equal(
            [(ReplacementNameState.Restored, (DateTime?)null), (ReplacementNameState.Restored, null), (ReplacementNameState.Restored, now)],
            (await NamesAsync(cancellationToken)).Select(name => (name.State, name.RestoredUtc)));
        Assert.Equal(
            [NameAudited(AuditAction.ReplacementNameRestored, srt), JobAudited(AuditAction.ReplacementRestored, replacement.Id)],
            await _harness.QuarantineAuditsAsync(cancellationToken));
        Assert.False((await _harness.LineAsync(restored.DownloadJobId, cancellationToken))!.CanRetry);
        Assert.Empty(_factory.Quarantine.Renames);
    }

    // ── a look that can't tell ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Quarantine_leaves_a_cut_move_unsettled_while_its_name_cant_be_read_and_settles_it_once_it_can()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (replacement, copy) = await PlantAsync(ReplacementJobState.Quarantining, quarantineCheckedUtc: _factory.Time.GetUtcNow().UtcDateTime);
        _harness.JellyfinServer.ScanRefusal = HttpStatusCode.ServiceUnavailable;
        var video = await AddNameAsync(replacement, copy, 0, ReplacementNameKind.Video, TheatricalPath, VideoFacts, ReplacementNameState.Moving);
        // The disk answers neither look: an I/O error at both places, which says nothing of where the film is.
        _factory.Files.AddProblem(video.QuarantinePath, FileInventoryStatus.Unreadable, "planted-unreadable-0026");
        _factory.Files.AddProblem(TheatricalPath, FileInventoryStatus.Unreadable, "planted-unreadable-0026");
        _factory.Logs.Clear();

        //Act
        await QuarantineAsync(cancellationToken);
        var waiting = await _harness.ReplacementAsync(cancellationToken);
        var waitingName = Assert.Single(await NamesAsync(cancellationToken));
        var auditsWhileWaiting = await _harness.QuarantineAuditsAsync(cancellationToken);
        // The disk reads again: the rename had happened.
        _factory.Files.Remove(TheatricalPath);
        _factory.Files.AddFile(video.QuarantinePath, VideoFacts);
        _factory.Time.Advance(ReplacementQuarantineTracker.PassInterval);
        await QuarantineAsync(cancellationToken);

        //Assert
        // Nothing decided on a failed look: the job stays as the cut left it, nothing recorded, and a Warning names it.
        Assert.Equal((ReplacementJobState.Quarantining, replacement.StateChangedUtc), (waiting.State, waiting.StateChangedUtc));
        Assert.Equal(ReplacementNameState.Moving, waitingName.State);
        Assert.Empty(auditsWhileWaiting);
        Assert.Contains(
            _factory.Logs.Entries,
            entry => entry.Level == LogLevel.Warning && entry.Message.Contains(replacement.Id.ToString(), StringComparison.Ordinal));
        var stopped = await _harness.ReplacementAsync(cancellationToken);
        Assert.Equal((ReplacementJobState.NeedsReview, (string?)CutWhileMovingTheVideoReason), (stopped.State, stopped.Reason));
        Assert.Equal(ReplacementNameState.Quarantined, Assert.Single(await NamesAsync(cancellationToken)).State);
        Assert.Empty(_factory.Quarantine.Renames);
    }

    [Fact]
    public async Task Quarantine_leaves_a_cut_deletion_unsettled_while_neither_of_its_names_folders_is_there_and_settles_it_once_one_is()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var movedUtc = _factory.Time.GetUtcNow().UtcDateTime;
        var (replacement, copy) = await PlantAsync(ReplacementJobState.Deleting, quarantineCheckedUtc: movedUtc);
        var video = await AddNameAsync(replacement, copy, 0, ReplacementNameKind.Video, TheatricalPath, VideoFacts, ReplacementNameState.Deleting, movedUtc);
        // An empty mount point: neither the film's folder nor its folder in the quarantine is there, so the film's absence
        // from the quarantine proves nothing.
        _factory.Files.RemoveFolder("/media/movies/Alien (1979)");
        var quarantineFolder = video.QuarantinePath[..video.QuarantinePath.LastIndexOf('/')];

        //Act
        await QuarantineAsync(cancellationToken);
        var waiting = await _harness.ReplacementAsync(cancellationToken);
        var waitingName = Assert.Single(await NamesAsync(cancellationToken));
        // The mount is back: the quarantine's folder is there, the film no longer in it.
        _factory.Files.AddFolder(quarantineFolder);
        _factory.Time.Advance(ReplacementQuarantineTracker.PassInterval);
        await QuarantineAsync(cancellationToken);

        //Assert
        Assert.Equal((ReplacementJobState.Deleting, ReplacementNameState.Deleting), (waiting.State, waitingName.State));
        var completed = await _harness.ReplacementAsync(cancellationToken);
        Assert.Equal(ReplacementJobState.Completed, completed.State);
        Assert.Equal(ReplacementNameState.Deleted, Assert.Single(await NamesAsync(cancellationToken)).State);
        Assert.Empty(_factory.Quarantine.Deletions);
    }

    [Fact]
    public async Task Quarantine_never_settles_a_job_someone_in_the_app_holds()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // An owner's Restore under way in a request: Restoring, a name in the middle, and the job held.
        var (replacement, copy) = await PlantAsync(ReplacementJobState.Restoring, quarantineCheckedUtc: _factory.Time.GetUtcNow().UtcDateTime);
        await AddNameAsync(replacement, copy, 0, ReplacementNameKind.Video, TheatricalPath, VideoFacts, ReplacementNameState.Restoring);
        var inHand = _factory.Resolve<ReplacementJobsInHand>();
        inHand.TryTake(replacement.Id);
        var described = _factory.Files.DescribedPaths.Count;
        try
        {
            //Act
            await QuarantineAsync(cancellationToken);

            //Assert
            var untouched = await _harness.ReplacementAsync(cancellationToken);
            Assert.Equal((ReplacementJobState.Restoring, replacement.StateChangedUtc), (untouched.State, untouched.StateChangedUtc));
            Assert.Equal(ReplacementNameState.Restoring, Assert.Single(await NamesAsync(cancellationToken)).State);
            Assert.Empty(await _harness.QuarantineAuditsAsync(cancellationToken));
            Assert.Equal(described, _factory.Files.DescribedPaths.Count);
            // Still the owner's action's: the pass never took it from it.
            Assert.True(inHand.Holds(replacement.Id));
        }
        finally
        {
            inHand.Release(replacement.Id);
        }
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The owner's replacement of Alien's theatrical cut by the Director's Cut release, verified: the new film downloaded
    /// to the Movies category's folder, on the disk, and identified by Jellyfin as Alien.
    /// </summary>
    private async Task<ReplacementJobEntity> VerifiedAsync(CancellationToken cancellationToken)
    {
        var release = await _harness.FindAsync("Alien", 1979, AlienRelease, imdbId: AlienImdbId);
        await _harness.VerifiedAsync(
            release,
            _harness.Movies,
            [TheatricalPath],
            [new FakeQBittorrentFile(NewName, NewSize, 1.0)],
            [(NewPath, TestMedia.File(inode: 7, sizeBytes: NewSize), TestMedia.Taken())],
            paths => _harness.JellyfinServer.PlantFilm(paths[0], "Alien", 1979, imdbId: AlienImdbId),
            cancellationToken);
        return await _harness.ReplacementAsync(cancellationToken);
    }

    /// <summary>
    /// A replacement of the theatrical cut in <paramref name="state"/> as a previous run left it — its download done and
    /// checked, its copy, no name yet —, planted rather than reached through the routes.
    /// </summary>
    private async Task<(ReplacementJobEntity Replacement, ReplacementJobFileEntity Copy)> PlantAsync(ReplacementJobState state, DateTime? quarantineCheckedUtc)
    {
        var release = await _harness.FindAsync("Alien", 1979, AlienRelease, imdbId: AlienImdbId);
        var db = _factory.Scope.GetRequiredService<ApplicationDbContext>();
        var download = await new DownloadJobDataFactory(db, _factory.Time).Create(
            release.Id,
            _harness.Client.Id,
            _harness.Client.Name,
            _harness.Movies.Id,
            DownloadJobStatus.Downloaded,
            _harness.Owner.User.Id,
            infoHashV1: PlantedHash,
            clientJobId: PlantedHash,
            progress: 1.0,
            overwriteCheck: DownloadOverwriteCheck.Checked);
        var replacements = new ReplacementJobDataFactory(db, _factory.Time);
        var replacement = await replacements.Create(
            download.Id, _harness.Owner.User.Id, state, quarantineCheckedUtc: quarantineCheckedUtc, quarantinedUtc: quarantineCheckedUtc);
        var copy = await replacements.AddFile(replacement.Id, TheatricalPath, VideoFacts);
        return (replacement, copy);
    }

    /// <summary>A name of <paramref name="replacement"/>'s <paramref name="copy"/> at <paramref name="position"/>, going to
    /// its place in /media's quarantine, read with <paramref name="facts"/>, in <paramref name="state"/>.</summary>
    private Task<ReplacementJobNameEntity> AddNameAsync(
        ReplacementJobEntity replacement,
        ReplacementJobFileEntity copy,
        int position,
        ReplacementNameKind kind,
        string appPath,
        FileFacts facts,
        ReplacementNameState state,
        DateTime? movedUtc = null) =>
        new ReplacementJobDataFactory(_factory.Scope.GetRequiredService<ApplicationDbContext>(), _factory.Time).AddName(
            replacement.Id,
            copy.Id,
            position,
            kind,
            appPath,
            QuarantinePathOf(replacement, appPath),
            ReplacementVerificationHarness.MediaRoot,
            facts,
            state,
            movedUtc: movedUtc);

    /// <summary>One pass of the mover, which must not have logged an error.</summary>
    private async Task QuarantineAsync(CancellationToken cancellationToken)
    {
        await _factory.QuarantineReplacementsAsync(cancellationToken);
        if (_factory.Logs.Entries.FirstOrDefault(entry => entry.Level >= LogLevel.Error) is { } error)
            throw new InvalidOperationException($"A pass logged an error: {error.Category}: {error.Message}");
    }

    /// <summary>The replacement's names, in their order.</summary>
    private async Task<IReadOnlyList<ReplacementJobNameEntity>> NamesAsync(CancellationToken cancellationToken)
    {
        List<ReplacementJobNameEntity> rows = [];
        await _factory.WithDbAsync(async db => rows = await db.ReplacementJobNames.AsNoTracking().OrderBy(name => name.Position).ToListAsync(cancellationToken));
        return rows;
    }

    /// <summary>What the disk shows at <paramref name="appPath"/> inside /media: whether a file is there, and its inode.</summary>
    private async Task<(FileInventoryStatus Status, long? Inode)> WhatIsAtAsync(string appPath, CancellationToken cancellationToken)
    {
        var described = await _factory.Files.DescribeAsync(appPath, [ReplacementVerificationHarness.MediaRoot], cancellationToken);
        return (described.Status, described.Value?.Inode);
    }

    private static string QuarantinePathOf(ReplacementJobEntity replacement, string appPath) =>
        ReplacementVerificationHarness.QuarantinePathOf(replacement.Id, appPath);

    /// <summary>An audit row of the job, the app's own (no actor), naming it by its release.</summary>
    private static (AuditAction, Guid?, AuditTargetKind?, Guid?, string?) JobAudited(AuditAction action, Guid replacementJobId) =>
        (action, null, AuditTargetKind.ReplacementJob, replacementJobId, AlienRelease);

    /// <summary>An audit row of one name, the app's own (no actor), naming it by the file's own name.</summary>
    private static (AuditAction, Guid?, AuditTargetKind?, Guid?, string?) NameAudited(AuditAction action, ReplacementJobNameEntity name) =>
        (action, null, AuditTargetKind.ReplacementJobName, name.Id, name.AppPath[(name.AppPath.LastIndexOf('/') + 1)..]);
}
