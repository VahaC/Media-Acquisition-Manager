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
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Replacements;

// MAM-40 — the owner's Restore (POST /api/replacements/removals/{id}/restore) and Delete for good (…/delete-for-good, no body:
// the whole removal) of a copy he removed, as a replacement's (MAM-26): Restore renames every name back where it was — the
// copy counts in its group again and Jellyfin hears it came back —, never over a file there now; Delete for good deletes them
// from the quarantine and tells Jellyfin nothing more. Each checks everything before anything is written — the switch, the
// files in the quarantine as the move left them — and records each name's intent and outcome between the removal's start and
// end, the owner their actor. A write refused part-way waits for review with the names already done kept so; a removal with
// nothing in the quarantine is refused; an unknown one is a bare 404; one someone in this process holds is refused. Unlike a
// replacement's, a removal waiting for review may be deleted for good: it has no Retry.
public sealed class CopyRemovalActionsHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/replacements/removals";
    private const string SameWork = "?class=SameWork";
    private const string AlienImdbId = "tt0078748";
    private const string KeptPath = "/media/movies/Alien (1979)/Alien (1979) 1080p.mkv";
    private const string RemovedPath = "/media/movies/Alien (1979)/Alien (1979) 2160p.mkv";
    private const string RemovedSrtPath = "/media/movies/Alien (1979)/Alien (1979) 2160p.uk.srt";
    private const long VideoSize = 3_891_719_630;
    private const long SrtSize = 52_345;
    private const string UpdatedCall = "POST /Library/Media/Updated";

    // Sentences the page shows, spelled out rather than read from product code.
    private const string NameTakenError = "“/media/movies/Alien (1979)/Alien (1979) 2160p.mkv” already holds a file, so nothing was put back.";
    private const string RestoreSwitchedOffError =
        "Replacing and removing copies is switched off on the Media folders page, so nothing can be put back. Turn on “Replace copies after verification”, then press Restore again.";
    private const string NothingToRestoreError = "Nothing of this removal is in the quarantine, so there's nothing to put back.";
    private const string VideoDeletedError =
        "This copy's video was deleted for good, so nothing is put back without it. Delete for good what the quarantine still holds.";
    private const string BeingWorkedOnError = "This removal is being worked on right now. Try again in a moment.";
    private const string SrtNotAllowed = "The app may not write in the folder of “/media/movies/Alien (1979)/Alien (1979) 2160p.uk.srt”.";
    private const string NothingToDeleteError = "This removal holds nothing in the quarantine to delete.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private ReplacementVerificationHarness _harness = null!;
    private CopyRemovalPlanter _removals = null!;

    public CopyRemovalActionsHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _harness = await ReplacementVerificationHarness.CreateAsync(_factory);
        _removals = new CopyRemovalPlanter(_factory, _harness);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── Restore ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Restore_puts_every_name_back_tells_jellyfin_they_came_back_and_the_copy_counts_in_its_group_again()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var removal = await RemoveAsync(cancellationToken);
        var names = await _removals.NamesAsync(removal.Id, cancellationToken);
        var trailBefore = (await _removals.AuditsAsync(cancellationToken)).Count;
        _harness.Mark();

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{removal.Id}/restore", _harness.Owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<CopyRemovalDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal(
            (CopyRemovalState.Restored, (string?)null, 0L, 0L, false, false),
            (body.State, body.Reason, body.SizeBytes, body.FreesBytes, body.CanRestore, body.CanDeleteForGood));
        Assert.All(body.Names, name => Assert.Equal(ReplacementNameState.Restored, name.State));
        // Each name back where it was, with its identity; nothing left in the quarantine.
        Assert.Equal([(names[0].QuarantinePath, RemovedPath), (names[1].QuarantinePath, RemovedSrtPath)], _factory.Quarantine.Renames.Skip(2));
        Assert.Equal((FileInventoryStatus.Found, (long?)2), await WhatIsAtAsync(RemovedPath, cancellationToken));
        Assert.Equal((FileInventoryStatus.Found, (long?)3), await WhatIsAtAsync(RemovedSrtPath, cancellationToken));
        var row = Assert.Single(await _removals.RemovalsAsync(cancellationToken));
        Assert.Equal((CopyRemovalState.Restored, (DateTime?)null), (row.State, row.LibraryNoticeDueUtc));
        Assert.Equal(
            [
                RemovalAudited(AuditAction.CopyRemovalRestoreStarted, removal.Id),
                NameAudited(AuditAction.CopyRemovalNameRestoring, names[0]),
                NameAudited(AuditAction.CopyRemovalNameRestored, names[0]),
                NameAudited(AuditAction.CopyRemovalNameRestoring, names[1]),
                NameAudited(AuditAction.CopyRemovalNameRestored, names[1]),
                RemovalAudited(AuditAction.CopyRemovalRestored, removal.Id),
            ],
            (await _removals.AuditsAsync(cancellationToken)).Skip(trailBefore));
        // ADR 0030 — Jellyfin heard they came back, in its own terms.
        Assert.Equal([UpdatedCall], _harness.Writes());
        Assert.Equal(["Created", "Created"], _harness.JellyfinServer.ScanUpdateTypes.Last());
        // Put back, the copy is a copy of Alien again.
        var group = await GroupAsync(cancellationToken);
        Assert.Equal([KeptPath, RemovedPath], group.Files.Select(file => file.File.AppPath));
    }

    [Fact]
    public async Task Restore_when_its_old_path_holds_a_file_now_is_409_and_writes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await PlantQuarantinedAsync();
        // Something new at the old path since: never written over.
        _factory.Files.AddFile(RemovedPath, TestMedia.File(inode: 9));
        var before = await _removals.NamesAsync(planted.Removal.Id, cancellationToken);

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{planted.Removal.Id}/restore", _harness.Owner.AccessToken);

        //Assert
        Assert.Equal((HttpStatusCode.Conflict, NameTakenError), (response.StatusCode, await response.ReadErrorAsync(cancellationToken)));
        await AssertUnchangedAsync(planted, before, cancellationToken);
    }

    [Fact]
    public async Task Restore_while_switched_off_is_409_and_writes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await PlantQuarantinedAsync();
        await _factory.WithDbAsync(db => db.ReplacementSettings.ExecuteUpdateAsync(setters => setters.SetProperty(settings => settings.IsEnabled, false), cancellationToken));
        var before = await _removals.NamesAsync(planted.Removal.Id, cancellationToken);

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{planted.Removal.Id}/restore", _harness.Owner.AccessToken);

        //Assert
        Assert.Equal((HttpStatusCode.Conflict, RestoreSwitchedOffError), (response.StatusCode, await response.ReadErrorAsync(cancellationToken)));
        await AssertUnchangedAsync(planted, before, cancellationToken);
    }

    [Fact]
    public async Task Restore_refused_part_way_waits_for_review_with_the_names_already_back()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await PlantQuarantinedAsync();
        _factory.Quarantine.Refuse(RemovedSrtPath, FileQuarantineStatus.NotAllowed, SrtNotAllowed);
        const string reason = "“Alien (1979) 2160p.uk.srt” couldn't be put back: " + SrtNotAllowed + " 1 of 2 names is back where it was.";

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{planted.Removal.Id}/restore", _harness.Owner.AccessToken);

        //Assert
        Assert.Equal((HttpStatusCode.Conflict, reason), (response.StatusCode, await response.ReadErrorAsync(cancellationToken)));
        var row = Assert.Single(await _removals.RemovalsAsync(cancellationToken));
        Assert.Equal((CopyRemovalState.NeedsReview, (string?)reason), (row.State, row.Reason));
        Assert.Equal(
            [(ReplacementNameState.Restored, (string?)null), (ReplacementNameState.Quarantined, (string?)SrtNotAllowed)],
            (await _removals.NamesAsync(planted.Removal.Id, cancellationToken)).Select(name => (name.State, name.Problem)));
        Assert.Equal(
            [
                AuditAction.CopyRemovalRestoreStarted,
                AuditAction.CopyRemovalNameRestoring,
                AuditAction.CopyRemovalNameRestored,
                AuditAction.CopyRemovalNameRestoring,
                AuditAction.CopyRemovalNameNotRestored,
                AuditAction.CopyRemovalNeedsReview,
            ],
            (await _removals.AuditsAsync(cancellationToken)).Select(audit => audit.Action));
    }

    [Fact]
    public async Task Restore_of_a_removal_that_moved_nothing_is_409()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await _removals.PlantAsync(
            CopyRemovalState.NotMoved, [new PlantedName(ReplacementNameKind.Video, RemovedPath, TestMedia.File(inode: 2), ReplacementNameState.Stayed, "Refused.")]);
        var before = await _removals.NamesAsync(planted.Removal.Id, cancellationToken);

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{planted.Removal.Id}/restore", _harness.Owner.AccessToken);

        //Assert
        Assert.Equal((HttpStatusCode.Conflict, NothingToRestoreError), (response.StatusCode, await response.ReadErrorAsync(cancellationToken)));
        await AssertUnchangedAsync(planted, before, cancellationToken);
    }

    [Fact]
    public async Task Restore_of_a_removal_whose_video_was_deleted_for_good_is_409()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A Delete for good stopped after the video: its subtitles would go back alone.
        var planted = await _removals.PlantAsync(
            CopyRemovalState.NeedsReview,
            [
                new PlantedName(ReplacementNameKind.Video, RemovedPath, TestMedia.File(inode: 2), ReplacementNameState.Deleted),
                new PlantedName(ReplacementNameKind.Companion, RemovedSrtPath, TestMedia.File(inode: 3, sizeBytes: SrtSize)),
            ]);
        var before = await _removals.NamesAsync(planted.Removal.Id, cancellationToken);

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{planted.Removal.Id}/restore", _harness.Owner.AccessToken);

        //Assert
        Assert.Equal((HttpStatusCode.Conflict, VideoDeletedError), (response.StatusCode, await response.ReadErrorAsync(cancellationToken)));
        await AssertUnchangedAsync(planted, before, cancellationToken);
    }

    [Fact]
    public async Task Restore_while_the_removal_is_being_worked_on_is_409_and_writes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await PlantQuarantinedAsync();
        var before = await _removals.NamesAsync(planted.Removal.Id, cancellationToken);
        var inHand = _factory.Resolve<CopyRemovalsInHand>();
        inHand.TryTake(planted.Removal.Id);

        //Act
        HttpResponseMessage response;
        try
        {
            response = await _factory.PostHttpRequest($"{BaseUrl}/{planted.Removal.Id}/restore", _harness.Owner.AccessToken);
        }
        finally
        {
            inHand.Release(planted.Removal.Id);
        }

        //Assert
        Assert.Equal((HttpStatusCode.Conflict, BeingWorkedOnError), (response.StatusCode, await response.ReadErrorAsync(cancellationToken)));
        await AssertUnchangedAsync(planted, before, cancellationToken);
    }

    [Fact]
    public async Task Restore_and_delete_for_good_of_an_unknown_removal_are_404_with_no_body()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();

        //Act
        var restore = await _factory.PostHttpRequest($"{BaseUrl}/{id}/restore", _harness.Owner.AccessToken);
        var delete = await _factory.PostHttpRequest($"{BaseUrl}/{id}/delete-for-good", _harness.Owner.AccessToken);

        //Assert
        Assert.Equal((HttpStatusCode.NotFound, ""), (restore.StatusCode, await restore.Content.ReadAsStringAsync(cancellationToken)));
        Assert.Equal((HttpStatusCode.NotFound, ""), (delete.StatusCode, await delete.Content.ReadAsStringAsync(cancellationToken)));
    }

    // ── Delete for good ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_for_good_deletes_every_name_from_the_quarantine_and_ends_the_removal()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var removal = await RemoveAsync(cancellationToken);
        var names = await _removals.NamesAsync(removal.Id, cancellationToken);
        var trailBefore = (await _removals.AuditsAsync(cancellationToken)).Count;
        _harness.Mark();

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{removal.Id}/delete-for-good", _harness.Owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<CopyRemovalDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal((CopyRemovalState.Deleted, 0L, false, false), (body.State, body.SizeBytes, body.CanRestore, body.CanDeleteForGood));
        Assert.Equal([names[0].QuarantinePath, names[1].QuarantinePath], _factory.Quarantine.Deletions);
        Assert.Equal((FileInventoryStatus.NotFound, (long?)null), await WhatIsAtAsync(names[0].QuarantinePath, cancellationToken));
        Assert.All(
            await _removals.NamesAsync(removal.Id, cancellationToken),
            name => Assert.Equal((ReplacementNameState.Deleted, (DateTime?)_factory.Time.GetUtcNow().UtcDateTime), (name.State, name.DeletedUtc)));
        Assert.Equal(CopyRemovalState.Deleted, Assert.Single(await _removals.RemovalsAsync(cancellationToken)).State);
        Assert.Equal(
            [
                RemovalAudited(AuditAction.CopyRemovalDeleteStarted, removal.Id),
                NameAudited(AuditAction.CopyRemovalNameDeleting, names[0]),
                NameAudited(AuditAction.CopyRemovalNameDeleted, names[0]),
                NameAudited(AuditAction.CopyRemovalNameDeleting, names[1]),
                NameAudited(AuditAction.CopyRemovalNameDeleted, names[1]),
                RemovalAudited(AuditAction.CopyRemovalDeletedForGood, removal.Id),
            ],
            (await _removals.AuditsAsync(cancellationToken)).Skip(trailBefore));
        // Jellyfin heard the names leave at the move; a deletion tells it nothing more. The kept copy is where it was.
        Assert.Empty(_harness.Writes());
        Assert.Equal((FileInventoryStatus.Found, (long?)1), await WhatIsAtAsync(KeptPath, cancellationToken));
    }

    [Fact]
    public async Task Delete_for_good_when_a_name_changed_in_the_quarantine_is_409_and_writes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await PlantQuarantinedAsync();
        var srtInQuarantine = CopyRemovalPlanter.QuarantinePathOf(planted.Removal.Id, RemovedSrtPath);
        _factory.Files.AddFile(srtInQuarantine, TestMedia.File(inode: 3, sizeBytes: SrtSize + 1));
        var before = await _removals.NamesAsync(planted.Removal.Id, cancellationToken);
        var changed = $"“{srtInQuarantine}” changed in the quarantine since it was moved there, so nothing was deleted.";

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{planted.Removal.Id}/delete-for-good", _harness.Owner.AccessToken);

        //Assert
        Assert.Equal((HttpStatusCode.Conflict, changed), (response.StatusCode, await response.ReadErrorAsync(cancellationToken)));
        await AssertUnchangedAsync(planted, before, cancellationToken);
    }

    [Fact]
    public async Task Delete_for_good_of_a_removal_waiting_for_review_deletes_what_the_quarantine_holds()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A restore stopped after the video: the subtitles still wait in the quarantine. A removal has no Retry, so Delete
        // for good settles it.
        var planted = await _removals.PlantAsync(
            CopyRemovalState.NeedsReview,
            [
                new PlantedName(ReplacementNameKind.Video, RemovedPath, TestMedia.File(inode: 2), ReplacementNameState.Restored),
                new PlantedName(ReplacementNameKind.Companion, RemovedSrtPath, TestMedia.File(inode: 3, sizeBytes: SrtSize)),
            ],
            reason: "Stopped.");

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{planted.Removal.Id}/delete-for-good", _harness.Owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<CopyRemovalDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal((CopyRemovalState.Deleted, (string?)null), (body.State, body.Reason));
        Assert.Equal([CopyRemovalPlanter.QuarantinePathOf(planted.Removal.Id, RemovedSrtPath)], _factory.Quarantine.Deletions);
        // The video stays back where it was.
        Assert.Equal((FileInventoryStatus.Found, (long?)2), await WhatIsAtAsync(RemovedPath, cancellationToken));
    }

    [Fact]
    public async Task Delete_for_good_of_a_removal_that_moved_nothing_is_409()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await _removals.PlantAsync(
            CopyRemovalState.NotMoved, [new PlantedName(ReplacementNameKind.Video, RemovedPath, TestMedia.File(inode: 2), ReplacementNameState.Stayed, "Refused.")]);
        var before = await _removals.NamesAsync(planted.Removal.Id, cancellationToken);

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{planted.Removal.Id}/delete-for-good", _harness.Owner.AccessToken);

        //Assert
        Assert.Equal((HttpStatusCode.Conflict, NothingToDeleteError), (response.StatusCode, await response.ReadErrorAsync(cancellationToken)));
        await AssertUnchangedAsync(planted, before, cancellationToken);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>Two inspected copies of Alien, the second with its subtitles, and the owner's real Remove of the second.</summary>
    private async Task<CopyRemovalEntity> RemoveAsync(CancellationToken cancellationToken)
    {
        await _harness.PlantFilmAsync("Alien", 1979, KeptPath, inode: 1, imdbId: AlienImdbId);
        await _harness.PlantFilmAsync("Alien", 1979, RemovedPath, inode: 2, imdbId: AlienImdbId);
        _factory.Files.AddFile(RemovedSrtPath, TestMedia.File(inode: 3, sizeBytes: SrtSize));
        await _harness.InspectAsync(cancellationToken);
        var response = await _factory.PostHttpRequest(BaseUrl, _harness.Owner.AccessToken, (await GroupAsync(cancellationToken)).RemoveRequestFor(RemovedPath));
        if (response.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"The removal answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellationToken)}");
        return Assert.Single(await _removals.RemovalsAsync(cancellationToken));
    }

    /// <summary>A removal resting in the quarantine with the video and its subtitles, as a Remove leaves it.</summary>
    private Task<PlantedRemoval> PlantQuarantinedAsync() =>
        _removals.PlantAsync(
            CopyRemovalState.Quarantined,
            [
                new PlantedName(ReplacementNameKind.Video, RemovedPath, TestMedia.File(inode: 2)),
                new PlantedName(ReplacementNameKind.Companion, RemovedSrtPath, TestMedia.File(inode: 3, sizeBytes: SrtSize)),
            ]);

    private async Task<LibraryDuplicateGroupDto> GroupAsync(CancellationToken cancellationToken) =>
        Assert.Single((await _factory.DuplicatesAsync(_harness.Owner.AccessToken, SameWork, cancellationToken)).Items);

    private async Task<(FileInventoryStatus Status, long? Inode)> WhatIsAtAsync(string appPath, CancellationToken cancellationToken)
    {
        var described = await _factory.Files.DescribeAsync(appPath, [ReplacementVerificationHarness.MediaRoot], cancellationToken);
        return (described.Status, described.Value?.Inode);
    }

    private (AuditAction, Guid?, AuditTargetKind?, Guid?, string?) RemovalAudited(AuditAction action, Guid copyRemovalId) =>
        (action, _harness.Owner.User.Id, AuditTargetKind.CopyRemoval, copyRemovalId, "Alien (1979)");

    private (AuditAction, Guid?, AuditTargetKind?, Guid?, string?) NameAudited(AuditAction action, CopyRemovalNameEntity name) =>
        (action, _harness.Owner.User.Id, AuditTargetKind.CopyRemovalName, name.Id, name.AppPath[(name.AppPath.LastIndexOf('/') + 1)..]);

    /// <summary>The removal and its names as planted, no audit row, and nothing renamed or deleted.</summary>
    private async Task AssertUnchangedAsync(PlantedRemoval planted, IReadOnlyList<CopyRemovalNameEntity> before, CancellationToken cancellationToken)
    {
        var row = Assert.Single(await _removals.RemovalsAsync(cancellationToken));
        Assert.Equal((planted.Removal.State, planted.Removal.Reason, planted.Removal.StateChangedUtc), (row.State, row.Reason, row.StateChangedUtc));
        Assert.Equal(
            before.Select(name => (name.Id, name.State, name.Problem)),
            (await _removals.NamesAsync(planted.Removal.Id, cancellationToken)).Select(name => (name.Id, name.State, name.Problem)));
        Assert.Empty(await _removals.AuditsAsync(cancellationToken));
        Assert.Empty(_factory.Quarantine.Renames);
        Assert.Empty(_factory.Quarantine.Deletions);
    }
}
