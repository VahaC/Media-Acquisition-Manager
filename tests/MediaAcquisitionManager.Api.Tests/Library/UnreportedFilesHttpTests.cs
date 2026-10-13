using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Library;

// MAM-42 — the Files in no library dialog (GET /api/library/unreported-files): the files the scan found that no library
// reports now, for every signed-in user, by path, with the size the app read, what its look found (why it couldn't read
// one the owner's only) and where each sits among the libraries' folders; a Problems and an Ignored view; paged by the
// server. And the owner's Ignore and Stop ignoring (PUT/DELETE …/{id}/ignored): the file leaves the Media folders row's
// counts at once and comes back, with an audit row each time, and nothing changes on the disk. Every name here is the
// owner's real Scan folders over HTTP, and every look the inspection worker's pass.
public sealed class UnreportedFilesHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library/unreported-files";
    private const string SourcesUrl = "/api/library/sources";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string Root = "/media";
    private const string IsoPath = "/media/Adobe Premiere Rush 2.10.0.30.iso";
    private const string BirthdayPath = "/media/home/Birthday 2019.mkv";
    private const string SamplePath = "/media/movies/Dune (2021)/Dune (2021) sample.mkv";
    private const string MovedIsoPath = "/media/software/Adobe Premiere Rush 2.10.0.30.iso";
    private const long IsoSizeBytes = 1_288_490_189;
    private const string ShortPath = "/media/movies/Alien. Collection/Shorts/[01] Alien. Containment (2019) WEBRip 1080p H.265 [ENG] [Hurtom].mkv";
    private const string TrailerPath = "/media/movies/Dune (2021)/Dune (2021)-trailer.mkv";
    private const string ExtraSamplePath = "/media/movies/Dune (2021)/Sample2.mkv";

    private static readonly string IsoError = string.Format(FakeFileInventory.UnreadableMediaError, IsoPath);

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;

    public UnreportedFilesHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task List_gives_every_counted_file_in_path_order_with_its_size_its_look_and_its_place_among_the_library_folders()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SeedLibraryFolderAsync();
        var firstSeenUtc = await ScanAndInspectAsync(cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal((3, 1, 25, 3, 1, 0), (list.TotalCount, list.Page, list.PageSize, list.CountedCount, list.ProblemCount, list.IgnoredCount));
        // By path, ordinal: the ISO in /media itself, then /media/home, then the library's /media/movies.
        Assert.Equal([IsoPath, BirthdayPath, SamplePath], list.Items.Select(file => file.AppPath));
        var (iso, birthday, sample) = (list.Items[0], list.Items[1], list.Items[2]);
        Assert.Equal(
            (IsoSizeBytes, firstSeenUtc, (DateTime?)null, new UnreportedFilePlaceDto(UnreportedFilePlaceKind.NoLibraryFolder, null)),
            (iso.SizeBytes, iso.FirstSeenUtc, iso.IgnoredUtc, iso.Place));
        // What ffprobe said, for the owner.
        Assert.Equal((FileInspectionStatus.MediaUnreadable, IsoError, (MediaFactsDto?)null), (iso.Inspection!.Status, iso.Inspection.Problem, iso.Inspection.Media));
        Assert.Equal(new UnreportedFilePlaceDto(UnreportedFilePlaceKind.NoLibraryFolder, null), birthday.Place);
        Assert.Equal((FileInspectionStatus.Inspected, 5_179_168L), (birthday.Inspection!.Status, birthday.Inspection.Media!.DurationMilliseconds));
        // Under the folder Фільми listed, yet Фільми didn't report it; " sample" with a space is no extra's name (MAM-43).
        Assert.Equal(new UnreportedFilePlaceDto(UnreportedFilePlaceKind.LibraryFolder, "Фільми"), sample.Place);
        Assert.Equal((FileInspectionStatus.Inspected, 5_604_672L, null), (sample.Inspection!.Status, sample.Inspection.Media!.DurationMilliseconds, sample.Inspection.Problem));
    }

    [Fact]
    public async Task List_keeps_the_files_the_library_takes_for_extras_on_the_extras_view_with_why_and_counts_none_of_them()
    {
        //Arrange
        // MAM-43, MAM-44 — production's Alien short and two names Jellyfin takes for extras, in Фільми's folder.
        var cancellationToken = TestContext.Current.CancellationToken;
        await ScanExtrasAsync(cancellationToken);

        //Act
        var all = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);
        var extras = await _factory.GetHttpRequest($"{BaseUrl}?view=extras", _member.AccessToken);

        //Assert
        var counted = await ReadListAsync(all, cancellationToken);
        Assert.Equal([BirthdayPath], counted.Items.Select(file => file.AppPath));
        Assert.Equal((1, 0, 0, 3), (counted.CountedCount, counted.ProblemCount, counted.IgnoredCount, counted.ExtrasCount));
        var list = await ReadListAsync(extras, cancellationToken);
        Assert.Equal(
            [
                (ShortPath, new UnreportedFilePlaceDto(UnreportedFilePlaceKind.ExtrasFolder, "Фільми", "Shorts")),
                (TrailerPath, new UnreportedFilePlaceDto(UnreportedFilePlaceKind.ExtraName, "Фільми", "-trailer")),
                (ExtraSamplePath, new UnreportedFilePlaceDto(UnreportedFilePlaceKind.ExtraName, "Фільми", "sample")),
            ],
            list.Items.Select(file => (file.AppPath, file.Place)));
        Assert.Equal(3, list.TotalCount);
    }

    [Fact]
    public async Task Sources_leave_the_extras_out_of_every_warning_even_one_that_couldnt_be_read_and_keep_their_room()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ScanExtrasAsync(cancellationToken);

        //Act
        var scan = (await ReadSourcesAsync(cancellationToken)).Scan;
        var problems = await ReadListAsync(await _factory.GetHttpRequest($"{BaseUrl}?view=problems", _owner.AccessToken), cancellationToken);

        //Assert
        // The trailer has no media ffprobe could read, yet warns of nothing: it is an extra (Vasyl's decision).
        Assert.Equal((new LibrarySourceInspectionDto(1, 0, 0, null), 0, 3), (scan.Files, scan.IgnoredCount, scan.ExtrasCount));
        Assert.Equal((4, 4), (scan.Space.FileCount, scan.Space.NameCount));
        Assert.Empty(problems.Items);
        var trailer = await FileAtAsync(TrailerPath, cancellationToken);
        Assert.Equal(FileInspectionStatus.MediaUnreadable, trailer.InspectionStatus);
    }

    [Fact]
    public async Task Ignore_an_extra_lists_it_under_ignored_and_no_longer_under_extras()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ScanExtrasAsync(cancellationToken);
        var alienShort = await FileAtAsync(ShortPath, cancellationToken);

        //Act
        var response = await _factory.PutHttpRequest($"{BaseUrl}/{alienShort.Id}/ignored", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var ignored = await ReadListAsync(await _factory.GetHttpRequest($"{BaseUrl}?view=ignored", _owner.AccessToken), cancellationToken);
        Assert.Equal([ShortPath], ignored.Items.Select(file => file.AppPath));
        Assert.Equal((1, 1, 2), (ignored.CountedCount, ignored.IgnoredCount, ignored.ExtrasCount));
        var scan = (await ReadSourcesAsync(cancellationToken)).Scan;
        Assert.Equal((1, 2), (scan.IgnoredCount, scan.ExtrasCount));
    }

    [Fact]
    public async Task List_as_a_member_gives_the_same_files_without_why_a_file_couldnt_be_read()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ScanAndInspectAsync(cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?view=problems", _member.AccessToken);

        //Assert
        var list = await ReadListAsync(response, cancellationToken);
        var iso = Assert.Single(list.Items);
        // The sentence quotes a path inside the app and what the media inspector said: the owner's diagnostics.
        Assert.Equal((IsoPath, FileInspectionStatus.MediaUnreadable, (string?)null), (iso.AppPath, iso.Inspection!.Status, iso.Inspection.Problem));
        Assert.Equal((1, 3, 1, 0), (list.TotalCount, list.CountedCount, list.ProblemCount, list.IgnoredCount));
    }

    [Fact]
    public async Task List_pages_the_files_on_the_server_and_counts_every_view_on_every_page()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ScanAndInspectAsync(cancellationToken);

        //Act
        var second = await _factory.GetHttpRequest($"{BaseUrl}?page=2&pageSize=2", _owner.AccessToken);
        var pastTheEnd = await _factory.GetHttpRequest($"{BaseUrl}?page=9&pageSize=2", _owner.AccessToken);

        //Assert
        var page = await ReadListAsync(second, cancellationToken);
        Assert.Equal((SamplePath, 3, 2, 2), (Assert.Single(page.Items).AppPath, page.TotalCount, page.Page, page.PageSize));
        var empty = await ReadListAsync(pastTheEnd, cancellationToken);
        Assert.Equal((0, 3, 3, 1), (empty.Items.Count, empty.TotalCount, empty.CountedCount, empty.ProblemCount));
    }

    [Fact]
    public async Task List_says_the_library_folders_are_unknown_while_no_sync_stored_one()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await new AllowedRootSeeder(_factory.Scope).Allow(Root);
        _factory.Files.AddFile(BirthdayPath, TestMedia.File(inode: 103));
        await _factory.ScanFoldersAsync(_owner.AccessToken, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        var file = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        // Not looked at yet either: no inspection ran.
        Assert.Equal((new UnreportedFilePlaceDto(UnreportedFilePlaceKind.FoldersUnknown, null), (LibraryFileInspectionDto?)null, (long?)null), (file.Place, file.Inspection, file.SizeBytes));
    }

    [Fact]
    public async Task List_leaves_out_a_scanned_file_once_a_sync_reports_its_path()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = new FakeJellyfinServer { ApiKey = ApiKey };
        jellyfin.Arm(_factory.JellyfinHttp);
        var livingRoom = await new IntegrationConnectionSeeder(_factory.Scope).Create("jellyfin", "Living room", apiKey: ApiKey);
        await new AllowedRootSeeder(_factory.Scope).Allow(Root);
        _factory.Files.AddFile(SamplePath, TestMedia.File(inode: 111));
        _factory.Files.AddFile(BirthdayPath, TestMedia.File(inode: 103));
        await _factory.ScanFoldersAsync(_owner.AccessToken, cancellationToken);
        jellyfin.Movies.Add(JellyfinSamples.MovieWith(new string('d', 32), "Дюна", "Dune", 2021, SamplePath, "tt1160419", "438631"));
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(livingRoom, _owner.User);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        // The Media folders row's rule: the library's name answers for the file at once, without a rescan.
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal([BirthdayPath], list.Items.Select(file => file.AppPath));
        Assert.Equal(1, list.CountedCount);
    }

    [Theory]
    [InlineData("?view=Duplicates", "“Duplicates” is not a view of the files in no library. Use Problems, Extras or Ignored, or leave it out for every file.")]
    [InlineData("?view=1", "“1” is not a view of the files in no library. Use Problems, Extras or Ignored, or leave it out for every file.")]
    [InlineData("?page=0", "Page must be 1 or more.")]
    [InlineData("?pageSize=101", "Page size must be between 1 and 100.")]
    public async Task List_with_a_view_or_page_it_doesnt_take_is_400_with_the_sentence(string query, string error)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl + query, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal($$"""{"error":"{{error}}"}""", await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task List_without_a_token_is_401()
    {
        //Arrange

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl);

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Ignore_takes_the_file_off_the_media_folders_counts_and_lists_it_under_ignored_with_an_audit_row()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ScanAndInspectAsync(cancellationToken);
        var before = (await ReadSourcesAsync(cancellationToken)).Scan;
        var iso = await FileAtAsync(IsoPath, cancellationToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(3));
        var ignoredUtc = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var response = await _factory.PutHttpRequest($"{BaseUrl}/{iso.Id}/ignored", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal((new LibrarySourceInspectionDto(2, 1, 0, IsoError), 0), (before.Files, before.IgnoredCount));
        var after = (await ReadSourcesAsync(cancellationToken)).Scan;
        // Off the counts and the problem sentence at once; still in the room the files take, which it still takes.
        Assert.Equal((new LibrarySourceInspectionDto(2, 0, 0, null), 1, before.Space), (after.Files, after.IgnoredCount, after.Space));
        var counted = await ReadListAsync(await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken), cancellationToken);
        Assert.Equal([BirthdayPath, SamplePath], counted.Items.Select(file => file.AppPath));
        Assert.Equal((2, 0, 1), (counted.CountedCount, counted.ProblemCount, counted.IgnoredCount));
        var ignored = await ReadListAsync(await _factory.GetHttpRequest($"{BaseUrl}?view=Ignored", _member.AccessToken), cancellationToken);
        Assert.Equal((IsoPath, (DateTime?)ignoredUtc), (Assert.Single(ignored.Items).AppPath, ignored.Items[0].IgnoredUtc));
        var audit = Assert.Single(await AuditAsync(cancellationToken));
        Assert.Equal(
            (AuditAction.UnreportedFileIgnored, _owner.User.Id, "owner", (AuditTargetKind?)AuditTargetKind.UnreportedFile, (Guid?)iso.Id, "Adobe Premiere Rush 2.10.0.30.iso"),
            (audit.Action, audit.ActorUserId!.Value, audit.ActorUserName, audit.TargetKind, audit.TargetId, audit.TargetName));
        // The app's own note: the row stays, and nothing on the disk was touched.
        var row = await FileAtAsync(IsoPath, cancellationToken);
        Assert.Equal((iso.Id, (DateTime?)ignoredUtc, FileInspectionStatus.MediaUnreadable), (row.Id, row.IgnoredUtc, row.InspectionStatus!.Value));
        Assert.Equal(FileInventoryStatus.Found, (await _factory.Files.DescribeAsync(IsoPath, [Root], cancellationToken)).Status);
    }

    [Fact]
    public async Task Ignore_twice_answers_204_both_times_and_records_one_audit_row()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ScanAndInspectAsync(cancellationToken);
        var iso = await FileAtAsync(IsoPath, cancellationToken);
        var first = await _factory.PutHttpRequest($"{BaseUrl}/{iso.Id}/ignored", _owner.AccessToken);
        var ignoredUtc = (await FileAtAsync(IsoPath, cancellationToken)).IgnoredUtc;
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var second = await _factory.PutHttpRequest($"{BaseUrl}/{iso.Id}/ignored", _owner.AccessToken);

        //Assert
        Assert.Equal((HttpStatusCode.NoContent, HttpStatusCode.NoContent), (first.StatusCode, second.StatusCode));
        // The first time stands.
        Assert.Equal(ignoredUtc, (await FileAtAsync(IsoPath, cancellationToken)).IgnoredUtc);
        Assert.Single(await AuditAsync(cancellationToken));
    }

    [Fact]
    public async Task UndoIgnore_counts_the_file_again_with_an_audit_row_and_records_nothing_for_a_file_that_counts()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ScanAndInspectAsync(cancellationToken);
        var iso = await FileAtAsync(IsoPath, cancellationToken);
        var birthday = await FileAtAsync(BirthdayPath, cancellationToken);
        await _factory.PutHttpRequest($"{BaseUrl}/{iso.Id}/ignored", _owner.AccessToken);
        // Later, so the audit rows' order is the clock's.
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var undo =await _factory.DeleteHttpRequest($"{BaseUrl}/{iso.Id}/ignored", _owner.AccessToken);
        var never = await _factory.DeleteHttpRequest($"{BaseUrl}/{birthday.Id}/ignored", _owner.AccessToken);

        //Assert
        Assert.Equal((HttpStatusCode.NoContent, HttpStatusCode.NoContent), (undo.StatusCode, never.StatusCode));
        var scan = (await ReadSourcesAsync(cancellationToken)).Scan;
        Assert.Equal((new LibrarySourceInspectionDto(2, 1, 0, IsoError), 0), (scan.Files, scan.IgnoredCount));
        Assert.Null((await FileAtAsync(IsoPath, cancellationToken)).IgnoredUtc);
        Assert.Equal(
            [(AuditAction.UnreportedFileIgnored, (Guid?)iso.Id), (AuditAction.UnreportedFileIgnoreUndone, iso.Id)],
            (await AuditAsync(cancellationToken)).Select(audit => (audit.Action, audit.TargetId)));
    }

    [Fact]
    public async Task Ignore_or_UndoIgnore_of_an_id_no_scanned_name_has_is_a_bare_404_and_writes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        new FakeJellyfinServer { ApiKey = ApiKey }.Arm(_factory.JellyfinHttp);
        var livingRoom = await new IntegrationConnectionSeeder(_factory.Scope).Create("jellyfin", "Living room", apiKey: ApiKey);
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(livingRoom, _owner.User);
        Guid librarysFile = default;
        await _factory.WithDbAsync(async db =>
            librarysFile = (await db.PhysicalFiles.AsNoTracking().FirstAsync(file => file.MediaVersionId != null, cancellationToken)).Id);

        //Act
        var answers = new List<HttpResponseMessage>
        {
            await _factory.PutHttpRequest($"{BaseUrl}/{Guid.NewGuid()}/ignored", _owner.AccessToken),
            await _factory.DeleteHttpRequest($"{BaseUrl}/{Guid.NewGuid()}/ignored", _owner.AccessToken),
            // A library's file is never ignored: the same answer as none, which doesn't confirm it exists.
            await _factory.PutHttpRequest($"{BaseUrl}/{librarysFile}/ignored", _owner.AccessToken),
        };

        //Assert
        foreach (var answer in answers)
        {
            Assert.Equal(HttpStatusCode.NotFound, answer.StatusCode);
            Assert.Equal(string.Empty, await answer.Content.ReadAsStringAsync(cancellationToken));
        }

        Assert.Empty(await AuditAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
            Assert.Equal(0, await db.PhysicalFiles.CountAsync(file => file.IgnoredUtc != null, cancellationToken)));
    }

    [Fact]
    public async Task Ignore_as_a_member_is_403_and_leaves_the_file_counted()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ScanAndInspectAsync(cancellationToken);
        var iso = await FileAtAsync(IsoPath, cancellationToken);

        //Act
        var response = await _factory.PutHttpRequest($"{BaseUrl}/{iso.Id}/ignored", _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("""{"error":"Only the installation owner can do this."}""", await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Null((await FileAtAsync(IsoPath, cancellationToken)).IgnoredUtc);
        Assert.Empty(await AuditAsync(cancellationToken));
    }

    [Fact]
    public async Task Scan_again_keeps_an_ignored_file_ignored_and_a_moved_one_counts_again_under_its_new_path()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ScanAndInspectAsync(cancellationToken);
        var iso = await FileAtAsync(IsoPath, cancellationToken);
        await _factory.PutHttpRequest($"{BaseUrl}/{iso.Id}/ignored", _owner.AccessToken);
        await _factory.ScanFoldersAsync(_owner.AccessToken, cancellationToken);
        var kept = await FileAtAsync(IsoPath, cancellationToken);
        _factory.Files.Move(IsoPath, MovedIsoPath);

        //Act
        await _factory.ScanFoldersAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // The second scan kept the row, and the mark on it (ADR 0021).
        Assert.Equal((iso.Id, true), (kept.Id, kept.IgnoredUtc is not null));
        var list = await ReadListAsync(await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken), cancellationToken);
        Assert.Equal([BirthdayPath, SamplePath, MovedIsoPath], list.Items.Select(file => file.AppPath));
        Assert.Equal(0, list.IgnoredCount);
        await _factory.WithDbAsync(async db =>
        {
            Assert.False(await db.PhysicalFiles.AnyAsync(file => file.ServicePath == IsoPath, cancellationToken));
            Assert.Null((await db.PhysicalFiles.AsNoTracking().SingleAsync(file => file.ServicePath == MovedIsoPath, cancellationToken)).IgnoredUtc);
        });
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>A Jellyfin library Фільми whose one folder is /media/movies, as a sync stores it, with no item.</summary>
    private async Task SeedLibraryFolderAsync()
    {
        var livingRoom = await new IntegrationConnectionSeeder(_factory.Scope).Create("jellyfin", "Living room", apiKey: ApiKey);
        await new LibraryLocationSeeder(_factory.Scope).Create(livingRoom, "Фільми", "/media/movies");
    }

    /// <summary>
    /// The ISO (no media: ffprobe can't read it), the birthday video and Dune's sample in the media folders, scanned and
    /// inspected; when the scan stored them.
    /// </summary>
    private async Task<DateTime> ScanAndInspectAsync(CancellationToken cancellationToken)
    {
        await new AllowedRootSeeder(_factory.Scope).Allow(Root);
        _factory.Files.AddFile(IsoPath, TestMedia.File(inode: 101, sizeBytes: IsoSizeBytes));
        _factory.Files.AddFile(BirthdayPath, TestMedia.File(inode: 103), TestMedia.Continental());
        _factory.Files.AddFile(SamplePath, TestMedia.File(inode: 111), TestMedia.Taken());
        var scannedUtc = _factory.Time.GetUtcNow().UtcDateTime;
        await _factory.ScanFoldersAsync(_owner.AccessToken, cancellationToken);
        await _factory.RunQueuedInspectionsAsync(cancellationToken);
        return scannedUtc;
    }

    /// <summary>
    /// MAM-44 — Фільми's folder holding the Alien short in a Shorts folder, a trailer ffprobe can't read and a sample,
    /// with the birthday video outside every library folder: scanned and inspected.
    /// </summary>
    private async Task ScanExtrasAsync(CancellationToken cancellationToken)
    {
        await SeedLibraryFolderAsync();
        await new AllowedRootSeeder(_factory.Scope).Allow(Root);
        _factory.Files.AddFile(ShortPath, TestMedia.File(inode: 121), TestMedia.Taken());
        _factory.Files.AddFile(TrailerPath, TestMedia.File(inode: 122));
        _factory.Files.AddFile(ExtraSamplePath, TestMedia.File(inode: 123), TestMedia.Taken());
        _factory.Files.AddFile(BirthdayPath, TestMedia.File(inode: 103), TestMedia.Continental());
        await _factory.ScanFoldersAsync(_owner.AccessToken, cancellationToken);
        await _factory.RunQueuedInspectionsAsync(cancellationToken);
    }

    private async Task<PhysicalFileEntity> FileAtAsync(string path, CancellationToken cancellationToken)
    {
        PhysicalFileEntity? row = null;
        await _factory.WithDbAsync(async db => row = await db.PhysicalFiles.AsNoTracking().SingleAsync(file => file.ServicePath == path, cancellationToken));
        return row!;
    }

    /// <summary>The audit rows in the order they were recorded.</summary>
    private async Task<List<AuditEventEntity>> AuditAsync(CancellationToken cancellationToken)
    {
        var rows = new List<AuditEventEntity>();
        await _factory.WithDbAsync(async db => rows.AddRange(
            (await db.AuditEvents.AsNoTracking().ToListAsync(cancellationToken)).OrderBy(auditEvent => auditEvent.CreatedUtc)));
        return rows;
    }

    private async Task<LibrarySourceListResponse> ReadSourcesAsync(CancellationToken cancellationToken)
    {
        var response = await _factory.GetHttpRequest(SourcesUrl, _owner.AccessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LibrarySourceListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }

    private static async Task<UnreportedFileListResponse> ReadListAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<UnreportedFileListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }
}
