using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions.Files;
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

// MAM-37 — copies of one work whose measured running times differ widely, on GET /api/library/duplicates?class=SameWork
// (Vasyl, 2026-10-08): the shortest copy strictly under three quarters of the longest flags the group for review with
// RunningTimes, beside any other distinction — a featurette under a film's ids, another film a library gave the first
// one's. A copy is what one version holds, its running time the sum of its files' measured durations; a copy with a file
// not measured decides nothing, and each copy counts once. The shortest and the longest measured copy are the group's
// evidence whenever two copies have a running time, flagged or not. Nothing is split, nothing written, and no copy is
// called redundant. The durations are those ffprobe measured on production, as read on 2026-10-08 — except the two
// parts of a stacked «Ніж у воді», made up to sum near the film's, as production holds no stacked copy — each measured
// here by a real inspection through the fake file inventory. The rest of the class is LibraryDuplicatesSameWorkHttpTests'; a possible match's
// durations LibraryDuplicatesPossibleHttpTests'; the wire names LibraryDuplicatesHttpTests'.
public sealed class LibraryDuplicatesRunningTimesHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library/duplicates";
    private const string SameWorkUrl = $"{BaseUrl}?class=SameWork";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string Root = "/media";

    /// <summary>A second Jellyfin, beside the factory's default address.</summary>
    private const string BedroomBaseUrl = "http://127.0.0.2:9";

    // «Ніж у воді» (1962) and the featurette filed beside it under its ids, in path order.
    private const string KnifePath = "/media/movies/Nóż w wodzie (1962)/Nóż w wodzie (1962).mkv";
    private const string FeaturettePath = "/media/movies/Nóż w wodzie (1962)/Polanski+Skolimowski o Noże.mkv";
    private const string KnifeImdbId = "tt0056291";
    private const string KnifeTmdbId = "11502";
    private const long KnifeMilliseconds = 5_731_959;
    private const long FeaturetteMilliseconds = 1_600_699;

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IMediaWorkDataFactory _works = null!;
    private IMediaVersionDataFactory _versions = null!;
    private IPhysicalFileDataFactory _physicalFiles = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private IntegrationConnectionEntity _livingRoom = null!;
    private IntegrationConnectionEntity _bedroom = null!;

    public LibraryDuplicatesRunningTimesHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var integrations = new IntegrationConnectionSeeder(_factory.Scope);
        var roots = new AllowedRootSeeder(_factory.Scope);
        var db = _factory.Scope.GetRequiredService<ApplicationDbContext>();
        _works = new MediaWorkDataFactory(db, _factory.Time);
        _versions = new MediaVersionDataFactory(db, _factory.Time);
        _physicalFiles = new PhysicalFileDataFactory(db, _factory.Time);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        // Two Jellyfins over one disk, with no path mapping: the app looks for each file where its library says it is.
        _livingRoom = await integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        _bedroom = await integrations.Create("jellyfin", "Bedroom", BedroomBaseUrl, apiKey: ApiKey);
        await roots.Allow(Root);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task List_flags_a_film_and_a_featurette_under_its_ids_for_review_by_their_running_times()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Two items of one library with the film's IMDb and TMDb ids: the film and a 27-minute featurette — no edition in
        // either name, no dynamic range measured, nothing but the running times to tell them apart.
        var film = await PlantMovieAsync(_livingRoom, "Ніж у воді", 1962, KnifePath, KnifeImdbId, KnifeTmdbId);
        var featurette = await PlantMovieAsync(_livingRoom, "Polanski+Skolimowski o Noże", 1962, FeaturettePath, KnifeImdbId, KnifeTmdbId);
        Measure(KnifePath, 1_048_577, KnifeMilliseconds);
        Measure(FeaturettePath, 1_048_579, FeaturetteMilliseconds);
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Act
        var owners = await _factory.GetHttpRequest(SameWorkUrl, _owner.AccessToken);
        var members = await _factory.GetHttpRequest(SameWorkUrl, _member.AccessToken);

        //Assert
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (owners.StatusCode, members.StatusCode));
        var ownersBody = await owners.Content.ReadAsStringAsync(cancellationToken);
        // Nothing in it is the owner's alone.
        Assert.Equal(ownersBody, await members.Content.ReadAsStringAsync(cancellationToken));
        var group = Assert.Single(ParseList(ownersBody).Items);
        // 27.9 % of the film: still one work by its ids, flagged for the owner to look, with the running times as the
        // only distinction.
        Assert.Equal((DuplicateClass.SameWork, true), (group.Class, group.NeedsReview));
        Assert.Equal([new DuplicateDistinctionDto(DuplicateDistinctionKind.RunningTimes, null, false)], group.Distinctions);
        Assert.Equal(new DuplicateRunningTimesDto(FeaturetteMilliseconds, KnifeMilliseconds), group.Evidence.RunningTimes);
        Assert.Equal(
            [
                new DuplicateIdentifierDto(MediaIdentifierScheme.Imdb, KnifeImdbId, null, null),
                new DuplicateIdentifierDto(MediaIdentifierScheme.Tmdb, KnifeTmdbId, null, null),
            ],
            group.Evidence.Identifiers);
        // A possible match's duration evidence is not this class's.
        Assert.Null(group.Evidence.DurationsAgree);
        Assert.Equal(
            [(KnifePath, film.Work.Id, (long?)KnifeMilliseconds), (FeaturettePath, featurette.Work.Id, FeaturetteMilliseconds)],
            group.Files.Select(file => (file.File.ServicePath, Assert.Single(file.Works).Id, file.File.Inspection!.Media!.DurationMilliseconds)));
        Assert.All(group.Files, file => Assert.Empty(file.Editions));
        // Never redundant, and grouping only reads: the library keeps two works, each with its own version.
        Assert.Null(group.Recoverable);
        await _factory.WithDbAsync(async db =>
        {
            var workOfVersion = await db.MediaVersions.AsNoTracking().ToDictionaryAsync(version => version.Id, version => version.MediaWorkId, cancellationToken);
            Assert.Equal(
                (2, 2, film.Work.Id, featurette.Work.Id),
                (await db.MediaWorks.CountAsync(cancellationToken), workOfVersion.Count, workOfVersion[film.Version.Id], workOfVersion[featurette.Version.Id]));
        });
    }

    [Fact]
    public async Task List_flags_running_times_that_differ_widely_beside_ids_that_disagree()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The film and the featurette as two items under the film's IMDb id, the featurette's given a TMDb id of its own
        // (made up: production's featurette is a version of the film's item). The TMDb ids already disagree; the
        // running times — 27.9 % — say more than that.
        const string featuretteTmdbId = "1196943";
        await PlantMovieAsync(_livingRoom, "Ніж у воді", 1962, KnifePath, KnifeImdbId, KnifeTmdbId);
        await PlantMovieAsync(_livingRoom, "Polanski+Skolimowski o Noże", 1962, FeaturettePath, KnifeImdbId, featuretteTmdbId);
        Measure(KnifePath, 1_048_577, KnifeMilliseconds);
        Measure(FeaturettePath, 1_048_579, FeaturetteMilliseconds);
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(SameWorkUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var group = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal((DuplicateClass.SameWork, true), (group.Class, group.NeedsReview));
        // Both, in kind order.
        Assert.Equal(
            [
                new DuplicateDistinctionDto(DuplicateDistinctionKind.IdentifierConflict, MediaIdentifierScheme.Tmdb, false),
                new DuplicateDistinctionDto(DuplicateDistinctionKind.RunningTimes, null, false),
            ],
            group.Distinctions);
        Assert.Equal(new DuplicateRunningTimesDto(FeaturetteMilliseconds, KnifeMilliseconds), group.Evidence.RunningTimes);
        // The id they share is the evidence; the TMDb ids each file's own work shows.
        Assert.Equal([new DuplicateIdentifierDto(MediaIdentifierScheme.Imdb, KnifeImdbId, null, null)], group.Evidence.Identifiers);
        Assert.Equal([KnifePath, FeaturettePath], group.Files.Select(file => file.File.ServicePath));
    }

    [Fact]
    public async Task List_flags_nothing_when_the_shortest_copy_runs_over_three_quarters_of_the_longest()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Alien³'s two cuts as production measured them, the theatrical at 79.3 % of the Special Assembly Cut — the
        // closest two in the library — in two libraries whose names say neither cut: one set of editions, none, so
        // copies of one work, and the running times far enough apart to say nothing.
        const string backupPath = "/media/backup/Alien³ (1992) BDRip 1080p.mkv";
        const string moviesPath = "/media/movies/Alien³ (1992)/Alien³ (1992) BDRip 1080p.mkv";
        await PlantMovieAsync(_livingRoom, "Чужий 3", 1992, moviesPath, "tt0103644");
        await PlantMovieAsync(_bedroom, "Alien³", 1992, backupPath, "tt0103644");
        Measure(moviesPath, 1_048_577, 6_892_192);
        Measure(backupPath, 1_048_579, 8_695_834);
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(SameWorkUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var group = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal((DuplicateClass.SameWork, false), (group.Class, group.NeedsReview));
        Assert.Empty(group.Distinctions);
        // The running times are evidence all the same.
        Assert.Equal(new DuplicateRunningTimesDto(6_892_192, 8_695_834), group.Evidence.RunningTimes);
        Assert.Equal([backupPath, moviesPath], group.Files.Select(file => file.File.ServicePath));
        Assert.All(group.Files, file => Assert.Empty(file.Editions));
    }

    [Fact]
    public async Task List_takes_no_running_time_from_a_copy_never_inspected()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The film measured; the featurette added after the inspection, so the app never looked at it.
        await PlantMovieAsync(_livingRoom, "Ніж у воді", 1962, KnifePath, KnifeImdbId);
        Measure(KnifePath, 1_048_577, KnifeMilliseconds);
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        await PlantMovieAsync(_livingRoom, "Polanski+Skolimowski o Noże", 1962, FeaturettePath, KnifeImdbId);

        //Act
        var response = await _factory.GetHttpRequest(SameWorkUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // An unknown decides nothing: one copy with a running time is no comparison, and no evidence.
        var group = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal((DuplicateClass.SameWork, false), (group.Class, group.NeedsReview));
        Assert.Empty(group.Distinctions);
        Assert.Null(group.Evidence.RunningTimes);
        Assert.Equal(
            [(KnifePath, (FileInspectionStatus?)FileInspectionStatus.Inspected), (FeaturettePath, null)],
            group.Files.Select(file => (file.File.ServicePath, file.File.Inspection?.Status)));
    }

    [Fact]
    public async Task List_compares_the_measured_copies_beside_one_never_inspected()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The film and the featurette measured; a third copy, the bedroom's, added after the inspection.
        const string backupPath = "/media/backup/Nóż w wodzie (1962).mkv";
        await PlantMovieAsync(_livingRoom, "Ніж у воді", 1962, KnifePath, KnifeImdbId);
        await PlantMovieAsync(_livingRoom, "Polanski+Skolimowski o Noże", 1962, FeaturettePath, KnifeImdbId);
        Measure(KnifePath, 1_048_577, KnifeMilliseconds);
        Measure(FeaturettePath, 1_048_579, FeaturetteMilliseconds);
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        await PlantMovieAsync(_bedroom, "Nóż w wodzie", 1962, backupPath, KnifeImdbId);

        //Act
        var response = await _factory.GetHttpRequest(SameWorkUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // The copy not looked at is left out, not the group: the two measured ones still flag it, and they alone are the
        // evidence — the unknown counts neither as no time nor as a reason to say nothing.
        var group = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal((DuplicateClass.SameWork, true), (group.Class, group.NeedsReview));
        Assert.Equal([new DuplicateDistinctionDto(DuplicateDistinctionKind.RunningTimes, null, false)], group.Distinctions);
        Assert.Equal(new DuplicateRunningTimesDto(FeaturetteMilliseconds, KnifeMilliseconds), group.Evidence.RunningTimes);
        Assert.Equal(
            [
                (backupPath, (FileInspectionStatus?)null),
                (KnifePath, FileInspectionStatus.Inspected),
                (FeaturettePath, FileInspectionStatus.Inspected),
            ],
            group.Files.Select(file => (file.File.ServicePath, file.File.Inspection?.Status)));
    }

    [Fact]
    public async Task List_takes_no_running_time_from_a_copy_whose_reading_failed()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The film measured; ffprobe couldn't read the featurette as media.
        await PlantMovieAsync(_livingRoom, "Ніж у воді", 1962, KnifePath, KnifeImdbId);
        await PlantMovieAsync(_livingRoom, "Polanski+Skolimowski o Noże", 1962, FeaturettePath, KnifeImdbId);
        Measure(KnifePath, 1_048_577, KnifeMilliseconds);
        _factory.Files.AddFile(FeaturettePath, TestMedia.File(1_048_579, 1_048_579_000));
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(SameWorkUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var group = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal((DuplicateClass.SameWork, false), (group.Class, group.NeedsReview));
        Assert.Empty(group.Distinctions);
        Assert.Null(group.Evidence.RunningTimes);
        Assert.Equal(
            [(KnifePath, FileInspectionStatus.Inspected), (FeaturettePath, FileInspectionStatus.MediaUnreadable)],
            group.Files.Select(file => (file.File.ServicePath, file.File.Inspection!.Status)));
    }

    [Fact]
    public async Task List_counts_a_copy_in_two_parts_as_the_sum_of_their_durations()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The living room's film stacked in two parts, each about half the bedroom's single file: one copy, which runs
        // as long as both.
        const string firstPartPath = "/media/movies/Nóż w wodzie (1962)/Nóż w wodzie (1962) CD1.mkv";
        const string secondPartPath = "/media/movies/Nóż w wodzie (1962)/Nóż w wodzie (1962) CD2.mkv";
        const string singlePath = "/media/backup/Nóż w wodzie (1962).mkv";
        var film = await PlantMovieAsync(_livingRoom, "Ніж у воді", 1962, firstPartPath, KnifeImdbId);
        await _physicalFiles.Create(film.Version.Id, secondPartPath, sourceConnectionId: _livingRoom.Id, sourceName: _livingRoom.Name);
        await PlantMovieAsync(_bedroom, "Nóż w wodzie", 1962, singlePath, KnifeImdbId);
        Measure(firstPartPath, 1_048_577, 2_870_000);
        Measure(secondPartPath, 1_048_579, 2_820_000);
        Measure(singlePath, 1_048_581, KnifeMilliseconds);
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(SameWorkUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // 5 690 000 ms of parts beside 5 731 959: no flag, though either part alone is half the single file.
        var group = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal((DuplicateClass.SameWork, false), (group.Class, group.NeedsReview));
        Assert.Empty(group.Distinctions);
        Assert.Equal(new DuplicateRunningTimesDto(2_870_000 + 2_820_000, KnifeMilliseconds), group.Evidence.RunningTimes);
        Assert.Equal([singlePath, firstPartPath, secondPartPath], group.Files.Select(file => file.File.ServicePath));
    }

    [Fact]
    public async Task List_takes_no_running_time_from_a_copy_with_a_part_not_measured()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The living room's two-part film with only its first part found and measured: half a film, which alone would
        // be under three quarters of the bedroom's.
        const string firstPartPath = "/media/movies/Nóż w wodzie (1962)/Nóż w wodzie (1962) CD1.mkv";
        const string secondPartPath = "/media/movies/Nóż w wodzie (1962)/Nóż w wodzie (1962) CD2.mkv";
        const string singlePath = "/media/backup/Nóż w wodzie (1962).mkv";
        var film = await PlantMovieAsync(_livingRoom, "Ніж у воді", 1962, firstPartPath, KnifeImdbId);
        await _physicalFiles.Create(film.Version.Id, secondPartPath, sourceConnectionId: _livingRoom.Id, sourceName: _livingRoom.Name);
        await PlantMovieAsync(_bedroom, "Nóż w wodzie", 1962, singlePath, KnifeImdbId);
        Measure(firstPartPath, 1_048_577, 2_870_000);
        Measure(singlePath, 1_048_581, KnifeMilliseconds);
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(SameWorkUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // A part unknown leaves the whole copy unknown: one copy with a running time, nothing to compare.
        var group = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal((DuplicateClass.SameWork, false), (group.Class, group.NeedsReview));
        Assert.Empty(group.Distinctions);
        Assert.Null(group.Evidence.RunningTimes);
        Assert.Equal(
            [(singlePath, FileInspectionStatus.Inspected), (firstPartPath, FileInspectionStatus.Inspected), (secondPartPath, FileInspectionStatus.NotFound)],
            group.Files.Select(file => (file.File.ServicePath, file.File.Inspection!.Status)));
    }

    [Fact]
    public async Task List_counts_one_path_two_libraries_report_as_one_copy()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Both libraries report the film at one path: one name, two versions holding one set of files — one copy, with
        // one running time. The featurette beside it was never looked at.
        await PlantMovieAsync(_livingRoom, "Ніж у воді", 1962, KnifePath, KnifeImdbId);
        await PlantMovieAsync(_bedroom, "Nóż w wodzie", 1962, KnifePath, KnifeImdbId);
        Measure(KnifePath, 1_048_577, KnifeMilliseconds);
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        await PlantMovieAsync(_livingRoom, "Polanski+Skolimowski o Noże", 1962, FeaturettePath, KnifeImdbId);

        //Act
        var response = await _factory.GetHttpRequest(SameWorkUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Counted once, the film's copy is the only one with a running time: no comparison, so no evidence.
        var group = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal((DuplicateClass.SameWork, false), (group.Class, group.NeedsReview));
        Assert.Empty(group.Distinctions);
        Assert.Null(group.Evidence.RunningTimes);
        Assert.Equal([KnifePath, FeaturettePath], group.Files.Select(file => file.File.ServicePath));
        Assert.Equal(2, group.Files[0].Works.Count);
    }

    [Fact]
    public async Task List_gives_the_shortest_and_the_longest_of_three_copies()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Terminator 2 three times, nothing in the names: 8 243 392, 9 206 823 and 9 366 400 ms — 88.0 % at the widest.
        const string backupPath = "/media/backup/Terminator 2 (1991).mkv";
        const string remuxPath = "/media/movies/Terminator 2 (1991)/Terminator 2 (1991) BDRemux 1080p.mkv";
        const string ripPath = "/media/movies/Terminator 2 (1991)/Terminator 2 (1991) BDRip 720p.mkv";
        await PlantMovieAsync(_livingRoom, "Термінатор 2: Судний день", 1991, remuxPath, "tt0103064");
        await PlantMovieAsync(_livingRoom, "Термінатор 2: Судний день", 1991, ripPath, "tt0103064");
        await PlantMovieAsync(_bedroom, "Terminator 2: Judgment Day", 1991, backupPath, "tt0103064");
        Measure(remuxPath, 1_048_577, 9_206_823);
        Measure(ripPath, 1_048_579, 8_243_392);
        Measure(backupPath, 1_048_581, 9_366_400);
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(SameWorkUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var group = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal((DuplicateClass.SameWork, false), (group.Class, group.NeedsReview));
        Assert.Empty(group.Distinctions);
        // The one between them changes nothing.
        Assert.Equal(new DuplicateRunningTimesDto(8_243_392, 9_366_400), group.Evidence.RunningTimes);
        Assert.Equal(3, group.Files.Count);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    // A film as a library reported it — its displayed title and year, the ids given, and one version holding the file at
    // the path given — in an item of its own: what a sync stores.
    private async Task<(MediaWorkEntity Work, MediaVersionEntity Version)> PlantMovieAsync(
        IntegrationConnectionEntity library, string title, int year, string path, string imdbId, string? tmdbId = null)
    {
        var itemId = Guid.NewGuid().ToString("N");
        var work = await _works.Create();
        await _works.AddTitle(work.Id, title, MediaTitleKind.Display, year, library.Id, library.Name, itemId);
        await _works.AddIdentifier(work.Id, MediaIdentifierScheme.Imdb, imdbId, library.Id, library.Name, itemId);
        if (tmdbId is not null)
            await _works.AddIdentifier(work.Id, MediaIdentifierScheme.Tmdb, tmdbId, library.Id, library.Name, itemId);
        var version = await _versions.Create(work.Id, itemId, sourceConnectionId: library.Id, sourceName: library.Name);
        await _physicalFiles.Create(version.Id, path, sourceConnectionId: library.Id, sourceName: library.Name);
        return (work, version);
    }

    // A file at the path, of its own identity and size, that the media inspector measures at the duration given and
    // nothing else: no tracks, so no dynamic range tells copies apart.
    private void Measure(string path, long inode, long durationMilliseconds) =>
        _factory.Files.AddFile(path, TestMedia.File(inode, inode * 1_000), new MediaFacts(TimeSpan.FromMilliseconds(durationMilliseconds), null, []));

    private static LibraryDuplicateListResponse ParseList(string body) =>
        JsonSerializer.Deserialize<LibraryDuplicateListResponse>(body, MediaAcquisitionManagerApiFactory.JsonOptions)!;

    private static async Task<LibraryDuplicateListResponse> ReadListAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<LibraryDuplicateListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
}
