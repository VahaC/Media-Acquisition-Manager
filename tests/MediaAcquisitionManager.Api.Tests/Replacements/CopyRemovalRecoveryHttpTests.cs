using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.CopyRemovals;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Replacements;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Replacements;

// MAM-40, ADR 0030 — a restart or a crash in the middle of the owner's Remove, Restore or Delete for good: the quarantine's
// own pass (ReplacementQuarantineTracker, after the replacements') finds the removal still Moving, Restoring or Deleting with
// no one in this process holding it, looks each name in the middle of a write up by device and inode where it was and where
// it was going, records what it found with the outcome row the write would have written — no actor —, and settles it: in
// review while a name is still in the quarantine or a move's video was found nowhere (the reason saying where to look),
// otherwise at the write's own end — NotMoved for a move —, the reason saying how far it got; then it sends the library
// notices removals still owe. A removal someone holds is left alone, and so is one whose names can't be looked up, for the
// next pass. It renames and deletes nothing.
public sealed class CopyRemovalRecoveryHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string VideoPath = "/media/movies/Alien (1979)/Alien (1979) 2160p.mkv";
    private const string SrtPath = "/media/movies/Alien (1979)/Alien (1979) 2160p.uk.srt";
    private const string UpdatedCall = "POST /Library/Media/Updated";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private ReplacementVerificationHarness _harness = null!;
    private CopyRemovalPlanter _removals = null!;

    public CopyRemovalRecoveryHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _harness = await ReplacementVerificationHarness.CreateAsync(_factory);
        _removals = new CopyRemovalPlanter(_factory, _harness);
        _harness.Mark();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Pass_settles_a_removal_cut_after_its_video_moved_for_review_and_tells_jellyfin_it_went()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Cut between the video's rename and its outcome: the video is in the quarantine, its subtitles still wait.
        var planted = await _removals.PlantAsync(
            CopyRemovalState.Moving,
            [
                new PlantedName(ReplacementNameKind.Video, VideoPath, TestMedia.File(inode: 2), ReplacementNameState.Moving),
                new PlantedName(ReplacementNameKind.Companion, SrtPath, TestMedia.File(inode: 3, sizeBytes: 52_345), ReplacementNameState.Planned),
            ]);
        _factory.Files.Move(VideoPath, planted.Names[0].QuarantinePath);
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        await PassAsync(cancellationToken);

        //Assert
        var removal = Assert.Single(await _removals.RemovalsAsync(cancellationToken));
        Assert.Equal(
            (CopyRemovalState.NeedsReview, (string?)"The app stopped while moving this copy: 1 of 2 names is in the quarantine.", (DateTime?)null),
            (removal.State, removal.Reason, removal.LibraryNoticeDueUtc));
        Assert.Equal(
            [(ReplacementNameState.Quarantined, (DateTime?)now), (ReplacementNameState.Planned, (DateTime?)null)],
            (await _removals.NamesAsync(removal.Id, cancellationToken)).Select(name => (name.State, name.MovedUtc)));
        Assert.Equal(
            [
                (AuditAction.CopyRemovalNameMoved, (Guid?)null, (AuditTargetKind?)AuditTargetKind.CopyRemovalName, (Guid?)planted.Names[0].Id, (string?)"Alien (1979) 2160p.mkv"),
                (AuditAction.CopyRemovalNeedsReview, null, AuditTargetKind.CopyRemoval, removal.Id, "Alien (1979)"),
            ],
            await _removals.AuditsAsync(cancellationToken));
        // ADR 0030 — the video left the library: Jellyfin is told, in its own terms, on the same pass.
        Assert.Equal([UpdatedCall], _harness.Writes());
        Assert.Equal([ReplacementVerificationHarness.InJellyfin(VideoPath)], Assert.Single(_harness.JellyfinServer.ScanRequests));
        Assert.Empty(_factory.Quarantine.Renames);
    }

    [Fact]
    public async Task Pass_ends_a_removal_cut_before_its_video_moved_as_not_moved()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Cut between the video's intent and its rename: the video is where it was.
        var planted = await _removals.PlantAsync(
            CopyRemovalState.Moving, [new PlantedName(ReplacementNameKind.Video, VideoPath, TestMedia.File(inode: 2), ReplacementNameState.Moving)]);

        //Act
        await PassAsync(cancellationToken);

        //Assert
        var removal = Assert.Single(await _removals.RemovalsAsync(cancellationToken));
        // Nothing is in the quarantine, so nothing is left to review: the removal ends there.
        Assert.Equal(
            (CopyRemovalState.NotMoved, (string?)"The app stopped while moving this copy."),
            (removal.State, removal.Reason));
        Assert.Equal(ReplacementNameState.Planned, Assert.Single(await _removals.NamesAsync(removal.Id, cancellationToken)).State);
        Assert.Equal(
            [AuditAction.CopyRemovalNameNotMoved, AuditAction.CopyRemovalNotMoved],
            (await _removals.AuditsAsync(cancellationToken)).Select(audit => audit.Action));
        Assert.Empty(_harness.Writes());
        Assert.Equal(planted.Removal.Id, removal.Id);
    }

    [Fact]
    public async Task Pass_settles_a_removal_cut_while_its_video_moved_and_found_nowhere_for_review_saying_where_to_look()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Cut between the video's intent and its outcome, and the video is neither where it was nor in the quarantine: both
        // folders are there, so its absence is no missing mount.
        var planted = await _removals.PlantAsync(
            CopyRemovalState.Moving,
            [
                new PlantedName(ReplacementNameKind.Video, VideoPath, TestMedia.File(inode: 2), ReplacementNameState.Moving),
                new PlantedName(ReplacementNameKind.Companion, SrtPath, TestMedia.File(inode: 3, sizeBytes: 52_345), ReplacementNameState.Planned),
            ]);
        _factory.Files.Remove(VideoPath);
        var quarantinePath = planted.Names[0].QuarantinePath;
        _factory.Files.AddFolder(quarantinePath[..quarantinePath.LastIndexOf('/')]);

        //Act
        await PassAsync(cancellationToken);
        var listed = await _factory.GetHttpRequest("/api/replacements/removals", _harness.Owner.AccessToken);

        //Assert
        var reason = $"The app stopped while moving this copy, and found its video neither at “{VideoPath}” nor in the quarantine at “{quarantinePath}”. "
            + "Look in both places to see where it went.";
        var removal = Assert.Single(await _removals.RemovalsAsync(cancellationToken));
        // Never NotMoved, which would end it out of sight: it waits for review under Removed copies, with nothing to restore
        // or delete, and nothing known to have left the library.
        Assert.Equal(
            (CopyRemovalState.NeedsReview, (string?)reason, (DateTime?)null),
            (removal.State, removal.Reason, removal.LibraryNoticeDueUtc));
        Assert.Equal(
            [
                (ReplacementNameState.Lost, (string?)"The app stopped while moving “Alien (1979) 2160p.mkv”, and found it neither where it was nor in the quarantine."),
                (ReplacementNameState.Planned, (string?)null),
            ],
            (await _removals.NamesAsync(removal.Id, cancellationToken)).Select(name => (name.State, name.Problem)));
        Assert.Equal(
            [
                (AuditAction.CopyRemovalNameNotMoved, (Guid?)null, (AuditTargetKind?)AuditTargetKind.CopyRemovalName, (Guid?)planted.Names[0].Id, (string?)"Alien (1979) 2160p.mkv"),
                (AuditAction.CopyRemovalNeedsReview, null, AuditTargetKind.CopyRemoval, removal.Id, "Alien (1979)"),
            ],
            await _removals.AuditsAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        var item = Assert.Single((await listed.Content.ReadFromJsonAsync<CopyRemovalListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!.Items);
        Assert.Equal(
            (removal.Id, CopyRemovalState.NeedsReview, (string?)reason, false, false),
            (item.Id, item.State, item.Reason, item.CanRestore, item.CanDeleteForGood));
        Assert.Empty(_harness.Writes());
        Assert.Empty(_factory.Quarantine.Renames);
    }

    [Fact]
    public async Task Pass_ends_a_restore_cut_after_its_last_name_came_back_restored_and_tells_jellyfin()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await _removals.PlantAsync(
            CopyRemovalState.Restoring,
            [
                new PlantedName(ReplacementNameKind.Video, VideoPath, TestMedia.File(inode: 2), ReplacementNameState.Restoring),
                new PlantedName(ReplacementNameKind.Companion, SrtPath, TestMedia.File(inode: 3, sizeBytes: 52_345), ReplacementNameState.Restoring),
            ]);
        // Both renamed back before the cut.
        foreach (var name in planted.Names)
            _factory.Files.Move(name.QuarantinePath, name.AppPath);

        //Act
        await PassAsync(cancellationToken);

        //Assert
        var removal = Assert.Single(await _removals.RemovalsAsync(cancellationToken));
        Assert.Equal((CopyRemovalState.Restored, (string?)null), (removal.State, removal.Reason));
        Assert.All(await _removals.NamesAsync(removal.Id, cancellationToken), name => Assert.Equal(ReplacementNameState.Restored, name.State));
        Assert.Equal(
            [AuditAction.CopyRemovalNameRestored, AuditAction.CopyRemovalNameRestored, AuditAction.CopyRemovalRestored],
            (await _removals.AuditsAsync(cancellationToken)).Select(audit => audit.Action));
        Assert.Equal(["Created", "Created"], Assert.Single(_harness.JellyfinServer.ScanUpdateTypes));
    }

    [Fact]
    public async Task Pass_settles_a_deletion_cut_part_way_for_review_with_what_the_quarantine_still_holds()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await _removals.PlantAsync(
            CopyRemovalState.Deleting,
            [
                new PlantedName(ReplacementNameKind.Video, VideoPath, TestMedia.File(inode: 2), ReplacementNameState.Deleting),
                new PlantedName(ReplacementNameKind.Companion, SrtPath, TestMedia.File(inode: 3, sizeBytes: 52_345)),
            ]);
        // The video was deleted before the cut; its subtitles are still in the quarantine.
        _factory.Files.Remove(planted.Names[0].QuarantinePath);

        //Act
        await PassAsync(cancellationToken);

        //Assert
        var removal = Assert.Single(await _removals.RemovalsAsync(cancellationToken));
        Assert.Equal(
            (CopyRemovalState.NeedsReview, (string?)"The app stopped while deleting this copy for good: 1 of 2 names is deleted."),
            (removal.State, removal.Reason));
        Assert.Equal(
            [ReplacementNameState.Deleted, ReplacementNameState.Quarantined],
            (await _removals.NamesAsync(removal.Id, cancellationToken)).Select(name => name.State));
        Assert.Equal(
            [AuditAction.CopyRemovalNameDeleted, AuditAction.CopyRemovalNeedsReview],
            (await _removals.AuditsAsync(cancellationToken)).Select(audit => audit.Action));
        Assert.Empty(_factory.Quarantine.Deletions);
        Assert.Empty(_harness.Writes());
    }

    [Fact]
    public async Task Pass_leaves_a_removal_someone_in_this_process_holds_as_it_is()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await _removals.PlantAsync(
            CopyRemovalState.Moving, [new PlantedName(ReplacementNameKind.Video, VideoPath, TestMedia.File(inode: 2), ReplacementNameState.Moving)]);
        var inHand = _factory.Resolve<CopyRemovalsInHand>();
        inHand.TryTake(planted.Removal.Id);

        //Act
        try
        {
            await PassAsync(cancellationToken);
        }
        finally
        {
            inHand.Release(planted.Removal.Id);
        }

        //Assert
        // The owner's request is writing for it: no cut at all.
        Assert.Equal(CopyRemovalState.Moving, Assert.Single(await _removals.RemovalsAsync(cancellationToken)).State);
        Assert.Equal(ReplacementNameState.Moving, Assert.Single(await _removals.NamesAsync(planted.Removal.Id, cancellationToken)).State);
        Assert.Empty(await _removals.AuditsAsync(cancellationToken));
    }

    [Fact]
    public async Task Pass_leaves_a_cut_removal_whose_name_cant_be_looked_up_for_the_next_pass()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await _removals.PlantAsync(
            CopyRemovalState.Moving, [new PlantedName(ReplacementNameKind.Video, VideoPath, TestMedia.File(inode: 2), ReplacementNameState.Moving)]);
        // The quarantine can't be read: where the video is says nothing.
        _factory.Files.AddProblem(planted.Names[0].QuarantinePath, FileInventoryStatus.Unreadable, "The app may not read the quarantine.");

        //Act
        await _factory.QuarantineReplacementsAsync(cancellationToken);

        //Assert
        Assert.Equal(CopyRemovalState.Moving, Assert.Single(await _removals.RemovalsAsync(cancellationToken)).State);
        Assert.Empty(await _removals.AuditsAsync(cancellationToken));
        // Warning, so it ships off the box: a removal can't wait here unseen.
        var warning = Assert.Single(_factory.Logs.Entries, entry => entry.Level == LogLevel.Warning && entry.Category == typeof(CopyRemovalRecovery).FullName);
        Assert.Equal(
            $"Copy removal {planted.Removal.Id} was cut while Moving, and 1 of its 1 names couldn't be looked up; the next pass looks again.",
            warning.Message);
    }

    [Fact]
    public async Task Pass_sends_a_library_notice_a_removal_still_owes_and_settles_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Jellyfin couldn't take the notice at the removal; it is owed since.
        var planted = await _removals.PlantAsync(
            CopyRemovalState.Quarantined,
            [new PlantedName(ReplacementNameKind.Video, VideoPath, TestMedia.File(inode: 2))],
            libraryNoticeDueUtc: _factory.Time.GetUtcNow().UtcDateTime);

        //Act
        await PassAsync(cancellationToken);

        //Assert
        Assert.Equal([UpdatedCall], _harness.Writes());
        Assert.Equal(["Deleted"], Assert.Single(_harness.JellyfinServer.ScanUpdateTypes));
        var removal = Assert.Single(await _removals.RemovalsAsync(cancellationToken));
        Assert.Equal((CopyRemovalState.Quarantined, (DateTime?)null), (removal.State, removal.LibraryNoticeDueUtc));
        Assert.Equal(planted.Removal.Id, removal.Id);
    }

    /// <summary>
    /// One pass of the quarantine's tracker, which must not have logged a warning or an error of the app's own. The
    /// framework's are left out: in a container ASP.NET Core warns, the first time a host needs Data Protection, that its
    /// keys may not outlive the container, whichever test that happens in.
    /// </summary>
    private async Task PassAsync(CancellationToken cancellationToken)
    {
        await _factory.QuarantineReplacementsAsync(cancellationToken);
        if (_factory.Logs.Entries.FirstOrDefault(entry => entry.Level >= LogLevel.Warning && IsTheApps(entry.Category)) is { } logged)
            throw new InvalidOperationException($"A pass logged a {logged.Level}: {logged.Category}: {logged.Message}");
    }

    private static bool IsTheApps(string category) => category.StartsWith("MediaAcquisitionManager.", StringComparison.Ordinal);
}
