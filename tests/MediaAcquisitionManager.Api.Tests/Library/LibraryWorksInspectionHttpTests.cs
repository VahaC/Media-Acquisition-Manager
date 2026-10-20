using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using MediaAcquisitionManager.TestInfrastructure.Texts;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Library;

// MAM-9 — what the works list says about each file once the app has looked at it itself: nothing until
// it has; for a file the media inspector read, the verified facts (duration, bit rate and every track in
// the file's order, an unknown value null rather than guessed); for a file it couldn't read, the status
// for everyone and the sentence saying why for the owner only, never facts. Two names of one inode show
// the facts read once; a file the integration's path mappings map is looked at where the app sees it.
// Every inspection here runs through the real pipeline — the owner's Inspect files, the worker, the
// runner — against the faked mounts and media inspector (FakeFileInventory).
public sealed class LibraryWorksInspectionHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library/works";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";

    private const string ContinentalPath =
        "/media/movies/John Wick. Collection/[01] The Continental (2023) - (Season 1)/[01] The Continental (2023) - S01E01 - BDRip 1080p x265 [3xUKR_ENG] [Hurtom].mkv";

    private const string District13Path =
        "/media/movies/District 13. Dilogy (2004-2009) BDRip 1080p H.265 [2xUKR_FRE] [Hurtom]/[01] Banlieue 13 (2004) BDRip 1080p H.265 [2xUKR_FRE] [Hurtom].mkv";

    private const string AliensTheatricalPath =
        "/media/movies/Alien. Collection/[02.1] Aliens (1986) [Theatrical Cut, Fundamental Collection] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom].mkv";

    private const string AliensSpecialEditionPath =
        "/media/movies/Alien. Collection/[02.2] Aliens (1986) [Special Edition] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom, UTOPIA].mkv";

    /// <summary>Where the Continental is inside the app when Jellyfin's <c>/media/movies</c> is mapped to
    /// the app's <c>/data/movies</c>.</summary>
    private const string MappedContinentalPath =
        "/data/movies/John Wick. Collection/[01] The Continental (2023) - (Season 1)/[01] The Continental (2023) - S01E01 - BDRip 1080p x265 [3xUKR_ENG] [Hurtom].mkv";

    private const string PermissionDeniedError = "The app may not read “" + ContinentalPath + "”: permission denied.";
    private const string NotARegularFileError = "“" + ContinentalPath + "” is a folder, not a file.";
    private const string LinkLeadsOutError = "“" + ContinentalPath + "” is a link that leads outside the media folders the app may read.";
    private const string GoneError = "Nothing is at “" + ContinentalPath + "”.";
    private const string NotCoveredError = "No path mapping of this integration covers “" + ContinentalPath + "”.";

    private const string UnreadableContinentalError =
        "ffprobe couldn't read “" + ContinentalPath + "” as media: Invalid data found when processing input.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private IAllowedRootSeeder _roots = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;

    public LibraryWorksInspectionHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    /// <summary>What the file inventory answers for a file the app can't read, and the status the file then
    /// shows; the sentence is whatever the inventory said.</summary>
    public static TheoryData<FileInventoryStatus, FileInspectionStatus, string> Problems => new()
    {
        { FileInventoryStatus.Unreadable, FileInspectionStatus.Unreadable, PermissionDeniedError },
        { FileInventoryStatus.NotARegularFile, FileInspectionStatus.NotARegularFile, NotARegularFileError },
        // A link inside the roots that leads out of them: the local inventory refuses to follow it.
        { FileInventoryStatus.OutsideAllowedRoots, FileInspectionStatus.OutsideAllowedRoots, LinkLeadsOutError },
        { FileInventoryStatus.NotFound, FileInspectionStatus.NotFound, GoneError },
    };

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _roots = new AllowedRootSeeder(_factory.Scope);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        new FakeJellyfinServer { ApiKey = ApiKey }.Arm(_factory.JellyfinHttp);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Works_gives_an_inspected_file_every_fact_the_media_inspector_read()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncLivingRoomAsync();
        await _roots.Allow("/media");
        _factory.Files.AddFile(ContinentalPath, TestMedia.File(inode: 1_001), TestMedia.Continental());
        _factory.Time.Advance(TimeSpan.FromMinutes(10));
        var inspectedAt = _factory.Time.GetUtcNow().UtcDateTime;
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((FileInspectionRunStatus.Succeeded, 4, 4, 1), (run.Status, run.FilesTotal, run.FilesChecked, run.FilesProbed));
        var inspection = FileAt(await ReadListAsync(response, cancellationToken), ContinentalPath).Inspection!;
        Assert.Equal((FileInspectionStatus.Inspected, null, inspectedAt), (inspection.Status, inspection.Problem, inspection.InspectedUtc));
        var media = inspection.Media!;
        Assert.Equal((5_179_168L, 6_011_343L), (media.DurationMilliseconds!.Value, media.BitRate!.Value));
        // The file's own order and every field of every track. The video stream gives no transfer
        // characteristic, so its dynamic range stays unknown rather than guessed SDR.
        Assert.Equal(
            [
                new MediaTrackDto(MediaTrackKind.Video, "hevc", "Main 10", 1920, 800, null, null, null, "eng", true, false, false),
                new MediaTrackDto(MediaTrackKind.Audio, "ac3", null, null, null, null, 6, "5.1(side)", "ukr", true, false, false),
                new MediaTrackDto(MediaTrackKind.Audio, "aac", "LC", null, null, null, 2, "stereo", "ukr", false, false, false),
                new MediaTrackDto(MediaTrackKind.Audio, "ac3", null, null, null, null, 2, "stereo", "ukr", false, false, false),
                new MediaTrackDto(MediaTrackKind.Audio, "ac3", null, null, null, null, 6, "5.1(side)", "eng", false, false, false),
                new MediaTrackDto(MediaTrackKind.Subtitle, "subrip", null, null, null, null, null, null, "ukr", true, true, false),
                new MediaTrackDto(MediaTrackKind.Subtitle, "subrip", null, null, null, null, null, null, "ukr", false, false, false),
                new MediaTrackDto(MediaTrackKind.Subtitle, "subrip", null, null, null, null, null, null, "eng", false, false, false),
                new MediaTrackDto(MediaTrackKind.Subtitle, "subrip", null, null, null, null, null, null, "eng", false, false, true),
            ],
            media.Tracks);
        await _factory.WithDbAsync(async db =>
        {
            var file = await db.PhysicalFiles.AsNoTracking().SingleAsync(row => row.ServicePath == ContinentalPath, cancellationToken);
            var reading = await db.MediaInspections.AsNoTracking().SingleAsync(row => row.Id == file.MediaInspectionId, cancellationToken);
            Assert.Equal(
                (FileInspectionStatus.Inspected, null, inspectedAt, TestMedia.DeviceId, 1_001L, 1),
                (file.InspectionStatus, file.InspectionError, file.InspectedUtc!.Value, file.DeviceId!.Value, file.Inode!.Value, file.LinkCount!.Value));
            // The cache row keyed by what the file system said, the modification time to the tick.
            Assert.Equal(
                (TestMedia.DeviceId, 1_001L, 3_891_719_630L, TestMedia.ModifiedUtc, null, 5_179_168L, 6_011_343L),
                (reading.DeviceId, reading.Inode, reading.SizeBytes, reading.ModifiedUtc, reading.Error, reading.DurationMilliseconds!.Value, reading.BitRate!.Value));
            Assert.Equal(
                Enumerable.Range(0, 9),
                await db.MediaInspectionTracks.Where(track => track.MediaInspectionId == reading.Id).Select(track => track.Position).OrderBy(position => position).ToListAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Works_sends_an_inspection_with_enum_names_and_every_unknown_value_as_null()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncLivingRoomAsync();
        await _roots.Allow("/media");
        _factory.Files.AddFile(ContinentalPath, TestMedia.File(inode: 1_001), TestMedia.Continental());
        // The Taken sample's streams stand in for District 13's: its video says BT.709, which is SDR.
        _factory.Files.AddFile(District13Path, TestMedia.File(inode: 1_002, sizeBytes: 4_670_197_619), TestMedia.Taken());
        _factory.Time.Advance(TimeSpan.FromMinutes(10));
        var inspectedAt = _factory.Time.GetUtcNow().UtcDateTime;
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var files = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))!["items"]!.AsArray()
            .SelectMany(work => work!["versions"]!.AsArray())
            .SelectMany(version => version!["files"]!.AsArray())
            .ToDictionary(file => (string)file!["servicePath"]!, file => file!.AsObject(), StringComparer.Ordinal);
        var inspection = files[ContinentalPath]["inspection"]!.AsObject();
        // The contract's shape exactly: camelCase, a null written out rather than left away, and nothing
        // more — no track title (owner's decision 4), no cache key.
        Assert.Equal(["status", "problem", "inspectedUtc", "media"], inspection.Select(property => property.Key));
        Assert.Equal("Inspected", (string)inspection["status"]!);
        Assert.Null(inspection["problem"]);
        var inspectedUtc = (string)inspection["inspectedUtc"]!;
        Assert.EndsWith("Z", inspectedUtc, StringComparison.Ordinal);
        Assert.Equal(inspectedAt, DateTime.Parse(inspectedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
        var media = inspection["media"]!.AsObject();
        Assert.Equal(["durationMilliseconds", "bitRate", "tracks"], media.Select(property => property.Key));
        Assert.Equal((5_179_168L, 6_011_343L), ((long)media["durationMilliseconds"]!, (long)media["bitRate"]!));
        var tracks = media["tracks"]!.AsArray().Select(track => track!.AsObject()).ToList();
        Assert.All(tracks, track => Assert.Equal(
            ["kind", "codec", "profile", "width", "height", "dynamicRange", "channels", "channelLayout", "language", "isDefault", "isForced", "isHearingImpaired"],
            track.Select(property => property.Key)));
        Assert.Equal(
            ["Video", "Audio", "Audio", "Audio", "Audio", "Subtitle", "Subtitle", "Subtitle", "Subtitle"],
            tracks.Select(track => (string)track["kind"]!));
        var video = tracks[0];
        Assert.Equal(("hevc", "Main 10", 1920, 800, "eng", true), ((string)video["codec"]!, (string)video["profile"]!, (int)video["width"]!, (int)video["height"]!, (string)video["language"]!, (bool)video["isDefault"]!));
        // Unknown, and what doesn't apply to a video track: present, as null.
        Assert.Null(video["dynamicRange"]);
        Assert.Null(video["channels"]);
        Assert.Null(video["channelLayout"]);
        var audio = tracks[1];
        Assert.Equal(("ac3", 6, "5.1(side)", "ukr"), ((string)audio["codec"]!, (int)audio["channels"]!, (string)audio["channelLayout"]!, (string)audio["language"]!));
        Assert.Null(audio["profile"]);
        Assert.Null(audio["width"]);
        Assert.Equal("Sdr", (string)files[District13Path]["inspection"]!["media"]!["tracks"]![0]!["dynamicRange"]!);
        await _factory.WithDbAsync(async db =>
            Assert.Equal(
                [DynamicRange.Sdr],
                await db.MediaInspectionTracks.Where(track => track.DynamicRange != null).Select(track => track.DynamicRange!.Value).ToListAsync(cancellationToken)));
    }

    [Fact]
    public async Task Works_gives_a_file_the_app_has_not_looked_at_no_inspection()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncLivingRoomAsync();
        await _roots.Allow("/media");
        // There to be read, but no inspection has run yet.
        _factory.Files.AddFile(ContinentalPath, TestMedia.File(inode: 1_001), TestMedia.Continental());

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var files = JsonNode.Parse(body)!["items"]!.AsArray()
            .SelectMany(work => work!["versions"]!.AsArray())
            .SelectMany(version => version!["files"]!.AsArray())
            .Select(file => file!.AsObject())
            .ToList();
        Assert.Equal(4, files.Count);
        // Written out as null, so the page can tell "not looked at yet" from a field it doesn't know.
        Assert.All(files, file =>
        {
            Assert.True(file.ContainsKey("inspection"));
            Assert.Null(file["inspection"]);
        });
        Assert.Empty(_factory.Files.DescribedPaths);
        await _factory.WithDbAsync(async db =>
            Assert.Equal(
                0,
                await db.PhysicalFiles.CountAsync(file => file.InspectionStatus != null || file.InspectedUtc != null || file.MediaInspectionId != null, cancellationToken)));
    }

    [Theory]
    [MemberData(nameof(Problems))]
    public async Task Works_gives_the_owner_the_status_and_the_sentence_of_a_file_the_app_could_not_read(
        FileInventoryStatus answer, FileInspectionStatus status, string problem)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncLivingRoomAsync();
        await _roots.Allow("/media");
        _factory.Files.AddProblem(ContinentalPath, answer, problem);
        _factory.Time.Advance(TimeSpan.FromMinutes(10));
        var inspectedAt = _factory.Time.GetUtcNow().UtcDateTime;
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            new LibraryFileInspectionDto(status, problem, inspectedAt, null),
            FileAt(await ReadListAsync(response, cancellationToken), ContinentalPath).Inspection);
        // Not even an attempt to read streams from what the file system wouldn't describe.
        Assert.DoesNotContain(ContinentalPath, _factory.Files.ReadPaths);
        await _factory.WithDbAsync(async db =>
        {
            var file = await db.PhysicalFiles.AsNoTracking().SingleAsync(row => row.ServicePath == ContinentalPath, cancellationToken);
            // Nothing was found there, so no identity is kept for it either.
            Assert.Equal(
                (status, problem, inspectedAt, null, null, null, null),
                (file.InspectionStatus!.Value, TestTexts.En(file.InspectionError), file.InspectedUtc!.Value, file.MediaInspectionId, file.DeviceId, file.Inode, file.LinkCount));
            Assert.Equal(0, await db.MediaInspections.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Works_gives_a_file_the_media_inspector_could_not_read_its_cached_failure_without_facts()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncLivingRoomAsync();
        await _roots.Allow("/media");
        // Found, with an identity, but no media in it.
        _factory.Files.AddFile(ContinentalPath, TestMedia.File(inode: 1_001), media: null);
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        // Inside the access token's 15 minutes, so the owner can ask again.
        _factory.Time.Advance(TimeSpan.FromMinutes(5));
        var inspectedAt = _factory.Time.GetUtcNow().UtcDateTime;
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // The second look answered from the cached failure: the same sentence, no facts, the new time.
        Assert.Equal(
            new LibraryFileInspectionDto(FileInspectionStatus.MediaUnreadable, UnreadableContinentalError, inspectedAt, null),
            FileAt(await ReadListAsync(response, cancellationToken), ContinentalPath).Inspection);
        Assert.Equal([ContinentalPath], _factory.Files.ReadPaths);
        await _factory.WithDbAsync(async db =>
        {
            var reading = await db.MediaInspections.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal((UnreadableContinentalError, null, null), (TestTexts.En(reading.Error), reading.DurationMilliseconds, reading.BitRate));
            Assert.Equal(0, await db.MediaInspectionTracks.CountAsync(cancellationToken));
            var file = await db.PhysicalFiles.AsNoTracking().SingleAsync(row => row.ServicePath == ContinentalPath, cancellationToken);
            Assert.Equal(
                (FileInspectionStatus.MediaUnreadable, UnreadableContinentalError, reading.Id),
                (file.InspectionStatus!.Value, TestTexts.En(file.InspectionError), file.MediaInspectionId!.Value));
        });
    }

    [Fact]
    public async Task Works_as_a_signed_in_non_owner_gives_the_status_of_a_file_not_read_but_not_why()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncLivingRoomAsync();
        await _roots.Allow("/media");
        _factory.Files.AddFile(ContinentalPath, TestMedia.File(inode: 1_001), TestMedia.Continental());
        _factory.Files.AddFile(District13Path, TestMedia.File(inode: 1_002), media: null);
        _factory.Time.Advance(TimeSpan.FromMinutes(10));
        var inspectedAt = _factory.Time.GetUtcNow().UtcDateTime;
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        // MAM-9 — the sentence quotes a path inside the app and what ffprobe said: the owner's
        // diagnostics. The status is everyone's, and so are the facts of a file that was read.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.DoesNotContain("ffprobe", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Nothing is at", body, StringComparison.Ordinal);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(
            new LibraryFileInspectionDto(FileInspectionStatus.MediaUnreadable, null, inspectedAt, null),
            FileAt(list, District13Path).Inspection);
        Assert.Equal(
            new LibraryFileInspectionDto(FileInspectionStatus.NotFound, null, inspectedAt, null),
            FileAt(list, AliensTheatricalPath).Inspection);
        var inspected = FileAt(list, ContinentalPath).Inspection!;
        Assert.Equal((FileInspectionStatus.Inspected, null), (inspected.Status, inspected.Problem));
        Assert.Equal(
            [.. TestMedia.Continental().Tracks.Select(ToDto)],
            inspected.Media!.Tracks);
        // Kept back from the response only: the sentence is stored for the owner.
        await _factory.WithDbAsync(async db =>
            Assert.Equal(
                string.Format(CultureInfo.InvariantCulture, FakeFileInventory.UnreadableMediaError, District13Path),
                TestTexts.En(await db.PhysicalFiles.Where(file => file.ServicePath == District13Path).Select(file => file.InspectionError).SingleAsync(cancellationToken))));
    }

    [Fact]
    public async Task Works_shows_two_names_of_one_inode_the_same_facts_read_once()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncLivingRoomAsync();
        await _roots.Allow("/media");
        // Hardlinks: one set of bytes behind both names of "Чужі".
        var bytes = TestMedia.File(inode: 2_002, sizeBytes: 14_845_708_473, linkCount: 2);
        _factory.Files.AddFile(AliensTheatricalPath, bytes, TestMedia.Taken());
        _factory.Files.AddFile(AliensSpecialEditionPath, bytes, TestMedia.Taken());
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?search=чужі", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        var theatrical = FileAt(list, AliensTheatricalPath).Inspection!.Media!;
        var specialEdition = FileAt(list, AliensSpecialEditionPath).Inspection!.Media!;
        Assert.Equal((5_604_672L, 40_781_887L), (theatrical.DurationMilliseconds!.Value, theatrical.BitRate!.Value));
        Assert.Equal((theatrical.DurationMilliseconds, theatrical.BitRate), (specialEdition.DurationMilliseconds, specialEdition.BitRate));
        Assert.Equal([.. TestMedia.Taken().Tracks.Select(ToDto)], theatrical.Tracks);
        Assert.Equal(theatrical.Tracks, specialEdition.Tracks);
        // One read for the inode, through whichever name came first.
        Assert.Single(_factory.Files.ReadPaths, path => path is AliensTheatricalPath or AliensSpecialEditionPath);
        await _factory.WithDbAsync(async db =>
        {
            var reading = await db.MediaInspections.AsNoTracking().SingleAsync(cancellationToken);
            var names = await db.PhysicalFiles.AsNoTracking()
                .Where(file => file.ServicePath == AliensTheatricalPath || file.ServicePath == AliensSpecialEditionPath)
                .ToListAsync(cancellationToken);
            Assert.Equal(2, names.Count);
            Assert.All(names, file => Assert.Equal(
                (reading.Id, TestMedia.DeviceId, 2_002L, 2),
                (file.MediaInspectionId!.Value, file.DeviceId!.Value, file.Inode!.Value, file.LinkCount!.Value)));
        });
    }

    [Fact]
    public async Task Works_shows_the_facts_of_a_file_its_integrations_path_mappings_map_into_the_app()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Jellyfin's /media/movies is the app's /data/movies: the app's own root and paths differ.
        await SyncLivingRoomAsync([new PathMappingPair("/media/movies", "/data/movies")]);
        await _roots.Allow("/data");
        _factory.Files.AddFile(MappedContinentalPath, TestMedia.File(inode: 1_001), TestMedia.Continental());
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var file = FileAt(await ReadListAsync(response, cancellationToken), ContinentalPath);
        Assert.Equal((MappedContinentalPath, PathMapStatus.Mapped), (file.AppPath, file.PathStatus));
        Assert.Equal((FileInspectionStatus.Inspected, 5_179_168L), (file.Inspection!.Status, file.Inspection.Media!.DurationMilliseconds!.Value));
        Assert.Equal(9, file.Inspection.Media.Tracks.Count);
        // ADR 0003 — looked at where the app sees it, never at the service's path.
        Assert.Contains(MappedContinentalPath, _factory.Files.DescribedPaths);
        Assert.DoesNotContain(_factory.Files.DescribedPaths, path => path.StartsWith("/media/", StringComparison.Ordinal));
        await _factory.WithDbAsync(async db =>
            Assert.Equal(
                FileInspectionStatus.Inspected,
                await db.PhysicalFiles.Where(row => row.ServicePath == ContinentalPath).Select(row => row.InspectionStatus).SingleAsync(cancellationToken)));
    }

    [Fact]
    public async Task Works_gives_a_file_its_mappings_dont_cover_no_app_path_with_the_mappers_sentence_for_the_owner_only()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A mapping for the shows only: the run finds no app path for the movies, and they stay unmapped.
        await SyncLivingRoomAsync([new PathMappingPair("/media/tv-shows", "/data/tv")]);
        await _roots.Allow("/media");
        _factory.Time.Advance(TimeSpan.FromMinutes(10));
        var inspectedAt = _factory.Time.GetUtcNow().UtcDateTime;
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Act
        var asOwner = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);
        var asMember = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        // MAM-9 — still unmapped, so the stored "no app path" still describes the file: the works list
        // agrees with the sources panel, which counts it as a problem, and only the owner reads why.
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (asOwner.StatusCode, asMember.StatusCode));
        var file = FileAt(await ReadListAsync(asOwner, cancellationToken), ContinentalPath);
        Assert.Equal(((string?)null, PathMapStatus.NotCovered), (file.AppPath, file.PathStatus));
        Assert.Equal(new LibraryFileInspectionDto(FileInspectionStatus.NoAppPath, NotCoveredError, inspectedAt, null), file.Inspection);
        Assert.Equal(
            new LibraryFileInspectionDto(FileInspectionStatus.NoAppPath, null, inspectedAt, null),
            FileAt(await ReadListAsync(asMember, cancellationToken), ContinentalPath).Inspection);
        Assert.Empty(_factory.Files.DescribedPaths);
    }

    [Fact]
    public async Task Works_gives_a_file_whose_mappings_were_fixed_since_the_run_no_inspection_until_the_next()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A mapping for the shows only: the run finds no app path for the movies.
        await SyncLivingRoomAsync([new PathMappingPair("/media/tv-shows", "/data/tv")]);
        await _roots.Allow("/media");
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        await _factory.WithDbAsync(db => db.PathMappings.ExecuteDeleteAsync(cancellationToken));

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        // MAM-9 — the owner fixed the mapping: the file has an app path now, so the run's "no app path"
        // no longer describes it; it reads as not inspected until the next run looks.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var file = FileAt(await ReadListAsync(response, cancellationToken), ContinentalPath);
        Assert.Equal((ContinentalPath, PathMapStatus.Identity, (LibraryFileInspectionDto?)null), (file.AppPath, file.PathStatus, file.Inspection));
        await _factory.WithDbAsync(async db =>
            Assert.Equal(
                FileInspectionStatus.NoAppPath,
                await db.PhysicalFiles.Where(row => row.ServicePath == ContinentalPath).Select(row => row.InspectionStatus).SingleAsync(cancellationToken)));
    }

    [Fact]
    public async Task Works_still_pages_and_searches_with_the_inspections_on_the_files()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncLivingRoomAsync();
        await _roots.Allow("/media");
        _factory.Files.AddFile(ContinentalPath, TestMedia.File(inode: 1_001), TestMedia.Continental());
        _factory.Files.AddFile(District13Path, TestMedia.File(inode: 1_002), TestMedia.Taken());
        _factory.Files.AddFile(AliensTheatricalPath, TestMedia.File(inode: 1_003), TestMedia.Taken());
        _factory.Files.AddFile(AliensSpecialEditionPath, TestMedia.File(inode: 1_004), TestMedia.Taken());
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Act
        var searched = await _factory.GetHttpRequest($"{BaseUrl}?search=чужі&page=1&pageSize=1", _owner.AccessToken);
        var paged = await _factory.GetHttpRequest($"{BaseUrl}?page=2&pageSize=2", _owner.AccessToken);

        //Assert
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (searched.StatusCode, paged.StatusCode));
        var found = await ReadListAsync(searched, cancellationToken);
        Assert.Equal((1, 1, 1), (found.TotalCount, found.Page, found.PageSize));
        var aliens = Assert.Single(found.Items);
        Assert.Equal("Чужі", aliens.Title);
        Assert.All(aliens.Versions.SelectMany(version => version.Files), file => Assert.Equal(FileInspectionStatus.Inspected, file.Inspection!.Status));
        var page = await ReadListAsync(paged, cancellationToken);
        Assert.Equal((3, 2, 2), (page.TotalCount, page.Page, page.PageSize));
        var last = Assert.Single(page.Items);
        Assert.All(last.Versions.SelectMany(version => version.Files), file => Assert.Equal(FileInspectionStatus.Inspected, file.Inspection!.Status));
        await _factory.WithDbAsync(async db =>
            Assert.Equal(4, await db.PhysicalFiles.CountAsync(file => file.InspectionStatus == FileInspectionStatus.Inspected, cancellationToken)));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    // The four sample movies of "Living room", stored the way a sync stores them.
    private async Task SyncLivingRoomAsync(IReadOnlyList<PathMappingPair>? pathMappings = null)
    {
        var jellyfin = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey, pathMappings: pathMappings);
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(jellyfin, _owner.User);
    }

    private static LibraryFileDto FileAt(LibraryWorkListResponse list, string servicePath) =>
        list.Items
            .SelectMany(work => work.Versions)
            .SelectMany(version => version.Files)
            .Single(file => file.ServicePath == servicePath);

    private static MediaTrackDto ToDto(MediaTrackFacts track) =>
        new(track.Kind, track.Codec, track.Profile, track.Width, track.Height, track.DynamicRange, track.Channels, track.ChannelLayout,
            track.Language, track.IsDefault, track.IsForced, track.IsHearingImpaired);

    private static async Task<LibraryWorkListResponse> ReadListAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<LibraryWorkListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
}
