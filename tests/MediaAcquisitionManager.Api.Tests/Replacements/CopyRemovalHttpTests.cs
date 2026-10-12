using System.Net;
using System.Net.Http.Json;
using System.Text;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Replacements;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Replacements;

// MAM-40 — the owner's Remove this copy on the Duplicates page (POST /api/replacements/removals): the file of a group of one
// work's copies, with its own companions, renamed into the quarantine of the allowed root holding it, at
// /media/.mam-quarantine/<removal id>/<its path inside /media> — the video first, then each companion, each with an intent
// row before its rename and an outcome row after it, between the removal's CopyRemovalStarted and CopyRemoved, the owner
// their actor —; Jellyfin told the names went, in its own terms; and the copy no longer counts in its group, so a group of
// two leaves the list and one of three keeps the other two. A video that can't be renamed ends the removal NotMoved with
// nothing in the quarantine, 409 with the sentence; one whose rename moved another file is lost, and leaves the removal
// waiting for review, 409 with where to look; a companion that can't stays, named on its row. A request without the
// group or the file is 400, a group or file not found a bare 404. The checks' refusals are CopyRemovalChecksHttpTests', the
// list's verdicts CopyRemovalVerdictsHttpTests', Restore and Delete for good CopyRemovalActionsHttpTests'.
public sealed class CopyRemovalHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/replacements/removals";
    private const string SameWork = "?class=SameWork";
    private const string AlienImdbId = "tt0078748";
    private const string KeptPath = "/media/movies/Alien (1979)/Alien (1979) 1080p.mkv";
    private const string RemovedPath = "/media/movies/Alien (1979)/Alien (1979) 2160p.mkv";
    private const string RemovedNfoPath = "/media/movies/Alien (1979)/Alien (1979) 2160p.nfo";
    private const string RemovedSrtPath = "/media/movies/Alien (1979)/Alien (1979) 2160p.uk.srt";
    private const string ThirdPath = "/media/movies/Alien (1979) Remux/Alien (1979) Remux.mkv";
    private const long VideoSize = 3_891_719_630;
    private const long NfoSize = 4_567;
    private const long SrtSize = 52_345;
    private const string UpdatedCall = "POST /Library/Media/Updated";
    private const string TorrentsListing = "GET /api/v2/torrents/info?includeFiles=true";

    // Sentences the page shows, spelled out rather than read from product code.
    private const string NotAllowedError = "The app may not write in the folder of “/media/movies/Alien (1979)/Alien (1979) 2160p.mkv”.";
    private const string VideoNotMovedReason = "“Alien (1979) 2160p.mkv” couldn't be moved into the quarantine: " + NotAllowedError;
    private const string IncompleteError = "Remove needs the group and the file you pressed it on.";
    private const string UnreadableBodyError =
        "The body must be JSON with the group and the file, such as { \"groupKey\": \"<key>\", \"fileId\": \"<id>\" }.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private ReplacementVerificationHarness _harness = null!;
    private CopyRemovalPlanter _removals = null!;

    public CopyRemovalHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _harness = await ReplacementVerificationHarness.CreateAsync(_factory);
        _removals = new CopyRemovalPlanter(_factory, _harness);
        // Two copies of Alien in the ticked Фільми, inspected: the one the owner keeps, and the one he removes with its .nfo
        // and its subtitles beside it.
        await _harness.PlantFilmAsync("Alien", 1979, KeptPath, inode: 1, imdbId: AlienImdbId);
        await _harness.PlantFilmAsync("Alien", 1979, RemovedPath, inode: 2, imdbId: AlienImdbId);
        _factory.Files.AddFile(RemovedNfoPath, TestMedia.File(inode: 3, sizeBytes: NfoSize));
        _factory.Files.AddFile(RemovedSrtPath, TestMedia.File(inode: 4, sizeBytes: SrtSize));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── removed ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Remove_of_a_copy_moves_its_video_and_own_companions_into_the_quarantine_and_answers_the_removal()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _harness.InspectAsync(cancellationToken);
        var group = await GroupAsync(cancellationToken);
        var removedFile = group.FileAt(RemovedPath);
        _harness.Mark();
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _harness.Owner.AccessToken, group.RemoveRequestFor(RemovedPath));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<CopyRemovalDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        var removal = Assert.Single(await _removals.RemovalsAsync(cancellationToken));
        Assert.Equal(
            (removal.Id, CopyRemovalState.Quarantined, (string?)null, "Alien (1979)", DuplicateClass.SameWork, RemovedPath, QuarantinePathOf(removal.Id, RemovedPath)),
            (body.Id, body.State, body.Reason, body.Title, body.GroupClass, body.OriginalPath, body.QuarantinePath));
        // Every name in the quarantine, each with one name: deleting them for good frees all they take.
        Assert.Equal(
            (VideoSize + NfoSize + SrtSize, VideoSize + NfoSize + SrtSize, "owner", now, now, true, true),
            (body.SizeBytes, body.FreesBytes, body.RequestedByUserName, body.CreatedUtc, body.StateChangedUtc, body.CanRestore, body.CanDeleteForGood));

        // The removal row: the group as it was, the file the owner pressed it on, its path as Jellyfin reported it, and the
        // library told already, so nothing is owed.
        Assert.Equal(
            (CopyRemovalState.Quarantined, group.Key, removedFile.File.Id, RemovedPath, ReplacementVerificationHarness.InJellyfin(RemovedPath)),
            (removal.State, removal.GroupKey, removal.PhysicalFileId, removal.AppPath, removal.ServicePath));
        Assert.Equal(
            ((Guid?)_harness.Owner.User.Id, "owner", now, now, (DateTime?)now, (DateTime?)null),
            (removal.RequestedByUserId, removal.RequestedByUserName, removal.CreatedUtc, removal.StateChangedUtc, removal.QuarantinedUtc, removal.LibraryNoticeDueUtc));

        // The video, then its own companions in the folder's order, each where it came from and where it went in the
        // quarantine of /media under the removal's id, with the identity the move expected and kept.
        var names = await _removals.NamesAsync(removal.Id, cancellationToken);
        Assert.Equal(
            [
                (0, ReplacementNameKind.Video, RemovedPath, QuarantinePathOf(removal.Id, RemovedPath), 2L, VideoSize, ReplacementNameState.Quarantined),
                (1, ReplacementNameKind.Companion, RemovedNfoPath, QuarantinePathOf(removal.Id, RemovedNfoPath), 3L, NfoSize, ReplacementNameState.Quarantined),
                (2, ReplacementNameKind.Companion, RemovedSrtPath, QuarantinePathOf(removal.Id, RemovedSrtPath), 4L, SrtSize, ReplacementNameState.Quarantined),
            ],
            names.Select(name => (name.Position, name.Kind, name.AppPath, name.QuarantinePath, name.Inode, name.SizeBytes, name.State)));
        Assert.All(
            names,
            name => Assert.Equal(
                (ReplacementVerificationHarness.MediaRoot, TestMedia.DeviceId, TestMedia.ModifiedUtc, (int?)1, (string?)null, (DateTime?)now),
                (name.AllowedRoot, name.DeviceId, name.ModifiedUtc, name.LinkCount, name.Problem, name.MovedUtc)));
        // The answer's names are the rows, in a replacement's shape.
        Assert.Equal(
            names.Select(name => new ReplacementNameDto(name.Id, name.Kind, name.AppPath, name.QuarantinePath, name.SizeBytes, false, name.State, null)),
            body.Names);

        // One rename per name, in that order, never a copy; the kept copy where it was.
        Assert.Equal(names.Select(name => (name.AppPath, name.QuarantinePath)), _factory.Quarantine.Renames);
        foreach (var name in names)
        {
            Assert.Equal((FileInventoryStatus.NotFound, (long?)null), await WhatIsAtAsync(name.AppPath, cancellationToken));
            Assert.Equal((FileInventoryStatus.Found, (long?)name.Inode), await WhatIsAtAsync(name.QuarantinePath, cancellationToken));
        }

        Assert.Equal((FileInventoryStatus.Found, (long?)1), await WhatIsAtAsync(KeptPath, cancellationToken));
        Assert.Empty(_factory.Quarantine.Deletions);

        // The trail, save by save: the claim, each name's intent then its outcome, the end — the owner their actor.
        Assert.Equal(
            [
                RemovalAudited(AuditAction.CopyRemovalStarted, removal.Id),
                NameAudited(AuditAction.CopyRemovalNameMoving, names[0]),
                NameAudited(AuditAction.CopyRemovalNameMoved, names[0]),
                NameAudited(AuditAction.CopyRemovalNameMoving, names[1]),
                NameAudited(AuditAction.CopyRemovalNameMoved, names[1]),
                NameAudited(AuditAction.CopyRemovalNameMoving, names[2]),
                NameAudited(AuditAction.CopyRemovalNameMoved, names[2]),
                RemovalAudited(AuditAction.CopyRemoved, removal.Id),
            ],
            await _removals.AuditsAsync(cancellationToken));

        // ADR 0030 — Jellyfin heard once, in its own terms, that the three names went; ADR 0031 — qBittorrent was only read.
        Assert.Equal([UpdatedCall], _harness.Writes());
        Assert.Equal(
            [ReplacementVerificationHarness.InJellyfin(RemovedPath), ReplacementVerificationHarness.InJellyfin(RemovedNfoPath), ReplacementVerificationHarness.InJellyfin(RemovedSrtPath)],
            Assert.Single(_harness.JellyfinServer.ScanRequests));
        Assert.Equal(["Deleted", "Deleted", "Deleted"], Assert.Single(_harness.JellyfinServer.ScanUpdateTypes));
        Assert.Contains(TorrentsListing, _factory.QBittorrentHttp.Requests.Select(request => $"{request.Method} {request.Uri.PathAndQuery}"));
        // The app's own warnings only: in a container ASP.NET Core warns, the first time a host needs Data Protection,
        // that its keys may not outlive the container.
        Assert.DoesNotContain(
            _factory.Logs.Entries,
            entry => entry.Level >= LogLevel.Warning && entry.Category.StartsWith("MediaAcquisitionManager.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Remove_takes_a_group_of_two_copies_off_the_duplicates_list()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _harness.InspectAsync(cancellationToken);
        var group = await GroupAsync(cancellationToken);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _harness.Owner.AccessToken, group.RemoveRequestFor(RemovedPath));
        var after = await _factory.DuplicatesAsync(_harness.Owner.AccessToken, cancellationToken: cancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // The copy in the quarantine counts in no group, though Jellyfin's last sync and the inspection still list it: one
        // copy is left, which is no duplicate.
        Assert.Equal((0, 0), (after.TotalCount, after.Items.Count));
    }

    [Fact]
    public async Task Remove_from_a_group_of_three_copies_keeps_the_other_two_as_a_group()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _harness.PlantFilmAsync("Alien", 1979, ThirdPath, inode: 5, imdbId: AlienImdbId);
        await _harness.InspectAsync(cancellationToken);
        var group = await GroupAsync(cancellationToken);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _harness.Owner.AccessToken, group.RemoveRequestFor(RemovedPath));
        var after = await GroupAsync(cancellationToken);

        //Assert
        Assert.Equal(3, group.Files.Count);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // By the path Jellyfin reported, ordinal: "Alien (1979) Remux/" sorts before "Alien (1979)/".
        Assert.Equal([ThirdPath, KeptPath], after.Files.Select(file => file.File.AppPath));
        // Still one work's copies, each removable: the other is still there to keep.
        Assert.All(after.Files, file => Assert.Equal(new CopyRemovalVerdictDto(true, null), file.Removal));
    }

    [Fact]
    public async Task Remove_of_an_episodes_copy_keeps_its_series_place_and_title_as_the_removals_title()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var series = await _harness.PlantSeriesAsync("Yellowstone", 2018, imdbId: "tt4236770", tmdbTvId: "73586");
        await _harness.PlantEpisodeAsync(series, 2, 3, "Off the Horse", "/media/tv-shows/Yellowstone/Season 2/Yellowstone S02E03.mkv", inode: 21);
        await _harness.PlantEpisodeAsync(series, 2, 3, "Off the Horse", "/media/tv-shows/Yellowstone/Season 2/Yellowstone S02E03 2160p.mkv", inode: 22);
        await _harness.InspectAsync(cancellationToken);
        var group = Assert.Single((await _factory.DuplicatesAsync(_harness.Owner.AccessToken, SameWork, cancellationToken)).Items, item => item.Files.Count == 2
            && item.Files.All(file => file.File.AppPath!.StartsWith("/media/tv-shows/", StringComparison.Ordinal)));

        //Act
        var response = await _factory.PostHttpRequest(
            BaseUrl, _harness.Owner.AccessToken, group.RemoveRequestFor("/media/tv-shows/Yellowstone/Season 2/Yellowstone S02E03 2160p.mkv"));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<CopyRemovalDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal("Yellowstone · S02E03 · Off the Horse", body.Title);
        Assert.Equal("Yellowstone · S02E03 · Off the Horse", Assert.Single(await _removals.RemovalsAsync(cancellationToken)).Title);
    }

    [Fact]
    public async Task Remove_of_the_last_two_copies_at_once_removes_one_and_finds_no_group_for_the_other()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _harness.InspectAsync(cancellationToken);
        var group = await GroupAsync(cancellationToken);

        //Act
        // Both requests the page could send at once: one at a time, the second checks against what the first left.
        var answers = await Task.WhenAll(
            _factory.PostHttpRequest(BaseUrl, _harness.Owner.AccessToken, group.RemoveRequestFor(KeptPath)),
            _factory.PostHttpRequest(BaseUrl, _harness.Owner.AccessToken, group.RemoveRequestFor(RemovedPath)));

        //Assert
        // Whichever went first, the other found no group left: the copy that stays was never removed with it.
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.NotFound], answers.Select(answer => answer.StatusCode).Order());
        var removal = Assert.Single(await _removals.RemovalsAsync(cancellationToken));
        Assert.Equal(CopyRemovalState.Quarantined, removal.State);
        var stayed = removal.AppPath == KeptPath ? RemovedPath : KeptPath;
        Assert.Equal((FileInventoryStatus.Found, (long?)(stayed == KeptPath ? 1 : 2)), await WhatIsAtAsync(stayed, cancellationToken));
    }

    // ── not moved ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Remove_when_the_video_cant_be_renamed_ends_not_moved_with_nothing_in_the_quarantine_and_is_409()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _harness.InspectAsync(cancellationToken);
        var group = await GroupAsync(cancellationToken);
        _factory.Quarantine.Refuse(RemovedPath, FileQuarantineStatus.NotAllowed, NotAllowedError);
        _harness.Mark();

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _harness.Owner.AccessToken, group.RemoveRequestFor(RemovedPath));

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(VideoNotMovedReason, await response.ReadErrorAsync(cancellationToken));
        // The removal ended there, kept as history: its video stayed, named, and its companions never moved.
        var removal = Assert.Single(await _removals.RemovalsAsync(cancellationToken));
        Assert.Equal(
            (CopyRemovalState.NotMoved, (string?)VideoNotMovedReason, (DateTime?)null, (DateTime?)null),
            (removal.State, removal.Reason, removal.QuarantinedUtc, removal.LibraryNoticeDueUtc));
        var names = await _removals.NamesAsync(removal.Id, cancellationToken);
        Assert.Equal(
            [
                (RemovedPath, ReplacementNameState.Stayed, (string?)NotAllowedError),
                (RemovedNfoPath, ReplacementNameState.Planned, (string?)null),
                (RemovedSrtPath, ReplacementNameState.Planned, (string?)null),
            ],
            names.Select(name => (name.AppPath, name.State, name.Problem)));
        Assert.Empty(_factory.Quarantine.Renames);
        Assert.Equal((FileInventoryStatus.Found, (long?)2), await WhatIsAtAsync(RemovedPath, cancellationToken));
        Assert.Equal(
            [
                RemovalAudited(AuditAction.CopyRemovalStarted, removal.Id),
                NameAudited(AuditAction.CopyRemovalNameMoving, names[0]),
                NameAudited(AuditAction.CopyRemovalNameNotMoved, names[0]),
                RemovalAudited(AuditAction.CopyRemovalNotMoved, removal.Id),
            ],
            await _removals.AuditsAsync(cancellationToken));
        // Nothing left the library, so Jellyfin hears nothing, and the copy still counts in its group, still removable.
        Assert.Empty(_harness.Writes());
        var after = await GroupAsync(cancellationToken);
        Assert.Equal((2, new CopyRemovalVerdictDto(true, null)), (after.Files.Count, after.FileAt(RemovedPath).Removal));
    }

    [Fact]
    public async Task Remove_whose_video_rename_moves_another_file_waits_for_review_saying_where_to_look_and_is_409()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _harness.InspectAsync(cancellationToken);
        var group = await GroupAsync(cancellationToken);
        // Another file takes the video's place after the last look, right before the rename, which then moves that file.
        _factory.Quarantine.Swap(RemovedPath, TestMedia.File(inode: 99, sizeBytes: 1_000));
        _harness.Mark();

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _harness.Owner.AccessToken, group.RemoveRequestFor(RemovedPath));
        var listed = await _factory.GetHttpRequest(BaseUrl, _harness.Owner.AccessToken);

        //Assert
        var removal = Assert.Single(await _removals.RemovalsAsync(cancellationToken));
        var quarantinePath = QuarantinePathOf(removal.Id, RemovedPath);
        var problem = $"A different file now sits in the quarantine at “{quarantinePath}”, as another file took the place of “Alien (1979) 2160p.mkv” "
            + "just before the rename, and this copy's own file wasn't found. Look there to see which file it is.";
        var reason = "“Alien (1979) 2160p.mkv” couldn't be moved into the quarantine: " + problem;
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(reason, await response.ReadErrorAsync(cancellationToken));
        // Never NotMoved, which would end it out of sight: it waits for review under Removed copies, with nothing to restore
        // or delete — what sits at its quarantine name is another file.
        Assert.Equal(
            (CopyRemovalState.NeedsReview, (string?)reason, (DateTime?)null, (DateTime?)null),
            (removal.State, removal.Reason, removal.QuarantinedUtc, removal.LibraryNoticeDueUtc));
        var names = await _removals.NamesAsync(removal.Id, cancellationToken);
        Assert.Equal(
            [
                (RemovedPath, ReplacementNameState.Lost, (string?)problem),
                (RemovedNfoPath, ReplacementNameState.Planned, (string?)null),
                (RemovedSrtPath, ReplacementNameState.Planned, (string?)null),
            ],
            names.Select(name => (name.AppPath, name.State, name.Problem)));
        Assert.Equal((FileInventoryStatus.Found, (long?)99), await WhatIsAtAsync(quarantinePath, cancellationToken));
        Assert.Equal((FileInventoryStatus.NotFound, (long?)null), await WhatIsAtAsync(RemovedPath, cancellationToken));
        Assert.Equal(
            [
                RemovalAudited(AuditAction.CopyRemovalStarted, removal.Id),
                NameAudited(AuditAction.CopyRemovalNameMoving, names[0]),
                NameAudited(AuditAction.CopyRemovalNameNotMoved, names[0]),
                RemovalAudited(AuditAction.CopyRemovalNeedsReview, removal.Id),
            ],
            await _removals.AuditsAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        var item = Assert.Single((await listed.Content.ReadFromJsonAsync<CopyRemovalListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!.Items);
        Assert.Equal(
            (removal.Id, CopyRemovalState.NeedsReview, (string?)reason, false, false),
            (item.Id, item.State, item.Reason, item.CanRestore, item.CanDeleteForGood));
        // Nothing of the copy is known to have left the library, so Jellyfin hears nothing.
        Assert.Empty(_harness.Writes());
    }

    [Fact]
    public async Task Remove_keeps_a_companion_that_cant_be_renamed_where_it_is_and_still_removes_the_video()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _harness.InspectAsync(cancellationToken);
        var group = await GroupAsync(cancellationToken);
        const string nfoNotAllowed = "The app may not write in the folder of “/media/movies/Alien (1979)/Alien (1979) 2160p.nfo”.";
        _factory.Quarantine.Refuse(RemovedNfoPath, FileQuarantineStatus.NotAllowed, nfoNotAllowed);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _harness.Owner.AccessToken, group.RemoveRequestFor(RemovedPath));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<CopyRemovalDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal((CopyRemovalState.Quarantined, (string?)null, VideoSize + SrtSize), (body.State, body.Reason, body.SizeBytes));
        Assert.Equal(
            [
                (RemovedPath, ReplacementNameState.Quarantined, (string?)null),
                (RemovedNfoPath, ReplacementNameState.Stayed, (string?)nfoNotAllowed),
                (RemovedSrtPath, ReplacementNameState.Quarantined, (string?)null),
            ],
            body.Names.Select(name => (name.OriginalPath, name.State, name.Problem)));
        Assert.Equal((FileInventoryStatus.Found, (long?)3), await WhatIsAtAsync(RemovedNfoPath, cancellationToken));
    }

    // ── the request ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Remove_of_a_key_no_group_has_is_404_and_writes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _harness.InspectAsync(cancellationToken);
        var request = (await GroupAsync(cancellationToken)).RemoveRequestFor(RemovedPath) with { GroupKey = "0123456789abcdef0123456789abcdef" };

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _harness.Owner.AccessToken, request);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("", await response.Content.ReadAsStringAsync(cancellationToken));
        await AssertNothingWrittenAsync(cancellationToken);
    }

    [Fact]
    public async Task Remove_of_a_file_not_in_the_group_is_404_and_writes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _harness.InspectAsync(cancellationToken);
        var request = (await GroupAsync(cancellationToken)).RemoveRequestFor(RemovedPath) with { FileId = Guid.NewGuid() };

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _harness.Owner.AccessToken, request);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertNothingWrittenAsync(cancellationToken);
    }

    [Theory]
    [InlineData("not-a-key")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF")]
    public async Task Remove_of_a_key_of_another_shape_is_404(string key)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _harness.InspectAsync(cancellationToken);
        var request = (await GroupAsync(cancellationToken)).RemoveRequestFor(RemovedPath) with { GroupKey = key };

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _harness.Owner.AccessToken, request);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertNothingWrittenAsync(cancellationToken);
    }

    [Fact]
    public async Task Remove_without_a_body_is_400_with_the_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _harness.Owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(IncompleteError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(cancellationToken);
    }

    [Fact]
    public async Task Remove_without_the_file_is_400_with_the_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _harness.InspectAsync(cancellationToken);
        var request = (await GroupAsync(cancellationToken)).RemoveRequestFor(RemovedPath) with { FileId = null };

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _harness.Owner.AccessToken, request);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(IncompleteError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(cancellationToken);
    }

    [Fact]
    public async Task Remove_with_a_body_that_isnt_a_request_is_400_with_what_it_must_be()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl)
        {
            Content = new StringContent("""{ "groupKey": 7, "fileId": "the big one" }""", Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new("Bearer", _harness.Owner.AccessToken);

        //Act
        var response = await _factory.SendHttpRequest(request);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(UnreadableBodyError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(cancellationToken);
    }

    [Fact]
    public async Task Remove_without_a_token_is_401()
    {
        //Arrange

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, body: new RemoveCopyRequest("0123456789abcdef0123456789abcdef", Guid.NewGuid()));

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>The one group of Alien's copies on the list, as the owner reads it.</summary>
    private async Task<LibraryDuplicateGroupDto> GroupAsync(CancellationToken cancellationToken) =>
        Assert.Single((await _factory.DuplicatesAsync(_harness.Owner.AccessToken, SameWork, cancellationToken)).Items);

    private static string QuarantinePathOf(Guid copyRemovalId, string appPath) => CopyRemovalPlanter.QuarantinePathOf(copyRemovalId, appPath);

    private async Task<(FileInventoryStatus Status, long? Inode)> WhatIsAtAsync(string appPath, CancellationToken cancellationToken)
    {
        var described = await _factory.Files.DescribeAsync(appPath, [ReplacementVerificationHarness.MediaRoot], cancellationToken);
        return (described.Status, described.Value?.Inode);
    }

    private (AuditAction, Guid?, AuditTargetKind?, Guid?, string?) RemovalAudited(AuditAction action, Guid copyRemovalId) =>
        (action, _harness.Owner.User.Id, AuditTargetKind.CopyRemoval, copyRemovalId, "Alien (1979)");

    private (AuditAction, Guid?, AuditTargetKind?, Guid?, string?) NameAudited(AuditAction action, Core.Entities.CopyRemovalNameEntity name) =>
        (action, _harness.Owner.User.Id, AuditTargetKind.CopyRemovalName, name.Id, name.AppPath[(name.AppPath.LastIndexOf('/') + 1)..]);

    /// <summary>No removal, name or audit row of one, and nothing renamed, deleted or made through the write contract.</summary>
    private async Task AssertNothingWrittenAsync(CancellationToken cancellationToken)
    {
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.CopyRemovals.CountAsync(cancellationToken));
            Assert.Equal(0, await db.CopyRemovalNames.CountAsync(cancellationToken));
        });
        Assert.Empty(await _removals.AuditsAsync(cancellationToken));
        Assert.Empty(_factory.Quarantine.Renames);
        Assert.Empty(_factory.Quarantine.Deletions);
        Assert.Empty(_factory.Quarantine.CreatedFolders);
    }
}
