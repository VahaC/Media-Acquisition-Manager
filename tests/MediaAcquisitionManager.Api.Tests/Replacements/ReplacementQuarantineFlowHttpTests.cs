using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Replacements;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Replacements;
using MediaAcquisitionManager.TestInfrastructure.Texts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Replacements;

// MAM-26, ADR 0030, ADR 0031, ADR 0033 — an old copy's whole way after its new file was verified, across the mover's pass,
// the library's notice and the owner's routes, with nothing in between planted by hand: the pass moves the ticked video and
// its own subtitles into the quarantine, the Jobs line says so, and Jellyfin hears in one request, in its own terms, that
// both went ("Deleted"); Restore puts both back with their device and inode and Jellyfin hears they came back ("Created");
// the job's details list what the quarantine holds, and Delete for good of exactly those ids completes the job, the trail
// reading the app's own rows (no actor) then the owner's; a copy a torrent holds stops for review until the owner removes
// the torrent and presses Retry, and the next pass moves it. MAM-28 — with qBittorrent's mapping deleted on Integrations, it
// stops naming the mappings instead, and once the owner puts the mapping back, Retry names the torrent. Each step's own
// cases are the other ReplacementQuarantine*, ReplacementRestore, ReplacementDeleteForGood, ReplacementRetry,
// ReplacementDetails and ReplacementLibraryNotice tests'.
public sealed class ReplacementQuarantineFlowHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/replacements";
    private const string MediaFoldersUrl = "/api/settings/allowed-roots";
    private const string AlienImdbId = "tt0078748";
    private const string AlienRelease = "Alien (1979) Director's Cut BDRemux 2160p HDR";
    private const string AlienFolder = "/media/movies/Alien (1979)";
    private const string TheatricalPath = "/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.mkv";
    private const string TheatricalSrtPath = "/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.srt";
    private const string TheatricalName = "Alien (1979) Theatrical Cut.mkv";
    private const string TheatricalSrtName = "Alien (1979) Theatrical Cut.srt";
    private const long TheatricalSize = 3_891_719_630;
    private const long SrtSize = 51_234;
    private const string TheatricalInTorrent = "Alien (1979)/Alien (1979) Theatrical Cut.mkv";
    private const string NewName = "Alien.1979.Directors.Cut.2160p.mkv";
    private const string NewPath = "/media/movies/Alien.1979.Directors.Cut.2160p.mkv";
    private const int NewSize = (3 * TestTorrent.PieceLength) + 5;
    private const string LookRequest = "POST /Library/Media/Updated";

    // The same old files as Jellyfin names them: its /data is the app's /media. Spelled out, never worked out.
    private const string JellyfinTheatricalPath = "/data/movies/Alien (1979)/Alien (1979) Theatrical Cut.mkv";
    private const string JellyfinTheatricalSrtPath = "/data/movies/Alien (1979)/Alien (1979) Theatrical Cut.srt";
    private const string JellyfinNewPath = "/data/movies/Alien.1979.Directors.Cut.2160p.mkv";

    // Sentences the Jobs page shows, spelled out rather than read from product code.
    private const string HeldReason =
        "Still held by the torrent “Alien.1979.Theatrical” in “qBittorrent”: remove that torrent there without its files, then press Retry.";
    private const string OutsideReason =
        "“qBittorrent”'s path mappings put 1 of its torrents outside the Media folders, so the app can't tell whether that torrent holds the old copy: "
        + "check those mappings under Integrations, then press Retry.";
    private const string IntegrationsUrl = "/api/settings/integrations";
    private const string TorrentsListing = "GET /api/v2/torrents/info?includeFiles=true";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private ReplacementVerificationHarness _harness = null!;

    public ReplacementQuarantineFlowHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _harness = await ReplacementVerificationHarness.CreateAsync(_factory);
        // Alien's theatrical cut, inspected, in the ticked Фільми, with its subtitles beside it.
        await _harness.PlantFilmAsync("Alien", 1979, TheatricalPath, inode: 1, imdbId: AlienImdbId, editionLabel: "Theatrical Cut");
        _factory.Files.AddFile(TheatricalSrtPath, TestMedia.File(inode: 3, sizeBytes: SrtSize));
        await _harness.InspectAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── moved, then put back ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Flow_moves_the_old_copy_tells_jellyfin_it_went_then_restore_puts_it_back_with_its_identity_and_tells_jellyfin_it_came_back()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (job, replacement) = await VerifiedFilmAsync(cancellationToken);
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        await QuarantineAsync(cancellationToken);
        var movedLine = await _harness.LineAsync(job.Id, cancellationToken);
        var moved = await _harness.ReplacementAsync(cancellationToken);
        var quarantinedVideo = await WhatIsAtAsync(QuarantinePathOf(replacement, TheatricalPath), cancellationToken);
        var quarantinedSrt = await WhatIsAtAsync(QuarantinePathOf(replacement, TheatricalSrtPath), cancellationToken);
        var writesAfterMove = _harness.Writes();
        using var mediaFolders = await _factory.GetHttpRequest(MediaFoldersUrl, _harness.Owner.AccessToken);
        using var mediaFoldersJson = JsonDocument.Parse(await mediaFolders.Content.ReadAsStringAsync(cancellationToken));
        using var response = await _factory.PostHttpRequest($"{BaseUrl}/{replacement.Id}/restore", _harness.Owner.AccessToken);

        //Assert
        // The pass: both names in the quarantine with the identity they had, the job at rest owing nothing, and the Jobs
        // line counting the copy moved — MAM-41 — and offering Restore and Delete for good.
        Assert.Equal(
            new DownloadJobReplacementDto(ReplacementJobState.Quarantined, 1, null, false, 0) { Id = replacement.Id, QuarantinedCopyCount = 1, CanRestore = true, CanDeleteForGood = true },
            movedLine);
        Assert.Equal(
            (ReplacementJobState.Quarantined, (DateTime?)now, (DateTime?)null),
            (moved.State, moved.QuarantinedUtc, moved.LibraryNoticeDueUtc));
        Assert.Equal((FileInventoryStatus.Found, TestMedia.DeviceId, 1L), quarantinedVideo);
        Assert.Equal((FileInventoryStatus.Found, TestMedia.DeviceId, 3L), quarantinedSrt);
        // ADR 0030 — Jellyfin heard once, in its own terms, that the video and its subtitles went; nothing else was written.
        Assert.Equal([LookRequest], writesAfterMove);
        // The Media folders page shows the room the quarantine takes, read from the job's names, under exactly the names the
        // page reads (frontend AllowedRoots, QuarantineSpace and ReplacementFolder).
        Assert.Equal(HttpStatusCode.OK, mediaFolders.StatusCode);
        var quarantine = Assert.Single(mediaFoldersJson.RootElement.GetProperty("quarantines").EnumerateArray());
        Assert.Equal(
            (ReplacementVerificationHarness.QuarantineRoot, 1, 2, TheatricalSize + SrtSize),
            (quarantine.GetProperty("path").GetString(), quarantine.GetProperty("replacementCount").GetInt32(), quarantine.GetProperty("fileCount").GetInt32(),
                quarantine.GetProperty("sizeBytes").GetInt64()));
        Assert.Equal(["path", "replacementCount", "removalCount", "fileCount", "sizeBytes"], quarantine.EnumerateObject().Select(property => property.Name));
        Assert.All(
            mediaFoldersJson.RootElement.GetProperty("libraryFolders").EnumerateArray(),
            folder => Assert.Equal(["appPath", "libraryNames", "ticked", "status", "renameProblems", "writeAccess"], folder.EnumerateObject().Select(property => property.Name)));

        // Restore: 200 with the job as it now stands.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var restoredJob = (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal(
            new DownloadJobReplacementDto(ReplacementJobState.Restored, 1, null, false, 0) { Id = replacement.Id },
            restoredJob.Replacement);
        var restored = await _harness.ReplacementAsync(cancellationToken);
        Assert.Equal(
            (ReplacementJobState.Restored, (string?)null, (DateTime?)null),
            (restored.State, TestTexts.En(restored.Reason), restored.LibraryNoticeDueUtc));
        Assert.Equal(
            [(TheatricalPath, ReplacementNameState.Restored, (DateTime?)now), (TheatricalSrtPath, ReplacementNameState.Restored, now)],
            (await NamesAsync(cancellationToken)).Select(name => (name.AppPath, name.State, name.RestoredUtc)));

        // Each name renamed into the quarantine and back again, never copied: back at its old path with the device and inode
        // it had, nothing left in the quarantine, the folder never made again.
        Assert.Equal(
            [
                (TheatricalPath, QuarantinePathOf(replacement, TheatricalPath)),
                (TheatricalSrtPath, QuarantinePathOf(replacement, TheatricalSrtPath)),
                (QuarantinePathOf(replacement, TheatricalPath), TheatricalPath),
                (QuarantinePathOf(replacement, TheatricalSrtPath), TheatricalSrtPath),
            ],
            _factory.Quarantine.Renames);
        Assert.Equal((FileInventoryStatus.Found, TestMedia.DeviceId, 1L), await WhatIsAtAsync(TheatricalPath, cancellationToken));
        Assert.Equal((FileInventoryStatus.Found, TestMedia.DeviceId, 3L), await WhatIsAtAsync(TheatricalSrtPath, cancellationToken));
        Assert.Equal(FileInventoryStatus.NotFound, (await WhatIsAtAsync(QuarantinePathOf(replacement, TheatricalPath), cancellationToken)).Status);
        Assert.Equal(FileInventoryStatus.NotFound, (await WhatIsAtAsync(QuarantinePathOf(replacement, TheatricalSrtPath), cancellationToken)).Status);
        Assert.Empty(await _harness.ChangedOldCopiesAsync(cancellationToken));
        Assert.Empty(_factory.Quarantine.Deletions);
        Assert.DoesNotContain(_factory.Quarantine.CreatedFolders, folder => folder == AlienFolder);

        // Jellyfin, after the verifier's look at the new file, told twice more — the names went, then they came back —, at
        // their old places in its own terms, and never a quarantine path.
        Assert.Equal([LookRequest, LookRequest], _harness.Writes());
        Assert.Equal(
            [
                [JellyfinNewPath],
                [JellyfinTheatricalPath, JellyfinTheatricalSrtPath],
                [JellyfinTheatricalPath, JellyfinTheatricalSrtPath],
            ],
            _harness.JellyfinServer.ScanRequests);
        Assert.Equal(
            [["Created"], ["Deleted", "Deleted"], ["Created", "Created"]],
            _harness.JellyfinServer.ScanUpdateTypes);

        // The trail, save by save: the mover's rows have no actor, Restore's carry the owner.
        var names = await NamesAsync(cancellationToken);
        Assert.Equal(
            [
                JobAudited(AuditAction.ReplacementQuarantineStarted, replacement.Id, null),
                NameAudited(AuditAction.ReplacementNameMoving, names[0], null),
                NameAudited(AuditAction.ReplacementNameMoved, names[0], null),
                NameAudited(AuditAction.ReplacementNameMoving, names[1], null),
                NameAudited(AuditAction.ReplacementNameMoved, names[1], null),
                JobAudited(AuditAction.ReplacementQuarantined, replacement.Id, null),
                JobAudited(AuditAction.ReplacementRestoreStarted, replacement.Id, _harness.Owner.User.Id),
                NameAudited(AuditAction.ReplacementNameRestoring, names[0], _harness.Owner.User.Id),
                NameAudited(AuditAction.ReplacementNameRestored, names[0], _harness.Owner.User.Id),
                NameAudited(AuditAction.ReplacementNameRestoring, names[1], _harness.Owner.User.Id),
                NameAudited(AuditAction.ReplacementNameRestored, names[1], _harness.Owner.User.Id),
                JobAudited(AuditAction.ReplacementRestored, replacement.Id, _harness.Owner.User.Id),
            ],
            await _harness.QuarantineAuditsAsync(cancellationToken));
    }

    // Toloka's whole title comes in the listing's description, where "Strip Cyrillic Letters" leaves the title its English
    // part alone (ADR 0004): the job is named by it on every row of its trail, as the Jobs page names it.
    [Fact]
    public async Task Flow_names_the_job_on_every_row_of_its_trail_by_the_listings_description_when_the_listing_gave_one()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        const string description = "Чужий / Alien (1979) Director's Cut BDRemux 2160p HDR";
        var (job, replacement) = await VerifiedFilmAsync(cancellationToken, description);
        await QuarantineAsync(cancellationToken);

        //Act
        using var response = await _factory.PostHttpRequest($"{BaseUrl}/{replacement.Id}/restore", _harness.Owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(description, job.ReleaseTitle);
        var jobRows = (await _harness.ReplacementAuditsAsync(cancellationToken))
            .Where(audit => audit.TargetKind == AuditTargetKind.ReplacementJob)
            .Select(audit => (audit.Action, audit.TargetName))
            .ToList();
        // The schedule's, the verifier's, the mover's and Restore's.
        Assert.Superset(
            new HashSet<AuditAction>
            {
                AuditAction.ReplacementScheduled, AuditAction.ReplacementCheckStarted, AuditAction.ReplacementQuarantineStarted,
                AuditAction.ReplacementQuarantined, AuditAction.ReplacementRestoreStarted, AuditAction.ReplacementRestored,
            },
            jobRows.Select(row => row.Action).ToHashSet());
        Assert.All(jobRows, row => Assert.Equal(description, row.TargetName));
        await _factory.WithDbAsync(async db => Assert.Equal(
            description,
            (await db.AuditEvents.AsNoTracking().SingleAsync(audit => audit.Action == AuditAction.DownloadRequested, cancellationToken)).TargetName));
    }

    // ── moved, then deleted for good ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Flow_deletes_for_good_exactly_the_names_the_details_list_completing_the_job_with_the_movers_rows_then_the_owners()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (job, replacement) = await VerifiedFilmAsync(cancellationToken);
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        await QuarantineAsync(cancellationToken);
        var writesAfterMove = _harness.Writes();
        using var detailsResponse = await _factory.GetHttpRequest($"{BaseUrl}/{replacement.Id}", _harness.Owner.AccessToken);
        var detailsJson = await detailsResponse.Content.ReadAsStringAsync(cancellationToken);
        var details = JsonSerializer.Deserialize<ReplacementDetailsDto>(detailsJson, MediaAcquisitionManagerApiFactory.JsonOptions)!;
        using var response = await _factory.PostHttpRequest(
            $"{BaseUrl}/{replacement.Id}/delete-for-good", _harness.Owner.AccessToken, new DeleteQuarantinedRequest([.. details.Names.Select(name => name.Id)]));

        //Assert
        // The details the owner confirms from: both names in the quarantine, the room they take and free, both actions
        // offered.
        Assert.Equal(HttpStatusCode.OK, detailsResponse.StatusCode);
        var names = await NamesAsync(cancellationToken);
        Assert.Equal(
            (replacement.Id, job.Id, ReplacementJobState.Quarantined, (string?)null, false, false, true, true, TheatricalSize + SrtSize, TheatricalSize + SrtSize),
            (details.Id, details.DownloadJobId, details.State, details.Reason, details.SwitchedOff, details.CanRetry, details.CanRestore,
                details.CanDeleteForGood, details.QuarantinedBytes, details.FreesBytes));
        Assert.Equal(
            [
                new ReplacementNameDto(
                    names[0].Id, ReplacementNameKind.Video, TheatricalPath, QuarantinePathOf(replacement, TheatricalPath), TheatricalSize, false,
                    ReplacementNameState.Quarantined, null),
                new ReplacementNameDto(
                    names[1].Id, ReplacementNameKind.Companion, TheatricalSrtPath, QuarantinePathOf(replacement, TheatricalSrtPath), SrtSize, false,
                    ReplacementNameState.Quarantined, null),
            ],
            details.Names);
        // Exactly the names the job's page reads (frontend ReplacementDetails and ReplacementName), every enum by name.
        using var document = JsonDocument.Parse(detailsJson);
        var video = document.RootElement.GetProperty("names")[0];
        Assert.Equal(
            [
                "id", "downloadJobId", "state", "reason", "switchedOff", "canRetry", "canRestore", "canDeleteForGood", "quarantinedBytes", "freesBytes",
                "names", "canCancel", "stop", "checkStage", "checkStartedUtc", "libraryAskedUtc", "checkWaitsUntilUtc", "dropOnlyCopyCount", "copies",
                "newFiles", "works",
            ],
            document.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal(
            ["id", "kind", "originalPath", "quarantinePath", "sizeBytes", "otherNames", "state", "problem"],
            video.EnumerateObject().Select(property => property.Name));
        Assert.Equal(
            ("Quarantined", "Video", "Quarantined"),
            (document.RootElement.GetProperty("state").GetString(), video.GetProperty("kind").GetString(), video.GetProperty("state").GetString()));

        // Delete for good: 200, Completed; both names gone from the quarantine and not put back anywhere.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var completedJob = (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal(
            new DownloadJobReplacementDto(ReplacementJobState.Completed, 1, null, false, 0) { Id = replacement.Id },
            completedJob.Replacement);
        var completed = await _harness.ReplacementAsync(cancellationToken);
        Assert.Equal(
            (ReplacementJobState.Completed, (string?)null, (DateTime?)null),
            (completed.State, TestTexts.En(completed.Reason), completed.LibraryNoticeDueUtc));
        Assert.Equal(
            [(ReplacementNameState.Deleted, (DateTime?)now), (ReplacementNameState.Deleted, now)],
            (await NamesAsync(cancellationToken)).Select(name => (name.State, name.DeletedUtc)));
        Assert.Equal([QuarantinePathOf(replacement, TheatricalPath), QuarantinePathOf(replacement, TheatricalSrtPath)], _factory.Quarantine.Deletions);
        foreach (var path in new[] { TheatricalPath, TheatricalSrtPath, QuarantinePathOf(replacement, TheatricalPath), QuarantinePathOf(replacement, TheatricalSrtPath) })
            Assert.Equal(FileInventoryStatus.NotFound, (await WhatIsAtAsync(path, cancellationToken)).Status);
        Assert.True(_factory.Files.IsFolder(AlienFolder));

        // ADR 0030 — the library heard at the move and hears nothing of the deletion.
        Assert.Equal([LookRequest], writesAfterMove);
        Assert.Equal([LookRequest], _harness.Writes());

        // The trail, save by save: the mover's rows have no actor, Delete for good's carry the owner.
        Assert.Equal(
            [
                JobAudited(AuditAction.ReplacementQuarantineStarted, replacement.Id, null),
                NameAudited(AuditAction.ReplacementNameMoving, names[0], null),
                NameAudited(AuditAction.ReplacementNameMoved, names[0], null),
                NameAudited(AuditAction.ReplacementNameMoving, names[1], null),
                NameAudited(AuditAction.ReplacementNameMoved, names[1], null),
                JobAudited(AuditAction.ReplacementQuarantined, replacement.Id, null),
                JobAudited(AuditAction.ReplacementDeleteStarted, replacement.Id, _harness.Owner.User.Id),
                NameAudited(AuditAction.ReplacementNameDeleting, names[0], _harness.Owner.User.Id),
                NameAudited(AuditAction.ReplacementNameDeleted, names[0], _harness.Owner.User.Id),
                NameAudited(AuditAction.ReplacementNameDeleting, names[1], _harness.Owner.User.Id),
                NameAudited(AuditAction.ReplacementNameDeleted, names[1], _harness.Owner.User.Id),
                JobAudited(AuditAction.ReplacementCompleted, replacement.Id, _harness.Owner.User.Id),
            ],
            await _harness.QuarantineAuditsAsync(cancellationToken));
    }

    // ── held, then retried ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Flow_of_a_copy_a_torrent_holds_stops_for_review_and_once_the_torrent_is_removed_retry_lets_the_next_pass_move_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (job, replacement) = await VerifiedFilmAsync(cancellationToken);
        var torrent = TorrentHolding("Alien.1979.Theatrical", TheatricalInTorrent, TheatricalSize);
        _harness.QBittorrent.AddTorrent(torrent);
        var addRequests = _harness.QBittorrent.AddRequests.Count;

        //Act
        await QuarantineAsync(cancellationToken);
        var heldLine = await _harness.LineAsync(job.Id, cancellationToken);
        var renamesWhileHeld = _factory.Quarantine.Renames.Count;
        // The owner removes the torrent in qBittorrent without its files, then presses Retry on the job.
        _harness.QBittorrent.RemoveTorrent(torrent.Hash);
        using var response = await _factory.PostHttpRequest($"{BaseUrl}/{replacement.Id}/retry", _harness.Owner.AccessToken);
        var retried = await _harness.ReplacementAsync(cancellationToken);
        _factory.Time.Advance(ReplacementQuarantineTracker.PassInterval);
        await QuarantineAsync(cancellationToken);

        //Assert
        // Held: stopped for review by the mover naming the torrent, Retry and (MAM-27) Keep both offered, nothing renamed.
        Assert.Equal(
            new DownloadJobReplacementDto(ReplacementJobState.NeedsReview, 1, HeldReason, false, 0)
            {
                Id = replacement.Id,
                CanRetry = true,
                CanCancel = true,
                Stop = ReplacementStop.Move,
            },
            heldLine);
        Assert.Equal(0, renamesWhileHeld);

        // Retry: 200 with the job verified again, the reason gone, and nothing written to the media.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var retriedJob = (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal(
            new DownloadJobReplacementDto(ReplacementJobState.Verified, 1, null, false, 0) { Id = replacement.Id, CanCancel = true },
            retriedJob.Replacement);
        Assert.Equal((ReplacementJobState.Verified, (string?)null), (retried.State, TestTexts.En(retried.Reason)));

        // The next pass: both names moved, the job at rest in the quarantine.
        var line = await _harness.LineAsync(job.Id, cancellationToken);
        Assert.Equal(
            new DownloadJobReplacementDto(ReplacementJobState.Quarantined, 1, null, false, 0) { Id = replacement.Id, QuarantinedCopyCount = 1, CanRestore = true, CanDeleteForGood = true },
            line);
        Assert.Equal(
            [
                (TheatricalPath, QuarantinePathOf(replacement, TheatricalPath)),
                (TheatricalSrtPath, QuarantinePathOf(replacement, TheatricalSrtPath)),
            ],
            _factory.Quarantine.Renames);
        Assert.Null(Assert.Single(await _harness.CopiesAsync(cancellationToken)).Problem);

        // ADR 0031 — nothing was ever written to qBittorrent: the owner removed the torrent there, not the app.
        Assert.Equal(addRequests, _harness.QBittorrent.AddRequests.Count);
        Assert.Equal([LookRequest], _harness.Writes());

        // The trail: the mover's stop, the owner's Retry, then the mover's move.
        var names = await NamesAsync(cancellationToken);
        Assert.Equal(
            [
                JobAudited(AuditAction.ReplacementNeedsReview, replacement.Id, null),
                JobAudited(AuditAction.ReplacementRetried, replacement.Id, _harness.Owner.User.Id),
                JobAudited(AuditAction.ReplacementQuarantineStarted, replacement.Id, null),
                NameAudited(AuditAction.ReplacementNameMoving, names[0], null),
                NameAudited(AuditAction.ReplacementNameMoved, names[0], null),
                NameAudited(AuditAction.ReplacementNameMoving, names[1], null),
                NameAudited(AuditAction.ReplacementNameMoved, names[1], null),
                JobAudited(AuditAction.ReplacementQuarantined, replacement.Id, null),
            ],
            await _harness.QuarantineAuditsAsync(cancellationToken));
    }

    // ── a path mapping deleted, then put back (MAM-28) ──────────────────────────────────────────

    [Fact]
    public async Task Flow_after_qbittorrents_path_mapping_was_deleted_stops_until_the_owner_puts_it_back_then_names_the_torrent_and_moves_once_it_is_removed()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (job, replacement) = await VerifiedFilmAsync(cancellationToken);
        var torrent = TorrentHolding("Alien.1979.Theatrical", TheatricalInTorrent, TheatricalSize);
        _harness.QBittorrent.AddTorrent(torrent);
        var addRequests = _harness.QBittorrent.AddRequests.Count;
        var qbittorrentMark = _factory.QBittorrentHttp.Requests.Count;

        //Act
        // The owner deletes qBittorrent's mapping on Integrations: its /downloads is no folder of the app's Media folders now.
        using var deleted = await SaveClientMappingsAsync([]);
        await QuarantineAsync(cancellationToken);
        var outsideLine = await _harness.LineAsync(job.Id, cancellationToken);
        var renamesWhileOutside = _factory.Quarantine.Renames.Count;
        // He puts /downloads → /media back, as the sentence says, and presses Retry: the torrent is found where it is now.
        using var restored = await SaveClientMappingsAsync([new PathMappingRequest("/downloads", ReplacementVerificationHarness.MediaRoot)]);
        using var firstRetry = await _factory.PostHttpRequest($"{BaseUrl}/{replacement.Id}/retry", _harness.Owner.AccessToken);
        _factory.Time.Advance(ReplacementQuarantineTracker.PassInterval);
        await QuarantineAsync(cancellationToken);
        var heldLine = await _harness.LineAsync(job.Id, cancellationToken);
        var renamesWhileHeld = _factory.Quarantine.Renames.Count;
        // He removes the torrent in qBittorrent without its files and presses Retry again.
        _harness.QBittorrent.RemoveTorrent(torrent.Hash);
        using var secondRetry = await _factory.PostHttpRequest($"{BaseUrl}/{replacement.Id}/retry", _harness.Owner.AccessToken);
        _factory.Time.Advance(ReplacementQuarantineTracker.PassInterval);
        await QuarantineAsync(cancellationToken);

        //Assert
        // The mapping gone: stopped for review naming the mappings, Retry and Keep both offered, nothing renamed.
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(
            new DownloadJobReplacementDto(ReplacementJobState.NeedsReview, 1, OutsideReason, false, 0)
            {
                Id = replacement.Id,
                CanRetry = true,
                CanCancel = true,
                Stop = ReplacementStop.Move,
            },
            outsideLine);
        Assert.Equal(0, renamesWhileOutside);

        // The mapping back: the next pass names the torrent that holds the copy, still renaming nothing.
        Assert.Equal((HttpStatusCode.NoContent, HttpStatusCode.OK), (restored.StatusCode, firstRetry.StatusCode));
        Assert.Equal(
            new DownloadJobReplacementDto(ReplacementJobState.NeedsReview, 1, HeldReason, false, 0)
            {
                Id = replacement.Id,
                CanRetry = true,
                CanCancel = true,
                Stop = ReplacementStop.Move,
            },
            heldLine);
        Assert.Equal(0, renamesWhileHeld);

        // The torrent gone: both names moved, the job at rest in the quarantine.
        Assert.Equal(HttpStatusCode.OK, secondRetry.StatusCode);
        Assert.Equal(
            new DownloadJobReplacementDto(ReplacementJobState.Quarantined, 1, null, false, 0) { Id = replacement.Id, QuarantinedCopyCount = 1, CanRestore = true, CanDeleteForGood = true },
            await _harness.LineAsync(job.Id, cancellationToken));
        Assert.Equal(
            [
                (TheatricalPath, QuarantinePathOf(replacement, TheatricalPath)),
                (TheatricalSrtPath, QuarantinePathOf(replacement, TheatricalSrtPath)),
            ],
            _factory.Quarantine.Renames);
        Assert.Null(Assert.Single(await _harness.CopiesAsync(cancellationToken)).Problem);

        // ADR 0031 — nothing was ever written to qBittorrent, which was listed once a pass, each time asked whatever its
        // mapping said; and the library heard only of the move.
        var calls = _factory.QBittorrentHttp.Requests.Skip(qbittorrentMark).Select(request => $"{request.Method} {request.Uri.PathAndQuery}").ToList();
        Assert.All(calls, call => Assert.True(call.StartsWith("GET ", StringComparison.Ordinal) || call == "POST /api/v2/auth/login", call));
        Assert.Equal(3, calls.Count(call => call == TorrentsListing));
        Assert.Equal(addRequests, _harness.QBittorrent.AddRequests.Count);
        Assert.Equal([LookRequest], _harness.Writes());
        // ADR 0009 — nothing outside the Media folders was ever looked at on the disk, whatever the mapping said.
        Assert.All(
            _factory.Files.DescribedPaths,
            path => Assert.StartsWith(ReplacementVerificationHarness.MediaRoot + "/", path, StringComparison.Ordinal));

        // The trail: each stop the mover's, each Retry the owner's, then the mover's move.
        var names = await NamesAsync(cancellationToken);
        Assert.Equal(
            [
                JobAudited(AuditAction.ReplacementNeedsReview, replacement.Id, null),
                JobAudited(AuditAction.ReplacementRetried, replacement.Id, _harness.Owner.User.Id),
                JobAudited(AuditAction.ReplacementNeedsReview, replacement.Id, null),
                JobAudited(AuditAction.ReplacementRetried, replacement.Id, _harness.Owner.User.Id),
                JobAudited(AuditAction.ReplacementQuarantineStarted, replacement.Id, null),
                NameAudited(AuditAction.ReplacementNameMoving, names[0], null),
                NameAudited(AuditAction.ReplacementNameMoved, names[0], null),
                NameAudited(AuditAction.ReplacementNameMoving, names[1], null),
                NameAudited(AuditAction.ReplacementNameMoved, names[1], null),
                JobAudited(AuditAction.ReplacementQuarantined, replacement.Id, null),
            ],
            await _harness.QuarantineAuditsAsync(cancellationToken));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The owner's replacement of Alien's theatrical cut by the Director's Cut release, verified: the new film downloaded
    /// to the Movies category's folder, on the disk, and identified by Jellyfin as Alien. Listed with the
    /// <paramref name="description"/> given.
    /// </summary>
    private async Task<(DownloadJobDto Job, ReplacementJobEntity Replacement)> VerifiedFilmAsync(CancellationToken cancellationToken, string? description = null)
    {
        var release = await _harness.FindAsync("Alien", 1979, AlienRelease, imdbId: AlienImdbId, description: description);
        var job = await _harness.VerifiedAsync(
            release,
            _harness.Movies,
            [TheatricalPath],
            [new FakeQBittorrentFile(NewName, NewSize, 1.0)],
            [(NewPath, TestMedia.File(inode: 7, sizeBytes: NewSize), TestMedia.Taken())],
            paths => _harness.JellyfinServer.PlantFilm(paths[0], "Alien", 1979, imdbId: AlienImdbId),
            cancellationToken);
        return (job, await _harness.ReplacementAsync(cancellationToken));
    }

    /// <summary>
    /// MAM-28 — the owner's Save of qBittorrent on Integrations with <paramref name="pathMappings"/> in place of its
    /// mappings, everything else as it is (<c>PUT /api/settings/integrations/{id}</c>): the user name sent again, the password
    /// and the switch left out, which keeps them.
    /// </summary>
    private Task<HttpResponseMessage> SaveClientMappingsAsync(IReadOnlyList<PathMappingRequest> pathMappings) =>
        _factory.PutHttpRequest(
            $"{IntegrationsUrl}/{_harness.Client.Id}",
            _harness.Owner.AccessToken,
            new UpdateIntegrationRequest(
                ReplacementVerificationHarness.ClientName, _harness.Client.BaseUrl, FakeQBittorrentServer.DefaultUserName, PathMappings: pathMappings));

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

    /// <summary>What the disk shows at <paramref name="appPath"/> inside /media: whether a file is there, and its device and
    /// inode (0 for none).</summary>
    private async Task<(FileInventoryStatus Status, long DeviceId, long Inode)> WhatIsAtAsync(string appPath, CancellationToken cancellationToken)
    {
        var described = await _factory.Files.DescribeAsync(appPath, [ReplacementVerificationHarness.MediaRoot], cancellationToken);
        return (described.Status, described.Value?.DeviceId ?? 0, described.Value?.Inode ?? 0);
    }

    /// <summary>A torrent of the Movies category, seeding, done, listing <paramref name="file"/> below
    /// <c>/downloads/movies</c> (the app's <c>/media/movies</c>) at <paramref name="size"/>; its hash made of its name.</summary>
    private static FakeQBittorrentTorrent TorrentHolding(string name, string file, long size) =>
        FakeQBittorrentTorrent.Of(
                new TorrentInfoHashes(Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(name))), null),
                name,
                "Movies",
                "stalledUP",
                savePath: "/downloads/movies")
            with { Files = [new FakeQBittorrentFile(file, size, 1)] };

    private static string QuarantinePathOf(ReplacementJobEntity replacement, string appPath) =>
        ReplacementVerificationHarness.QuarantinePathOf(replacement.Id, appPath);

    /// <summary>An audit row of the job, by <paramref name="actor"/> (none for the app's own), naming it by its release.</summary>
    private static (AuditAction, Guid?, AuditTargetKind?, Guid?, string?) JobAudited(AuditAction action, Guid replacementJobId, Guid? actor) =>
        (action, actor, AuditTargetKind.ReplacementJob, replacementJobId, AlienRelease);

    /// <summary>An audit row of one name, by <paramref name="actor"/> (none for the app's own), naming it by the file's own
    /// name.</summary>
    private static (AuditAction, Guid?, AuditTargetKind?, Guid?, string?) NameAudited(AuditAction action, ReplacementJobNameEntity name, Guid? actor) =>
        (action, actor, AuditTargetKind.ReplacementJobName, name.Id, name.AppPath == TheatricalPath ? TheatricalName : TheatricalSrtName);
}
