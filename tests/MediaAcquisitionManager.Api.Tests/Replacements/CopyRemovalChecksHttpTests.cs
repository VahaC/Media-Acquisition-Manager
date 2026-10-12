using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Replacements;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Replacements;

// MAM-40 — every check of the owner's Remove this copy, before anything is written: the stored rules, which the Duplicates
// list shows as each file's verdict in the very same sentence (one place judges both) — replacing and removing copies
// switched on, the file inside the Media folders and in a ticked library folder, read by the app as media, no clip of a
// disc, holding no work outside its group, held by no open replacement nor removal, and another copy the app read as
// media —, then at the request the platform, the identity the page showed, the disk, another copy still on it, and every
// download client (check 6). Each refusal is 409 with its sentence and keeps no removal row, name, audit row or rename; a
// group of one file's names is no group a copy is removed from. A companion a torrent holds stays where it is, and its video
// still goes.
public sealed class CopyRemovalChecksHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/replacements/removals";
    private const string SameWork = "?class=SameWork";
    private const string AlienImdbId = "tt0078748";
    private const string KeptPath = "/media/movies/Alien (1979)/Alien (1979) 1080p.mkv";
    private const string RemovedPath = "/media/movies/Alien (1979)/Alien (1979) 2160p.mkv";
    private const string RemovedNfoPath = "/media/movies/Alien (1979)/Alien (1979) 2160p.nfo";
    private const string ThirdPath = "/media/movies/Alien (1979) Remux/Alien (1979) Remux.mkv";
    private const long VideoSize = 3_891_719_630;

    // The rules' sentences, spelled out rather than read from product code.
    private const string SwitchedOffReason = "Removing copies is switched off. Turn on “Replace copies after verification” under Media folders.";
    private const string OutsideReason = "This file isn't inside the Media folders, so the app can't move it.";
    private const string NotTickedReason = "This file's library folder isn't ticked for replacement under Media folders.";
    private const string NotInspectedReason = "The app hasn't inspected this file yet. Press Inspect files on the Library page first.";
    private const string NotReadableReason = "The app's last look couldn't read this file as media, so it can't tell what would go.";
    private const string OtherWorkReason = "This file also holds a film or an episode outside this group, which would go with it.";
    private const string HeldByReplacementReason = "A replacement on the Jobs page holds this file.";
    private const string HeldByRemovalReason = "A removal under Removed copies holds this file.";
    private const string LastReadableReason =
        "No other copy in this group is one the app has read as media, so this one stays: it may be the only one that plays.";
    private const string NotRemovableClassReason = "Only a copy in “Exact copies” or “Same film, different files” can be removed.";

    // The request's sentences.
    private const string NotSupportedError = "The app can't rename files on the platform it runs on, so nothing was removed.";
    private const string PageChangedReason = "This copy changed since the page showed it. Reload the page to see it as it is now.";
    private const string ChangedReason =
        "“/media/movies/Alien (1979)/Alien (1979) 2160p.mkv” changed since the app last read it, so it stays where it is. Inspect the library's files again, then press Remove again.";
    private const string GoneReason = "“/media/movies/Alien (1979)/Alien (1979) 2160p.mkv” is gone, so there is nothing to remove.";
    private const string NoOtherCopyReason =
        "No other copy of this group is on the disk as the app last read it, so this one stays. Inspect the library's files again to see what is there.";
    private const string HeldReason =
        "Still held by the torrent “Alien.1979.2160p” in “qBittorrent”: remove that torrent there without its files, then press Remove again.";
    private const string UnreachableReason =
        "The app couldn't tell whether a torrent in “qBittorrent” holds this copy: Couldn't reach qBittorrent at http://qbittorrent.test:8080: "
        + "nothing accepted the connection. Check the address and port, and that qBittorrent is running.";
    private const string CompanionHeldProblem = "Held by the torrent “Alien.1979.2160p.Info” in “qBittorrent”, so it stays where it is.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private ReplacementVerificationHarness _harness = null!;
    private CopyRemovalPlanter _removals = null!;

    public CopyRemovalChecksHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _harness = await ReplacementVerificationHarness.CreateAsync(_factory);
        _removals = new CopyRemovalPlanter(_factory, _harness);
        // Two copies of Alien in the ticked Фільми, the one to remove with its .nfo beside it; inspected by each test.
        await _harness.PlantFilmAsync("Alien", 1979, KeptPath, inode: 1, imdbId: AlienImdbId);
        await _harness.PlantFilmAsync("Alien", 1979, RemovedPath, inode: 2, imdbId: AlienImdbId);
        _factory.Files.AddFile(RemovedNfoPath, TestMedia.File(inode: 3, sizeBytes: 4_567));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── the stored rules: the list's verdict and the request's refusal, one sentence ──────────────

    [Fact]
    public async Task Remove_while_switched_off_is_409_as_the_list_says()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _harness.InspectAsync(cancellationToken);
        await _factory.WithDbAsync(db => db.ReplacementSettings.ExecuteUpdateAsync(setters => setters.SetProperty(settings => settings.IsEnabled, false), cancellationToken));

        //Act
        var (verdict, response) = await VerdictAndRemoveAsync(RemovedPath, cancellationToken);

        //Assert
        await AssertRefusedAsync(SwitchedOffReason, verdict, response, cancellationToken);
    }

    [Fact]
    public async Task Remove_of_a_file_outside_the_media_folders_is_409_as_the_list_says()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _harness.InspectAsync(cancellationToken);
        // The owner took /media out of the list since: its quarantine can't take the file.
        await _factory.WithDbAsync(async db =>
        {
            await db.AllowedRoots.ExecuteDeleteAsync(cancellationToken);
            db.AllowedRoots.Add(new AllowedRootEntity { Path = "/elsewhere", Position = 0 });
            await db.SaveChangesAsync(cancellationToken);
        });

        //Act
        var (verdict, response) = await VerdictAndRemoveAsync(RemovedPath, cancellationToken);

        //Assert
        await AssertRefusedAsync(OutsideReason, verdict, response, cancellationToken);
    }

    [Fact]
    public async Task Remove_of_a_file_in_an_unticked_folder_is_409_as_the_list_says()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _harness.InspectAsync(cancellationToken);
        await _factory.WithDbAsync(db => db.ReplacementFolders
            .Where(tick => tick.Path == ReplacementVerificationHarness.FilmsFolder)
            .ExecuteDeleteAsync(cancellationToken));

        //Act
        var (verdict, response) = await VerdictAndRemoveAsync(RemovedPath, cancellationToken);

        //Assert
        await AssertRefusedAsync(NotTickedReason, verdict, response, cancellationToken);
    }

    [Fact]
    public async Task Remove_of_a_file_the_app_hasnt_inspected_is_409_as_the_list_says()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _harness.InspectAsync(cancellationToken);
        // A third copy, synced after the inspection.
        await _harness.PlantFilmAsync("Alien", 1979, ThirdPath, inode: 5, imdbId: AlienImdbId);

        //Act
        var (verdict, response) = await VerdictAndRemoveAsync(ThirdPath, cancellationToken);

        //Assert
        await AssertRefusedAsync(NotInspectedReason, verdict, response, cancellationToken);
    }

    [Fact]
    public async Task Remove_of_a_file_the_app_couldnt_read_as_media_is_409_and_so_is_the_last_one_it_could()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The kept copy is there, but ffprobe can't read it: the other may be the only one that plays.
        _factory.Files.AddFile(KeptPath, TestMedia.File(inode: 1));
        await _harness.InspectAsync(cancellationToken);
        var group = await GroupAsync(cancellationToken);

        //Act
        var unreadable = await _factory.PostHttpRequest(BaseUrl, _harness.Owner.AccessToken, group.RemoveRequestFor(KeptPath));
        var lastReadable = await _factory.PostHttpRequest(BaseUrl, _harness.Owner.AccessToken, group.RemoveRequestFor(RemovedPath));

        //Assert
        Assert.Equal(new CopyRemovalVerdictDto(false, NotReadableReason), group.FileAt(KeptPath).Removal);
        Assert.Equal(new CopyRemovalVerdictDto(false, LastReadableReason), group.FileAt(RemovedPath).Removal);
        Assert.Equal((HttpStatusCode.Conflict, NotReadableReason), (unreadable.StatusCode, await unreadable.ReadErrorAsync(cancellationToken)));
        Assert.Equal((HttpStatusCode.Conflict, LastReadableReason), (lastReadable.StatusCode, await lastReadable.ReadErrorAsync(cancellationToken)));
        await AssertNothingWrittenAsync(cancellationToken);
    }

    [Fact]
    public async Task Remove_whose_only_other_readable_member_is_a_second_name_of_the_same_file_is_409_as_the_list_says()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A second name of the removed copy's file, which Jellyfin holds as another version; and the kept copy is there, but
        // ffprobe can't read it. The second name is no other copy: it goes nowhere else when this one goes.
        await _harness.PlantFilmAsync("Alien", 1979, ThirdPath, inode: 2, imdbId: AlienImdbId);
        _factory.Files.AddFile(KeptPath, TestMedia.File(inode: 1));
        await _harness.InspectAsync(cancellationToken);
        var group = await GroupAsync(cancellationToken);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _harness.Owner.AccessToken, group.RemoveRequestFor(RemovedPath));

        //Assert
        Assert.Equal([ThirdPath, KeptPath, RemovedPath], group.Files.Select(file => file.File.AppPath));
        Assert.Equal(new CopyRemovalVerdictDto(false, LastReadableReason), group.FileAt(RemovedPath).Removal);
        Assert.Equal(new CopyRemovalVerdictDto(false, LastReadableReason), group.FileAt(ThirdPath).Removal);
        Assert.Equal((HttpStatusCode.Conflict, LastReadableReason), (response.StatusCode, await response.ReadErrorAsync(cancellationToken)));
        await AssertNothingWrittenAsync(cancellationToken);
    }

    [Fact]
    public async Task Remove_of_a_file_that_also_holds_another_work_is_409_as_the_list_says()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Jellyfin also made the removed copy a version of Aliens: removing it would take Aliens away too.
        var works = new MediaWorkSeeder(_factory.Scope);
        var aliens = await works.CreateMovie(_harness.Jellyfin, "Aliens", 1986, imdbId: "tt0090605");
        var libraryPath = ReplacementVerificationHarness.InJellyfin(RemovedPath);
        await works.AddVersion(_harness.Jellyfin, aliens, JellyfinItemIds.Of(JellyfinItemIds.MovieType, libraryPath + "#aliens"), _harness.Films, null, libraryPath);
        await _harness.InspectAsync(cancellationToken);

        //Act
        var (verdict, response) = await VerdictAndRemoveAsync(RemovedPath, cancellationToken);

        //Assert
        await AssertRefusedAsync(OtherWorkReason, verdict, response, cancellationToken);
    }

    [Fact]
    public async Task Remove_of_a_copy_an_open_replacement_holds_is_409_as_the_list_says()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _harness.InspectAsync(cancellationToken);
        // A replacement of this very copy, verified and about to move it.
        await new ReplacementQuarantinePlanter(_factory, _harness).PlantAsync(
            ReplacementJobState.Verified, [], copies: [new PlantedCopy(RemovedPath, TestMedia.File(inode: 2))]);

        //Act
        var (verdict, response) = await VerdictAndRemoveAsync(RemovedPath, cancellationToken);

        //Assert
        await AssertRefusedAsync(HeldByReplacementReason, verdict, response, cancellationToken, removalsBefore: 0);
    }

    [Fact]
    public async Task Remove_of_a_copy_an_open_removal_holds_is_409_as_the_list_says()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _harness.InspectAsync(cancellationToken);
        // A removal of this very copy under way: claimed, its video still to move.
        await _removals.PlantAsync(
            CopyRemovalState.Moving, [new PlantedName(ReplacementNameKind.Video, RemovedPath, TestMedia.File(inode: 2), ReplacementNameState.Planned)]);

        //Act
        var (verdict, response) = await VerdictAndRemoveAsync(RemovedPath, cancellationToken);

        //Assert
        await AssertRefusedAsync(HeldByRemovalReason, verdict, response, cancellationToken, removalsBefore: 1);
    }

    [Fact]
    public async Task Remove_of_a_group_of_one_files_names_is_409()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A second name of the kept copy's file, which Jellyfin holds as another work: one file, two names.
        await _harness.PlantFilmAsync("Alien", 1979, ThirdPath, inode: 1, imdbId: AlienImdbId);
        await _harness.InspectAsync(cancellationToken);
        var sameFile = Assert.Single((await _factory.DuplicatesAsync(_harness.Owner.AccessToken, "?class=SameFile", cancellationToken)).Items);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _harness.Owner.AccessToken, sameFile.RemoveRequestFor(ThirdPath));

        //Assert
        // One file's names free nothing: the list offers no removal there, and the request refuses it.
        Assert.All(sameFile.Files, file => Assert.Null(file.Removal));
        Assert.Equal((HttpStatusCode.Conflict, NotRemovableClassReason), (response.StatusCode, await response.ReadErrorAsync(cancellationToken)));
        await AssertNothingWrittenAsync(cancellationToken);
    }

    // ── the request's own checks ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Remove_on_a_platform_that_cant_rename_is_409()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _harness.InspectAsync(cancellationToken);
        var group = await GroupAsync(cancellationToken);
        _factory.Quarantine.Capabilities = new FileQuarantineCapabilities(CanRename: false);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _harness.Owner.AccessToken, group.RemoveRequestFor(RemovedPath));

        //Assert
        Assert.Equal((HttpStatusCode.Conflict, NotSupportedError), (response.StatusCode, await response.ReadErrorAsync(cancellationToken)));
        await AssertNothingWrittenAsync(cancellationToken);
    }

    [Fact]
    public async Task Remove_with_an_identity_other_than_the_one_the_app_read_is_409()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _harness.InspectAsync(cancellationToken);
        // The page showed another inode: an inspection since read the file anew, and the owner would remove what he didn't see.
        var request = (await GroupAsync(cancellationToken)).RemoveRequestFor(RemovedPath) with { Inode = 99 };

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _harness.Owner.AccessToken, request);

        //Assert
        Assert.Equal((HttpStatusCode.Conflict, PageChangedReason), (response.StatusCode, await response.ReadErrorAsync(cancellationToken)));
        await AssertNothingWrittenAsync(cancellationToken);
    }

    [Fact]
    public async Task Remove_of_a_copy_that_changed_on_the_disk_since_it_was_inspected_is_409()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _harness.InspectAsync(cancellationToken);
        var group = await GroupAsync(cancellationToken);
        // Rewritten at its path since the inspection: another size.
        _factory.Files.AddFile(RemovedPath, TestMedia.File(inode: 2, sizeBytes: VideoSize + 1), TestMedia.Taken());

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _harness.Owner.AccessToken, group.RemoveRequestFor(RemovedPath));

        //Assert
        Assert.Equal((HttpStatusCode.Conflict, ChangedReason), (response.StatusCode, await response.ReadErrorAsync(cancellationToken)));
        await AssertNothingWrittenAsync(cancellationToken);
    }

    [Fact]
    public async Task Remove_of_a_copy_gone_from_the_disk_is_409()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _harness.InspectAsync(cancellationToken);
        var group = await GroupAsync(cancellationToken);
        _factory.Files.Remove(RemovedPath);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _harness.Owner.AccessToken, group.RemoveRequestFor(RemovedPath));

        //Assert
        Assert.Equal((HttpStatusCode.Conflict, GoneReason), (response.StatusCode, await response.ReadErrorAsync(cancellationToken)));
        await AssertNothingWrittenAsync(cancellationToken);
    }

    [Fact]
    public async Task Remove_when_the_other_copy_is_no_longer_on_the_disk_is_409()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _harness.InspectAsync(cancellationToken);
        var group = await GroupAsync(cancellationToken);
        // The copy that would stay went since the inspection: removing this one would leave none.
        _factory.Files.Remove(KeptPath);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _harness.Owner.AccessToken, group.RemoveRequestFor(RemovedPath));

        //Assert
        Assert.Equal((HttpStatusCode.Conflict, NoOtherCopyReason), (response.StatusCode, await response.ReadErrorAsync(cancellationToken)));
        await AssertNothingWrittenAsync(cancellationToken);
    }

    [Fact]
    public async Task Remove_of_a_copy_a_torrent_holds_is_409_naming_the_torrent()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _harness.InspectAsync(cancellationToken);
        var group = await GroupAsync(cancellationToken);
        _harness.QBittorrent.AddTorrent(TorrentHoldings.Holding("Alien.1979.2160p", "Alien (1979)/Alien (1979) 2160p.mkv", VideoSize));
        _harness.Mark();

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _harness.Owner.AccessToken, group.RemoveRequestFor(RemovedPath));

        //Assert
        Assert.Equal((HttpStatusCode.Conflict, HeldReason), (response.StatusCode, await response.ReadErrorAsync(cancellationToken)));
        await AssertNothingWrittenAsync(cancellationToken);
        // ADR 0031 — qBittorrent was read, never written to.
        Assert.Empty(_harness.Writes());
    }

    [Fact]
    public async Task Remove_when_a_download_client_cant_be_read_is_409()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _harness.InspectAsync(cancellationToken);
        var group = await GroupAsync(cancellationToken);
        // Anything uncertain is never an all-clear (ADR 0031).
        _harness.QBittorrent.Unreachable = true;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _harness.Owner.AccessToken, group.RemoveRequestFor(RemovedPath));

        //Assert
        Assert.Equal((HttpStatusCode.Conflict, UnreachableReason), (response.StatusCode, await response.ReadErrorAsync(cancellationToken)));
        await AssertNothingWrittenAsync(cancellationToken);
    }

    [Fact]
    public async Task Remove_leaves_a_companion_a_torrent_holds_where_it_is_and_moves_the_video()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _harness.InspectAsync(cancellationToken);
        var group = await GroupAsync(cancellationToken);
        _harness.QBittorrent.AddTorrent(TorrentHoldings.Holding("Alien.1979.2160p.Info", "Alien (1979)/Alien (1979) 2160p.nfo", 4_567));

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _harness.Owner.AccessToken, group.RemoveRequestFor(RemovedPath));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<CopyRemovalDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal(CopyRemovalState.Quarantined, body.State);
        Assert.Equal(
            [(RemovedPath, ReplacementNameState.Quarantined, (string?)null), (RemovedNfoPath, ReplacementNameState.Stayed, (string?)CompanionHeldProblem)],
            body.Names.Select(name => (name.OriginalPath, name.State, name.Problem)));
        // Only the video was renamed; the .nfo stays for the torrent.
        Assert.Equal([RemovedPath], _factory.Quarantine.Renames.Select(rename => rename.From));
        var removal = Assert.Single(await _removals.RemovalsAsync(cancellationToken));
        Assert.Equal(
            [AuditAction.CopyRemovalStarted, AuditAction.CopyRemovalNameMoving, AuditAction.CopyRemovalNameMoved, AuditAction.CopyRemoved],
            (await _removals.AuditsAsync(cancellationToken)).Select(audit => audit.Action));
        Assert.Equal(CopyRemovalState.Quarantined, removal.State);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private async Task<LibraryDuplicateGroupDto> GroupAsync(CancellationToken cancellationToken) =>
        Assert.Single((await _factory.DuplicatesAsync(_harness.Owner.AccessToken, SameWork, cancellationToken)).Items);

    /// <summary>The list's verdict on the file at <paramref name="appPath"/>, then the owner's Remove of it as the page sends
    /// it.</summary>
    private async Task<(CopyRemovalVerdictDto? Verdict, HttpResponseMessage Response)> VerdictAndRemoveAsync(string appPath, CancellationToken cancellationToken)
    {
        var group = await GroupAsync(cancellationToken);
        var verdict = group.FileAt(appPath).Removal;
        return (verdict, await _factory.PostHttpRequest(BaseUrl, _harness.Owner.AccessToken, group.RemoveRequestFor(appPath)));
    }

    /// <summary>The list said <paramref name="reason"/>, and the request was refused with the same sentence, writing
    /// nothing: no removal beyond the <paramref name="removalsBefore"/> planted, no audit row of one, nothing renamed.</summary>
    private async Task AssertRefusedAsync(
        string reason, CopyRemovalVerdictDto? verdict, HttpResponseMessage response, CancellationToken cancellationToken, int removalsBefore = 0)
    {
        Assert.Equal(new CopyRemovalVerdictDto(false, reason), verdict);
        Assert.Equal((HttpStatusCode.Conflict, reason), (response.StatusCode, await response.ReadErrorAsync(cancellationToken)));
        await AssertNothingWrittenAsync(cancellationToken, removalsBefore);
    }

    /// <summary>No removal beyond the planted ones, no audit row of one, and nothing renamed, deleted or made through the
    /// write contract.</summary>
    private async Task AssertNothingWrittenAsync(CancellationToken cancellationToken, int removalsBefore = 0)
    {
        await _factory.WithDbAsync(async db => Assert.Equal(removalsBefore, await db.CopyRemovals.CountAsync(cancellationToken)));
        Assert.Empty(await _removals.AuditsAsync(cancellationToken));
        Assert.Empty(_factory.Quarantine.Renames);
        Assert.Empty(_factory.Quarantine.Deletions);
        Assert.Empty(_factory.Quarantine.CreatedFolders);
    }
}
