using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Files;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using MediaAcquisitionManager.TestInfrastructure.Texts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Library;

// MAM-19 — the last step of a file inspection run (ADR 0022, Vasyl's decision 1): the files a byte-identical
// copy could hide among are read whole for their SHA-256 through the file inventory, inside the allowed roots
// in the owner's order, one at a time (Vasyl's decision 3), and each hash is kept under the key the read came
// with — the file's identity, size and modification time — so two copies keep two rows with one hash. What the
// step did is on the run (GET /api/library/inspections/{id}, and the Library page's sources): how many files
// needed a hash, how many it dealt with, read and couldn't hash, counted as it goes, with the first one's
// sentence for the owner only. A file it couldn't hash, or that changed while it was read, is never a failure
// of the run and leaves nothing kept; a file that changed after the look is kept under its new key, which
// counts from the next run; the log line gives counts, never a path. Each run is the owner's Inspect files
// over HTTP. Which files need a hash is FileHashCandidatesHttpTests'; what one run keeps for the next
// FileHashCacheHttpTests'; a run stopped or broken while hashing FileHashWorkerHttpTests'; the room the files
// take LibrarySpaceHttpTests'.
public sealed class FileInspectionHashHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library/inspections";
    private const string SourcesUrl = "/api/library/sources";
    private const string Root = "/media";

    // In path order — the order a run hashes in — the backup first, the film the library holds, a spare last.
    private const string CopyPath = "/media/backup/Taken (2008)/Taken (2008) BDRemux 1080p.mkv";
    private const string TakenPath = "/media/movies/Taken (2008)/Taken (2008) BDRemux 1080p.mkv";
    private const string SparePath = "/media/usb/Taken (2008) BDRemux 1080p.mkv";
    private const string ContinentalPath = "/media/tv/The Continental (2023)/Season 1/The Continental S01E01.mkv";
    private const long CopyInode = 1_048_579;
    private const long TakenInode = 1_048_577;
    private const long SpareInode = 1_048_581;
    private const long ContinentalInode = 1_048_578;
    private const long TakenSizeBytes = 30_870_249_472;

    // Spelled out rather than read from the product code: these are what the owner reads on the page.
    private const string TakenLockedError = "The app may not read “/media/movies/Taken (2008)/Taken (2008) BDRemux 1080p.mkv”: permission denied.";

    private const string SpareLockedError = "The app may not read “/media/usb/Taken (2008) BDRemux 1080p.mkv”: permission denied.";

    private const string CopyGoneError = "Nothing is at “/media/backup/Taken (2008)/Taken (2008) BDRemux 1080p.mkv”.";

    private const string ChangedWhileHashingError =
        "“/media/movies/Taken (2008)/Taken (2008) BDRemux 1080p.mkv” changed while the app was reading it for its hash, so the hash can't be said to belong to either version.";

    private const string ChangedSinceLookError =
        "A file changed after this inspection looked at it, so its hash counts from the next inspection.";

    private const string IdentityUnknownError =
        "This platform gives files no identity (device and inode), so the app can't keep what it reads from them.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IAllowedRootSeeder _roots = null!;
    private IMediaWorkDataFactory _works = null!;
    private IMediaVersionDataFactory _versions = null!;
    private IPhysicalFileDataFactory _physicalFiles = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;

    public FileInspectionHashHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _roots = new AllowedRootSeeder(_factory.Scope);
        var db = _factory.Scope.GetRequiredService<ApplicationDbContext>();
        _works = new MediaWorkDataFactory(db, _factory.Time);
        _versions = new MediaVersionDataFactory(db, _factory.Time);
        _physicalFiles = new PhysicalFileDataFactory(db, _factory.Time);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Inspect_hashes_two_files_of_one_size_once_each_and_keeps_each_hash_under_its_key()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        await PlantFileAsync(CopyPath);
        await PlantFileAsync(TakenPath);
        var copy = AddTaken(CopyPath, CopyInode);
        var taken = AddTaken(TakenPath, TakenInode);
        var now = Now();

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{run.Id}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Two files of one size whose media facts agree: each read whole once, as the last step of the run.
        Assert.Equal(
            new FileInspectionRunDto(
                run.Id, FileInspectionTrigger.Requested, FileInspectionRunStatus.Succeeded, "owner", now, now, now, 2, 2, 2,
                new FileHashingDto(2, 2, 2, 0, null), null),
            await ReadRunAsync(response, cancellationToken));
        Assert.Equal([CopyPath, TakenPath], _factory.Files.HashedPaths);
        // Each under its own key to the tick, with what the inventory read: SHA-256 in lowercase hex, as
        // sha256sum prints it.
        var hashes = await HashesAsync(cancellationToken);
        Assert.Equal([StoredHash.Of(taken, now), StoredHash.Of(copy, now)], hashes);
        Assert.All(hashes, hash => Assert.Matches("^[0-9a-f]{64}$", hash.Sha256));
        Assert.Equal(((int?)2, 2, 2, 0, (string?)null), HashingOf(await RunRowAsync(run.Id, cancellationToken)));
        // What the page reads, by these names.
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))!;
        Assert.Equal(["candidates", "checked", "read", "problemCount", "problem"], body["hashing"]!.AsObject().Select(property => property.Key));
    }

    [Fact]
    public async Task Inspect_of_a_byte_identical_copy_keeps_two_hashes_with_the_same_sha256()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        await PlantFileAsync(CopyPath);
        await PlantFileAsync(TakenPath);
        var takenFacts = TestMedia.File(TakenInode, TakenSizeBytes);
        var copyFacts = TestMedia.File(CopyInode, TakenSizeBytes, TestMedia.ModifiedUtc.AddDays(3));
        var sha256 = FakeFileInventory.DefaultSha256(takenFacts);
        _factory.Files.AddFile(TakenPath, takenFacts, TestMedia.Taken());
        // Copied, not linked: another inode and a later write, every byte the same.
        _factory.Files.AddFile(CopyPath, copyFacts, TestMedia.Taken(), hash: FakeFileInventory.HashOf(copyFacts, sha256));
        var now = Now();

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((FileInspectionRunStatus.Succeeded, new FileHashingDto(2, 2, 2, 0, null)), (run.Status, run.Hashing));
        // One row per file, each under its own key: telling them apart as copies is MAM-20's grouping.
        Assert.Equal([StoredHash.Of(takenFacts, now, sha256), StoredHash.Of(copyFacts, now, sha256)], await HashesAsync(cancellationToken));
    }

    [Fact]
    public async Task Inspect_gives_the_hash_the_allowed_roots_in_the_owners_order()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Stored second but listed first: the order is the owner's (Position), not the rows'.
        var roots = new AllowedRootDataFactory(_factory.Scope.GetRequiredService<ApplicationDbContext>());
        await roots.Create("/srv/media", position: 1);
        await roots.Create(Root, position: 0);
        await PlantFileAsync(TakenPath);
        await PlantFileAsync("/srv/media/movies/Taken (2008).mkv");
        AddTaken(TakenPath, TakenInode);
        AddTaken("/srv/media/movies/Taken (2008).mkv", CopyInode);

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((FileInspectionRunStatus.Succeeded, new FileHashingDto(2, 2, 2, 0, null)), (run.Status, run.Hashing));
        // Two hashes, each given the roots as they were when the run started: the inventory reads inside them only.
        Assert.Equal(2, _factory.Files.HashRootsGiven.Count);
        Assert.All(_factory.Files.HashRootsGiven, given => Assert.Equal([Root, "/srv/media"], given));
        Assert.Equal(2, (await HashesAsync(cancellationToken)).Count);
    }

    [Fact]
    public async Task Inspect_hashes_exactly_one_file_at_a_time()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        foreach (var (path, inode) in new[] { (CopyPath, CopyInode), (TakenPath, TakenInode), (SparePath, SpareInode) })
        {
            await PlantFileAsync(path);
            AddTaken(path, inode);
        }

        // The first hash waits for another to start: a bound above one would put two under way for certain.
        _factory.Files.DuringHash = HoldTheFirstForAnother();

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((FileInspectionRunStatus.Succeeded, new FileHashingDto(3, 3, 3, 0, null)), (run.Status, run.Hashing));
        // Vasyl's decision 3: a hash reads a file from its first byte to its last, and two such reads on one
        // spinning disk make its head jump between them. Never two at once, in path order.
        Assert.Equal(1, FileInspectionRunner.MaximumConcurrentHashes);
        Assert.Equal(FileInspectionRunner.MaximumConcurrentHashes, _factory.Files.MostHashesAtOnce);
        Assert.Equal([CopyPath, TakenPath, SparePath], _factory.Files.HashedPaths);
        Assert.Equal(3, (await HashesAsync(cancellationToken)).Count);
    }

    [Fact]
    public async Task Inspect_reports_its_hashing_progress_while_it_hashes()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        foreach (var (path, inode) in new[] { (CopyPath, CopyInode), (TakenPath, TakenInode), (SparePath, SpareInode) })
        {
            await PlantFileAsync(path);
            AddTaken(path, inode);
        }

        // What the Library page reads while a file is hashed: a fresh context, as another request has, and
        // the page itself once, while the second is read.
        var seen = new List<(FileInspectionRunStatus Status, int? Candidates, int Checked, int Read)>();
        HttpStatusCode? pageStatus = null;
        FileInspectionRunDto? onThePage = null;
        _factory.Files.DuringHash = async (path, token) =>
        {
            await _factory.WithDbAsync(async db =>
            {
                var row = await db.FileInspectionRuns.AsNoTracking().SingleAsync(token);
                seen.Add((row.Status, row.HashCandidates, row.HashCandidatesChecked, row.HashCandidatesRead));
            });
            if (path != TakenPath)
                return;

            using var page = await _factory.GetHttpRequest(SourcesUrl, _owner.AccessToken);
            pageStatus = page.StatusCode;
            onThePage = (await page.Content.ReadFromJsonAsync<LibrarySourceListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, token))!
                .Inspection.ActiveRun;
        };
        var now = Now();

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // The candidates are counted before the first is read, and each one dealt with counts at once.
        Assert.Equal(
            [(FileInspectionRunStatus.Running, (int?)3, 0, 0), (FileInspectionRunStatus.Running, (int?)3, 1, 1), (FileInspectionRunStatus.Running, (int?)3, 2, 2)],
            seen);
        // The page shows "1 of 3 hashed" in the run's own row while it goes, every file already looked at.
        Assert.Equal(HttpStatusCode.OK, pageStatus);
        Assert.Equal(
            new FileInspectionRunDto(
                run.Id, FileInspectionTrigger.Requested, FileInspectionRunStatus.Running, "owner", now, now, null, 3, 3, 3,
                new FileHashingDto(3, 1, 1, 0, null), null),
            onThePage);
        Assert.Equal((FileInspectionRunStatus.Succeeded, new FileHashingDto(3, 3, 3, 0, null)), (run.Status, run.Hashing));
    }

    [Fact]
    public async Task Inspect_of_a_file_that_cant_be_hashed_beside_a_good_one_succeeds_and_gives_its_sentence_to_the_owner_only()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        var copyName = await PlantFileAsync(CopyPath);
        var takenName = await PlantFileAsync(TakenPath);
        var copy = AddTaken(CopyPath, CopyInode);
        // Looked at and read by the media inspector, then locked before the whole file could be read.
        AddTaken(TakenPath, TakenInode, new FileInventoryResult<FileContentHash>(FileInventoryStatus.Unreadable, null, Message.Legacy(TakenLockedError)));
        var now = Now();

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        var members = await _factory.GetHttpRequest($"{BaseUrl}/{run.Id}", _member.AccessToken);
        var ownersPage = await _factory.GetHttpRequest(SourcesUrl, _owner.AccessToken);
        var membersPage = await _factory.GetHttpRequest(SourcesUrl, _member.AccessToken);

        //Assert
        // One file the app can't read whole is a count and a sentence, never the run's failure.
        Assert.Equal(
            (FileInspectionRunStatus.Succeeded, (string?)null, new FileHashingDto(2, 2, 1, 1, TakenLockedError)),
            (run.Status, run.Error, run.Hashing));
        Assert.Equal([CopyPath, TakenPath], _factory.Files.HashedPaths);
        Assert.Equal([StoredHash.Of(copy, now)], await HashesAsync(cancellationToken));
        Assert.Equal(((int?)2, 2, 1, 1, (string?)TakenLockedError), HashingOf(await RunRowAsync(run.Id, cancellationToken)));
        // The sentence quotes a path in the owner's media folders: the owner's, like a file's own problem; the
        // counts are everyone's.
        Assert.Equal(HttpStatusCode.OK, members.StatusCode);
        Assert.Equal(new FileHashingDto(2, 2, 1, 1, null), (await ReadRunAsync(members, cancellationToken)).Hashing);
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (ownersPage.StatusCode, membersPage.StatusCode));
        Assert.Equal(new FileHashingDto(2, 2, 1, 1, TakenLockedError), (await ReadListAsync(ownersPage, cancellationToken)).Inspection.LastFinishedRun!.Hashing);
        Assert.Equal(new FileHashingDto(2, 2, 1, 1, null), (await ReadListAsync(membersPage, cancellationToken)).Inspection.LastFinishedRun!.Hashing);
        Assert.DoesNotContain(Root, await membersPage.Content.ReadAsStringAsync(cancellationToken), StringComparison.Ordinal);
        // The hash is the run's last step, not the file's inspection: both names keep what the inspector read.
        await _factory.WithDbAsync(async db =>
            Assert.Equal(
                [FileInspectionStatus.Inspected, FileInspectionStatus.Inspected],
                await db.PhysicalFiles
                    .Where(file => file.Id == copyName.Id || file.Id == takenName.Id)
                    .Select(file => file.InspectionStatus!.Value)
                    .ToListAsync(cancellationToken)));
    }

    [Fact]
    public async Task Inspect_keeps_the_first_hashing_problem_in_path_order()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        await PlantFileAsync(SparePath);
        await PlantFileAsync(TakenPath);
        await PlantFileAsync(CopyPath);
        // Two can't be hashed: the backup, gone since the look, comes before the locked spare in path order.
        AddTaken(CopyPath, CopyInode, new FileInventoryResult<FileContentHash>(FileInventoryStatus.NotFound, null, Message.Legacy(CopyGoneError)));
        var taken = AddTaken(TakenPath, TakenInode);
        AddTaken(SparePath, SpareInode, new FileInventoryResult<FileContentHash>(FileInventoryStatus.Unreadable, null, Message.Legacy(SpareLockedError)));
        var now = Now();

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // Both counted; the first one's sentence is the one named, so the page reads the same on every poll.
        Assert.Equal((FileInspectionRunStatus.Succeeded, new FileHashingDto(3, 3, 1, 2, CopyGoneError)), (run.Status, run.Hashing));
        Assert.Equal([CopyPath, TakenPath, SparePath], _factory.Files.HashedPaths);
        Assert.Equal([StoredHash.Of(taken, now)], await HashesAsync(cancellationToken));
        Assert.Equal(((int?)3, 3, 1, 2, (string?)CopyGoneError), HashingOf(await RunRowAsync(run.Id, cancellationToken)));
    }

    [Fact]
    public async Task Inspect_of_a_hash_the_inventory_gives_no_reason_for_counts_the_apps_own()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        await PlantFileAsync(CopyPath);
        await PlantFileAsync(TakenPath);
        AddTaken(CopyPath, CopyInode);
        AddTaken(TakenPath, TakenInode, new FileInventoryResult<FileContentHash>(FileInventoryStatus.Unreadable, null, null));

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // Counted with a reason the owner can read, never an empty one.
        Assert.Equal(
            (FileInspectionRunStatus.Succeeded, new FileHashingDto(2, 2, 1, 1, "The app couldn't read the file.")),
            (run.Status, run.Hashing));
    }

    [Fact]
    public async Task Inspect_cuts_a_hashing_problem_longer_than_its_column_with_an_ellipsis()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        await PlantFileAsync(CopyPath);
        await PlantFileAsync(TakenPath);
        AddTaken(CopyPath, CopyInode);
        // An inventory that says far more than a column holds (1,000 characters).
        var sentence = $"The app may not read “{TakenPath}”:" + string.Concat(Enumerable.Repeat(" permission denied.", 60));
        AddTaken(TakenPath, TakenInode, new FileInventoryResult<FileContentHash>(FileInventoryStatus.Unreadable, null, Message.Legacy(sentence)));

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.True(sentence.Length > FileInspectionRunEntity.ErrorMaximumLength);
        // MAM-47 (ADR 0042) — stored as key and values: the column's converter fits the message into its 1,000
        // characters by cutting its longest value, which ends with an ellipsis, so the owner sees it was cut.
        var shown = run.Hashing.Problem!;
        Assert.Equal((FileInspectionRunStatus.Succeeded, new FileHashingDto(2, 2, 1, 1, shown)), (run.Status, run.Hashing));
        Assert.EndsWith("…", shown, StringComparison.Ordinal);
        Assert.StartsWith(shown[..^1], sentence, StringComparison.Ordinal);
        Assert.True(shown.Length < sentence.Length);
        Assert.Equal(shown, TestTexts.En((await RunRowAsync(run.Id, cancellationToken)).HashProblem));
        await _factory.WithDbAsync(async db =>
        {
            var stored = await db.Database.SqlQueryRaw<string>("SELECT \"HashProblem\" AS \"Value\" FROM \"FileInspectionRuns\"").SingleAsync(cancellationToken);
            Assert.True(stored.Length <= FileInspectionRunEntity.ErrorMaximumLength, $"{stored.Length} characters stored");
        });
    }

    [Fact]
    public async Task Inspect_of_a_file_that_changed_while_it_was_hashed_keeps_nothing_and_the_next_run_hashes_it_again()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        await PlantFileAsync(CopyPath);
        await PlantFileAsync(TakenPath);
        var copy = AddTaken(CopyPath, CopyInode);
        // Written while it was read whole: what was read belongs to neither version.
        AddTaken(
            TakenPath,
            TakenInode,
            new FileInventoryResult<FileContentHash>(
                FileInventoryStatus.ChangedWhileReading, null, Message.Of("files.inventory.changed-while-hashing", ("path", TakenPath))));
        var changing = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        var changingHashes = await HashesAsync(cancellationToken);
        var firstUtc = Now();
        // Still since: the next run's look finds the same key, and the read agrees with it.
        var taken = AddTaken(TakenPath, TakenInode);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var now = Now();

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // The ticket's "done when": a file changing during hashing is not cached, and the run says so.
        Assert.Equal((FileInspectionRunStatus.Succeeded, new FileHashingDto(2, 2, 1, 1, ChangedWhileHashingError)), (changing.Status, changing.Hashing));
        Assert.Equal([StoredHash.Of(copy, firstUtc)], changingHashes);
        // So the next run reads it again, and keeps it now; the other is answered from the cache.
        Assert.Equal((FileInspectionRunStatus.Succeeded, new FileHashingDto(2, 2, 1, 0, null)), (run.Status, run.Hashing));
        Assert.Equal([CopyPath, TakenPath, TakenPath], _factory.Files.HashedPaths);
        Assert.Equal([StoredHash.Of(taken, now), StoredHash.Of(copy, firstUtc)], await HashesAsync(cancellationToken));
    }

    [Fact]
    public async Task Inspect_of_a_file_that_changed_between_the_look_and_the_hash_keeps_the_hash_under_the_reads_key_for_the_next_run()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        var takenName = await PlantFileAsync(TakenPath);
        await PlantFileAsync(CopyPath);
        var copy = AddTaken(CopyPath, CopyInode);
        var looked = TestMedia.File(TakenInode, TakenSizeBytes);
        // Rewritten in place after the look, and still while it was read whole: the inventory's facts agree
        // before and after the read, and they are the new bytes'.
        var rewritten = TestMedia.File(TakenInode, TakenSizeBytes, TestMedia.ModifiedUtc.AddSeconds(5));
        _factory.Files.AddFile(TakenPath, looked, TestMedia.Taken(), hash: FakeFileInventory.HashOf(rewritten, FakeFileInventory.DefaultSha256(rewritten)));
        var firstUtc = Now();
        var first = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        var firstHashes = await HashesAsync(cancellationToken);
        var lookedTicks = await ReadModifiedTicksAsync(takenName.Id, cancellationToken);
        // The next run's look sees the rewrite.
        _factory.Files.AddFile(TakenPath, rewritten, TestMedia.Taken());
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // Kept under the bytes it read, never under the key of the file the look saw; the name still carries
        // the look's key, so it has no hash until the next look — which the owner is told.
        Assert.Equal((FileInspectionRunStatus.Succeeded, new FileHashingDto(2, 2, 2, 1, ChangedSinceLookError)), (first.Status, first.Hashing));
        Assert.Equal([StoredHash.Of(rewritten, firstUtc), StoredHash.Of(copy, firstUtc)], firstHashes);
        Assert.Equal((long?)looked.ModifiedUtc.Ticks, lookedTicks);
        // The next run's look gives the new key: the hash is there already, and nothing is read whole again.
        Assert.Equal((FileInspectionRunStatus.Succeeded, 1, new FileHashingDto(2, 2, 0, 0, null)), (run.Status, run.FilesProbed, run.Hashing));
        Assert.Equal([CopyPath, TakenPath], _factory.Files.HashedPaths);
        Assert.Equal(firstHashes, await HashesAsync(cancellationToken));
        Assert.Equal((long?)rewritten.ModifiedUtc.Ticks, await ReadModifiedTicksAsync(takenName.Id, cancellationToken));
    }

    [Fact]
    public async Task Inspect_of_two_names_of_one_file_looked_at_either_side_of_a_write_keeps_the_first_hash_of_the_new_bytes_and_succeeds()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        await PlantFileAsync(CopyPath);
        await PlantFileAsync(TakenPath);
        await PlantFileAsync(SparePath);
        // Two names of one file, the write landing between their looks: the first name's look saw the old
        // bytes, the second's the new, and both reads find the new. Each name is a candidate under its own key.
        var looked = TestMedia.File(TakenInode, TakenSizeBytes);
        var rewritten = TestMedia.File(TakenInode, TakenSizeBytes, TestMedia.ModifiedUtc.AddSeconds(5));
        _factory.Files.AddFile(CopyPath, looked, TestMedia.Taken(), hash: FakeFileInventory.HashOf(rewritten, FakeFileInventory.DefaultSha256(rewritten)));
        // Another digest for the same key, so which of the two stands shows.
        _factory.Files.AddFile(TakenPath, rewritten, TestMedia.Taken(), hash: FakeFileInventory.HashOf(rewritten, new string('e', 64)));
        var spare = AddTaken(SparePath, SpareInode);
        var now = Now();

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // The second store of the new bytes' key meets the unique index: the first hash stands, the run goes on
        // to the next file and succeeds, and only the name whose look was older counts a problem.
        Assert.Equal(
            (FileInspectionRunStatus.Succeeded, new FileHashingDto(3, 3, 3, 1, ChangedSinceLookError)),
            (run.Status, run.Hashing));
        Assert.Equal([CopyPath, TakenPath, SparePath], _factory.Files.HashedPaths);
        Assert.Equal([StoredHash.Of(rewritten, now), StoredHash.Of(spare, now)], await HashesAsync(cancellationToken));
    }

    [Fact]
    public async Task Inspect_of_a_hash_that_comes_without_an_identity_keeps_nothing_and_counts_a_problem()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        await PlantFileAsync(CopyPath);
        await PlantFileAsync(TakenPath);
        var copy = AddTaken(CopyPath, CopyInode);
        // The look knew the file's identity; the read whole came back without one.
        var withoutIdentity = new FileFacts(TakenSizeBytes, TestMedia.ModifiedUtc, null, null, 1);
        AddTaken(TakenPath, TakenInode, FakeFileInventory.HashOf(withoutIdentity, FakeFileInventory.DefaultSha256(withoutIdentity)));
        var now = Now();

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // Read, but an unknown identity beats a guessed one: nothing is kept under a key that can't tell two
        // files apart.
        Assert.Equal((FileInspectionRunStatus.Succeeded, new FileHashingDto(2, 2, 2, 1, IdentityUnknownError)), (run.Status, run.Hashing));
        Assert.Equal([StoredHash.Of(copy, now)], await HashesAsync(cancellationToken));
    }

    [Fact]
    public async Task Inspect_logs_how_many_files_it_hashed_and_never_a_path()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        foreach (var path in new[] { CopyPath, TakenPath, SparePath, ContinentalPath })
            await PlantFileAsync(path);
        AddTaken(CopyPath, CopyInode);
        AddTaken(TakenPath, TakenInode);
        AddTaken(SparePath, SpareInode, new FileInventoryResult<FileContentHash>(FileInventoryStatus.Unreadable, null, Message.Legacy(SpareLockedError)));
        // A size no other file has: looked at and read by the inspector, never hashed.
        _factory.Files.AddFile(ContinentalPath, TestMedia.File(ContinentalInode), TestMedia.Continental());

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((FileInspectionRunStatus.Succeeded, new FileHashingDto(3, 3, 2, 1, SpareLockedError)), (run.Status, run.Hashing));
        // Counts only: the app's log never names a path, which says how the owner's disks are laid out — the
        // sentence of the file it couldn't hash stays on the run, for the owner.
        var finished = Assert.Single(_factory.Logs.Entries, entry => entry.Category == typeof(FileInspectionRunner).FullName);
        Assert.Equal(
            (LogLevel.Information, $"File inspection {run.Id} finished: 4 of 4 files checked, 4 read by the media inspector; 3 files could be copies, 2 read whole for their hash, 1 not hashed."),
            (finished.Level, finished.Message));
        Assert.DoesNotContain(
            _factory.Logs.Entries,
            entry => entry.Category.StartsWith("MediaAcquisitionManager.", StringComparison.Ordinal)
                && entry.Message.Contains(Root, StringComparison.Ordinal));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private DateTime Now() => _factory.Time.GetUtcNow().UtcDateTime;

    // A movie with one version naming servicePath — what a sync stores for a file it reported.
    private async Task<PhysicalFileEntity> PlantFileAsync(string servicePath)
    {
        var work = await _works.Create();
        var version = await _versions.Create(work.Id, $"{Guid.NewGuid():N}");
        return await _physicalFiles.Create(version.Id, servicePath);
    }

    // A file at appPath of Taken's size whose media facts are Taken's — as far as anything but its bytes can
    // tell, a copy of the film — that hashes to the fake's own hash of its facts unless hash says otherwise.
    private FileFacts AddTaken(string appPath, long inode, FileInventoryResult<FileContentHash>? hash = null)
    {
        var facts = TestMedia.File(inode, TakenSizeBytes);
        _factory.Files.AddFile(appPath, facts, TestMedia.Taken(), hash: hash);
        return facts;
    }

    // Holds the first hash until another starts, or half a second passes; the others pass at once. With one
    // hash at a time none starts meanwhile, so it waits the half second; with more, two are under way.
    private static Func<string, CancellationToken, Task> HoldTheFirstForAnother()
    {
        var arrived = 0;
        var another = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return async (_, cancellationToken) =>
        {
            if (Interlocked.Increment(ref arrived) > 1)
            {
                another.TrySetResult();
                return;
            }

            await Task.WhenAny(another.Task, Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken));
        };
    }

    // Every stored hash, in inode order.
    private async Task<List<StoredHash>> HashesAsync(CancellationToken cancellationToken)
    {
        var hashes = new List<StoredHash>();
        await _factory.WithDbAsync(async db =>
            hashes = [.. (await db.FileHashes.AsNoTracking().ToListAsync(cancellationToken))
                .Select(StoredHash.Of)
                .OrderBy(hash => hash.Inode)
                .ThenBy(hash => hash.DeviceId)
                .ThenBy(hash => hash.ModifiedTicks)]);
        return hashes;
    }

    private async Task<FileInspectionRunEntity> RunRowAsync(Guid runId, CancellationToken cancellationToken)
    {
        FileInspectionRunEntity? run = null;
        await _factory.WithDbAsync(async db => run = await db.FileInspectionRuns.AsNoTracking().SingleAsync(row => row.Id == runId, cancellationToken));
        return run!;
    }

    // The modification time the last look read on a name, in ticks so a lost tick shows.
    private async Task<long?> ReadModifiedTicksAsync(Guid fileId, CancellationToken cancellationToken)
    {
        DateTime? modifiedUtc = null;
        await _factory.WithDbAsync(async db =>
            modifiedUtc = await db.PhysicalFiles.Where(file => file.Id == fileId).Select(file => file.ReadModifiedUtc).SingleAsync(cancellationToken));
        return modifiedUtc?.Ticks;
    }

    private static (int?, int, int, int, string?) HashingOf(FileInspectionRunEntity run) =>
        (run.HashCandidates, run.HashCandidatesChecked, run.HashCandidatesRead, run.HashProblemCount, TestTexts.En(run.HashProblem));

    private static async Task<FileInspectionRunDto> ReadRunAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<FileInspectionRunDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;

    private static async Task<LibrarySourceListResponse> ReadListAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<LibrarySourceListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;

    /// <summary>A stored hash: its key, the modification time in ticks so a lost tick shows, the hash and when
    /// it was read.</summary>
    private sealed record StoredHash(long DeviceId, long Inode, long SizeBytes, long ModifiedTicks, string Sha256, DateTime CreatedUtc)
    {
        public static StoredHash Of(FileHashEntity hash) =>
            new(hash.DeviceId, hash.Inode, hash.SizeBytes, hash.ModifiedUtc.Ticks, hash.Sha256, hash.CreatedUtc);

        /// <summary>What a run keeps for a file with <paramref name="facts"/> read at <paramref name="createdUtc"/>:
        /// the fake's own hash of the facts unless <paramref name="sha256"/> says otherwise.</summary>
        public static StoredHash Of(FileFacts facts, DateTime createdUtc, string? sha256 = null) =>
            new(facts.DeviceId!.Value, facts.Inode!.Value, facts.SizeBytes, facts.ModifiedUtc.Ticks, sha256 ?? FakeFileInventory.DefaultSha256(facts), createdUtc);
    }
}
