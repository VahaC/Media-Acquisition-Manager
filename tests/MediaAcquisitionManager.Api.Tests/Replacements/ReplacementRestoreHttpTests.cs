using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Replacements;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Replacements;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Replacements;

// MAM-26, ADR 0030, ADR 0033 — the owner's Restore (POST /api/replacements/{id}/restore): every name the replacement holds
// in the quarantine renamed back to where it was, keeping its device and inode, videos first, each with its intent row
// committed before the rename and its outcome row after, the owner the actor; the job ends Restored with the library owed a
// notice. Every check comes before anything is written — replacement switched off, an old path no longer in a ticked
// folder, a name changed in the quarantine, its old path taken, its folder gone (no folder is made for it) — and refuses
// with 409, renaming and recording nothing — so does a link at the old path that leads nowhere, a video Delete for good
// already took, and a quarantine out of the Media folders, while one whose root a parent took the place of is still reached.
// A rename refused part-way, a rename back that moved another file put at the name (lost), or the switch turned off or the
// folder unticked between two names, stops the job for review with the names already back kept so, and says how many; a
// request cut after a rename leaves the job to the mover's next pass, which finds the name back. One request at a time per
// job: a second click, or the mover, gets 409. A member never reaches any of it. The mover's own moves are
// ReplacementQuarantine*HttpTests'; Delete for good is ReplacementDeleteForGoodHttpTests'.
public sealed class ReplacementRestoreHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/replacements";
    private const string OwnerOnlyBody = """{"error":"Only the installation owner can do this."}""";
    private const string FolderPath = "/media/movies/Alien (1979)";
    private const string TheatricalPath = "/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.mkv";
    private const string SubtitlePath = "/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.uk.srt";
    private const string TheatricalName = "Alien (1979) Theatrical Cut.mkv";
    private const string SubtitleName = "Alien (1979) Theatrical Cut.uk.srt";
    private const long SubtitleSize = 98_304;

    // Sentences the job's page shows, spelled out rather than read from product code.
    private const string NothingToRestoreError = "Nothing of this replacement is in the quarantine, so there's nothing to put back.";
    private const string SwitchedOffError =
        "Replacement is switched off on the Media folders page, so nothing can be put back. Switch it on, then press Restore again.";
    private const string NotTickedError =
        "“/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.mkv” is no longer in a folder ticked for replacement, so nothing was put back.";
    private const string ChangedError = "“{0}” changed in the quarantine since it was moved there, so nothing was put back.";
    private const string TakenError = "“/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.mkv” already holds a file, so nothing was put back.";
    private const string FolderGoneError = "The folder “/media/movies/Alien (1979)” is gone, so nothing was put back; the app creates no folder.";
    private const string NotSupportedError = "The app can't rename files on the platform it runs on, so nothing was put back.";
    private const string BeingWorkedOnError = "This replacement is being worked on right now. Try again in a moment.";
    private const string SubtitleTakenProblem =
        "Something is already at “/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.uk.srt”, and the app never renames over it.";
    private const string SubtitleTakenReason =
        "“Alien (1979) Theatrical Cut.uk.srt” couldn't be put back: Something is already at “/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.uk.srt”, and the app never renames over it. 1 of 2 names is back where it was.";
    private const string SwitchedOffMidwayReason =
        "“Alien (1979) Theatrical Cut.uk.srt” couldn't be put back: Replacement was switched off on the Media folders page. 1 of 2 names is back where it was.";
    private const string MoverStoppedReason = "Still held by the torrent “Alien” in “qBittorrent”: remove that torrent there without its files, then press Retry.";
    private const string UntickedMidwayReason =
        "“Alien (1979) Theatrical Cut.uk.srt” couldn't be put back: “/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.uk.srt” is no longer in a "
        + "folder ticked for replacement. 1 of 2 names is back where it was.";
    private const string SwappedProblem =
        "A different file now sits at “/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.uk.srt”, as another file took the name's place in the "
        + "quarantine just before the rename back, and the old copy's own file wasn't found.";
    private const string SwappedReason = "“Alien (1979) Theatrical Cut.uk.srt” couldn't be put back: " + SwappedProblem + " 1 of 2 names is back where it was.";
    private const string VideoDeletedError =
        "A video of this replacement was deleted for good, so nothing is put back without it. Press Retry, then Delete for good what the quarantine still holds.";
    private const string NoLongerInMediaFoldersError = "“/media/.mam-quarantine” is no longer inside the Media folders, so nothing was put back.";
    private const string CutRestoreReason = "The app stopped while putting the old copies back: 1 of 2 names is back where it was.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private ReplacementVerificationHarness _harness = null!;
    private ReplacementQuarantinePlanter _planter = null!;

    public ReplacementRestoreHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    /// <summary>Each check Restore makes before it writes, by name.</summary>
    public static TheoryData<string> Refusals =>
        ["switched off", "unticked folder", "changed in the quarantine", "old path taken", "old path a link leading nowhere", "folder gone"];

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _harness = await ReplacementVerificationHarness.CreateAsync(_factory);
        _planter = new ReplacementQuarantinePlanter(_factory, _harness);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── put back ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Restore_of_a_quarantined_replacement_puts_every_name_back_with_its_identity_and_is_200_restored()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var planted = await PlantQuarantinedAsync(ReplacementJobState.Quarantined);
        var id = planted.Replacement.Id;
        // The library won't take the notice now, so it stays owed whatever the notice does with it; the notice itself is
        // ReplacementLibraryNoticeTests', and a notice Jellyfin took is ReplacementQuarantineFlowHttpTests'.
        _harness.JellyfinServer.ScanRefusal = HttpStatusCode.ServiceUnavailable;
        _factory.Logs.Clear();

        //Act
        var response = await RestoreAsync(id);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var job = await ReadJobAsync(response, cancellationToken);
        Assert.Equal(planted.Download.Id, job.Id);
        Assert.Equal(
            (ReplacementJobState.Restored, id, (string?)null, 0, false),
            (job.Replacement!.State, job.Replacement.Id, job.Replacement.Reason, job.Replacement.QuarantinedCopyCount, job.Replacement.CanRetry));
        // Videos first, then companions; each back at its own path, never through a folder made for it.
        Assert.Equal([(QuarantinePathOf(id, TheatricalPath), TheatricalPath), (QuarantinePathOf(id, SubtitlePath), SubtitlePath)], _factory.Quarantine.Renames);
        Assert.Empty(_factory.Quarantine.CreatedFolders);
        Assert.Equal([(1L, TestMedia.DeviceId), (2L, TestMedia.DeviceId)], [await IdentityAtAsync(TheatricalPath), await IdentityAtAsync(SubtitlePath)]);
        Assert.Equal(FileInventoryStatus.NotFound, (await DescribeAsync(QuarantinePathOf(id, TheatricalPath))).Status);
        var replacement = await JobAsync(id, cancellationToken);
        Assert.Equal(
            (ReplacementJobState.Restored, (string?)null, now, (DateTime?)now, (DateTime?)null),
            (replacement.State, replacement.Reason, replacement.StateChangedUtc, replacement.LibraryNoticeDueUtc, replacement.QuarantineCheckedUtc));
        Assert.Equal(
            [(ReplacementNameState.Restored, (DateTime?)now, (string?)null), (ReplacementNameState.Restored, now, null)],
            (await NamesAsync(id, cancellationToken)).Select(name => (name.State, name.RestoredUtc, name.Problem)));
        Assert.Equal(
            [
                JobRow(AuditAction.ReplacementRestoreStarted, id),
                NameRow(AuditAction.ReplacementNameRestoring, planted.Names[0], TheatricalName),
                NameRow(AuditAction.ReplacementNameRestored, planted.Names[0], TheatricalName),
                NameRow(AuditAction.ReplacementNameRestoring, planted.Names[1], SubtitleName),
                NameRow(AuditAction.ReplacementNameRestored, planted.Names[1], SubtitleName),
                JobRow(AuditAction.ReplacementRestored, id),
            ],
            await _harness.QuarantineAuditsAsync(cancellationToken));
        // Ids and states only: a path or a title never reaches a log that ships off the box.
        var logged = _factory.Logs.Entries.Where(entry => entry.Category == typeof(ReplacementJobActions).FullName).Select(entry => entry.Message).ToList();
        Assert.Equal([$"Replacement job {id} went from Quarantined to Restoring.", $"Replacement job {id} went from Restoring to Restored."], logged);
    }

    [Fact]
    public async Task Restore_of_a_replacement_in_review_puts_back_what_the_quarantine_holds_and_leaves_what_stayed()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        // The mover moved the video and stopped over the subtitle, which a torrent holds where it is.
        var planted = await _planter.PlantAsync(
            ReplacementJobState.NeedsReview,
            [
                new PlantedName(ReplacementNameKind.Video, TheatricalPath, TestMedia.File(inode: 1)),
                new PlantedName(ReplacementNameKind.Companion, SubtitlePath, Subtitle(), ReplacementNameState.Stayed, "Held by a torrent."),
            ],
            quarantineCheckedUtc: now,
            reason: MoverStoppedReason);
        var id = planted.Replacement.Id;

        //Act
        var response = await RestoreAsync(id);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ReplacementJobState.Restored, (await ReadJobAsync(response, cancellationToken)).Replacement!.State);
        Assert.Equal([(QuarantinePathOf(id, TheatricalPath), TheatricalPath)], _factory.Quarantine.Renames);
        Assert.Equal(
            [(ReplacementNameState.Restored, (string?)null), (ReplacementNameState.Stayed, "Held by a torrent.")],
            (await NamesAsync(id, cancellationToken)).Select(name => (name.State, name.Problem)));
        var replacement = await JobAsync(id, cancellationToken);
        Assert.Equal((ReplacementJobState.Restored, (string?)null), (replacement.State, replacement.Reason));
        Assert.Equal(
            [
                JobRow(AuditAction.ReplacementRestoreStarted, id),
                NameRow(AuditAction.ReplacementNameRestoring, planted.Names[0], TheatricalName),
                NameRow(AuditAction.ReplacementNameRestored, planted.Names[0], TheatricalName),
                JobRow(AuditAction.ReplacementRestored, id),
            ],
            await _harness.QuarantineAuditsAsync(cancellationToken));
    }

    [Fact]
    public async Task Restore_of_two_copies_puts_back_every_video_before_any_companion()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        const string aliensPath = "/media/movies/Aliens (1986)/Aliens (1986).mkv";
        var planted = await _planter.PlantAsync(
            ReplacementJobState.Quarantined,
            [
                new PlantedName(ReplacementNameKind.Video, TheatricalPath, TestMedia.File(inode: 1)),
                new PlantedName(ReplacementNameKind.Companion, SubtitlePath, Subtitle()),
                new PlantedName(ReplacementNameKind.Video, aliensPath, TestMedia.File(inode: 3)),
            ]);
        var id = planted.Replacement.Id;

        //Act
        var response = await RestoreAsync(id);

        //Assert
        // The films first: a companion that can't come back then costs a subtitle, never a film.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            [TheatricalPath, aliensPath, SubtitlePath],
            _factory.Quarantine.Renames.Select(rename => rename.To));
        Assert.All(await NamesAsync(id, cancellationToken), name => Assert.Equal(ReplacementNameState.Restored, name.State));
    }

    // ── refused before anything is written ──────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task Restore_refused_by_a_check_is_409_with_why_and_writes_and_renames_nothing(string refusal)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await PlantQuarantinedAsync(ReplacementJobState.Quarantined);
        var id = planted.Replacement.Id;
        var expected = refusal switch
        {
            "switched off" => SwitchedOffError,
            "unticked folder" => NotTickedError,
            "changed in the quarantine" => string.Format(ChangedError, QuarantinePathOf(id, TheatricalPath)),
            "old path taken" or "old path a link leading nowhere" => TakenError,
            "folder gone" => FolderGoneError,
            _ => throw new ArgumentOutOfRangeException(nameof(refusal), refusal, null),
        };
        switch (refusal)
        {
            case "switched off":
                await _factory.WithDbAsync(db => db.ReplacementSettings.ExecuteUpdateAsync(
                    setters => setters.SetProperty(settings => settings.IsEnabled, false), cancellationToken));
                break;
            case "unticked folder":
                await _factory.WithDbAsync(db => db.ReplacementFolders.ExecuteDeleteAsync(cancellationToken));
                break;
            case "changed in the quarantine":
                _factory.Files.AddFile(QuarantinePathOf(id, TheatricalPath), TestMedia.File(inode: 1, sizeBytes: 5));
                break;
            case "old path taken":
                _factory.Files.AddFile(TheatricalPath, TestMedia.File(inode: 99));
                break;
            case "old path a link leading nowhere":
                // A look follows it and finds nothing, yet a rename back can't go where it is.
                _factory.Files.AddLinkedName(TheatricalPath, "/media/movies/Alien (1979)/gone.mkv");
                break;
            case "folder gone":
                _factory.Files.RemoveFolder(FolderPath);
                break;
        }

        //Act
        var response = await RestoreAsync(id);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(expected, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(planted, cancellationToken);
        Assert.Empty(_factory.Quarantine.CreatedFolders);
        Assert.Equal(refusal != "folder gone", _factory.Files.IsFolder(FolderPath));
    }

    [Fact]
    public async Task Restore_after_a_delete_for_good_stopped_part_way_is_409_and_renames_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Delete for good took the video, then stopped over the subtitle: putting the subtitle back alone would end the job
        // saying the old copy is back.
        var planted = await _planter.PlantAsync(
            ReplacementJobState.NeedsReview,
            [
                new PlantedName(ReplacementNameKind.Video, TheatricalPath, TestMedia.File(inode: 1), ReplacementNameState.Deleted),
                new PlantedName(ReplacementNameKind.Companion, SubtitlePath, Subtitle()),
            ],
            quarantineCheckedUtc: _factory.Time.GetUtcNow().UtcDateTime,
            reason: "“Alien (1979) Theatrical Cut.uk.srt” couldn't be deleted: The app may not write there. 1 of 2 names is deleted.");

        //Act
        var response = await RestoreAsync(planted.Replacement.Id);
        var details = (await (await _factory.GetHttpRequest($"{BaseUrl}/{planted.Replacement.Id}", _harness.Owner.AccessToken))
            .Content.ReadFromJsonAsync<ReplacementDetailsDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(VideoDeletedError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(planted, cancellationToken);
        // The page offers what can finish it instead: Retry, which settles it where it rests, then Delete for good.
        Assert.Equal((false, true, false), (details.CanRestore, details.CanRetry, details.CanDeleteForGood));
    }

    [Fact]
    public async Task Restore_after_the_media_folder_was_replaced_by_its_parent_still_puts_every_name_back()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await PlantQuarantinedAsync(ReplacementJobState.Quarantined);
        var id = planted.Replacement.Id;
        // The owner put / in /media's place — a Save that keeps the quarantine within reach. The write is still made inside
        // /media, the root whose quarantine holds the names: given /, no quarantine would be found at its top.
        await _factory.WithDbAsync(db => db.AllowedRoots.ExecuteUpdateAsync(setters => setters.SetProperty(root => root.Path, "/"), cancellationToken));

        //Act
        var response = await RestoreAsync(id);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ReplacementJobState.Restored, (await ReadJobAsync(response, cancellationToken)).Replacement!.State);
        Assert.Equal([(QuarantinePathOf(id, TheatricalPath), TheatricalPath), (QuarantinePathOf(id, SubtitlePath), SubtitlePath)], _factory.Quarantine.Renames);
        Assert.All(await NamesAsync(id, cancellationToken), name => Assert.Equal(ReplacementNameState.Restored, name.State));
    }

    [Fact]
    public async Task Restore_once_the_quarantines_media_folder_left_the_list_is_409_and_renames_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await PlantQuarantinedAsync(ReplacementJobState.Quarantined);
        // A list the Save would have refused, written straight to the database: no allowed root holds /media any more.
        await _factory.WithDbAsync(db => db.AllowedRoots.ExecuteUpdateAsync(setters => setters.SetProperty(root => root.Path, "/srv"), cancellationToken));

        //Act
        var response = await RestoreAsync(planted.Replacement.Id);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(NoLongerInMediaFoldersError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(planted, cancellationToken);
    }

    [Fact]
    public async Task Restore_on_a_platform_that_cant_rename_is_409_and_writes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await PlantQuarantinedAsync(ReplacementJobState.Quarantined);
        _factory.Quarantine.Capabilities = new FileQuarantineCapabilities(CanRename: false);

        //Act
        var response = await RestoreAsync(planted.Replacement.Id);

        //Assert
        // Refused before the claim: the job doesn't stop for review over the platform it runs on.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(NotSupportedError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(planted, cancellationToken);
    }

    [Theory]
    [InlineData(ReplacementJobState.Verified, ReplacementNameState.Planned)]
    [InlineData(ReplacementJobState.Quarantining, ReplacementNameState.Moving)]
    [InlineData(ReplacementJobState.NeedsReview, ReplacementNameState.Stayed)]
    [InlineData(ReplacementJobState.Restored, ReplacementNameState.Restored)]
    [InlineData(ReplacementJobState.Completed, ReplacementNameState.Deleted)]
    public async Task Restore_of_a_replacement_with_nothing_in_the_quarantine_to_put_back_is_409_and_writes_nothing(
        ReplacementJobState state, ReplacementNameState nameState)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await _planter.PlantAsync(state, [new PlantedName(ReplacementNameKind.Video, TheatricalPath, TestMedia.File(inode: 1), nameState)]);

        //Act
        var response = await RestoreAsync(planted.Replacement.Id);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(NothingToRestoreError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(planted, cancellationToken);
    }

    // ── stopped part-way ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Restore_where_a_name_is_taken_part_way_stops_for_review_with_how_many_came_back_and_is_409()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var planted = await PlantQuarantinedAsync(ReplacementJobState.Quarantined);
        var id = planted.Replacement.Id;
        // The library won't take the notice now, so it stays owed whatever the notice does with it (its own tests are
        // ReplacementLibraryNoticeTests').
        _harness.JellyfinServer.ScanRefusal = HttpStatusCode.ServiceUnavailable;
        // Something lands at the subtitle's old path once the video is back, after the checks.
        _factory.Quarantine.AfterRename = (_, to, _) =>
        {
            if (to == TheatricalPath)
                _factory.Files.AddFile(SubtitlePath, TestMedia.File(inode: 77, sizeBytes: 1));
            return Task.CompletedTask;
        };

        //Act
        var response = await RestoreAsync(id);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(SubtitleTakenReason, await response.ReadErrorAsync(cancellationToken));
        Assert.Equal([(QuarantinePathOf(id, TheatricalPath), TheatricalPath)], _factory.Quarantine.Renames);
        // The subtitle stays in the quarantine, and what landed at its old path is left alone.
        Assert.Equal((2L, TestMedia.DeviceId), await IdentityAtAsync(QuarantinePathOf(id, SubtitlePath)));
        Assert.Equal((77L, TestMedia.DeviceId), await IdentityAtAsync(SubtitlePath));
        var replacement = await JobAsync(id, cancellationToken);
        // Waiting for review as a stop of the mover's stage, so Retry and Restore are offered; the library owed a notice of
        // the video that came back.
        Assert.Equal(
            (ReplacementJobState.NeedsReview, (string?)SubtitleTakenReason, (DateTime?)now, (DateTime?)now),
            (replacement.State, replacement.Reason, replacement.QuarantineCheckedUtc, replacement.LibraryNoticeDueUtc));
        Assert.Equal(
            [(ReplacementNameState.Restored, (string?)null), (ReplacementNameState.Quarantined, SubtitleTakenProblem)],
            (await NamesAsync(id, cancellationToken)).Select(name => (name.State, name.Problem)));
        Assert.Equal(
            [
                JobRow(AuditAction.ReplacementRestoreStarted, id),
                NameRow(AuditAction.ReplacementNameRestoring, planted.Names[0], TheatricalName),
                NameRow(AuditAction.ReplacementNameRestored, planted.Names[0], TheatricalName),
                NameRow(AuditAction.ReplacementNameRestoring, planted.Names[1], SubtitleName),
                NameRow(AuditAction.ReplacementNameNotRestored, planted.Names[1], SubtitleName),
                JobRow(AuditAction.ReplacementNeedsReview, id),
            ],
            await _harness.QuarantineAuditsAsync(cancellationToken));
        var line = (await _harness.LineAsync(planted.Download.Id, cancellationToken))!;
        Assert.Equal((ReplacementJobState.NeedsReview, true), (line.State, line.CanRetry));
    }

    [Fact]
    public async Task Restore_with_replacement_switched_off_between_two_names_stops_for_review_and_renames_no_more()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await PlantQuarantinedAsync(ReplacementJobState.Quarantined);
        var id = planted.Replacement.Id;
        // The owner switches replacement off on the Media folders page while the video is being put back.
        _factory.Quarantine.AfterRename = (_, _, token) =>
            _factory.WithDbAsync(db => db.ReplacementSettings.ExecuteUpdateAsync(setters => setters.SetProperty(settings => settings.IsEnabled, false), token));

        //Act
        var response = await RestoreAsync(id);

        //Assert
        // ADR 0033 — the switch is read again before every rename: the subtitle isn't tried, and no intent is written for it.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(SwitchedOffMidwayReason, await response.ReadErrorAsync(cancellationToken));
        Assert.Equal([(QuarantinePathOf(id, TheatricalPath), TheatricalPath)], _factory.Quarantine.Renames);
        Assert.Equal(
            [(ReplacementNameState.Restored, (string?)null), (ReplacementNameState.Quarantined, null)],
            (await NamesAsync(id, cancellationToken)).Select(name => (name.State, name.Problem)));
        Assert.Equal(ReplacementJobState.NeedsReview, (await JobAsync(id, cancellationToken)).State);
        Assert.Equal(
            [
                JobRow(AuditAction.ReplacementRestoreStarted, id),
                NameRow(AuditAction.ReplacementNameRestoring, planted.Names[0], TheatricalName),
                NameRow(AuditAction.ReplacementNameRestored, planted.Names[0], TheatricalName),
                JobRow(AuditAction.ReplacementNeedsReview, id),
            ],
            await _harness.QuarantineAuditsAsync(cancellationToken));
    }

    [Fact]
    public async Task Restore_whose_folder_is_unticked_between_two_names_stops_for_review_and_renames_no_more()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await PlantQuarantinedAsync(ReplacementJobState.Quarantined);
        var id = planted.Replacement.Id;
        // The owner unticks Фільми on the Media folders page while the video is being put back.
        _factory.Quarantine.AfterRename = (_, _, token) => _factory.WithDbAsync(db => db.ReplacementFolders.ExecuteDeleteAsync(token));

        //Act
        var response = await RestoreAsync(id);

        //Assert
        // ADR 0033 — the tick is read again before every rename back, as the switch is.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(UntickedMidwayReason, await response.ReadErrorAsync(cancellationToken));
        Assert.Equal([(QuarantinePathOf(id, TheatricalPath), TheatricalPath)], _factory.Quarantine.Renames);
        Assert.Equal(
            [ReplacementNameState.Restored, ReplacementNameState.Quarantined],
            (await NamesAsync(id, cancellationToken)).Select(name => name.State));
        Assert.Equal(
            [
                JobRow(AuditAction.ReplacementRestoreStarted, id),
                NameRow(AuditAction.ReplacementNameRestoring, planted.Names[0], TheatricalName),
                NameRow(AuditAction.ReplacementNameRestored, planted.Names[0], TheatricalName),
                JobRow(AuditAction.ReplacementNeedsReview, id),
            ],
            await _harness.QuarantineAuditsAsync(cancellationToken));
    }

    [Fact]
    public async Task Restore_whose_rename_back_moves_another_file_put_at_the_name_records_it_lost_and_stops_for_review()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await PlantQuarantinedAsync(ReplacementJobState.Quarantined);
        var id = planted.Replacement.Id;
        _harness.JellyfinServer.ScanRefusal = HttpStatusCode.ServiceUnavailable;
        // Another file takes the subtitle's place in the quarantine after the last look, and the rename back moves it.
        _factory.Quarantine.Swap(QuarantinePathOf(id, SubtitlePath), TestMedia.File(inode: 55, sizeBytes: 7));

        //Act
        var response = await RestoreAsync(id);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(SwappedReason, await response.ReadErrorAsync(cancellationToken));
        // Lost, never Quarantined again: what the quarantine held under its name wasn't the old copy's file.
        Assert.Equal(
            [(ReplacementNameState.Restored, (string?)null), (ReplacementNameState.Lost, SwappedProblem)],
            (await NamesAsync(id, cancellationToken)).Select(name => (name.State, name.Problem)));
        Assert.Equal((55L, TestMedia.DeviceId), await IdentityAtAsync(SubtitlePath));
        Assert.Equal(ReplacementJobState.NeedsReview, (await JobAsync(id, cancellationToken)).State);
        Assert.Equal(
            [
                JobRow(AuditAction.ReplacementRestoreStarted, id),
                NameRow(AuditAction.ReplacementNameRestoring, planted.Names[0], TheatricalName),
                NameRow(AuditAction.ReplacementNameRestored, planted.Names[0], TheatricalName),
                NameRow(AuditAction.ReplacementNameRestoring, planted.Names[1], SubtitleName),
                NameRow(AuditAction.ReplacementNameNotRestored, planted.Names[1], SubtitleName),
                JobRow(AuditAction.ReplacementNeedsReview, id),
            ],
            await _harness.QuarantineAuditsAsync(cancellationToken));
    }

    // ── cut ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Restore_cut_after_a_rename_is_settled_by_the_next_pass_with_the_name_found_back()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await PlantQuarantinedAsync(ReplacementJobState.Quarantined);
        var id = planted.Replacement.Id;
        _harness.JellyfinServer.ScanRefusal = HttpStatusCode.ServiceUnavailable;
        // The request breaks right after the video is back, before its outcome is written: the intent was committed before
        // the rename, in a transaction of its own.
        _factory.Quarantine.AfterRename = (_, to, _) =>
            to == TheatricalPath ? throw new InvalidOperationException("planted-exception-message-0026") : Task.CompletedTask;
        var response = await RestoreAsync(id);
        var cutJob = await JobAsync(id, cancellationToken);
        var cutNames = await NamesAsync(id, cancellationToken);
        var cutAudits = await _harness.QuarantineAuditsAsync(cancellationToken);
        var held = _factory.Resolve<ReplacementJobsInHand>().Holds(id);
        _factory.Quarantine.AfterRename = null;
        _factory.Time.Advance(ReplacementQuarantineTracker.PassInterval);

        //Act
        await _factory.QuarantineReplacementsAsync(cancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(ReplacementJobState.Restoring, cutJob.State);
        Assert.Equal([ReplacementNameState.Restoring, ReplacementNameState.Quarantined], cutNames.Select(name => name.State));
        Assert.Equal(
            [JobRow(AuditAction.ReplacementRestoreStarted, id), NameRow(AuditAction.ReplacementNameRestoring, planted.Names[0], TheatricalName)],
            cutAudits);
        // The request let the job go however it ended, so the next pass could take it up.
        Assert.False(held);
        var stopped = await JobAsync(id, cancellationToken);
        Assert.Equal((ReplacementJobState.NeedsReview, (string?)CutRestoreReason), (stopped.State, stopped.Reason));
        Assert.Equal(
            [ReplacementNameState.Restored, ReplacementNameState.Quarantined],
            (await NamesAsync(id, cancellationToken)).Select(name => name.State));
        Assert.Equal(
            [
                .. cutAudits,
                AppRow(AuditAction.ReplacementNameRestored, AuditTargetKind.ReplacementJobName, planted.Names[0].Id, TheatricalName),
                AppRow(AuditAction.ReplacementNeedsReview, AuditTargetKind.ReplacementJob, id, ReplacementQuarantinePlanter.ReleaseTitle),
            ],
            await _harness.QuarantineAuditsAsync(cancellationToken));
        Assert.Single(_factory.Quarantine.Renames);
        var details = (await (await _factory.GetHttpRequest($"{BaseUrl}/{id}", _harness.Owner.AccessToken))
            .Content.ReadFromJsonAsync<ReplacementDetailsDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.True(details.CanRestore);
    }

    // ── one at a time ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Restore_of_a_replacement_someone_is_working_on_is_409_and_writes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await PlantQuarantinedAsync(ReplacementJobState.Quarantined);
        var inHand = _factory.Resolve<ReplacementJobsInHand>();
        // The mover — or another request — holds the job in this process.
        var taken = inHand.TryTake(planted.Replacement.Id);

        //Act
        HttpResponseMessage response;
        try
        {
            response = await RestoreAsync(planted.Replacement.Id);
        }
        finally
        {
            inHand.Release(planted.Replacement.Id);
        }

        //Assert
        Assert.True(taken);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(BeingWorkedOnError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(planted, cancellationToken);
    }

    [Fact]
    public async Task Restore_clicked_twice_at_once_renames_each_name_once_and_answers_one_200_and_one_409()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await PlantQuarantinedAsync(ReplacementJobState.Quarantined);
        var id = planted.Replacement.Id;
        HttpResponseMessage? second = null;
        // The second click arrives once the first passed its checks and is about to claim the job.
        _factory.TransactionHook.BeforeNextTransaction(_ => true, async _ => second = await RestoreAsync(id));

        //Act
        var first = await RestoreAsync(id);

        //Assert
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second?.StatusCode);
        Assert.Equal(BeingWorkedOnError, await second!.ReadErrorAsync(cancellationToken));
        Assert.Equal([(QuarantinePathOf(id, TheatricalPath), TheatricalPath), (QuarantinePathOf(id, SubtitlePath), SubtitlePath)], _factory.Quarantine.Renames);
        Assert.Single(await _harness.QuarantineAuditsAsync(cancellationToken), audit => audit.Action == AuditAction.ReplacementRestoreStarted);
        Assert.False(_factory.Resolve<ReplacementJobsInHand>().Holds(id));
    }

    // ── whose ───────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("an unknown replacement")]
    [InlineData("another user's replacement")]
    public async Task Restore_of_an_unknown_or_another_users_replacement_is_a_bare_404_and_writes_nothing(string which)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var member = await new UserSeeder(_factory.Scope).CreateAuthorized("member");
        // ADR 0014 — a job is its sender's; this one is the member's, planted as a hand-edited row would leave it.
        var planted = await PlantQuarantinedAsync(ReplacementJobState.Quarantined, requestedByUserId: member.User.Id);
        var id = which == "an unknown replacement" ? Guid.NewGuid() : planted.Replacement.Id;

        //Act
        var response = await RestoreAsync(id);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
        await AssertNothingWrittenAsync(planted, cancellationToken);
    }

    [Fact]
    public async Task Restore_and_delete_for_good_of_the_owners_quarantined_job_as_a_member_are_403_and_move_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var member = await new UserSeeder(_factory.Scope).CreateAuthorized("member");
        // The owner's real quarantined job, not a made-up id: the gate must come before the lookup.
        var planted = await PlantQuarantinedAsync(ReplacementJobState.Quarantined);
        var id = planted.Replacement.Id;

        //Act
        var restore = await _factory.PostHttpRequest($"{BaseUrl}/{id}/restore", member.AccessToken);
        var delete = await _factory.PostHttpRequest(
            $"{BaseUrl}/{id}/delete-for-good", member.AccessToken, new DeleteQuarantinedRequest([.. planted.Names.Select(name => name.Id)]));

        //Assert
        // ADR 0033 — Restore and Delete for good are the owner's alone.
        Assert.Equal(
            [(HttpStatusCode.Forbidden, OwnerOnlyBody), (HttpStatusCode.Forbidden, OwnerOnlyBody)],
            [(restore.StatusCode, await restore.Content.ReadAsStringAsync(cancellationToken)), (delete.StatusCode, await delete.Content.ReadAsStringAsync(cancellationToken))]);
        await AssertNothingWrittenAsync(planted, cancellationToken);
        Assert.Empty(_factory.Quarantine.Deletions);
    }

    [Fact]
    public async Task Restore_and_delete_for_good_of_a_members_own_quarantined_job_are_403_and_move_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var member = await new UserSeeder(_factory.Scope).CreateAuthorized("member");
        // ADR 0014 — a job is its sender's, and a member can't send a replacement: this one is planted as a hand-edited row
        // would leave it. The actions find it for him — the owner's job they don't —, so Restore and Delete for good would
        // each move his names past a gate that came too late.
        var planted = await PlantQuarantinedAsync(ReplacementJobState.Quarantined, member.User.Id);
        var id = planted.Replacement.Id;

        //Act
        var restore = await _factory.PostHttpRequest($"{BaseUrl}/{id}/restore", member.AccessToken);
        var delete = await _factory.PostHttpRequest(
            $"{BaseUrl}/{id}/delete-for-good", member.AccessToken, new DeleteQuarantinedRequest([.. planted.Names.Select(name => name.Id)]));

        //Assert
        // ADR 0033 — Restore and Delete for good are the owner's alone, whoever sent the job.
        Assert.Equal(
            [(HttpStatusCode.Forbidden, OwnerOnlyBody), (HttpStatusCode.Forbidden, OwnerOnlyBody)],
            [(restore.StatusCode, await restore.Content.ReadAsStringAsync(cancellationToken)), (delete.StatusCode, await delete.Content.ReadAsStringAsync(cancellationToken))]);
        await AssertNothingWrittenAsync(planted, cancellationToken);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static FileFacts Subtitle() => TestMedia.File(inode: 2, sizeBytes: SubtitleSize);

    private static string QuarantinePathOf(Guid replacementJobId, string appPath) =>
        ReplacementVerificationHarness.QuarantinePathOf(replacementJobId, appPath);

    /// <summary>A replacement in <paramref name="state"/> holding Alien's theatrical cut and its subtitle in the
    /// quarantine.</summary>
    private Task<PlantedReplacement> PlantQuarantinedAsync(ReplacementJobState state, Guid? requestedByUserId = null) =>
        _planter.PlantAsync(
            state,
            [
                new PlantedName(ReplacementNameKind.Video, TheatricalPath, TestMedia.File(inode: 1)),
                new PlantedName(ReplacementNameKind.Companion, SubtitlePath, Subtitle()),
            ],
            requestedByUserId: requestedByUserId);

    private Task<HttpResponseMessage> RestoreAsync(Guid replacementJobId) =>
        _factory.PostHttpRequest($"{BaseUrl}/{replacementJobId}/restore", _harness.Owner.AccessToken);

    private static async Task<DownloadJobDto> ReadJobAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;

    private Task<FileInventoryResult<FileFacts>> DescribeAsync(string appPath) =>
        _factory.Files.DescribeAsync(appPath, [ReplacementVerificationHarness.MediaRoot]);

    /// <summary>The inode and device of the file the disk describes at <paramref name="appPath"/>; throws when none.</summary>
    private async Task<(long Inode, long DeviceId)> IdentityAtAsync(string appPath)
    {
        var facts = (await DescribeAsync(appPath)).Value ?? throw new InvalidOperationException($"Nothing is at “{appPath}”.");
        return (facts.Inode ?? -1, facts.DeviceId ?? -1);
    }

    private async Task<ReplacementJobEntity> JobAsync(Guid replacementJobId, CancellationToken cancellationToken)
    {
        ReplacementJobEntity job = null!;
        await _factory.WithDbAsync(async db => job = await db.ReplacementJobs.AsNoTracking().SingleAsync(row => row.Id == replacementJobId, cancellationToken));
        return job;
    }

    private async Task<IReadOnlyList<ReplacementJobNameEntity>> NamesAsync(Guid replacementJobId, CancellationToken cancellationToken)
    {
        List<ReplacementJobNameEntity> names = [];
        await _factory.WithDbAsync(async db => names = await db.ReplacementJobNames
            .AsNoTracking()
            .Where(name => name.ReplacementJobId == replacementJobId)
            .OrderBy(name => name.Position)
            .ToListAsync(cancellationToken));
        return names;
    }

    private (AuditAction, Guid?, AuditTargetKind?, Guid?, string?) JobRow(AuditAction action, Guid replacementJobId) =>
        (action, _harness.Owner.User.Id, AuditTargetKind.ReplacementJob, replacementJobId, ReplacementQuarantinePlanter.ReleaseTitle);

    private (AuditAction, Guid?, AuditTargetKind?, Guid?, string?) NameRow(AuditAction action, ReplacementJobNameEntity name, string fileName) =>
        (action, _harness.Owner.User.Id, AuditTargetKind.ReplacementJobName, name.Id, fileName);

    /// <summary>An audit row of the app's own, no actor: what the mover's recovery writes.</summary>
    private static (AuditAction, Guid?, AuditTargetKind?, Guid?, string?) AppRow(AuditAction action, AuditTargetKind kind, Guid targetId, string targetName) =>
        (action, null, kind, targetId, targetName);

    /// <summary>
    /// That the replacement, its names and its files are as <paramref name="planted"/> left them: the same state and
    /// reason, no notice owed, every name where it was, no audit row of the quarantine, and nothing renamed or deleted.
    /// </summary>
    private async Task AssertNothingWrittenAsync(PlantedReplacement planted, CancellationToken cancellationToken)
    {
        var replacement = await JobAsync(planted.Replacement.Id, cancellationToken);
        Assert.Equal(
            (planted.Replacement.State, planted.Replacement.Reason, planted.Replacement.StateChangedUtc, (DateTime?)null),
            (replacement.State, replacement.Reason, replacement.StateChangedUtc, replacement.LibraryNoticeDueUtc));
        Assert.Equal(
            planted.Names.Select(name => (name.State, name.Problem)),
            (await NamesAsync(planted.Replacement.Id, cancellationToken)).Select(name => (name.State, name.Problem)));
        Assert.Empty(await _harness.QuarantineAuditsAsync(cancellationToken));
        Assert.Empty(_factory.Quarantine.Renames);
        Assert.Empty(_factory.Quarantine.Deletions);
    }
}
