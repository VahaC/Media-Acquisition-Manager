using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediaAcquisitionManager.Api.Tests.Library;

// MAM-40 — the owner's Keep both (Keep all) of copies of one work and of byte-identical copies (PUT and DELETE
// /api/library/duplicates/{key}/decision with Dismissed; Vasyl's decisions of 2026-10-12): the group leaves every tab
// but the dismissed one ("Kept as they are"), where Undo brings it back; each change with an audit row naming the group
// by its first work. Their key stays when a file joins, so the decision stores the members kept and holds while every
// member the group has now is among them: a new file of a work in the group, or a new copy of the bytes, brings the
// group back undecided under the same key, a member leaving keeps it kept, and a new work changes the key. Copies are
// never confirmed, one file's names are never decided — 400 with a sentence each —, and both routes are the owner's
// alone. A possible match's decisions are LibraryDuplicateDecisionsHttpTests'.
public sealed class LibraryDuplicateKeepBothHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library/duplicates";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string Root = "/media";

    /// <summary>A second Jellyfin, beside the factory's default address.</summary>
    private const string BedroomBaseUrl = "http://127.0.0.2:9";

    /// <summary>A third, for a work that joins a group.</summary>
    private const string AtticBaseUrl = "http://127.0.0.3:9";

    /// <summary>A well-formed key no group has.</summary>
    private const string UnknownKey = "0123456789abcdef0123456789abcdef";

    private const string AlienImdbId = "tt0078748";
    private const string BrazilImdbId = "tt0088846";

    // The film's copies: the living room's, the bedroom's, and the files that join or leave them.
    private const string AlienPath = "/media/movies/Alien (1979)/Alien (1979).mkv";
    private const string AlienRemuxPath = "/media/movies/Alien (1979)/Alien (1979) Remux.mkv";
    private const string BedroomAlienPath = "/media/films/Alien (1979).mkv";
    private const string BedroomAlien2160Path = "/media/films/Alien (1979) 2160p.mkv";
    private const string AtticAlienPath = "/media/attic/Alien (1979).mkv";

    // Byte-identical copies, in path order: an archive, a backup, the library's film; and the film's second name.
    private const string ArchivePath = "/media/archive/Taken (2008) BDRemux 1080p.mkv";
    private const string CopyPath = "/media/backup/Taken (2008)/Taken (2008) BDRemux 1080p.mkv";
    private const string LinkPath = "/media/downloads/Taken (2008) BDRemux 1080p.mkv";
    private const string TakenPath = "/media/movies/Taken (2008)/Taken (2008) BDRemux 1080p.mkv";
    private const string UnreportedArchivePath = "/media/archive/Taken.2008.BDRemux.1080p.mkv";
    private const long TakenInode = 1_048_577;
    private const long CopyInode = 1_048_579;
    private const long ArchiveInode = 1_048_581;
    private const long TakenSizeBytes = 30_870_249_472;

    // Spelled out rather than read from the product code: these are what the owner reads on the page.
    private const string OwnerOnlyBody = """{"error":"Only the installation owner can do this."}""";
    private const string CopiesAreNotConfirmedError =
        "These files are copies by their bytes or registry ids, which no one has to confirm: keep them as they are, or remove a copy.";
    private const string OneFileError =
        "These are names of one file, not copies of it: removing a name frees nothing, so there is nothing to keep or remove.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private IAllowedRootSeeder _roots = null!;
    private IMediaWorkDataFactory _works = null!;
    private IMediaVersionDataFactory _versions = null!;
    private IPhysicalFileDataFactory _physicalFiles = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private IntegrationConnectionEntity _livingRoom = null!;
    private IntegrationConnectionEntity _bedroom = null!;

    public LibraryDuplicateKeepBothHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _roots = new AllowedRootSeeder(_factory.Scope);
        var db = _factory.Scope.GetRequiredService<ApplicationDbContext>();
        _works = new MediaWorkDataFactory(db, _factory.Time);
        _versions = new MediaVersionDataFactory(db, _factory.Time);
        _physicalFiles = new PhysicalFileDataFactory(db, _factory.Time);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        // Two Jellyfins over one disk, with no path mapping: each file's app path is the path its library gives.
        _livingRoom = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        _bedroom = await _integrations.Create("jellyfin", "Bedroom", BedroomBaseUrl, apiKey: ApiKey);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Keep_copies_of_one_work_is_204_and_lists_them_only_as_kept_with_the_decision_and_an_audit_row()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantCopiesAsync("Alien", 1979, AlienImdbId, AlienPath, BedroomAlienPath);
        await PlantCopiesAsync("Brazil", 1985, BrazilImdbId, "/media/movies/Brazil (1985)/Brazil (1985).mkv", "/media/films/Brazil (1985).mkv");
        var key = await KeyOfAsync(DuplicateClass.SameWork, "Alien", cancellationToken);
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var response = await KeepAsync(key, _owner.AccessToken);
        var all = await ListAsync("", _member.AccessToken, cancellationToken);
        var sameWork = await ListAsync("class=SameWork", _member.AccessToken, cancellationToken);
        var kept = await ListAsync("decision=Dismissed", _owner.AccessToken, cancellationToken);
        var keptForMembers = await ListAsync("class=SameWork&decision=dismissed", _member.AccessToken, cancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        // The members kept are stored by path, sorted, one per line: the group's key holds only its works.
        var row = await SingleDecisionAsync(cancellationToken);
        Assert.Equal(
            (key, DuplicateMatchDecision.Dismissed, (Guid?)_owner.User.Id, "owner", now, now, Kept(BedroomAlienPath, AlienPath)),
            (row.GroupKey, row.Decision, row.DecidedByUserId, row.DecidedByUserName, row.DecidedUtc, row.CreatedUtc, row.KeptMembers));
        // Its audit row targets the group by its key, named by its first work: a same-work group offers no title.
        var audit = Assert.Single(await AuditAsync(cancellationToken));
        Assert.Equal(
            (AuditAction.DuplicateCopiesKept, (Guid?)_owner.User.Id, "owner", (AuditTargetKind?)AuditTargetKind.DuplicateMatch, (Guid?)Guid.ParseExact(key, "N"), "Alien (1979)", now),
            (audit.Action, audit.ActorUserId, audit.ActorUserName, audit.TargetKind, audit.TargetId, audit.TargetName, audit.CreatedUtc));
        // Gone from every tab but "Kept as they are", the totals counting without it.
        Assert.Equal((1, 1), (all.TotalCount, sameWork.TotalCount));
        Assert.Equal(["Brazil"], all.Items.Select(TitleOf));
        Assert.Equal(["Brazil"], sameWork.Items.Select(TitleOf));
        var group = Assert.Single(kept.Items);
        Assert.Equal((1, key, DuplicateClass.SameWork, 2), (kept.TotalCount, group.Key, group.Class, group.Files.Count));
        Assert.Equal(new DuplicateDecisionDto(DuplicateMatchDecision.Dismissed, now, "owner"), group.Decision);
        // Everyone reads that it was kept and when, never by whom.
        Assert.Equal(new DuplicateDecisionDto(DuplicateMatchDecision.Dismissed, now, null), Assert.Single(keptForMembers.Items).Decision);
    }

    [Fact]
    public async Task Keep_byte_identical_copies_lists_them_as_kept_with_the_room_a_copy_takes()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantByteIdenticalCopiesAsync(cancellationToken);
        var key = await SingleKeyAsync(DuplicateClass.ByteIdentical, cancellationToken);

        //Act
        var response = await KeepAsync(key, _owner.AccessToken);
        var byteIdentical = await ListAsync("class=ByteIdentical", _owner.AccessToken, cancellationToken);
        var kept = await ListAsync("decision=Dismissed", _owner.AccessToken, cancellationToken);
        var keptByteIdentical = await ListAsync("class=ByteIdentical&decision=Dismissed", _owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0, byteIdentical.TotalCount);
        // The estimate is as sure under "Kept as they are" as on its own tab: both copies on ext4, one copy's room.
        foreach (var list in new[] { kept, keptByteIdentical })
        {
            var group = Assert.Single(list.Items);
            Assert.Equal((key, DuplicateClass.ByteIdentical, (DuplicateMatchDecision?)DuplicateMatchDecision.Dismissed), (group.Key, group.Class, group.Decision?.Decision));
            Assert.Equal(new DuplicateRecoverableSpaceDto(TakenSizeBytes, null), group.Recoverable);
        }

        Assert.Equal(Kept(CopyPath, TakenPath), (await SingleDecisionAsync(cancellationToken)).KeptMembers);
        // Named by the first work with a title: the backup's own item has none.
        var audit = Assert.Single(await AuditAsync(cancellationToken));
        Assert.Equal((AuditAction.DuplicateCopiesKept, "Taken (2008)"), (audit.Action, audit.TargetName));
    }

    [Fact]
    public async Task Keep_the_same_copies_again_is_204_and_writes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantCopiesAsync("Alien", 1979, AlienImdbId, AlienPath, BedroomAlienPath);
        var key = await KeyOfAsync(DuplicateClass.SameWork, "Alien", cancellationToken);
        var keptUtc = _factory.Time.GetUtcNow().UtcDateTime;
        await KeepAsync(key, _owner.AccessToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(5));

        //Act
        var response = await _factory.PutHttpRequest(DecisionUrl(key), _owner.AccessToken, new DuplicateDecisionRequest("dismissed"));

        //Assert
        // A double click or a retry: no row change, no second audit row.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(keptUtc, (await SingleDecisionAsync(cancellationToken)).DecidedUtc);
        Assert.Equal([AuditAction.DuplicateCopiesKept], (await AuditAsync(cancellationToken)).Select(audit => audit.Action));
    }

    [Fact]
    public async Task Undo_of_kept_copies_forgets_the_decision_with_an_audit_row_and_they_are_back_undecided()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantCopiesAsync("Alien", 1979, AlienImdbId, AlienPath, BedroomAlienPath);
        var key = await KeyOfAsync(DuplicateClass.SameWork, "Alien", cancellationToken);
        await KeepAsync(key, _owner.AccessToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(5));
        var undoneUtc = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var response = await _factory.DeleteHttpRequest(DecisionUrl(key), _owner.AccessToken);
        var all = await ListAsync("", _member.AccessToken, cancellationToken);
        var kept = await ListAsync("decision=Dismissed", _member.AccessToken, cancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await _factory.WithDbAsync(async db => Assert.Equal(0, await db.DuplicateMatchDecisions.CountAsync(cancellationToken)));
        var audit = await AuditAsync(cancellationToken);
        Assert.Equal([AuditAction.DuplicateCopiesKept, AuditAction.DuplicateCopiesKeptUndone], audit.Select(row => row.Action));
        Assert.Equal(
            ((Guid?)_owner.User.Id, (AuditTargetKind?)AuditTargetKind.DuplicateMatch, (Guid?)Guid.ParseExact(key, "N"), "Alien (1979)", undoneUtc),
            (audit[1].ActorUserId, audit[1].TargetKind, audit[1].TargetId, audit[1].TargetName, audit[1].CreatedUtc));
        // Back on the list, undecided, and off "Kept as they are".
        var group = Assert.Single(all.Items);
        Assert.Equal((key, DuplicateClass.SameWork, (DuplicateDecisionDto?)null), (group.Key, group.Class, group.Decision));
        Assert.Equal(0, kept.TotalCount);
    }

    [Fact]
    public async Task A_new_file_of_a_work_in_kept_copies_brings_them_back_undecided_under_the_same_key()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (_, bedroomAlien) = await PlantCopiesAsync("Alien", 1979, AlienImdbId, AlienPath, BedroomAlienPath);
        var key = await KeyOfAsync(DuplicateClass.SameWork, "Alien", cancellationToken);
        await KeepAsync(key, _owner.AccessToken);
        // The bedroom's item gets a 2160p version: a third copy of the same two works.
        await PlantVersionAsync(bedroomAlien, BedroomAlien2160Path);

        //Act
        var all = await ListAsync("", _member.AccessToken, cancellationToken);
        var kept = await ListAsync("decision=Dismissed", _member.AccessToken, cancellationToken);
        var undo = await _factory.DeleteHttpRequest(DecisionUrl(key), _owner.AccessToken);

        //Assert
        // Vasyl, 2026-10-12 — the owner kept two copies, not this third one: the group is asked about again, its key
        // unchanged, as a same-work key holds only the works.
        var group = Assert.Single(all.Items);
        Assert.Equal((key, 3, (DuplicateDecisionDto?)null), (group.Key, group.Files.Count, group.Decision));
        Assert.Equal(0, kept.TotalCount);
        // The kept row is ignored, never deleted: there is no decision in force to undo, so nothing is written.
        Assert.Equal(HttpStatusCode.NoContent, undo.StatusCode);
        Assert.Equal(Kept(BedroomAlienPath, AlienPath), (await SingleDecisionAsync(cancellationToken)).KeptMembers);
        Assert.Equal([AuditAction.DuplicateCopiesKept], (await AuditAsync(cancellationToken)).Select(audit => audit.Action));
    }

    [Fact]
    public async Task Keep_again_after_a_new_file_joined_stores_the_members_now_with_another_audit_row()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (_, bedroomAlien) = await PlantCopiesAsync("Alien", 1979, AlienImdbId, AlienPath, BedroomAlienPath);
        var key = await KeyOfAsync(DuplicateClass.SameWork, "Alien", cancellationToken);
        var createdUtc = _factory.Time.GetUtcNow().UtcDateTime;
        await KeepAsync(key, _owner.AccessToken);
        await PlantVersionAsync(bedroomAlien, BedroomAlien2160Path);
        _factory.Time.Advance(TimeSpan.FromMinutes(5));
        var keptAgainUtc = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var response = await KeepAsync(key, _owner.AccessToken);
        var kept = await ListAsync("decision=Dismissed", _owner.AccessToken, cancellationToken);

        //Assert
        // Keeping the group as it is now overwrites the row: its three members in ordinal order (a space sorts before a
        // dot), and when it was last kept.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var row = await SingleDecisionAsync(cancellationToken);
        Assert.Equal(
            (DuplicateMatchDecision.Dismissed, Kept(BedroomAlien2160Path, BedroomAlienPath, AlienPath), keptAgainUtc, createdUtc),
            (row.Decision, row.KeptMembers, row.DecidedUtc, row.CreatedUtc));
        Assert.Equal([AuditAction.DuplicateCopiesKept, AuditAction.DuplicateCopiesKept], (await AuditAsync(cancellationToken)).Select(audit => audit.Action));
        var group = Assert.Single(kept.Items);
        Assert.Equal((key, 3), (group.Key, group.Files.Count));
    }

    [Fact]
    public async Task Kept_copies_stay_kept_when_a_member_file_leaves()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Three copies: the living room's item with two versions, and the bedroom's.
        var (livingRoomAlien, _) = await PlantCopiesAsync("Alien", 1979, AlienImdbId, AlienPath, BedroomAlienPath);
        var remux = await PlantVersionAsync(livingRoomAlien, AlienRemuxPath);
        var key = await KeyOfAsync(DuplicateClass.SameWork, "Alien", cancellationToken);
        await KeepAsync(key, _owner.AccessToken);
        // The library stops reporting the remux, as a sync after it was deleted would: its work keeps another copy, so the
        // group keeps its key.
        await _factory.WithDbAsync(db => db.PhysicalFiles.Where(file => file.MediaVersionId == remux.Id).ExecuteDeleteAsync(cancellationToken));

        //Act
        var all = await ListAsync("", _member.AccessToken, cancellationToken);
        var kept = await ListAsync("decision=Dismissed", _member.AccessToken, cancellationToken);

        //Assert
        // Vasyl, 2026-10-12 — a file leaving keeps the decision: every copy left was among those kept.
        Assert.Equal(0, all.TotalCount);
        var group = Assert.Single(kept.Items);
        Assert.Equal((key, 2, (DuplicateMatchDecision?)DuplicateMatchDecision.Dismissed), (group.Key, group.Files.Count, group.Decision?.Decision));
        Assert.Equal(Kept(BedroomAlienPath, AlienRemuxPath, AlienPath), (await SingleDecisionAsync(cancellationToken)).KeptMembers);
    }

    [Fact]
    public async Task A_new_work_joining_kept_copies_gives_them_another_key_and_brings_them_back()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantCopiesAsync("Alien", 1979, AlienImdbId, AlienPath, BedroomAlienPath);
        var keptKey = await KeyOfAsync(DuplicateClass.SameWork, "Alien", cancellationToken);
        await KeepAsync(keptKey, _owner.AccessToken);
        // A third library reports the film under the same IMDb id.
        var attic = await _integrations.Create("jellyfin", "Attic", AtticBaseUrl, apiKey: ApiKey);
        await PlantMovieAsync(attic, "Alien", 1979, AtticAlienPath, AlienImdbId);

        //Act
        var all = await ListAsync("", _owner.AccessToken, cancellationToken);
        var keepOld = await KeepAsync(keptKey, _owner.AccessToken);
        var undoOld = await _factory.DeleteHttpRequest(DecisionUrl(keptKey), _owner.AccessToken);

        //Assert
        // ADR 0029 — the group of three works has a key of its own and is asked about again.
        var group = Assert.Single(all.Items);
        Assert.NotEqual(keptKey, group.Key);
        Assert.Equal((DuplicateClass.SameWork, 3, (DuplicateDecisionDto?)null), (group.Class, group.Files.Count, group.Decision));
        // The old key names no group now: a bare 404, and its row stays, matching nothing.
        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (keepOld.StatusCode, undoOld.StatusCode));
        Assert.Empty(await keepOld.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(keptKey, (await SingleDecisionAsync(cancellationToken)).GroupKey);
        Assert.Equal([AuditAction.DuplicateCopiesKept], (await AuditAsync(cancellationToken)).Select(audit => audit.Action));
    }

    [Fact]
    public async Task A_new_copy_joining_kept_byte_identical_copies_brings_them_back_undecided()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var sha256 = await PlantByteIdenticalCopiesAsync(cancellationToken);
        var key = await SingleKeyAsync(DuplicateClass.ByteIdentical, cancellationToken);
        await KeepAsync(key, _owner.AccessToken);
        // An archive copy of the same bytes turns up and is inspected.
        await PlantFileAsync(ArchivePath, title: null);
        var archiveFacts = TestMedia.File(ArchiveInode, TakenSizeBytes, TestMedia.ModifiedUtc.AddDays(5));
        _factory.Files.AddFile(ArchivePath, archiveFacts, TestMedia.Taken(), hash: FakeFileInventory.HashOf(archiveFacts, sha256));
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Act
        var byteIdentical = await ListAsync("class=ByteIdentical", _owner.AccessToken, cancellationToken);
        var kept = await ListAsync("decision=Dismissed", _owner.AccessToken, cancellationToken);

        //Assert
        // The key is the hash's, so it stays; the third copy wasn't among those kept.
        var group = Assert.Single(byteIdentical.Items);
        Assert.Equal((key, 3, (DuplicateDecisionDto?)null), (group.Key, group.Files.Count, group.Decision));
        Assert.Equal(new DuplicateRecoverableSpaceDto(2 * TakenSizeBytes, null), group.Recoverable);
        Assert.Equal(0, kept.TotalCount);
    }

    [Fact]
    public async Task Decide_confirmed_of_copies_is_400_with_the_sentence_and_writes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantCopiesAsync("Alien", 1979, AlienImdbId, AlienPath, BedroomAlienPath);
        await PlantByteIdenticalCopiesAsync(cancellationToken);
        var sameWorkKey = await KeyOfAsync(DuplicateClass.SameWork, "Alien", cancellationToken);
        var byteIdenticalKey = await SingleKeyAsync(DuplicateClass.ByteIdentical, cancellationToken);

        //Act
        var sameWork = await DecideAsync(sameWorkKey, "Confirmed", _owner.AccessToken);
        var byteIdentical = await DecideAsync(byteIdenticalKey, "confirmed", _owner.AccessToken);

        //Assert
        // One registry id or one hash already makes them copies: the owner keeps them or removes one, never confirms.
        Assert.Equal((HttpStatusCode.BadRequest, HttpStatusCode.BadRequest), (sameWork.StatusCode, byteIdentical.StatusCode));
        Assert.Equal(CopiesAreNotConfirmedError, await sameWork.ReadErrorAsync(cancellationToken));
        Assert.Equal(CopiesAreNotConfirmedError, await byteIdentical.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(cancellationToken);
    }

    [Fact]
    public async Task Decide_and_undo_of_one_files_names_are_400_with_the_sentence_and_write_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        // The download, linked into the movies folder: one file under two names.
        await PlantFileAsync(LinkPath, title: null);
        await PlantFileAsync(TakenPath, title: null);
        var facts = TestMedia.File(TakenInode, TakenSizeBytes, linkCount: 2);
        _factory.Files.AddFile(LinkPath, facts, TestMedia.Taken());
        _factory.Files.AddFile(TakenPath, facts, TestMedia.Taken());
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        var key = await SingleKeyAsync(DuplicateClass.SameFile, cancellationToken);

        //Act
        var keep = await KeepAsync(key, _owner.AccessToken);
        var confirm = await DecideAsync(key, "Confirmed", _owner.AccessToken);
        var undo = await _factory.DeleteHttpRequest(DecisionUrl(key), _owner.AccessToken);

        //Assert
        // Removing a name frees nothing: there is no copy to keep or remove.
        Assert.Equal(
            (HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest),
            (keep.StatusCode, confirm.StatusCode, undo.StatusCode));
        Assert.Equal(OneFileError, await keep.ReadErrorAsync(cancellationToken));
        Assert.Equal(OneFileError, await confirm.ReadErrorAsync(cancellationToken));
        Assert.Equal(OneFileError, await undo.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(cancellationToken);
    }

    [Fact]
    public async Task Keep_and_undo_as_a_member_are_403_with_the_owner_sentence_and_change_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantCopiesAsync("Alien", 1979, AlienImdbId, AlienPath, BedroomAlienPath);
        await PlantCopiesAsync("Brazil", 1985, BrazilImdbId, "/media/movies/Brazil (1985)/Brazil (1985).mkv", "/media/films/Brazil (1985).mkv");
        var alienKey = await KeyOfAsync(DuplicateClass.SameWork, "Alien", cancellationToken);
        var brazilKey = await KeyOfAsync(DuplicateClass.SameWork, "Brazil", cancellationToken);
        await KeepAsync(alienKey, _owner.AccessToken);

        //Act
        var keep = await KeepAsync(brazilKey, _member.AccessToken);
        var undo = await _factory.DeleteHttpRequest(DecisionUrl(alienKey), _member.AccessToken);

        //Assert
        // The owner's alone, as a possible match's decisions are.
        Assert.Equal((HttpStatusCode.Forbidden, HttpStatusCode.Forbidden), (keep.StatusCode, undo.StatusCode));
        Assert.Equal(OwnerOnlyBody, await keep.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(OwnerOnlyBody, await undo.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(alienKey, (await SingleDecisionAsync(cancellationToken)).GroupKey);
        Assert.Equal([AuditAction.DuplicateCopiesKept], (await AuditAsync(cancellationToken)).Select(audit => audit.Action));
    }

    [Fact]
    public async Task Keep_and_undo_of_a_key_no_group_has_are_bare_404s_and_write_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantCopiesAsync("Alien", 1979, AlienImdbId, AlienPath, BedroomAlienPath);

        //Act
        var keep = await KeepAsync(UnknownKey, _owner.AccessToken);
        var undo = await _factory.DeleteHttpRequest(DecisionUrl(UnknownKey), _owner.AccessToken);

        //Assert
        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (keep.StatusCode, undo.StatusCode));
        Assert.Empty(await keep.Content.ReadAsStringAsync(cancellationToken));
        Assert.Empty(await undo.Content.ReadAsStringAsync(cancellationToken));
        await AssertNothingWrittenAsync(cancellationToken);
    }

    [Fact]
    public async Task Keep_an_episodes_copies_names_the_audit_row_by_its_series_and_place()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // One episode in two libraries, each under its own item of the show, with the episode's own IMDb id.
        foreach (var (library, path) in new[] { (_livingRoom, "/media/tv/Clarkson's Farm/Season 2/Clarkson's Farm S02E03.mkv"), (_bedroom, "/media/series/Clarkson's Farm S02E03.mkv") })
        {
            var series = await PlantSeriesAsync(library, "Clarkson's Farm", 2021, "136166");
            await PlantEpisodeAsync(library, series, 2, 3, "Wet", path, "tt14993832");
        }

        var key = await SingleKeyAsync(DuplicateClass.SameWork, cancellationToken);

        //Act
        var response = await KeepAsync(key, _owner.AccessToken);

        //Assert
        // An episode as its card heads it: its show's title and its place, never the episode's own title alone.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var audit = Assert.Single(await AuditAsync(cancellationToken));
        Assert.Equal((AuditAction.DuplicateCopiesKept, "Clarkson's Farm S02E03"), (audit.Action, audit.TargetName));
    }

    [Fact]
    public async Task Keep_copies_no_library_reports_names_the_audit_row_by_the_first_ones_file_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        // Two names only the folder scan found, of two files with the same bytes: no work to name the group by.
        await _physicalFiles.CreateUnreported(UnreportedArchivePath);
        await _physicalFiles.CreateUnreported(CopyPath);
        var archiveFacts = TestMedia.File(ArchiveInode, TakenSizeBytes);
        var copyFacts = TestMedia.File(CopyInode, TakenSizeBytes, TestMedia.ModifiedUtc.AddDays(3));
        _factory.Files.AddFile(UnreportedArchivePath, archiveFacts, TestMedia.Taken());
        _factory.Files.AddFile(CopyPath, copyFacts, TestMedia.Taken(), hash: FakeFileInventory.HashOf(copyFacts, FakeFileInventory.DefaultSha256(archiveFacts)));
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        var key = await SingleKeyAsync(DuplicateClass.ByteIdentical, cancellationToken);

        //Act
        var response = await KeepAsync(key, _owner.AccessToken);

        //Assert
        // By the file name of its first path: the archive's, before the backup's.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var audit = Assert.Single(await AuditAsync(cancellationToken));
        Assert.Equal((AuditAction.DuplicateCopiesKept, "Taken.2008.BDRemux.1080p.mkv"), (audit.Action, audit.TargetName));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static string DecisionUrl(string key) => $"{BaseUrl}/{Uri.EscapeDataString(key)}/decision";

    /// <summary>What a kept group's row stores of the app paths given, in the order given: each name's path key — the
    /// app path, as no mapping applies — one per line.</summary>
    private static string Kept(params string[] appPaths) => string.Join('\n', appPaths.Select(path => "app:" + path));

    private Task<HttpResponseMessage> DecideAsync(string key, string decision, string? token) =>
        _factory.PutHttpRequest(DecisionUrl(key), token, new DuplicateDecisionRequest(decision));

    /// <summary>The owner's Keep both (Keep all): the decision <c>Dismissed</c>.</summary>
    private Task<HttpResponseMessage> KeepAsync(string key, string? token) => DecideAsync(key, "Dismissed", token);

    // A film as a library reported it, in an item of its own: its title and year, the IMDb id given, one version and one
    // name — what a sync stores.
    private async Task<(MediaWorkEntity Work, MediaVersionEntity Version)> PlantMovieAsync(
        IntegrationConnectionEntity library, string title, int year, string servicePath, string imdbId)
    {
        var itemId = Guid.NewGuid().ToString("N");
        var work = await _works.Create();
        await _works.AddTitle(work.Id, title, MediaTitleKind.Display, year, library.Id, library.Name, itemId);
        await _works.AddIdentifier(work.Id, MediaIdentifierScheme.Imdb, imdbId, library.Id, library.Name, itemId);
        var version = await _versions.Create(work.Id, itemId, sourceConnectionId: library.Id, sourceName: library.Name);
        await _physicalFiles.Create(version.Id, servicePath, sourceConnectionId: library.Id, sourceName: library.Name);
        return (work, version);
    }

    // A SameWork group: the film in the living room and in the bedroom under one IMDb id.
    private async Task<((MediaWorkEntity Work, MediaVersionEntity Version) LivingRoom, (MediaWorkEntity Work, MediaVersionEntity Version) Bedroom)> PlantCopiesAsync(
        string title, int year, string imdbId, string livingRoomPath, string bedroomPath) =>
        (await PlantMovieAsync(_livingRoom, title, year, livingRoomPath, imdbId), await PlantMovieAsync(_bedroom, title, year, bedroomPath, imdbId));

    // Another version of the film's item, as Jellyfin reports an item of several, holding the file at the path given.
    private async Task<MediaVersionEntity> PlantVersionAsync((MediaWorkEntity Work, MediaVersionEntity Version) film, string path)
    {
        var library = film.Version.SourceConnectionId;
        var version = await _versions.Create(
            film.Work.Id, film.Version.SourceItemId, Guid.NewGuid().ToString("N"), sourceConnectionId: library, sourceName: film.Version.SourceName);
        await _physicalFiles.Create(version.Id, path, sourceConnectionId: library, sourceName: film.Version.SourceName);
        return version;
    }

    // A series as a library reported it: its title and year and its TMDb show id. It holds no file.
    private async Task<MediaWorkEntity> PlantSeriesAsync(IntegrationConnectionEntity library, string title, int year, string tmdbTvId)
    {
        var itemId = Guid.NewGuid().ToString("N");
        var series = await _works.Create(MediaWorkKind.Series);
        await _works.AddTitle(series.Id, title, MediaTitleKind.Display, year, library.Id, library.Name, itemId);
        await _works.AddIdentifier(series.Id, MediaIdentifierScheme.TmdbTv, tmdbTvId, library.Id, library.Name, itemId);
        return series;
    }

    // An episode of the series at its season and number, with its own IMDb id, and one version holding the file.
    private async Task PlantEpisodeAsync(
        IntegrationConnectionEntity library, MediaWorkEntity series, int season, int episode, string title, string path, string imdbId)
    {
        var itemId = Guid.NewGuid().ToString("N");
        var work = await _works.Create(MediaWorkKind.Episode);
        await _works.AddTitle(work.Id, title, MediaTitleKind.Display, null, library.Id, library.Name, itemId);
        await _works.AddIdentifier(work.Id, MediaIdentifierScheme.Imdb, imdbId, library.Id, library.Name, itemId);
        await _works.AddEpisodePosition(work.Id, series.Id, season, episode, library.Id, library.Name, itemId);
        var version = await _versions.Create(work.Id, itemId, sourceConnectionId: library.Id, sourceName: library.Name);
        await _physicalFiles.Create(version.Id, path, sourceConnectionId: library.Id, sourceName: library.Name);
    }

    // A file name the living room reported, in a work and version of its own — titled with Taken's title and year when
    // asked, else with none and no id, so it is in no possible match.
    private async Task PlantFileAsync(string servicePath, string? title)
    {
        var itemId = Guid.NewGuid().ToString("N");
        var work = await _works.Create();
        if (title is not null)
            await _works.AddTitle(work.Id, title, MediaTitleKind.Display, 2008, _livingRoom.Id, _livingRoom.Name, itemId);
        var version = await _versions.Create(work.Id, itemId, sourceConnectionId: _livingRoom.Id, sourceName: _livingRoom.Name);
        await _physicalFiles.Create(version.Id, servicePath, sourceConnectionId: _livingRoom.Id, sourceName: _livingRoom.Name);
    }

    // A ByteIdentical group, inspected over HTTP: the film at TakenPath, titled Taken (2008), and a backup at CopyPath in an
    // untitled item, another inode written later, every byte the same, both on ext4. Returns the bytes' hash.
    private async Task<string> PlantByteIdenticalCopiesAsync(CancellationToken cancellationToken)
    {
        await _roots.Allow(Root);
        await PlantFileAsync(TakenPath, "Taken");
        await PlantFileAsync(CopyPath, title: null);
        var takenFacts = TestMedia.File(TakenInode, TakenSizeBytes);
        var copyFacts = TestMedia.File(CopyInode, TakenSizeBytes, TestMedia.ModifiedUtc.AddDays(3));
        var sha256 = FakeFileInventory.DefaultSha256(takenFacts);
        _factory.Files.AddFile(TakenPath, takenFacts, TestMedia.Taken());
        _factory.Files.AddFile(CopyPath, copyFacts, TestMedia.Taken(), hash: FakeFileInventory.HashOf(copyFacts, sha256));
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        return sha256;
    }

    /// <summary>The key the list gives the group of <paramref name="duplicateClass"/> whose works are called
    /// <paramref name="title"/>.</summary>
    private async Task<string> KeyOfAsync(DuplicateClass duplicateClass, string title, CancellationToken cancellationToken) =>
        (await ListAsync($"class={duplicateClass}", _owner.AccessToken, cancellationToken)).Items.Single(group => TitleOf(group) == title).Key;

    /// <summary>The key of the one group of <paramref name="duplicateClass"/> on the list.</summary>
    private async Task<string> SingleKeyAsync(DuplicateClass duplicateClass, CancellationToken cancellationToken) =>
        Assert.Single((await ListAsync($"class={duplicateClass}", _owner.AccessToken, cancellationToken)).Items).Key;

    // The title a group is ordered by, as its first file's first work shows it: every planted group's works share one.
    private static string? TitleOf(LibraryDuplicateGroupDto group) => group.Files[0].Works[0].Title;

    private async Task<LibraryDuplicateListResponse> ListAsync(string query, string token, CancellationToken cancellationToken)
    {
        var response = await _factory.GetHttpRequest(query.Length == 0 ? BaseUrl : $"{BaseUrl}?{query}", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LibraryDuplicateListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }

    private async Task<DuplicateMatchDecisionEntity> SingleDecisionAsync(CancellationToken cancellationToken)
    {
        DuplicateMatchDecisionEntity? row = null;
        await _factory.WithDbAsync(async db => row = await db.DuplicateMatchDecisions.AsNoTracking().SingleAsync(cancellationToken));
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

    private async Task AssertNothingWrittenAsync(CancellationToken cancellationToken) =>
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.DuplicateMatchDecisions.CountAsync(cancellationToken));
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
}
