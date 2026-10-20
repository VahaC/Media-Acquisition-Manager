using System.Globalization;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Replacements;
using MediaAcquisitionManager.TestInfrastructure.Texts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Replacements;

// MAM-26, ADR 0030, ADR 0033 — check 5, right before the move: an old copy that changed since it was picked (its size, its
// modification time, its identity), that is gone, that is no longer in a ticked folder or no longer inside the Media
// folders, that is a clip of a disc folder, that is the new file itself, whose new file changed since it was verified, or
// that the disk won't read, that is a link to another file, and a folder that can't be listed or a companion without an
// identity, stop the job for review before anything is claimed — no name row, nothing renamed, nothing asked of
// qBittorrent, one ReplacementNeedsReview row of the app's own, the copy's problem and the mark that lets the owner retry.
// A rename the file system refuses — another mount, a name already taken in the quarantine, a platform that can't rename
// without risking a copy — leaves the video where it is and stops the claimed job, its companions never tried; a
// companion's refused rename leaves that companion, named on the job, and its video moves. A rename that moved another
// file put at the name after the last look loses that name, whichever it is, and stops the job. The moves themselves are
// ReplacementQuarantineHttpTests'.
public sealed class ReplacementQuarantineChecksHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string AlienImdbId = "tt0078748";
    private const string AlienRelease = "Alien (1979) Director's Cut BDRemux 2160p HDR";
    private const string TheatricalPath = "/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.mkv";
    private const string TheatricalNfoPath = "/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.nfo";
    private const string TheatricalSrtPath = "/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.srt";
    private const string DiscClipPath = "/media/movies/Alien (1979)/BDMV/STREAM/00000.m2ts";
    private const string NewName = "Alien.1979.Directors.Cut.2160p.mkv";
    private const string NewPath = "/media/movies/Alien.1979.Directors.Cut.2160p.mkv";
    private const int NewSize = (3 * TestTorrent.PieceLength) + 5;
    private const string TorrentsListing = "GET /api/v2/torrents/info?includeFiles=true";
    private const string PlantedHash = "0026002600260026002600260026002600260026";

    // Sentences the Jobs page shows, spelled out rather than read from product code.
    private const string ChangedReason =
        "“/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.mkv” changed since it was picked for replacement, so it stays where it is.";
    private const string GoneReason = "“/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.mkv” is gone, so there is nothing to move.";
    private const string NotTickedReason =
        "“/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.mkv” is no longer in a folder ticked for replacement, so it stays where it is.";
    private const string OutsideMediaFoldersReason =
        "“/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.mkv” is no longer inside the Media folders, so it stays where it is.";
    private const string DiscClipReason = "“00000.m2ts” is part of a disc folder, which can't be replaced one file at a time.";
    private const string NewFileReason = "“/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.mkv” is the new file's own path, so it stays where it is.";
    private const string PairChangedReason =
        "The new file paired with “Alien (1979) Theatrical Cut.mkv” changed or went since it was verified, so nothing moves.";
    // {0} is the replacement's id, a folder of the quarantine.
    private const string OtherMountReason =
        "“Alien (1979) Theatrical Cut.mkv” couldn't be moved into the quarantine: “/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.mkv” and "
        + "“/media/.mam-quarantine/{0}/movies/Alien (1979)/Alien (1979) Theatrical Cut.mkv” lie on different mounts, so renaming it would copy it.";
    private const string NameTakenReason =
        "“Alien (1979) Theatrical Cut.mkv” couldn't be moved into the quarantine: Something is already at "
        + "“/media/.mam-quarantine/{0}/movies/Alien (1979)/Alien (1979) Theatrical Cut.mkv”, and the app never renames over it.";
    private const string CantRenameReason =
        "“Alien (1979) Theatrical Cut.mkv” couldn't be moved into the quarantine: This platform can't rename a file without risking a copy, "
        + "so the app renames nothing here.";
    private const string UnreadableError = "planted-unreadable-0026: “/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.mkv” can't be read.";
    private const string UnreadableReason =
        "The app couldn't read “/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.mkv” again, so it stays where it is: "
        + "planted-unreadable-0026: “/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.mkv” can't be read.";
    private const string FolderNotListedError = "The app may not write in the folder of “/media/movies/Alien (1979)”.";
    private const string CompanionUnreadableReason =
        "The app couldn't read “Alien (1979) Theatrical Cut.srt” well enough to tell whether a torrent holds it.";
    private const string SrtNotAllowedError ="The app may not write in the folder of “/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.srt”.";
    private const string LinkReason =
        "“Alien (1979) Theatrical Cut.mkv” is a link to another file, and the app moves only a file itself, so it stays where it is.";
    // {0} is the name swapped, its file's own name.
    private const string SwappedProblem =
        "A different file now sits in the quarantine under “{0}”, as another file took its place just before the rename, and the old copy's own file wasn't found.";
    private const string VideoSwappedReason = "“Alien (1979) Theatrical Cut.mkv” couldn't be moved into the quarantine: " + SwappedProblem;
    private const string CompanionSwappedReason =
        "“Alien (1979) Theatrical Cut.srt” couldn't be moved into the quarantine: " + SwappedProblem + " 2 of 3 names are in the quarantine.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private ReplacementVerificationHarness _harness = null!;
    private int _qbittorrentMark;
    private int _describedMark;

    public ReplacementQuarantineChecksHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _harness = await ReplacementVerificationHarness.CreateAsync(_factory);
        // Alien's theatrical cut, inspected, in the ticked Фільми, with its .nfo and its subtitles beside it.
        await _harness.PlantFilmAsync("Alien", 1979, TheatricalPath, inode: 1, imdbId: AlienImdbId, editionLabel: "Theatrical Cut");
        _factory.Files.AddFile(TheatricalNfoPath, TestMedia.File(inode: 2, sizeBytes: 4_567));
        _factory.Files.AddFile(TheatricalSrtPath, TestMedia.File(inode: 3, sizeBytes: 51_234));
        await _harness.InspectAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── check 5 ─────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("size")]
    [InlineData("modification time")]
    [InlineData("identity")]
    public async Task Quarantine_of_a_copy_that_changed_since_it_was_picked_stops_for_review_and_moves_nothing(string what)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (job, replacement) = await VerifiedAsync(cancellationToken);
        // Rewritten in place, touched, or another file put at its name since the owner picked it.
        var changed = what switch
        {
            "size" => TestMedia.File(inode: 1, sizeBytes: 3_891_719_631),
            "modification time" => TestMedia.File(inode: 1, modifiedUtc: TestMedia.ModifiedUtc.AddTicks(1)),
            _ => TestMedia.File(inode: 99),
        };
        _factory.Files.AddFile(TheatricalPath, changed, TestMedia.Taken());

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        await AssertStoppedBeforeTheClaimAsync(job, replacement, ChangedReason, cancellationToken);
        Assert.Equal((FileInventoryStatus.Found, (long?)changed.Inode, (long?)changed.SizeBytes), await WhatIsAtAsync(TheatricalPath, cancellationToken));
    }

    [Fact]
    public async Task Quarantine_of_a_copy_that_is_gone_stops_for_review()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (job, replacement) = await VerifiedAsync(cancellationToken);
        _factory.Files.Remove(TheatricalPath);

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        await AssertStoppedBeforeTheClaimAsync(job, replacement, GoneReason, cancellationToken);
        Assert.Equal((FileInventoryStatus.Found, (long?)2, (long?)4_567), await WhatIsAtAsync(TheatricalNfoPath, cancellationToken));
    }

    [Fact]
    public async Task Quarantine_of_a_copy_no_longer_in_a_ticked_folder_stops_for_review()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (job, replacement) = await VerifiedAsync(cancellationToken);
        // The owner unticked Фільми since he scheduled it; the switch is still on.
        await _factory.WithDbAsync(db => db.ReplacementFolders.ExecuteDeleteAsync(cancellationToken));

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        await AssertStoppedBeforeTheClaimAsync(job, replacement, NotTickedReason, cancellationToken);
        Assert.Empty(await _harness.ChangedOldCopiesAsync(cancellationToken));
    }

    [Fact]
    public async Task Quarantine_of_a_copy_no_longer_inside_the_media_folders_stops_for_review()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (job, replacement) = await VerifiedAsync(cancellationToken);
        // The owner's Media folders are now /srv alone.
        await _factory.WithDbAsync(db => db.AllowedRoots.ExecuteUpdateAsync(setters => setters.SetProperty(root => root.Path, "/srv"), cancellationToken));

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        await AssertStoppedBeforeTheClaimAsync(job, replacement, OutsideMediaFoldersReason, cancellationToken);
        // Not even looked at: outside the Media folders the app may read nothing.
        Assert.DoesNotContain(TheatricalPath, _factory.Files.DescribedPaths.Skip(_describedMark));
    }

    [Fact]
    public async Task Quarantine_of_a_clip_of_a_disc_folder_stops_for_review()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A Blu-ray folder's clip, as a verified replacement left it: nothing checks for one when it is scheduled.
        var (download, replacement) = await PlantVerifiedAsync(DiscClipPath);
        _factory.Files.AddFile(DiscClipPath, TestMedia.File(inode: 9));
        _qbittorrentMark = _factory.QBittorrentHttp.Requests.Count;

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        var line = await _harness.LineAsync(download.Id, cancellationToken);
        Assert.Equal((ReplacementJobState.NeedsReview, (string?)DiscClipReason), (line!.State, line.Reason));
        var stopped = await _harness.ReplacementAsync(cancellationToken);
        Assert.Equal((ReplacementJobState.NeedsReview, (string?)DiscClipReason), (stopped.State, TestTexts.En(stopped.Reason)));
        Assert.Equal(DiscClipReason, TestTexts.En(Assert.Single(await _harness.CopiesAsync(cancellationToken)).Problem));
        Assert.Empty(await NamesAsync(cancellationToken));
        Assert.Equal([JobAudited(AuditAction.ReplacementNeedsReview, replacement.Id)], await _harness.QuarantineAuditsAsync(cancellationToken));
        Assert.Empty(_factory.Quarantine.Renames);
        Assert.Empty(_factory.Quarantine.CreatedFolders);
        Assert.Empty(_factory.QBittorrentHttp.Requests.Skip(_qbittorrentMark));
        Assert.Equal((FileInventoryStatus.Found, (long?)9, (long?)3_891_719_630), await WhatIsAtAsync(DiscClipPath, cancellationToken));
    }

    [Fact]
    public async Task Quarantine_of_a_copy_that_is_the_new_file_itself_stops_for_review()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (job, replacement) = await VerifiedAsync(cancellationToken);
        // The new file's row names the old copy's own identity, as a download written over it by a link would.
        await _factory.WithDbAsync(db => db.ReplacementJobNewFiles.ExecuteUpdateAsync(setters => setters.SetProperty(file => file.Inode, 1L), cancellationToken));

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        await AssertStoppedBeforeTheClaimAsync(job, replacement, NewFileReason, cancellationToken);
        Assert.Empty(await _harness.ChangedOldCopiesAsync(cancellationToken));
    }

    [Fact]
    public async Task Quarantine_whose_new_file_changed_since_it_was_verified_stops_for_review()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (job, replacement) = await VerifiedAsync(cancellationToken);
        // Another file at the new file's name since Jellyfin identified it.
        _factory.Files.AddFile(NewPath, TestMedia.File(inode: 8, sizeBytes: NewSize), TestMedia.Taken());

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        await AssertStoppedBeforeTheClaimAsync(job, replacement, PairChangedReason, cancellationToken);
        Assert.Empty(await _harness.ChangedOldCopiesAsync(cancellationToken));
    }

    [Fact]
    public async Task Quarantine_of_a_copy_the_disk_wont_read_stops_for_review_with_the_disks_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (job, replacement) = await VerifiedAsync(cancellationToken);
        _factory.Files.AddProblem(TheatricalPath, FileInventoryStatus.Unreadable, UnreadableError);

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        await AssertStoppedBeforeTheClaimAsync(job, replacement, UnreadableReason, cancellationToken);
    }

    [Fact]
    public async Task Quarantine_of_a_copy_that_is_a_link_to_another_file_stops_for_review_and_moves_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (job, replacement) = await VerifiedAsync(cancellationToken);
        // The video's name is now a link to the same file kept elsewhere: the look follows it and finds the file picked,
        // but renaming the link would leave that file where it is.
        const string storePath = "/media/store/Alien (1979) Theatrical Cut.mkv";
        var facts = (await _factory.Files.DescribeAsync(TheatricalPath, [ReplacementVerificationHarness.MediaRoot], cancellationToken)).Value!;
        _factory.Files.Remove(TheatricalPath);
        _factory.Files.AddFile(storePath, facts, TestMedia.Taken());
        _factory.Files.AddLinkedName(TheatricalPath, storePath);

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        await AssertStoppedBeforeTheClaimAsync(job, replacement, LinkReason, cancellationToken);
        Assert.Equal((FileInventoryStatus.Found, (long?)facts.Inode, (long?)facts.SizeBytes), await WhatIsAtAsync(storePath, cancellationToken));
    }

    // ── the companions ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Quarantine_of_a_copy_whose_folder_cant_be_listed_stops_for_review_with_the_listings_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (job, replacement) = await VerifiedAsync(cancellationToken);
        _factory.Quarantine.Refuse("/media/movies/Alien (1979)", FileQuarantineStatus.NotAllowed, FolderNotListedError);

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        await AssertStoppedBeforeTheClaimAsync(job, replacement, FolderNotListedError, cancellationToken);
    }

    [Fact]
    public async Task Quarantine_of_a_copy_whose_companion_has_no_identity_stops_for_review()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (job, replacement) = await VerifiedAsync(cancellationToken);
        // Without its device and inode no torrent's hold on the subtitles can be confirmed, so none can be ruled out.
        _factory.Files.AddFile(TheatricalSrtPath, new FileFacts(51_234, TestMedia.ModifiedUtc, DeviceId: null, Inode: null, LinkCount: null));

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        await AssertStoppedBeforeTheClaimAsync(job, replacement, CompanionUnreadableReason, cancellationToken);
    }

    // ── the rename refused ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Quarantine_across_two_mounts_renames_nothing_and_stops_for_review()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (job, replacement) = await VerifiedAsync(cancellationToken);
        // The film's folder is a mount of its own inside /media: the rename would be a copy (EXDEV).
        _factory.Quarantine.SetMount("/media/movies/Alien (1979)", 2);

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        await AssertVideoStayedAsync(job, replacement, Format(OtherMountReason, replacement), cancellationToken);
    }

    [Fact]
    public async Task Quarantine_onto_a_name_already_taken_in_the_quarantine_renames_nothing_and_stops_for_review()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (job, replacement) = await VerifiedAsync(cancellationToken);
        // Something sits where the video would go: never renamed over (EEXIST).
        var taken = ReplacementVerificationHarness.QuarantinePathOf(replacement.Id, TheatricalPath);
        _factory.Files.AddFile(taken, TestMedia.File(inode: 99, sizeBytes: 1_000));

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        await AssertVideoStayedAsync(job, replacement, Format(NameTakenReason, replacement), cancellationToken);
        Assert.Equal((FileInventoryStatus.Found, (long?)99, (long?)1_000), await WhatIsAtAsync(taken, cancellationToken));
    }

    [Fact]
    public async Task Quarantine_on_a_platform_that_cant_rename_without_risking_a_copy_renames_nothing_and_stops_for_review()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (job, replacement) = await VerifiedAsync(cancellationToken);
        _factory.Quarantine.Capabilities = new FileQuarantineCapabilities(CanRename: false);

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        await AssertVideoStayedAsync(job, replacement, CantRenameReason, cancellationToken);
    }

    [Fact]
    public async Task Quarantine_of_a_companion_whose_rename_is_refused_leaves_it_named_on_the_job_and_moves_the_video()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (_, replacement) = await VerifiedAsync(cancellationToken);
        // The kernel refuses the subtitles' rename (EACCES).
        _factory.Quarantine.Refuse(TheatricalSrtPath, FileQuarantineStatus.NotAllowed, SrtNotAllowedError);

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        var moved = await _harness.ReplacementAsync(cancellationToken);
        Assert.Equal((ReplacementJobState.Quarantined, (string?)null), (moved.State, TestTexts.En(moved.Reason)));
        var names = await NamesAsync(cancellationToken);
        Assert.Equal(
            [
                (TheatricalPath, ReplacementNameState.Quarantined, (string?)null),
                (TheatricalNfoPath, ReplacementNameState.Quarantined, null),
                (TheatricalSrtPath, ReplacementNameState.Stayed, SrtNotAllowedError),
            ],
            names.Select(name => (name.AppPath, name.State, TestTexts.En(name.Problem))));
        Assert.Equal((FileInventoryStatus.Found, (long?)3, (long?)51_234), await WhatIsAtAsync(TheatricalSrtPath, cancellationToken));
        Assert.Equal(
            [
                JobAudited(AuditAction.ReplacementQuarantineStarted, replacement.Id),
                NameAudited(AuditAction.ReplacementNameMoving, names[0]),
                NameAudited(AuditAction.ReplacementNameMoved, names[0]),
                NameAudited(AuditAction.ReplacementNameMoving, names[1]),
                NameAudited(AuditAction.ReplacementNameMoved, names[1]),
                NameAudited(AuditAction.ReplacementNameMoving, names[2]),
                NameAudited(AuditAction.ReplacementNameNotMoved, names[2]),
                JobAudited(AuditAction.ReplacementQuarantined, replacement.Id),
            ],
            await _harness.QuarantineAuditsAsync(cancellationToken));
    }

    [Theory]
    [InlineData("the video")]
    [InlineData("a companion")]
    public async Task Quarantine_whose_rename_moves_another_file_put_at_the_name_records_the_name_lost_and_stops_for_review(string which)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (job, replacement) = await VerifiedAsync(cancellationToken);
        // Another file takes the name's place after the last look, right before the rename, which then moves that file.
        var swappedPath = which == "the video" ? TheatricalPath : TheatricalSrtPath;
        var swappedName = swappedPath[(swappedPath.LastIndexOf('/') + 1)..];
        _factory.Quarantine.Swap(swappedPath, TestMedia.File(inode: 99, sizeBytes: 1_000));
        // The library won't take a notice, so whatever is owed stays owed; the notice itself is ReplacementLibraryNoticeTests'.
        _harness.JellyfinServer.ScanRefusal = System.Net.HttpStatusCode.ServiceUnavailable;

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        var reason = which == "the video" ? Format(VideoSwappedReason, swappedName) : Format(CompanionSwappedReason, swappedName);
        var problem = Format(SwappedProblem, swappedName);
        var line = await _harness.LineAsync(job.Id, cancellationToken);
        Assert.Equal((ReplacementJobState.NeedsReview, (string?)reason, true), (line!.State, line.Reason, line.CanRetry));
        Assert.Equal(reason, TestTexts.En(Assert.Single(await _harness.CopiesAsync(cancellationToken)).Problem));
        // Never Quarantined: what sits at its quarantine name is another file, which Restore and Delete for good must
        // never take for the old copy.
        var names = await NamesAsync(cancellationToken);
        IReadOnlyList<(string, ReplacementNameState, string?)> expected = which == "the video"
            ?
            [
                (TheatricalPath, ReplacementNameState.Lost, problem),
                (TheatricalNfoPath, ReplacementNameState.Planned, null),
                (TheatricalSrtPath, ReplacementNameState.Planned, null),
            ]
            :
            [
                (TheatricalPath, ReplacementNameState.Quarantined, null),
                (TheatricalNfoPath, ReplacementNameState.Quarantined, null),
                (TheatricalSrtPath, ReplacementNameState.Lost, problem),
            ];
        Assert.Equal(expected, names.Select(name => (name.AppPath, name.State, TestTexts.En(name.Problem))));
        var swapped = names.Single(name => name.AppPath == swappedPath);
        Assert.Equal((FileInventoryStatus.Found, (long?)99, (long?)1_000), await WhatIsAtAsync(swapped.QuarantinePath, cancellationToken));
        Assert.Equal((FileInventoryStatus.NotFound, (long?)null, (long?)null), await WhatIsAtAsync(swappedPath, cancellationToken));
        Assert.Equal(
            [
                JobAudited(AuditAction.ReplacementQuarantineStarted, replacement.Id),
                .. names.TakeWhile(name => name.AppPath != swappedPath)
                    .SelectMany(name => new[] { NameAudited(AuditAction.ReplacementNameMoving, name), NameAudited(AuditAction.ReplacementNameMoved, name) }),
                NameAudited(AuditAction.ReplacementNameMoving, swapped),
                NameAudited(AuditAction.ReplacementNameNotMoved, swapped),
                JobAudited(AuditAction.ReplacementNeedsReview, replacement.Id),
            ],
            await _harness.QuarantineAuditsAsync(cancellationToken));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The owner's replacement of Alien's theatrical cut by the Director's Cut release, verified: the new film downloaded
    /// to the Movies category's folder, on the disk, and identified by Jellyfin as Alien. The marks of what follows are taken
    /// then.
    /// </summary>
    private async Task<(DownloadJobDto Job, ReplacementJobEntity Replacement)> VerifiedAsync(CancellationToken cancellationToken)
    {
        var release = await _harness.FindAsync("Alien", 1979, AlienRelease, imdbId: AlienImdbId);
        var job = await _harness.VerifiedAsync(
            release,
            _harness.Movies,
            [TheatricalPath],
            [new FakeQBittorrentFile(NewName, NewSize, 1.0)],
            [(NewPath, TestMedia.File(inode: 7, sizeBytes: NewSize), TestMedia.Taken())],
            paths => _harness.JellyfinServer.PlantFilm(paths[0], "Alien", 1979, imdbId: AlienImdbId),
            cancellationToken);
        _qbittorrentMark = _factory.QBittorrentHttp.Requests.Count;
        _describedMark = _factory.Files.DescribedPaths.Count;
        return (job, await _harness.ReplacementAsync(cancellationToken));
    }

    /// <summary>
    /// A replacement of the copy at <paramref name="appPath"/> as a verified one stands in the database — its download
    /// done and checked —, planted rather than reached through the routes, for a copy the routes would let through only
    /// with a library of its kind.
    /// </summary>
    private async Task<(DownloadJobEntity Download, ReplacementJobEntity Replacement)> PlantVerifiedAsync(string appPath)
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
        var replacement = await replacements.Create(download.Id, _harness.Owner.User.Id, ReplacementJobState.Verified);
        await replacements.AddFile(replacement.Id, appPath, TestMedia.File(inode: 9));
        return (download, replacement);
    }

    /// <summary>One pass of the mover, which must not have logged an error.</summary>
    private async Task QuarantineAsync(CancellationToken cancellationToken)
    {
        await _factory.QuarantineReplacementsAsync(cancellationToken);
        if (_factory.Logs.Entries.FirstOrDefault(entry => entry.Level >= LogLevel.Error) is { } error)
            throw new InvalidOperationException($"A pass logged an error: {error.Category}: {error.Message}");
    }

    /// <summary>
    /// The job stopped for review with <paramref name="reason"/> before the move was claimed — on the Jobs page too —, with
    /// the copy's problem, the mark that lets the owner retry, and one audit row of the app's own; no name planned, nothing
    /// renamed or made in the quarantine, and qBittorrent not even read.
    /// </summary>
    private async Task AssertStoppedBeforeTheClaimAsync(DownloadJobDto job, ReplacementJobEntity replacement, string reason, CancellationToken cancellationToken)
    {
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var line = await _harness.LineAsync(job.Id, cancellationToken);
        Assert.Equal((ReplacementJobState.NeedsReview, (string?)reason), (line!.State, line.Reason));
        var stopped = await _harness.ReplacementAsync(cancellationToken);
        Assert.Equal(
            (ReplacementJobState.NeedsReview, (string?)reason, now, (DateTime?)now, (DateTime?)null, (DateTime?)null),
            (stopped.State, TestTexts.En(stopped.Reason), stopped.StateChangedUtc, stopped.QuarantineCheckedUtc, stopped.QuarantinedUtc, stopped.LibraryNoticeDueUtc));
        var copy = Assert.Single(await _harness.CopiesAsync(cancellationToken));
        Assert.Equal(reason, TestTexts.En(copy.Problem));
        Assert.Empty(await NamesAsync(cancellationToken));
        Assert.Equal([JobAudited(AuditAction.ReplacementNeedsReview, replacement.Id)], await _harness.QuarantineAuditsAsync(cancellationToken));
        Assert.Empty(_factory.Quarantine.Renames);
        Assert.Empty(_factory.Quarantine.CreatedFolders);
        Assert.Empty(_factory.QBittorrentHttp.Requests.Skip(_qbittorrentMark));
        Assert.All(_harness.Writes(), write => Assert.Equal("POST /Library/Media/Updated", write));
    }

    /// <summary>
    /// The claimed job stopped for review with <paramref name="reason"/> because its video's rename was refused: the video
    /// stayed with the refusal's sentence, its companions never tried, nothing renamed or made in the quarantine, no notice
    /// owed, and both the claim's and the name's rows written; qBittorrent was only read.
    /// </summary>
    private async Task AssertVideoStayedAsync(DownloadJobDto job, ReplacementJobEntity replacement, string reason, CancellationToken cancellationToken)
    {
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var line = await _harness.LineAsync(job.Id, cancellationToken);
        Assert.Equal((ReplacementJobState.NeedsReview, (string?)reason), (line!.State, line.Reason));
        var stopped = await _harness.ReplacementAsync(cancellationToken);
        Assert.Equal(
            (ReplacementJobState.NeedsReview, (DateTime?)now, (DateTime?)null, (DateTime?)null),
            (stopped.State, stopped.QuarantineCheckedUtc, stopped.QuarantinedUtc, stopped.LibraryNoticeDueUtc));
        Assert.Equal(reason, TestTexts.En(Assert.Single(await _harness.CopiesAsync(cancellationToken)).Problem));
        var names = await NamesAsync(cancellationToken);
        var refusal = reason[(reason.IndexOf(": ", StringComparison.Ordinal) + 2)..];
        Assert.Equal(
            [
                (TheatricalPath, ReplacementNameState.Stayed, (string?)refusal),
                (TheatricalNfoPath, ReplacementNameState.Planned, null),
                (TheatricalSrtPath, ReplacementNameState.Planned, null),
            ],
            names.Select(name => (name.AppPath, name.State, TestTexts.En(name.Problem))));
        Assert.Equal(
            [
                JobAudited(AuditAction.ReplacementQuarantineStarted, replacement.Id),
                NameAudited(AuditAction.ReplacementNameMoving, names[0]),
                NameAudited(AuditAction.ReplacementNameNotMoved, names[0]),
                JobAudited(AuditAction.ReplacementNeedsReview, replacement.Id),
            ],
            await _harness.QuarantineAuditsAsync(cancellationToken));
        Assert.Empty(_factory.Quarantine.Renames);
        Assert.Empty(_factory.Quarantine.CreatedFolders);
        Assert.Empty(await _harness.ChangedOldCopiesAsync(cancellationToken));
        Assert.Equal((FileInventoryStatus.Found, (long?)2, (long?)4_567), await WhatIsAtAsync(TheatricalNfoPath, cancellationToken));
        Assert.All(
            _factory.QBittorrentHttp.Requests.Skip(_qbittorrentMark).Select(request => $"{request.Method} {request.Uri.PathAndQuery}"),
            call => Assert.True(call.StartsWith("GET ", StringComparison.Ordinal) || call == "POST /api/v2/auth/login", call));
        Assert.Contains(TorrentsListing, _factory.QBittorrentHttp.Requests.Skip(_qbittorrentMark).Select(request => $"{request.Method} {request.Uri.PathAndQuery}"));
    }

    /// <summary>The replacement's names, in their order.</summary>
    private async Task<IReadOnlyList<ReplacementJobNameEntity>> NamesAsync(CancellationToken cancellationToken)
    {
        List<ReplacementJobNameEntity> rows = [];
        await _factory.WithDbAsync(async db => rows = await db.ReplacementJobNames.AsNoTracking().OrderBy(name => name.Position).ToListAsync(cancellationToken));
        return rows;
    }

    /// <summary>What the disk shows at <paramref name="appPath"/> inside /media: whether a file is there, its inode and its
    /// size.</summary>
    private async Task<(FileInventoryStatus Status, long? Inode, long? SizeBytes)> WhatIsAtAsync(string appPath, CancellationToken cancellationToken)
    {
        var described = await _factory.Files.DescribeAsync(appPath, [ReplacementVerificationHarness.MediaRoot], cancellationToken);
        return (described.Status, described.Value?.Inode, described.Value?.SizeBytes);
    }

    private static string Format(string reason, ReplacementJobEntity replacement) => string.Format(CultureInfo.InvariantCulture, reason, replacement.Id);

    private static string Format(string reason, string name) => string.Format(CultureInfo.InvariantCulture, reason, name);

    /// <summary>An audit row of the job, the app's own (no actor), naming it by its release.</summary>
    private static (AuditAction, Guid?, AuditTargetKind?, Guid?, string?) JobAudited(AuditAction action, Guid replacementJobId) =>
        (action, null, AuditTargetKind.ReplacementJob, replacementJobId, AlienRelease);

    /// <summary>An audit row of one name, the app's own (no actor), naming it by the file's own name.</summary>
    private static (AuditAction, Guid?, AuditTargetKind?, Guid?, string?) NameAudited(AuditAction action, ReplacementJobNameEntity name) =>
        (action, null, AuditTargetKind.ReplacementJobName, name.Id, name.AppPath[(name.AppPath.LastIndexOf('/') + 1)..]);
}
