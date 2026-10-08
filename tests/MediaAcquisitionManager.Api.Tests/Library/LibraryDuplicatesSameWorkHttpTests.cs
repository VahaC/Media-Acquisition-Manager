using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
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
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediaAcquisitionManager.Api.Tests.Library;

// MAM-20 — the third class of specification §3.3 on GET /api/library/duplicates (ADR 0017, ADR 0028): copies of one
// work across libraries and integrations, as two libraries' copies of one film are two works (ADR 0008). Works are
// joined by their registry keys — their own ids with the owner's links applied (MAM-34, ADR 0026), an IMDb id whatever
// the kind, a TMDb or TVDB id within its kind, an episode's show ids with its numbers, and a film item the owner linked
// as an episode by the linked show — never by a title or a year, and a work without ids never. A group holds two copies
// or more. Works whose ids disagree, and copies whose measured dynamic ranges differ, stay one group flagged for review
// with the disagreement named, each file listing its own work with its own ids (Vasyl, 2026-10-07); a group of one work's
// copies is never redundant, so it carries no estimate. MAM-37 (Vasyl, 2026-10-08) — but two episodes of one show at
// different places are never one group, whatever ids they share, nor are an episode's files whose names carry different
// segment letters ("S03E09a", "S03E09b") copies of each other, nor copies whose file names claim years more than one apart
// ("(1982)", "(2017)"), nor copies whose names say different editions ("[Theatrical Cut]", "[Extended Cut]": "це НЕ
// ДУБЛІ"); the library keeps them as it has them. Works come from real syncs through the fake Jellyfin where its samples
// hold the case, else are planted as a sync stores them; identities and dynamic ranges from real inspections through the
// fake file inventory. Access, validation, order and wire names are LibraryDuplicatesHttpTests'; names of one file
// LibraryDuplicatesSameFileHttpTests'; byte-identical files LibraryDuplicatesByteIdenticalHttpTests'; matches by title
// and year LibraryDuplicatesPossibleHttpTests'; copies whose running times differ widely (MAM-37)
// LibraryDuplicatesRunningTimesHttpTests'.
public sealed class LibraryDuplicatesSameWorkHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library/duplicates";
    private const string SameWorkUrl = $"{BaseUrl}?class=SameWork";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string Root = "/media";

    /// <summary>A second Jellyfin, beside the factory's default address.</summary>
    private const string BedroomBaseUrl = "http://127.0.0.2:9";

    // In path order: the bedroom's copies under /media/backup sort before the living room's under /media/movies.
    private const string AliensCopyPath = "/media/backup/Aliens (1986)/Aliens (1986) BDRip 1080p.mkv";
    private const string AliensPath = "/media/movies/Alien. Collection/Aliens (1986) BDRemux 1080p.mkv";
    private const string AliensImdbId = "tt0090605";
    private const string AliensTmdbId = "679";

    /// <summary>The TMDb id of another film, which a library gave a copy of "Чужі" by mistake.</summary>
    private const string OtherTmdbId = "8077";

    private const long AliensInode = 1_048_577;
    private const long CopyInode = 1_048_579;
    private const long AliensSizeBytes = 14_845_708_473;
    private const long CopySizeBytes = 9_663_676_416;

    /// <summary>The Grand Tour's ids, on each of its series items (ADR 0017: one show, several items).</summary>
    private const string GrandTourImdbId = "tt5712554";

    private const string GrandTourTmdbTvId = "68004";
    private const string GrandTourTvdbId = "314614";

    // Mickey Mouse Clubhouse's third season as production holds it.
    private const string MickeySeasonThree =
        "/media/cartoons/Mickey Mouse Clubhouse WEB-DL 1080p [Hurtom] by urij546724/Mickey Mouse Clubhouse (2010-2012) [S3] [ukr_eng] WEB-DL 1080p [Hurtom] by urij546724";

    private const string GoldenBooBooPath = $"{MickeySeasonThree}/Mickey Mouse Clubhouse_S03E31_-_The Golden Boo Boo [ukr_eng] WEB-DL 1080p [Hurtom].mkv";
    private const string DonaldPath = $"{MickeySeasonThree}/Mickey Mouse Clubhouse_S03E33_-_Donald Hatches an Egg [ukr_eng] WEB-DL 1080p [Hurtom].mkv";

    /// <summary>The ids "The Golden Boo Boo" (S03E31) carries: those of "Donald Hatches an Egg" (S03E33).</summary>
    private const string DonaldImdbId = "tt5625312";

    private const string DonaldTvdbId = "4354865";

    // The Penguins of Madagascar as production holds it, and the series' ids since it was identified.
    private const string PenguinsFolder =
        "/media/cartoons/The Penguins Of Madagascar (Season 1-3) (2008-2015) WEBDL 1080p [Ukr,Eng] Sub [Eng] [ Hurtom ] by Jatx_11";

    private const string RogerDodgerPath = $"{PenguinsFolder}/Season 1/The Penguins Of Madagascar - S01E25 - Roger Dodger WEBDL 1080p [Ukr,Eng] Sub [Eng] [ Hurtom ].mkv";
    private const string AllKingNoKingdomPath = $"{PenguinsFolder}/Season 1/The Penguins Of Madagascar - S01E37 - All King, No Kingdom WEBDL 1080p [Ukr,Eng] Sub [Eng] [ Hurtom ].mkv";
    private const string PenguinsImdbId = "tt0892700";
    private const string PenguinsTmdbTvId = "7869";
    private const string PenguinsTvdbId = "85075";

    /// <summary>The IMDb id S01E25 "Roger Dodger" and S01E37 "All King, No Kingdom" share: IMDb lists them as paired
    /// segments.</summary>
    private const string RogerDodgerImdbId = "tt1544978";

    private const string RogerDodgerTvdbId = "477811";

    // Blade Runner as production holds it: one item of two versions with the 1982 film's ids, Jellyfin's, the second
    // version's file Blade Runner 2049 — each measured as production's ffprobe read it.
    private const string FinalCutPath =
        "/media/movies/Blade Runner. Dilogy/[01] Blade Runner (1982) [The Final Cut] UHD-BDRip 1080p H.265 SDR [2xUKR_ENG] [Hurtom].mkv";

    private const string SequelPath = "/media/movies/Blade Runner. Dilogy/[02] Blade Runner 2049 (2017) [US Transfer] BDRip 1080p H.265 [UKR_ENG] [Hurtom].mkv";
    private const string BladeRunnerImdbId = "tt0083658";
    private const string BladeRunnerTmdbId = "78";
    private const long FinalCutMilliseconds = 7_051_384;
    private const long SequelMilliseconds = 9_827_808;

    // Rambo: Last Blood as production holds it: one item of two versions, a cut each.
    private const string RamboTheatricalPath =
        "/media/movies/Rambo. Collection/[05.1] Rambo. Last Blood (2019) [Theatrical Cut, US Transfer] BDRip 1080p H.265 [UKR_ENG] [Hurtom].mkv";

    private const string RamboExtendedPath =
        "/media/movies/Rambo. Collection/[05.2] Rambo. Last Blood (2019) [Extended Cut, FRA Transfer] BDRip 1080p H.265 [UKR_ENG] [Hurtom].mkv";

    private const string RamboImdbId = "tt1206885";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IAllowedRootSeeder _roots = null!;
    private ILibraryItemLinkSeeder _links = null!;
    private IMediaWorkDataFactory _works = null!;
    private IMediaVersionDataFactory _versions = null!;
    private IPhysicalFileDataFactory _physicalFiles = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private FakeJellyfinServer _jellyfin = null!;
    private FakeJellyfinServer _bedroomJellyfin = null!;
    private IntegrationConnectionEntity _livingRoom = null!;
    private IntegrationConnectionEntity _bedroom = null!;

    public LibraryDuplicatesSameWorkHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _roots = new AllowedRootSeeder(_factory.Scope);
        _links = new LibraryItemLinkSeeder(_factory.Scope);
        var db = _factory.Scope.GetRequiredService<ApplicationDbContext>();
        _works = new MediaWorkDataFactory(db, _factory.Time);
        _versions = new MediaVersionDataFactory(db, _factory.Time);
        _physicalFiles = new PhysicalFileDataFactory(db, _factory.Time);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        // Two Jellyfins, each a library of its own, holding nothing until a test says what; with no path mapping, the
        // app finds each file where its library says it is.
        _jellyfin = new FakeJellyfinServer { ApiKey = ApiKey };
        _jellyfin.Movies.Clear();
        _bedroomJellyfin = new FakeJellyfinServer(BedroomBaseUrl) { ApiKey = ApiKey };
        _bedroomJellyfin.Movies.Clear();
        FakeJellyfinServer.Arm(_factory.JellyfinHttp, _jellyfin, _bedroomJellyfin);
        _livingRoom = await integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        _bedroom = await integrations.Create("jellyfin", "Bedroom", BedroomBaseUrl, apiKey: ApiKey);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task List_groups_the_same_movie_in_two_libraries_by_its_imdb_id()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        // Two works, one per library (ADR 0008), one IMDb id between them; the bedroom's library found no TMDb id.
        var livingRoom = await PlantMovieAsync(_livingRoom, "Чужі", 1986, AliensPath, AliensImdbId, AliensTmdbId);
        var bedroom = await PlantMovieAsync(_bedroom, "Aliens", 1986, AliensCopyPath, AliensImdbId);
        _factory.Files.AddFile(AliensPath, TestMedia.File(AliensInode, AliensSizeBytes), TestMedia.Taken());
        _factory.Files.AddFile(AliensCopyPath, TestMedia.File(CopyInode, CopySizeBytes), TestMedia.Taken());
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Act
        var owners = await _factory.GetHttpRequest(SameWorkUrl, _owner.AccessToken);
        var members = await _factory.GetHttpRequest(SameWorkUrl, _member.AccessToken);

        //Assert
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (owners.StatusCode, members.StatusCode));
        var ownersBody = await owners.Content.ReadAsStringAsync(cancellationToken);
        // Nothing in it is the owner's alone: every signed-in user reads the same group.
        Assert.Equal(ownersBody, await members.Content.ReadAsStringAsync(cancellationToken));
        var list = ParseList(ownersBody);
        Assert.Equal((1, 1, 25), (list.TotalCount, list.Page, list.PageSize));
        var group = Assert.Single(list.Items);
        Assert.Equal((DuplicateClass.SameWork, false), (group.Class, group.NeedsReview));
        Assert.Matches("^[0-9a-f]{32}$", group.Key);
        Assert.Empty(group.Distinctions);
        // The key that makes them one; no identity, hash or title is this class's evidence, and no possible match's
        // agreement of durations — the copies' running times are (MAM-37), here one rip's twice.
        Assert.Equal([new DuplicateIdentifierDto(MediaIdentifierScheme.Imdb, AliensImdbId, null, null)], group.Evidence.Identifiers);
        Assert.Equal(((DuplicateIdentityDto?)null, (string?)null, (bool?)null), (group.Evidence.Identity, group.Evidence.Sha256, group.Evidence.DurationsAgree));
        Assert.Empty(group.Evidence.Titles);
        var taken = (long)TestMedia.Taken().Duration!.Value.TotalMilliseconds;
        Assert.Equal(new DuplicateRunningTimesDto(taken, taken), group.Evidence.RunningTimes);
        // Two files, each its own room; which copy to keep is the owner's call, so no estimate.
        Assert.Equal(new LibrarySpaceDto(AliensSizeBytes + CopySizeBytes, 2, 2, 0), group.Space);
        Assert.Null(group.Recoverable);
        Assert.Equal(
            [
                (AliensCopyPath, (long?)CopySizeBytes, (long?)TestMedia.DeviceId, (long?)CopyInode, (int?)1, (string?)null),
                (AliensPath, (long?)AliensSizeBytes, (long?)TestMedia.DeviceId, (long?)AliensInode, (int?)1, (string?)null),
            ],
            group.Files.Select(file => (file.File.ServicePath, file.ReadSizeBytes, file.DeviceId, file.Inode, file.LinkCount, file.Sha256)));
        Assert.All(group.Files, file => Assert.Empty(file.Editions));
        // Each file with the work of its own library, its title and its ids as that library gave them.
        Assert.Equal(
            [
                (bedroom.Work.Id, MediaWorkKind.Movie, (string?)"Aliens", (int?)1986, (LibraryEpisodeDto?)null, (string?)null, bedroom.Version.Id, (string?)null, (string?)"Bedroom"),
                (livingRoom.Work.Id, MediaWorkKind.Movie, "Чужі", 1986, null, null, livingRoom.Version.Id, null, "Living room"),
            ],
            group.Files.Select(file => Line(Assert.Single(file.Works))));
        Assert.Equal(
            [new LibraryIdentifierDto(MediaIdentifierScheme.Imdb, AliensImdbId, "Bedroom")],
            group.Files[0].Works[0].Identifiers);
        Assert.Equal(
            [
                new LibraryIdentifierDto(MediaIdentifierScheme.Imdb, AliensImdbId, "Living room"),
                new LibraryIdentifierDto(MediaIdentifierScheme.Tmdb, AliensTmdbId, "Living room"),
            ],
            group.Files[1].Works[0].Identifiers);
        // Grouping only reads: the library keeps two works, each with its own version.
        await _factory.WithDbAsync(async db =>
        {
            var workOfVersion = await db.MediaVersions.AsNoTracking().ToDictionaryAsync(version => version.Id, version => version.MediaWorkId, cancellationToken);
            Assert.Equal(
                (2, 2, bedroom.Work.Id, livingRoom.Work.Id),
                (await db.MediaWorks.CountAsync(cancellationToken), workOfVersion.Count, workOfVersion[bedroom.Version.Id], workOfVersion[livingRoom.Version.Id]));
        });
    }

    [Fact]
    public async Task List_groups_two_libraries_copies_by_a_tmdb_id_the_owner_linked()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // ADR 0026 — the bedroom's library found no ids for its copy; the owner linked the item to TMDb 679, the id the
        // living room's library gave its own.
        var livingRoom = await PlantMovieAsync(_livingRoom, "Чужі", 1986, AliensPath, tmdbId: AliensTmdbId);
        var bedroom = await PlantMovieAsync(_bedroom, "Aliens", 1986, AliensCopyPath);
        var link = await _links.Create(_bedroom, bedroom.Version.SourceItemId!, bedroom.Work.Id, MediaIdentifierScheme.Tmdb, AliensTmdbId);

        //Act
        var owners = await _factory.GetHttpRequest(SameWorkUrl, _owner.AccessToken);
        var members = await _factory.GetHttpRequest(SameWorkUrl, _member.AccessToken);

        //Assert
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (owners.StatusCode, members.StatusCode));
        var group = Assert.Single((await ReadListAsync(owners, cancellationToken)).Items);
        Assert.Equal((DuplicateClass.SameWork, false), (group.Class, group.NeedsReview));
        Assert.Empty(group.Distinctions);
        Assert.Equal([new DuplicateIdentifierDto(MediaIdentifierScheme.Tmdb, AliensTmdbId, null, null)], group.Evidence.Identifiers);
        Assert.Equal([bedroom.Work.Id, livingRoom.Work.Id], group.Files.Select(file => Assert.Single(file.Works).Id));
        // The linked id with who set it, the name for the owner only (MAM-34).
        Assert.Equal(
            [new LibraryIdentifierDto(MediaIdentifierScheme.Tmdb, AliensTmdbId, "owner", ProvenanceSourceKind.Person, link.SetUtc)],
            group.Files[0].Works[0].Identifiers);
        Assert.Equal([new LibraryIdentifierDto(MediaIdentifierScheme.Tmdb, AliensTmdbId, "Living room")], group.Files[1].Works[0].Identifiers);
        var membersGroup = Assert.Single((await ReadListAsync(members, cancellationToken)).Items);
        Assert.Equal(group.Key, membersGroup.Key);
        Assert.Equal(
            [new LibraryIdentifierDto(MediaIdentifierScheme.Tmdb, AliensTmdbId, null, ProvenanceSourceKind.Person, link.SetUtc)],
            membersGroup.Files[0].Works[0].Identifiers);
    }

    [Fact]
    public async Task List_groups_copies_by_a_linked_id_in_place_of_the_wrong_one_a_library_gave()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // MAM-34 — the bedroom's library gave its copy another film's TMDb id; the owner linked the item to 679, the
        // living room's, and the wrong id gives way while the link isn't in the library yet. The raw rows still hold it.
        var livingRoom = await PlantMovieAsync(_livingRoom, "Чужі", 1986, AliensPath, tmdbId: AliensTmdbId);
        var bedroom = await PlantMovieAsync(_bedroom, "Aliens", 1986, AliensCopyPath, tmdbId: OtherTmdbId);
        var link = await _links.Create(
            _bedroom, bedroom.Version.SourceItemId!, bedroom.Work.Id, MediaIdentifierScheme.Tmdb, AliensTmdbId, configure: link => link.SupersededValue = OtherTmdbId);

        //Act
        var response = await _factory.GetHttpRequest(SameWorkUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // The ids as every reader counts them: one TMDb id, no disagreement to review.
        var group = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal((DuplicateClass.SameWork, false), (group.Class, group.NeedsReview));
        Assert.Empty(group.Distinctions);
        Assert.Equal([new DuplicateIdentifierDto(MediaIdentifierScheme.Tmdb, AliensTmdbId, null, null)], group.Evidence.Identifiers);
        Assert.Equal([bedroom.Work.Id, livingRoom.Work.Id], group.Files.Select(file => Assert.Single(file.Works).Id));
        Assert.Equal(
            [new LibraryIdentifierDto(MediaIdentifierScheme.Tmdb, AliensTmdbId, "owner", ProvenanceSourceKind.Person, link.SetUtc)],
            group.Files[0].Works[0].Identifiers);
        await _factory.WithDbAsync(async db =>
            Assert.Equal(
                [OtherTmdbId],
                await db.MediaWorkIdentifiers.AsNoTracking()
                    .Where(identifier => identifier.MediaWorkId == bedroom.Work.Id)
                    .Select(identifier => identifier.Value)
                    .ToListAsync(cancellationToken)));
    }

    [Fact]
    public async Task List_lists_three_movie_items_with_one_imdb_id_as_one_work_never_redundant()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // ADR 0017 — The Continental S01E01, filed as a film three times in one library: three items with one IMDb id,
        // which the sync makes one work of three versions.
        var imdbId = (string)JellyfinSamples.Movie(JellyfinSamples.ContinentalId)["ProviderIds"]!["Imdb"]!;
        string[] paths =
        [
            "/media/backup/The Continental S01E01.mkv",
            "/media/movies/The Continental (2023)/The Continental (2023) S01E01 BDRip 1080p.mkv",
            "/media/usb/The Continental (2023) S01E01 WEB-DL 2160p.mkv",
        ];
        foreach (var (path, index) in paths.Select((path, index) => (path, index)))
            _jellyfin.Movies.Add(JellyfinSamples.MovieWith(ItemId((char)('a' + index)), "The Continental", null, 2023, path, imdbId));
        var run = await _factory.SyncLibraryAsync(_livingRoom.Id, _owner.AccessToken, cancellationToken);
        var workId = await WorkOfItemAsync(ItemId('a'), cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(SameWorkUrl, _member.AccessToken);

        //Assert
        Assert.Equal((LibrarySyncStatus.Succeeded, 1, 3, 3), (run.Status, run.WorksCount, run.VersionsCount, run.FilesCount));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var group = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal((DuplicateClass.SameWork, false), (group.Class, group.NeedsReview));
        Assert.Empty(group.Distinctions);
        // A one-work group's evidence is the work's own keys.
        Assert.Equal([new DuplicateIdentifierDto(MediaIdentifierScheme.Imdb, imdbId, null, null)], group.Evidence.Identifiers);
        Assert.Equal(paths, group.Files.Select(file => file.File.ServicePath));
        Assert.All(group.Files, file => Assert.Equal((workId, "The Continental", (int?)2023), (Assert.Single(file.Works).Id, file.Works[0].Title, file.Works[0].Year)));
        Assert.Equal(3, group.Files.Select(file => file.Works[0].VersionId).Distinct().Count());
        // Three copies of one work are never "redundant": no room is said to be recoverable.
        Assert.Null(group.Recoverable);
    }

    [Fact]
    public async Task List_makes_no_group_of_the_files_of_one_item_without_ids()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // ADR 0017 — Tom & Jerry Kids: one episode item holding the 65 files of its folder, no series and no ids. The
        // files are 65 different cartoons, not copies of one.
        _jellyfin.Episodes.Add(JellyfinSamples.Episode(JellyfinSamples.TomAndJerryKidsId));
        var run = await _factory.SyncLibraryAsync(_livingRoom.Id, _owner.AccessToken, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal((LibrarySyncStatus.Succeeded, 1, 65, 65), (run.Status, run.WorksCount, run.VersionsCount, run.FilesCount));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(0, list.TotalCount);
        Assert.Empty(list.Items);
    }

    [Fact]
    public async Task List_makes_no_group_of_an_episodes_a_and_b_files()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Mickey Mouse Clubhouse S03E09, "Road Rally": two items, the a and b files, with the episode's ids and place —
        // one work of two versions, each holding a file of its own. MAM-37 (Vasyl, 2026-10-08) — the a file is no copy of
        // the b file, as the segment letters of their names say.
        _jellyfin.Series.Add(JellyfinSamples.Series(JellyfinSamples.MickeyMouseClubhouseId));
        _jellyfin.Episodes.Add(JellyfinSamples.Episode(JellyfinSamples.RoadRallyAId));
        _jellyfin.Episodes.Add(JellyfinSamples.Episode(JellyfinSamples.RoadRallyBId));
        await _factory.SyncLibraryAsync(_livingRoom.Id, _owner.AccessToken, cancellationToken);
        var episodeId = await WorkOfItemAsync(JellyfinSamples.RoadRallyAId, cancellationToken);
        var halfA = (string)JellyfinSamples.Episode(JellyfinSamples.RoadRallyAId)["Path"]!;
        var halfB = (string)JellyfinSamples.Episode(JellyfinSamples.RoadRallyBId)["Path"]!;

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(0, list.TotalCount);
        Assert.Empty(list.Items);
        // Only the list leaves them out: the library still holds one episode of two versions, a file each (ADR 0017).
        Assert.Equal(episodeId, await WorkOfItemAsync(JellyfinSamples.RoadRallyBId, cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            var files = await (
                    from file in db.PhysicalFiles.AsNoTracking()
                    join version in db.MediaVersions.AsNoTracking() on file.MediaVersionId equals (Guid?)version.Id
                    select new { file.ServicePath, VersionId = version.Id, version.MediaWorkId })
                .ToListAsync(cancellationToken);
            Assert.Equal(
                [(halfA, episodeId), (halfB, episodeId)],
                files.Select(file => (file.ServicePath, file.MediaWorkId)).OrderBy(file => file.ServicePath, StringComparer.Ordinal));
            Assert.Equal(2, files.Select(file => file.VersionId).Distinct().Count());
        });
    }

    [Fact]
    public async Task List_makes_no_group_of_two_episodes_with_one_episodes_ids_under_different_numbers()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // ADR 0017 — "The Golden Boo Boo" (S03E31) carries the ids of "Donald Hatches an Egg" (S03E33), and production's
        // library titles both "Donald Hatches an Egg": two works, as their places differ, one episode's ids between them.
        // MAM-37 (Vasyl, 2026-10-08) — two episodes: never one group, nor a possible match by their one title and year.
        _jellyfin.Series.Add(JellyfinSamples.Series(JellyfinSamples.MickeyMouseClubhouseId));
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith(
            ItemId('c'), "Donald Hatches an Egg", JellyfinSamples.MickeyMouseClubhouseId, 3, 31, GoldenBooBooPath, DonaldImdbId, DonaldTvdbId));
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith(
            ItemId('d'), "Donald Hatches an Egg", JellyfinSamples.MickeyMouseClubhouseId, 3, 33, DonaldPath, DonaldImdbId, DonaldTvdbId));
        await _factory.SyncLibraryAsync(_livingRoom.Id, _owner.AccessToken, cancellationToken);
        var goldenBooBoo = await WorkOfItemAsync(ItemId('c'), cancellationToken);
        var donald = await WorkOfItemAsync(ItemId('d'), cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // No class lists them: no copies of one work, and two episodes the library places apart are no possible match.
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(0, list.TotalCount);
        Assert.Empty(list.Items);
        Assert.NotEqual(goldenBooBoo, donald);
    }

    [Fact]
    public async Task List_makes_no_group_of_two_episodes_of_one_show_at_different_places_under_one_imdb_id()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The Penguins of Madagascar's S01E25 and S01E37: one IMDb id, a TVDB id each, two places.
        AddPenguinsPair();
        await _factory.SyncLibraryAsync(_livingRoom.Id, _owner.AccessToken, cancellationToken);
        var rogerDodger = await WorkOfItemAsync(ItemId('a'), cancellationToken);
        var allKingNoKingdom = await WorkOfItemAsync(ItemId('b'), cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(0, list.TotalCount);
        Assert.Empty(list.Items);
        // Two works in the library, each with the IMDb id they share.
        await _factory.WithDbAsync(async db =>
        {
            var holders = await db.MediaWorkIdentifiers.AsNoTracking()
                .Where(identifier => identifier.Value == RogerDodgerImdbId)
                .Select(identifier => identifier.MediaWorkId)
                .Distinct()
                .ToListAsync(cancellationToken);
            Assert.Equal(new[] { rogerDodger, allKingNoKingdom }.Order(), holders.Order());
        });
    }

    [Fact]
    public async Task List_groups_one_episodes_copies_and_leaves_another_episode_under_its_imdb_id_out()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The pair in the living room, and the bedroom's copy of S01E25 with its ids.
        const string copyPath = "/media/backup/The Penguins of Madagascar/Season 1/The Penguins Of Madagascar - S01E25 - Roger Dodger.mkv";
        AddPenguinsPair();
        _bedroomJellyfin.Series.Add(JellyfinSamples.SeriesWith(
            ItemId('2'), "The Penguins of Madagascar", 2008, "/media/backup/The Penguins of Madagascar", PenguinsImdbId, PenguinsTmdbTvId, PenguinsTvdbId));
        _bedroomJellyfin.Episodes.Add(JellyfinSamples.EpisodeWith(
            ItemId('c'), "Roger Dodger", ItemId('2'), 1, 25, copyPath, RogerDodgerImdbId, RogerDodgerTvdbId));
        await _factory.SyncLibraryAsync(_livingRoom.Id, _owner.AccessToken, cancellationToken);
        await _factory.SyncLibraryAsync(_bedroom.Id, _owner.AccessToken, cancellationToken);
        var livingRoom = await WorkOfItemAsync(ItemId('a'), cancellationToken);
        var bedroom = await WorkOfItemAsync(ItemId('c'), cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // S01E25's two copies, nothing to review; S01E37, under the same IMDb id, in no group.
        var group = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal((DuplicateClass.SameWork, false), (group.Class, group.NeedsReview));
        Assert.Empty(group.Distinctions);
        Assert.Equal(
            [(copyPath, bedroom), (RogerDodgerPath, livingRoom)],
            group.Files.Select(file => (file.File.ServicePath, Assert.Single(file.Works).Id)));
        Assert.Equal(
            [
                new DuplicateIdentifierDto(MediaIdentifierScheme.Imdb, RogerDodgerImdbId, null, null),
                new DuplicateIdentifierDto(MediaIdentifierScheme.Imdb, PenguinsImdbId, 1, 25),
                new DuplicateIdentifierDto(MediaIdentifierScheme.Tvdb, RogerDodgerTvdbId, null, null),
                new DuplicateIdentifierDto(MediaIdentifierScheme.Tvdb, PenguinsTvdbId, 1, 25),
                new DuplicateIdentifierDto(MediaIdentifierScheme.TmdbTv, PenguinsTmdbTvId, 1, 25),
            ],
            group.Evidence.Identifiers);
    }

    [Fact]
    public async Task List_joins_a_film_item_carrying_the_imdb_id_of_two_episodes_to_neither()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The bedroom files a cartoon as a film, with the IMDb id the Penguins pair shares: it may be either episode, so
        // it is a copy of neither — an item that fits two works joins neither (LibraryWorkGrouping's rule).
        AddPenguinsPair();
        await _factory.SyncLibraryAsync(_livingRoom.Id, _owner.AccessToken, cancellationToken);
        await PlantMovieAsync(_bedroom, "Roger Dodger", 2008, "/media/backup/The Penguins of Madagascar - Roger Dodger.mkv", RogerDodgerImdbId);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(0, list.TotalCount);
        Assert.Empty(list.Items);
    }

    [Fact]
    public async Task List_makes_no_group_of_one_work_the_library_holds_at_two_places_of_a_show()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A show the library knows by no id, and S03E31 with every id of S03E33: with no show ids to tell them apart, the
        // sync makes the two episodes one work at two places (ADR 0017). MAM-37 — still two episodes, never a group.
        _jellyfin.Series.Add(JellyfinSamples.SeriesWith(ItemId('1'), "Mickey Mouse Clubhouse", 2006, "/media/cartoons/Mickey Mouse Clubhouse"));
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith(ItemId('c'), "Donald Hatches an Egg", ItemId('1'), 3, 31, GoldenBooBooPath, DonaldImdbId, DonaldTvdbId));
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith(ItemId('d'), "Donald Hatches an Egg", ItemId('1'), 3, 33, DonaldPath, DonaldImdbId, DonaldTvdbId));
        await _factory.SyncLibraryAsync(_livingRoom.Id, _owner.AccessToken, cancellationToken);
        var episodeId = await WorkOfItemAsync(ItemId('c'), cancellationToken);
        var seriesId = await WorkOfItemAsync(ItemId('1'), cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        // What the case needs: one work, at two places of one show.
        Assert.Equal(episodeId, await WorkOfItemAsync(ItemId('d'), cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            var places = await db.MediaEpisodePositions.AsNoTracking()
                .Where(position => position.MediaWorkId == episodeId)
                .Select(position => new { position.SeriesWorkId, position.SeasonNumber, position.EpisodeNumber })
                .ToListAsync(cancellationToken);
            Assert.Equal(
                [((Guid?)seriesId, (int?)3, (int?)31), (seriesId, 3, 33)],
                places.Select(place => (place.SeriesWorkId, place.SeasonNumber, place.EpisodeNumber)).OrderBy(place => place.EpisodeNumber));
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(0, list.TotalCount);
        Assert.Empty(list.Items);
    }

    [Fact]
    public async Task List_groups_two_films_carrying_the_ids_of_a_work_at_two_places_without_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The living room's one work at two places of a show without ids, as above, and two film items in the bedroom with
        // S03E33's IMDb id. MAM-37 — that work is a copy of nothing, so the films don't join it, and are each other's copy.
        const string firstFilmPath = "/media/backup/Donald Hatches an Egg (2012).mkv";
        const string secondFilmPath = "/media/backup/Mickey Mouse Clubhouse - Donald Hatches an Egg.mkv";
        _jellyfin.Series.Add(JellyfinSamples.SeriesWith(ItemId('1'), "Mickey Mouse Clubhouse", 2006, "/media/cartoons/Mickey Mouse Clubhouse"));
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith(ItemId('c'), "Donald Hatches an Egg", ItemId('1'), 3, 31, GoldenBooBooPath, DonaldImdbId, DonaldTvdbId));
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith(ItemId('d'), "Donald Hatches an Egg", ItemId('1'), 3, 33, DonaldPath, DonaldImdbId, DonaldTvdbId));
        await _factory.SyncLibraryAsync(_livingRoom.Id, _owner.AccessToken, cancellationToken);
        var episodeId = await WorkOfItemAsync(ItemId('c'), cancellationToken);
        var firstFilm = await PlantMovieAsync(_bedroom, "Donald Hatches an Egg", 2012, firstFilmPath, DonaldImdbId);
        var secondFilm = await PlantMovieAsync(_bedroom, "Donald Hatches an Egg", 2012, secondFilmPath, DonaldImdbId);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        // What the case needs: the two episodes are one work.
        Assert.Equal(episodeId, await WorkOfItemAsync(ItemId('d'), cancellationToken));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // One group in every class: the films' copies, by the IMDb id they share; the work's files in none.
        var group = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal((DuplicateClass.SameWork, false), (group.Class, group.NeedsReview));
        Assert.Empty(group.Distinctions);
        Assert.Equal([new DuplicateIdentifierDto(MediaIdentifierScheme.Imdb, DonaldImdbId, null, null)], group.Evidence.Identifiers);
        Assert.Equal(
            [(firstFilmPath, firstFilm.Work.Id), (secondFilmPath, secondFilm.Work.Id)],
            group.Files.Select(file => (file.File.ServicePath, Assert.Single(file.Works).Id)));
    }

    [Fact]
    public async Task List_groups_each_segment_of_an_episode_with_its_copy_in_another_library()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Road Rally's a and b files in the living room, and a copy of each in the bedroom, with the episode's ids and
        // place: each file is a copy of the other library's file of its letter, and of nothing else.
        const string copyA = "/media/backup/Mickey Mouse Clubhouse/Mickey Mouse Clubhouse_S03E09a_-_Road Rally [ukr_eng] WEB-DL 1080p [Hurtom].mkv";
        const string copyB = "/media/backup/Mickey Mouse Clubhouse/Mickey Mouse Clubhouse_S03E09b_-_Road Rally [ukr_eng] WEB-DL 1080p [Hurtom].mkv";
        _jellyfin.Series.Add(JellyfinSamples.Series(JellyfinSamples.MickeyMouseClubhouseId));
        _jellyfin.Episodes.Add(JellyfinSamples.Episode(JellyfinSamples.RoadRallyAId));
        _jellyfin.Episodes.Add(JellyfinSamples.Episode(JellyfinSamples.RoadRallyBId));
        _bedroomJellyfin.Series.Add(JellyfinSamples.SeriesWith(
            ItemId('3'), "Mickey Mouse Clubhouse", 2006, "/media/backup/Mickey Mouse Clubhouse", "tt0784896", "3934", "79854"));
        _bedroomJellyfin.Episodes.Add(JellyfinSamples.EpisodeWith(ItemId('e'), "Road Rally", ItemId('3'), 3, 9, copyA, "tt1715023", "2804541"));
        _bedroomJellyfin.Episodes.Add(JellyfinSamples.EpisodeWith(ItemId('f'), "Road Rally", ItemId('3'), 3, 9, copyB, "tt1715023", "2804541"));
        await _factory.SyncLibraryAsync(_livingRoom.Id, _owner.AccessToken, cancellationToken);
        await _factory.SyncLibraryAsync(_bedroom.Id, _owner.AccessToken, cancellationToken);
        var livingRoom = await WorkOfItemAsync(JellyfinSamples.RoadRallyAId, cancellationToken);
        var bedroom = await WorkOfItemAsync(ItemId('e'), cancellationToken);
        var halfA = (string)JellyfinSamples.Episode(JellyfinSamples.RoadRallyAId)["Path"]!;
        var halfB = (string)JellyfinSamples.Episode(JellyfinSamples.RoadRallyBId)["Path"]!;

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Two groups of the same two works, one per letter, each with a key of its own; nothing to review.
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(2, list.TotalCount);
        Assert.All(list.Items, group => Assert.Equal((DuplicateClass.SameWork, false), (group.Class, group.NeedsReview)));
        Assert.All(list.Items, group => Assert.Empty(group.Distinctions));
        Assert.NotEqual(list.Items[0].Key, list.Items[1].Key);
        Assert.Equal(
            [[(copyA, bedroom), (halfA, livingRoom)], [(copyB, bedroom), (halfB, livingRoom)]],
            list.Items
                .Select(group => group.Files.Select(file => (file.File.ServicePath, Assert.Single(file.Works).Id)).ToList())
                .OrderBy(files => files[0].ServicePath, StringComparer.Ordinal));
    }

    [Fact]
    public async Task List_gives_each_segment_group_its_own_running_times()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        // The libraries above, each file measured: the a files run 11 minutes, the b files 7. MAM-37 — a group's running
        // times are its own letter's copies'; the four together would flag both groups, as 7 minutes is under three
        // quarters of 11.
        const string copyA = "/media/backup/Mickey Mouse Clubhouse/Mickey Mouse Clubhouse_S03E09a_-_Road Rally [ukr_eng] WEB-DL 1080p [Hurtom].mkv";
        const string copyB = "/media/backup/Mickey Mouse Clubhouse/Mickey Mouse Clubhouse_S03E09b_-_Road Rally [ukr_eng] WEB-DL 1080p [Hurtom].mkv";
        const long aMilliseconds = 660_000;
        const long bMilliseconds = 420_000;
        _jellyfin.Series.Add(JellyfinSamples.Series(JellyfinSamples.MickeyMouseClubhouseId));
        _jellyfin.Episodes.Add(JellyfinSamples.Episode(JellyfinSamples.RoadRallyAId));
        _jellyfin.Episodes.Add(JellyfinSamples.Episode(JellyfinSamples.RoadRallyBId));
        _bedroomJellyfin.Series.Add(JellyfinSamples.SeriesWith(
            ItemId('3'), "Mickey Mouse Clubhouse", 2006, "/media/backup/Mickey Mouse Clubhouse", "tt0784896", "3934", "79854"));
        _bedroomJellyfin.Episodes.Add(JellyfinSamples.EpisodeWith(ItemId('e'), "Road Rally", ItemId('3'), 3, 9, copyA, "tt1715023", "2804541"));
        _bedroomJellyfin.Episodes.Add(JellyfinSamples.EpisodeWith(ItemId('f'), "Road Rally", ItemId('3'), 3, 9, copyB, "tt1715023", "2804541"));
        await _factory.SyncLibraryAsync(_livingRoom.Id, _owner.AccessToken, cancellationToken);
        await _factory.SyncLibraryAsync(_bedroom.Id, _owner.AccessToken, cancellationToken);
        var halfA = (string)JellyfinSamples.Episode(JellyfinSamples.RoadRallyAId)["Path"]!;
        var halfB = (string)JellyfinSamples.Episode(JellyfinSamples.RoadRallyBId)["Path"]!;
        Measure(halfA, 1_048_577, aMilliseconds);
        Measure(copyA, 1_048_579, aMilliseconds);
        Measure(halfB, 1_048_581, bMilliseconds);
        Measure(copyB, 1_048_583, bMilliseconds);
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(SameWorkUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // One group per letter, each with its own letter's files and running times, nothing to review.
        List<LibraryDuplicateGroupDto> groups =
        [
            .. (await ReadListAsync(response, cancellationToken)).Items.OrderBy(group => group.Files[0].File.ServicePath, StringComparer.Ordinal),
        ];
        Assert.Equal(2, groups.Count);
        Assert.All(groups, group => Assert.Equal((DuplicateClass.SameWork, false), (group.Class, group.NeedsReview)));
        Assert.All(groups, group => Assert.Empty(group.Distinctions));
        Assert.Equal([[copyA, halfA], [copyB, halfB]], groups.Select(group => group.Files.Select(file => file.File.ServicePath).ToList()));
        Assert.Equal(
            [new DuplicateRunningTimesDto(aMilliseconds, aMilliseconds), new DuplicateRunningTimesDto(bMilliseconds, bMilliseconds)],
            groups.Select(group => group.Evidence.RunningTimes));
    }

    [Fact]
    public async Task List_keeps_an_episodes_whole_file_apart_from_its_segment_file()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // MAM-37 — one episode by its ids and place in both libraries, a whole file in one and the "a" file in the
        // other: a segment is no copy of the whole episode.
        var livingRoomSeries = await PlantSeriesAsync(_livingRoom, "Mickey Mouse Clubhouse", 2006, "3934");
        var bedroomSeries = await PlantSeriesAsync(_bedroom, "Mickey Mouse Clubhouse", 2006, "3934");
        await PlantEpisodeAsync(
            _livingRoom, livingRoomSeries, 3, 9, "Road Rally", $"{MickeySeasonThree}/Mickey Mouse Clubhouse_S03E09_-_Road Rally [ukr_eng] WEB-DL 1080p [Hurtom].mkv", "tt1715023");
        await PlantEpisodeAsync(
            _bedroom, bedroomSeries, 3, 9, "Road Rally", "/media/backup/Mickey Mouse Clubhouse/Mickey Mouse Clubhouse_S03E09a_-_Road Rally [ukr_eng] WEB-DL 1080p [Hurtom].mkv", "tt1715023");

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(0, list.TotalCount);
        Assert.Empty(list.Items);
    }

    [Fact]
    public async Task List_makes_no_group_of_a_films_versions_whose_names_claim_years_far_apart()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        // MAM-37 (Vasyl, 2026-10-08: "Blade Runner 2049 взагалі не має бути в дублікатах") — Blade Runner as production holds
        // it: one work of two versions under the 1982 film's ids. Two films, neither a copy of the other, whatever the
        // ids say, and both rules keep the pair apart: the years the names claim, 1982 and 2017, and the editions they
        // say, The Final Cut and none. As one group, they would have been flagged for their running times, 71.7 %.
        var film = await PlantMovieAsync(_livingRoom, "Той, хто біжить по лезу", 1982, FinalCutPath, BladeRunnerImdbId, BladeRunnerTmdbId);
        var sequel = await PlantVersionAsync(film, SequelPath);
        Measure(FinalCutPath, 1_048_577, FinalCutMilliseconds);
        Measure(SequelPath, 1_048_579, SequelMilliseconds);
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // In no class at all.
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(0, list.TotalCount);
        Assert.Empty(list.Items);
        // Only the list leaves them out: the library still holds one work of two versions, a measured file each.
        await _factory.WithDbAsync(async db =>
        {
            var files = await (
                    from file in db.PhysicalFiles.AsNoTracking()
                    join version in db.MediaVersions.AsNoTracking() on file.MediaVersionId equals (Guid?)version.Id
                    select new { file.ServicePath, VersionId = version.Id, version.MediaWorkId, file.InspectionStatus })
                .ToListAsync(cancellationToken);
            Assert.Equal(
                [
                    (FinalCutPath, film.Version.Id, film.Work.Id, FileInspectionStatus.Inspected),
                    (SequelPath, sequel.Id, film.Work.Id, FileInspectionStatus.Inspected),
                ],
                files.Select(file => (file.ServicePath, file.VersionId, file.MediaWorkId, file.InspectionStatus)).OrderBy(file => file.ServicePath, StringComparer.Ordinal));
        });
    }

    [Fact]
    public async Task List_makes_no_group_of_two_films_under_one_id_whose_names_claim_years_far_apart()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // MAM-37 — the same two films as two items of the library, the sequel's given the 1982 film's ids, and names that
        // say no edition, so only the years they claim keep them apart: still two films.
        const string filmPath = "/media/movies/Blade Runner (1982)/Blade Runner (1982).mkv";
        const string sequelPath = "/media/movies/Blade Runner 2049 (2017)/Blade Runner 2049 (2017).mkv";
        await PlantMovieAsync(_livingRoom, "Той, хто біжить по лезу", 1982, filmPath, BladeRunnerImdbId, BladeRunnerTmdbId);
        await PlantMovieAsync(_livingRoom, "Той, хто біжить по лезу 2049", 2017, sequelPath, BladeRunnerImdbId, BladeRunnerTmdbId);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(0, list.TotalCount);
        Assert.Empty(list.Items);
    }

    [Fact]
    public async Task List_keeps_a_films_versions_one_group_when_a_year_shares_its_bracket_with_a_word()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // MAM-37 — Seksmisja, one work of two versions: one name claiming 1984, and the remux's a year far from it that
        // shares its bracket with a word, "[2005 DVDRemux]". A year beside a word in a bracket is a release's as often as
        // the film's, so the remux's name claims none, and nothing is split; read as 2005, it would part the copies.
        // Production's "[1983 DVDRemux]" stays one group either way, as 1983 is a year from 1984.
        const string releasePath = "/media/movies/Seksmisja (1984)/Seksmisja (1984).mp4";
        const string remuxPath = "/media/movies/Seksmisja [2005 DVDRemux].mkv";
        var film = await PlantMovieAsync(_livingRoom, "Сексмісія", 1984, releasePath, "tt0088083");
        var remux = await PlantVersionAsync(film, remuxPath);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var group = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal((DuplicateClass.SameWork, false), (group.Class, group.NeedsReview));
        Assert.Empty(group.Distinctions);
        Assert.Equal(
            [(releasePath, film.Version.Id), (remuxPath, remux.Id)],
            group.Files.Select(file => (file.File.ServicePath, Assert.Single(file.Works).VersionId)));
        // Nothing split, so the key the group had before MAM-37, the one the Duplicates page addresses it by.
        Assert.Equal(KeyOf($"SameWork|{film.Work.Id:D}"), group.Key);
    }

    [Fact]
    public async Task List_keeps_copies_whose_names_claim_a_run_of_years_one_apart_one_group_under_its_key()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // MAM-37 — one work of three versions whose names claim 1982, 1983 and 1984: each a year from the next, so one run
        // of years, though its ends are two apart — no cut of it would keep every two copies a year apart together.
        const string firstPath = "/media/movies/Koyaanisqatsi (1982).mkv";
        const string secondPath = "/media/movies/Koyaanisqatsi (1983).mkv";
        const string thirdPath = "/media/movies/Koyaanisqatsi (1984).mkv";
        var film = await PlantMovieAsync(_livingRoom, "Кояніскаці", 1982, firstPath, "tt0085809");
        var second = await PlantVersionAsync(film, secondPath);
        var third = await PlantVersionAsync(film, thirdPath);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var group = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal((DuplicateClass.SameWork, false), (group.Class, group.NeedsReview));
        Assert.Empty(group.Distinctions);
        Assert.Equal(
            [(firstPath, film.Version.Id), (secondPath, second.Id), (thirdPath, third.Id)],
            group.Files.Select(file => (file.File.ServicePath, Assert.Single(file.Works).VersionId)));
        Assert.Equal(KeyOf($"SameWork|{film.Work.Id:D}"), group.Key);
    }

    [Fact]
    public async Task List_takes_a_copy_whose_parts_claim_years_far_apart_as_claiming_none()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // MAM-37 — one work of two versions: production's WEB-DL, claiming 1980, and a copy in two parts whose second
        // part's name claims the sequel's 1989. A copy whose names claim years further apart than one film's is no single
        // year's: it claims none, so 1980 is the only year, nothing is split, and the copies are one group.
        const string webPath = "/media/movies/The Gods Must Be Crazy (1980) [2xUkr,Eng sub.Eng] WEB-DL 1080p [Hurtom].mkv";
        const string firstPartPath = "/media/backup/The Gods Must Be Crazy/The Gods Must Be Crazy (1980) CD1.mkv";
        const string secondPartPath = "/media/backup/The Gods Must Be Crazy/The Gods Must Be Crazy (1989) CD2.mkv";
        var film = await PlantMovieAsync(_livingRoom, "Мабуть, Боги з'їхали з глузду", 1980, webPath, "tt0080801");
        var parts = await PlantVersionAsync(film, firstPartPath);
        await _physicalFiles.Create(parts.Id, secondPartPath, sourceConnectionId: _livingRoom.Id, sourceName: _livingRoom.Name);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var group = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal((DuplicateClass.SameWork, false), (group.Class, group.NeedsReview));
        Assert.Equal(
            [(firstPartPath, parts.Id), (secondPartPath, parts.Id), (webPath, film.Version.Id)],
            group.Files.Select(file => (file.File.ServicePath, Assert.Single(file.Works).VersionId)));
        // No "|Years:1980": the second part's 1989 split nothing.
        Assert.Equal(KeyOf($"SameWork|{film.Work.Id:D}"), group.Key);
    }

    [Fact]
    public async Task List_keeps_a_copy_whose_name_claims_no_year_with_the_films_copy_under_its_key()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        // MAM-37 — «Ніж у воді» as production holds it: one work of two versions, the film's name claiming 1962 and the
        // featurette's no year. Vasyl, 2026-10-08: "це дубль" — still one group, flagged by the running times alone.
        const string filmPath = "/media/movies/Nóż w wodzie (1962) Subs UA/Nóż w wodzie (1962).mkv";
        const string featurettePath = "/media/movies/Nóż w wodzie (1962) Subs UA/Polanski+Skolimowski o Noże.mkv";
        const long filmMilliseconds = 5_731_959;
        const long featuretteMilliseconds = 1_600_699;
        var film = await PlantMovieAsync(_livingRoom, "Ніж у воді", 1962, filmPath, "tt0056291", "11502");
        var featurette = await PlantVersionAsync(film, featurettePath);
        Measure(filmPath, 1_048_577, filmMilliseconds);
        Measure(featurettePath, 1_048_579, featuretteMilliseconds);
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var group = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal((DuplicateClass.SameWork, true), (group.Class, group.NeedsReview));
        Assert.Equal([new DuplicateDistinctionDto(DuplicateDistinctionKind.RunningTimes, null, false)], group.Distinctions);
        Assert.Equal(new DuplicateRunningTimesDto(featuretteMilliseconds, filmMilliseconds), group.Evidence.RunningTimes);
        Assert.Equal(
            [(filmPath, film.Version.Id), (featurettePath, featurette.Id)],
            group.Files.Select(file => (file.File.ServicePath, Assert.Single(file.Works).VersionId)));
        // A name that claims no year splits nothing: the key it had before MAM-37.
        Assert.Equal(KeyOf($"SameWork|{film.Work.Id:D}"), group.Key);
    }

    [Fact]
    public async Task List_groups_copies_by_the_years_their_names_claim_and_leaves_one_claiming_none_out()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // MAM-37 — production's Blade Runner item in the living room, and in the bedroom another item with its ids of three
        // versions: a copy of each film and a documentary whose name claims no year. Each film's copies are a group of
        // their own; the documentary can be placed with neither, and is a copy of nothing.
        const string finalCutCopyPath = "/media/backup/Blade Runner (1982) [The Final Cut].mkv";
        const string sequelCopyPath = "/media/backup/Blade Runner 2049 (2017).mkv";
        const string documentaryPath = "/media/backup/Dangerous Days. Making Blade Runner.mkv";
        var livingRoom = await PlantMovieAsync(_livingRoom, "Той, хто біжить по лезу", 1982, FinalCutPath, BladeRunnerImdbId, BladeRunnerTmdbId);
        await PlantVersionAsync(livingRoom, SequelPath);
        var bedroom = await PlantMovieAsync(_bedroom, "Blade Runner", 1982, finalCutCopyPath, BladeRunnerImdbId, BladeRunnerTmdbId);
        await PlantVersionAsync(bedroom, sequelCopyPath);
        await PlantVersionAsync(bedroom, documentaryPath);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        List<LibraryDuplicateGroupDto> groups =
        [
            .. (await ReadListAsync(response, cancellationToken)).Items.OrderBy(group => group.Files[0].File.ServicePath, StringComparer.Ordinal),
        ];
        Assert.Equal(2, groups.Count);
        Assert.All(groups, group => Assert.Equal((DuplicateClass.SameWork, false), (group.Class, group.NeedsReview)));
        Assert.All(groups, group => Assert.Empty(group.Distinctions));
        Assert.Equal(
            [[(finalCutCopyPath, bedroom.Work.Id), (FinalCutPath, livingRoom.Work.Id)], [(sequelCopyPath, bedroom.Work.Id), (SequelPath, livingRoom.Work.Id)]],
            groups.Select(group => group.Files.Select(file => (file.File.ServicePath, Assert.Single(file.Works).Id)).ToList()));
        // Each named by its years, so neither takes the key the two works' one group would have.
        var works = string.Join('|', new[] { livingRoom.Work.Id, bedroom.Work.Id }.Select(id => id.ToString("D")).Order(StringComparer.Ordinal));
        Assert.Equal([KeyOf($"SameWork|{works}|Years:1982"), KeyOf($"SameWork|{works}|Years:2017")], groups.Select(group => group.Key));
    }

    [Fact]
    public async Task List_groups_the_copies_claiming_no_year_beside_two_clusters_as_a_part_of_their_own()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // MAM-37 — as above, with the documentary in both libraries: its two copies claim no year, so they can be placed
        // with neither film, and are each other's copies — a third group beside the films'.
        const string finalCutCopyPath = "/media/backup/Blade Runner (1982) [The Final Cut].mkv";
        const string sequelCopyPath = "/media/backup/Blade Runner 2049 (2017).mkv";
        const string documentaryCopyPath = "/media/backup/Dangerous Days. Making Blade Runner.mkv";
        const string documentaryPath = "/media/movies/Blade Runner. Dilogy/Dangerous Days. Making Blade Runner.mkv";
        var livingRoom = await PlantMovieAsync(_livingRoom, "Той, хто біжить по лезу", 1982, FinalCutPath, BladeRunnerImdbId, BladeRunnerTmdbId);
        await PlantVersionAsync(livingRoom, SequelPath);
        await PlantVersionAsync(livingRoom, documentaryPath);
        var bedroom = await PlantMovieAsync(_bedroom, "Blade Runner", 1982, finalCutCopyPath, BladeRunnerImdbId, BladeRunnerTmdbId);
        await PlantVersionAsync(bedroom, sequelCopyPath);
        await PlantVersionAsync(bedroom, documentaryCopyPath);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        List<LibraryDuplicateGroupDto> groups =
        [
            .. (await ReadListAsync(response, cancellationToken)).Items.OrderBy(group => group.Files[0].File.ServicePath, StringComparer.Ordinal),
        ];
        Assert.Equal(3, groups.Count);
        Assert.All(groups, group => Assert.Equal((DuplicateClass.SameWork, false), (group.Class, group.NeedsReview)));
        Assert.Equal(
            [[finalCutCopyPath, FinalCutPath], [sequelCopyPath, SequelPath], [documentaryCopyPath, documentaryPath]],
            groups.Select(group => group.Files.Select(file => file.File.ServicePath).ToList()));
        // The copies that claim no year named "none", beside the films' years.
        var works = string.Join('|', new[] { livingRoom.Work.Id, bedroom.Work.Id }.Select(id => id.ToString("D")).Order(StringComparer.Ordinal));
        Assert.Equal(
            [KeyOf($"SameWork|{works}|Years:1982"), KeyOf($"SameWork|{works}|Years:2017"), KeyOf($"SameWork|{works}|Years:none")],
            groups.Select(group => group.Key));
    }

    [Fact]
    public async Task List_keeps_an_episodes_copies_one_group_whatever_years_their_names_claim()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // MAM-37 — one episode by its ids and place in both libraries, whose names claim years two apart: the living
        // room's 2025, its show's, as production's "S01E01. MobLand (2025). …" names do, and the bedroom's 2027, its
        // season's. An episode's year is its show's or its season's, never its own, and its place says which episode it
        // is: the years that part a film's copies split an episode's not at all.
        const string livingRoomPath = "/media/tv-shows/MobLand (2025)/Season 2/S02E01. MobLand (2025).mkv";
        const string bedroomPath = "/media/backup/MobLand/MobLand (2027) - S02E01.mkv";
        var livingRoomSeries = await PlantSeriesAsync(_livingRoom, "MobLand", 2025, "100001");
        var bedroomSeries = await PlantSeriesAsync(_bedroom, "MobLand", 2025, "100001");
        var livingRoom = await PlantEpisodeAsync(_livingRoom, livingRoomSeries, 2, 1, "Episode 1", livingRoomPath, "tt1000001");
        var bedroom = await PlantEpisodeAsync(_bedroom, bedroomSeries, 2, 1, "Episode 1", bedroomPath, "tt1000001");

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var group = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal((DuplicateClass.SameWork, false), (group.Class, group.NeedsReview));
        Assert.Empty(group.Distinctions);
        Assert.Equal(
            [(bedroomPath, bedroom.Work.Id), (livingRoomPath, livingRoom.Work.Id)],
            group.Files.Select(file => (file.File.ServicePath, Assert.Single(file.Works).Id)));
        // No "|Years:": the key the two works' group always had.
        var works = string.Join('|', new[] { livingRoom.Work.Id, bedroom.Work.Id }.Select(id => id.ToString("D")).Order(StringComparer.Ordinal));
        Assert.Equal(KeyOf($"SameWork|{works}"), group.Key);
    }

    [Fact]
    public async Task List_flags_two_works_with_one_imdb_id_and_different_tmdb_ids_for_review_and_lists_their_files_apart()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // One IMDb id, two TMDb ids: one of the libraries is wrong, and which one is the owner's to say.
        var livingRoom = await PlantMovieAsync(_livingRoom, "Чужі", 1986, AliensPath, AliensImdbId, AliensTmdbId);
        var bedroom = await PlantMovieAsync(_bedroom, "Aliens", 1986, AliensCopyPath, AliensImdbId, OtherTmdbId);

        //Act
        var response = await _factory.GetHttpRequest(SameWorkUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        // Vasyl, 2026-10-07 — still the same work, not a possible match nor a class of its own: flagged, with the
        // registry that disagrees.
        var group = Assert.Single(list.Items);
        Assert.Equal((DuplicateClass.SameWork, true), (group.Class, group.NeedsReview));
        Assert.Equal([new DuplicateDistinctionDto(DuplicateDistinctionKind.IdentifierConflict, MediaIdentifierScheme.Tmdb, false)], group.Distinctions);
        Assert.Equal([new DuplicateIdentifierDto(MediaIdentifierScheme.Imdb, AliensImdbId, null, null)], group.Evidence.Identifiers);
        // Each file with its own work's ids, so the owner sees which TMDb id each library gave.
        Assert.Equal([bedroom.Work.Id, livingRoom.Work.Id], group.Files.Select(file => Assert.Single(file.Works).Id));
        Assert.Equal(
            [
                new LibraryIdentifierDto(MediaIdentifierScheme.Imdb, AliensImdbId, "Bedroom"),
                new LibraryIdentifierDto(MediaIdentifierScheme.Tmdb, OtherTmdbId, "Bedroom"),
            ],
            group.Files[0].Works[0].Identifiers);
        Assert.Equal(
            [
                new LibraryIdentifierDto(MediaIdentifierScheme.Imdb, AliensImdbId, "Living room"),
                new LibraryIdentifierDto(MediaIdentifierScheme.Tmdb, AliensTmdbId, "Living room"),
            ],
            group.Files[1].Works[0].Identifiers);
        // Not looked at yet: two names whose room isn't known.
        Assert.Equal(new LibrarySpaceDto(0, 0, 0, 2), group.Space);
        Assert.Null(group.Recoverable);
    }

    [Fact]
    public async Task List_groups_the_episodes_of_a_series_the_library_knows_as_two_series_items()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // ADR 0017 — the living room's library knows The Grand Tour as two series items, one per season folder; the
        // bedroom's as one. Neither episode file has ids of its own: each is known by its show's ids with its numbers.
        _jellyfin.Series.Add(JellyfinSamples.SeriesWith(
            ItemId('1'), "The Grand Tour", 2016, "/media/tv-shows/The Grand Tour S01", GrandTourImdbId, GrandTourTmdbTvId, GrandTourTvdbId));
        _jellyfin.Series.Add(JellyfinSamples.SeriesWith(
            ItemId('2'), "The Grand Tour", 2016, "/media/tv-shows/The Grand Tour S02", GrandTourImdbId, GrandTourTmdbTvId, GrandTourTvdbId));
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith(
            ItemId('a'), "The Holy Trinity", ItemId('1'), 1, 1, "/media/tv-shows/The Grand Tour S01/The Grand Tour S01E01.mkv"));
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith(
            ItemId('b'), "Past, Present or Future", ItemId('2'), 2, 1, "/media/tv-shows/The Grand Tour S02/The Grand Tour S02E01.mkv"));
        _bedroomJellyfin.Series.Add(JellyfinSamples.SeriesWith(
            ItemId('3'), "The Grand Tour", 2016, "/media/tv/The Grand Tour", GrandTourImdbId, GrandTourTmdbTvId, GrandTourTvdbId));
        _bedroomJellyfin.Episodes.Add(JellyfinSamples.EpisodeWith(
            ItemId('c'), "Past, Present or Future", ItemId('3'), 2, 1, "/media/tv/The Grand Tour/Season 2/The Grand Tour S02E01.mkv"));
        await _factory.SyncLibraryAsync(_livingRoom.Id, _owner.AccessToken, cancellationToken);
        await _factory.SyncLibraryAsync(_bedroom.Id, _owner.AccessToken, cancellationToken);
        var livingRoomSeries = await WorkOfItemAsync(ItemId('1'), cancellationToken);
        var livingRoomSecondSeries = await WorkOfItemAsync(ItemId('2'), cancellationToken);
        var livingRoomEpisode = await WorkOfItemAsync(ItemId('b'), cancellationToken);
        var bedroomSeries = await WorkOfItemAsync(ItemId('3'), cancellationToken);
        var bedroomEpisode = await WorkOfItemAsync(ItemId('c'), cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(SameWorkUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // S02E01 in both libraries, one group; S01E01 has no other copy.
        var group = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal((DuplicateClass.SameWork, false), (group.Class, group.NeedsReview));
        Assert.Empty(group.Distinctions);
        Assert.Equal(
            [
                new DuplicateIdentifierDto(MediaIdentifierScheme.Imdb, GrandTourImdbId, 2, 1),
                new DuplicateIdentifierDto(MediaIdentifierScheme.Tvdb, GrandTourTvdbId, 2, 1),
                new DuplicateIdentifierDto(MediaIdentifierScheme.TmdbTv, GrandTourTmdbTvId, 2, 1),
            ],
            group.Evidence.Identifiers);
        // The living room's two series items are one series (MAM-32); each library keeps its own series and episode,
        // so the join crosses two series works by the show's ids.
        Assert.Equal(livingRoomSeries, livingRoomSecondSeries);
        Assert.NotEqual(livingRoomSeries, bedroomSeries);
        Assert.NotEqual(livingRoomEpisode, bedroomEpisode);
        Assert.Equal(
            [
                ("/media/tv-shows/The Grand Tour S02/The Grand Tour S02E01.mkv", livingRoomEpisode, (LibraryEpisodeDto?)new LibraryEpisodeDto(livingRoomSeries, 2, 1), (string?)"The Grand Tour", (string?)"Living room"),
                ("/media/tv/The Grand Tour/Season 2/The Grand Tour S02E01.mkv", bedroomEpisode, new LibraryEpisodeDto(bedroomSeries, 2, 1), "The Grand Tour", "Bedroom"),
            ],
            group.Files.Select(file => (file.File.ServicePath, Assert.Single(file.Works).Id, file.Works[0].Episode, file.Works[0].SeriesTitle, file.Works[0].SourceName)));
    }

    [Fact]
    public async Task List_keeps_a_movie_and_a_series_sharing_a_tmdb_number_apart()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // ADR 0017 — TMDb numbers films and shows apart: the film 1399 and the show 1399 are two different works.
        await PlantMovieAsync(_livingRoom, "Kingdom", 2011, "/media/movies/Kingdom (2011).mkv", tmdbId: "1399");
        var series = await PlantSeriesAsync(_bedroom, "Game of Thrones", 2011, "1399");
        await PlantEpisodeAsync(_bedroom, series, 1, 1, "Winter Is Coming", "/media/tv/Game of Thrones/S01E01.mkv");

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(0, list.TotalCount);
        Assert.Empty(list.Items);
    }

    [Fact]
    public async Task List_keeps_a_film_a_show_and_its_episode_synced_with_one_tmdb_number_apart()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Jellyfin gives the film, the show and the show's episode the TMDb number 1399, each in its own number space:
        // the sync files the film's as a film id, the show's as a show id, and keeps none for the episode (ADR 0017).
        _jellyfin.Movies.Add(JellyfinSamples.MovieWith(ItemId('a'), "Kingdom", null, 2011, "/media/movies/Kingdom (2011).mkv", tmdbId: "1399"));
        _bedroomJellyfin.Series.Add(JellyfinSamples.SeriesWith(
            ItemId('1'), "Game of Thrones", 2011, "/media/tv/Game of Thrones", "tt0944947", "1399", "121361"));
        var episode = JellyfinSamples.EpisodeWith(
            ItemId('b'), "Winter Is Coming", ItemId('1'), 1, 1, "/media/tv/Game of Thrones/S01E01.mkv", "tt1480055", "3254641");
        episode["ProviderIds"]!["Tmdb"] = "1399";
        _bedroomJellyfin.Episodes.Add(episode);
        await _factory.SyncLibraryAsync(_livingRoom.Id, _owner.AccessToken, cancellationToken);
        await _factory.SyncLibraryAsync(_bedroom.Id, _owner.AccessToken, cancellationToken);
        var film = await WorkOfItemAsync(ItemId('a'), cancellationToken);
        var series = await WorkOfItemAsync(ItemId('1'), cancellationToken);
        var winterIsComing = await WorkOfItemAsync(ItemId('b'), cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // No class offers them as one: no shared key, and a film is never an episode's possible match.
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(0, list.TotalCount);
        Assert.Empty(list.Items);
        await _factory.WithDbAsync(async db =>
        {
            var tmdbIds = await db.MediaWorkIdentifiers.AsNoTracking()
                .Where(identifier => identifier.Value == "1399")
                .Select(identifier => new { identifier.MediaWorkId, identifier.Scheme })
                .ToListAsync(cancellationToken);
            Assert.Equal(
                [(film, MediaIdentifierScheme.Tmdb), (series, MediaIdentifierScheme.TmdbTv)],
                tmdbIds.Select(identifier => (identifier.MediaWorkId, identifier.Scheme)).OrderBy(identifier => identifier.Scheme));
            Assert.DoesNotContain(tmdbIds, identifier => identifier.MediaWorkId == winterIsComing);
        });
    }

    [Fact]
    public async Task List_groups_a_film_item_and_the_librarys_episode_with_one_imdb_id()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // ADR 0017 — IMDb numbers films and episodes in one space: the living room files The Continental's first episode
        // as a film, the bedroom as the episode of its show, and both libraries give it the episode's IMDb id.
        const string imdbId = "tt23899004";
        const string filmPath = "/media/movies/The Continental (2023)/The Continental S01E01.mkv";
        const string episodePath = "/media/tv/The Continental/Season 1/The Continental S01E01.mkv";
        var film = await PlantMovieAsync(_livingRoom, "The Continental", 2023, filmPath, imdbId);
        var series = await PlantSeriesAsync(_bedroom, "The Continental: From the World of John Wick", 2023, "117480");
        var episode = await PlantEpisodeAsync(_bedroom, series, 1, 1, "Night 1: Brothers in Arms", episodePath, imdbId);

        //Act
        var response = await _factory.GetHttpRequest(SameWorkUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // One work by the IMDb id, whatever each library says the item is; nothing for the owner to review.
        var group = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal((DuplicateClass.SameWork, false), (group.Class, group.NeedsReview));
        Assert.Empty(group.Distinctions);
        Assert.Equal([new DuplicateIdentifierDto(MediaIdentifierScheme.Imdb, imdbId, null, null)], group.Evidence.Identifiers);
        Assert.Equal(
            [
                (filmPath, film.Work.Id, MediaWorkKind.Movie, (LibraryEpisodeDto?)null, (string?)"Living room"),
                (episodePath, episode.Work.Id, MediaWorkKind.Episode, new LibraryEpisodeDto(series.Id, 1, 1), "Bedroom"),
            ],
            group.Files.Select(file => (file.File.ServicePath, Assert.Single(file.Works).Id, file.Works[0].Kind, file.Works[0].Episode, file.Works[0].SourceName)));
    }

    [Fact]
    public async Task List_flags_two_episodes_whose_shows_ids_differ_for_review()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // One episode by its own IMDb id in both libraries, at one place, but each library knows its show under another
        // TMDb show id: one of them is wrong, and which is the owner's to say.
        const string livingRoomPath = "/media/cartoons/Mickey Mouse Clubhouse/S03E09.mkv";
        const string bedroomPath = "/media/backup/Mickey Mouse Clubhouse/S03E09.mkv";
        var livingRoomSeries = await PlantSeriesAsync(_livingRoom, "Mickey Mouse Clubhouse", 2006, "3934");
        var bedroomSeries = await PlantSeriesAsync(_bedroom, "Mickey Mouse Clubhouse", 2006, "9999");
        var livingRoom = await PlantEpisodeAsync(_livingRoom, livingRoomSeries, 3, 9, "Road Rally", livingRoomPath, "tt1715023");
        var bedroom = await PlantEpisodeAsync(_bedroom, bedroomSeries, 3, 9, "Road Rally", bedroomPath, "tt1715023");

        //Act
        var response = await _factory.GetHttpRequest(SameWorkUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Vasyl, 2026-10-07 — still one work, flagged: the shows' registry disagrees.
        var group = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal((DuplicateClass.SameWork, true), (group.Class, group.NeedsReview));
        Assert.Equal([new DuplicateDistinctionDto(DuplicateDistinctionKind.IdentifierConflict, MediaIdentifierScheme.TmdbTv, true)], group.Distinctions);
        Assert.Equal([new DuplicateIdentifierDto(MediaIdentifierScheme.Imdb, "tt1715023", null, null)], group.Evidence.Identifiers);
        // Each file with its own work, in its own library's show.
        Assert.Equal(
            [
                (bedroomPath, bedroom.Work.Id, (LibraryEpisodeDto?)new LibraryEpisodeDto(bedroomSeries.Id, 3, 9), (string?)"Bedroom"),
                (livingRoomPath, livingRoom.Work.Id, new LibraryEpisodeDto(livingRoomSeries.Id, 3, 9), "Living room"),
            ],
            group.Files.Select(file => (file.File.ServicePath, Assert.Single(file.Works).Id, file.Works[0].Episode, file.Works[0].SourceName)));
    }

    [Fact]
    public async Task List_groups_a_film_item_linked_as_an_episode_with_the_librarys_own_episode()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // MAM-34 — Jellyfin lists the episode as a film, without ids; the owner linked the item to the TMDb show, season
        // 1, episode 1. The library's TV side holds that show's own S01E01.
        const string showTmdbTvId = "88829";
        var film = await PlantMovieAsync(
            _livingRoom, "Greatest Events of World War II in Colour.S01E01.Blitzkrieg", 2025, "/media/documentary/Greatest Events S01E01 Blitzkrieg.mkv");
        await _links.Create(
            _livingRoom,
            film.Version.SourceItemId!,
            film.Work.Id,
            MediaIdentifierScheme.TmdbTv,
            showTmdbTvId,
            LibraryLinkTarget.ShowEpisode,
            configure: link =>
            {
                link.SeasonNumber = 1;
                link.EpisodeNumber = 1;
            });
        var series = await PlantSeriesAsync(_livingRoom, "Greatest Events of World War II in Colour", 2019, showTmdbTvId);
        var episode = await PlantEpisodeAsync(_livingRoom, series, 1, 1, "Blitzkrieg", "/media/tv-shows/Greatest Events/S01E01.mkv");

        //Act
        var response = await _factory.GetHttpRequest(SameWorkUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // ADR 0028 — the link's show with its numbers is the show's own key with the episode's: one work, two copies.
        var group = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal((DuplicateClass.SameWork, false), (group.Class, group.NeedsReview));
        Assert.Empty(group.Distinctions);
        Assert.Equal([new DuplicateIdentifierDto(MediaIdentifierScheme.TmdbTv, showTmdbTvId, 1, 1)], group.Evidence.Identifiers);
        // The library still says what each item is: a film, and an episode of its series.
        Assert.Equal(
            [
                ("/media/documentary/Greatest Events S01E01 Blitzkrieg.mkv", film.Work.Id, MediaWorkKind.Movie, (LibraryEpisodeDto?)null, (string?)null),
                ("/media/tv-shows/Greatest Events/S01E01.mkv", episode.Work.Id, MediaWorkKind.Episode, new LibraryEpisodeDto(series.Id, 1, 1), "Greatest Events of World War II in Colour"),
            ],
            group.Files.Select(file => (file.File.ServicePath, Assert.Single(file.Works).Id, file.Works[0].Kind, file.Works[0].Episode, file.Works[0].SeriesTitle)));
        // An episode-to-show link counts none of the show's ids as the film's own.
        Assert.Empty(group.Files[0].Works[0].Identifiers);
        // Grouping as copies changes nothing in the library.
        await _factory.WithDbAsync(async db =>
        {
            var kinds = await db.MediaWorks.AsNoTracking().ToDictionaryAsync(work => work.Id, work => work.Kind, cancellationToken);
            Assert.Equal(
                (3, MediaWorkKind.Movie, MediaWorkKind.Series, MediaWorkKind.Episode),
                (kinds.Count, kinds[film.Work.Id], kinds[series.Id], kinds[episode.Work.Id]));
        });
    }

    [Fact]
    public async Task List_makes_no_group_of_different_cuts_of_one_film()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // One film by its IMDb id in both libraries, two cuts: the names say which. MAM-37 (Vasyl, 2026-10-08: different
        // cuts are not duplicates) — two versions, neither a copy of the other.
        const string directorsCutPath = "/media/movies/Alien (1979)/Alien (1979) [Director's Cut] BDRip 1080p.mkv";
        const string theatricalCutPath = "/media/backup/Alien (1979) [Theatrical Cut] BDRip 1080p.mkv";
        var livingRoom = await PlantMovieAsync(_livingRoom, "Чужий", 1979, directorsCutPath, "tt0078748");
        var bedroom = await PlantMovieAsync(_bedroom, "Alien", 1979, theatricalCutPath, "tt0078748");

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // In no class: not copies of one work, and two works one by their ids are no possible match.
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(0, list.TotalCount);
        Assert.Empty(list.Items);
        // Only the list leaves them out: the library still holds both works, each with the IMDb id they share.
        await _factory.WithDbAsync(async db =>
        {
            var holders = await db.MediaWorkIdentifiers.AsNoTracking()
                .Where(identifier => identifier.Value == "tt0078748")
                .Select(identifier => identifier.MediaWorkId)
                .ToListAsync(cancellationToken);
            Assert.Equal(new[] { livingRoom.Work.Id, bedroom.Work.Id }.Order(), holders.Order());
        });
    }

    [Fact]
    public async Task List_makes_no_group_of_the_theatrical_and_the_extended_cut_of_one_film()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Rambo: Last Blood as production holds it: one work of two versions, a cut each. MAM-37 (Vasyl, 2026-10-08:
        // "це НЕ ДУБЛІ!!!!! бо різні версії").
        var film = await PlantMovieAsync(_livingRoom, "Рембо 5", 2019, RamboTheatricalPath, RamboImdbId);
        var extended = await PlantVersionAsync(film, RamboExtendedPath);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(0, list.TotalCount);
        Assert.Empty(list.Items);
        // Only the list leaves them out: the library still holds one work of two versions, a file each.
        await _factory.WithDbAsync(async db =>
        {
            var files = await (
                    from file in db.PhysicalFiles.AsNoTracking()
                    join version in db.MediaVersions.AsNoTracking() on file.MediaVersionId equals (Guid?)version.Id
                    select new { file.ServicePath, VersionId = version.Id, version.MediaWorkId })
                .ToListAsync(cancellationToken);
            Assert.Equal(
                [(RamboTheatricalPath, film.Version.Id, film.Work.Id), (RamboExtendedPath, extended.Id, film.Work.Id)],
                files.Select(file => (file.ServicePath, file.VersionId, file.MediaWorkId)).OrderBy(file => file.ServicePath, StringComparer.Ordinal));
        });
    }

    [Fact]
    public async Task List_makes_no_group_of_three_editions_of_one_film()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // MAM-37 — Terminator 2 as production holds it: one work of three versions, whose names say "UK Remastered,
        // Theatrical Cut", "Special Edition" and "Special Extended Edition" — three sets of editions, the whole set
        // deciding, so no two copies of one.
        const string folder = "/media/movies/Terminator. Collection";
        const string remasteredPath = $"{folder}/[02.1] Terminator 2. Judgment Day (1991) [UK Remastered, Theatrical Cut] BDRip 1080p H.265 [4xUKR_ENG] [Hurtom].mkv";
        const string specialPath = $"{folder}/[02.2] Terminator 2. Judgment Day (1991) [Lionsgate Transfer, Special Edition] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom].mkv";
        const string extendedPath = $"{folder}/[02.3] Terminator 2. Judgment Day (1991) [JPN Transfer, Special Extended Edition] BDRip 1080p H.265 [4xUKR_ENG] [Hurtom].mkv";
        var film = await PlantMovieAsync(_livingRoom, "Термінатор 2: Судний день", 1991, remasteredPath, "tt0103064");
        await PlantVersionAsync(film, specialPath);
        await PlantVersionAsync(film, extendedPath);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(0, list.TotalCount);
        Assert.Empty(list.Items);
    }

    [Fact]
    public async Task List_makes_no_group_of_a_cartoon_and_its_widescreen_copy()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // MAM-37 — Tom & Jerry's "Feedin' the Kiddie" as production holds it: one work of two versions, the second's name
        // saying Widescreen and the first's nothing. A frame is an edition too, and a copy that says none beside one that
        // says one is another.
        const string folder = "/media/cartoons/Tom & Jerry e001-e161 (1940-1967)";
        const string plainPath = $"{folder}/107-1. Feedin' the Kiddie (1957).mkv";
        const string widescreenPath = $"{folder}/107-2. Feedin' the Kiddie (1957) (Widescreen).mkv";
        var cartoon = await PlantMovieAsync(_livingRoom, "Feedin' the Kiddie", 1957, plainPath, "tt0050388");
        await PlantVersionAsync(cartoon, widescreenPath);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(0, list.TotalCount);
        Assert.Empty(list.Items);
    }

    [Fact]
    public async Task List_groups_two_copies_of_one_cut_under_the_works_key()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Terminator Salvation's Director's Cut in both libraries, the names saying so in two ways: one cut, so copies of
        // each other — one set of editions splits nothing.
        const string livingRoomPath = "/media/movies/Terminator. Collection/[04.2] Terminator Salvation (2009) [Director's Cut] BDRip 1080p H.265 [UKR_ENG] [Hurtom].mkv";
        const string bedroomPath = "/media/backup/Terminator Salvation (2009) Directors Cut 2160p.mkv";
        var livingRoom = await PlantMovieAsync(_livingRoom, "Термінатор: Спасіння прийде", 2009, livingRoomPath, "tt0438488");
        var bedroom = await PlantMovieAsync(_bedroom, "Terminator Salvation", 2009, bedroomPath, "tt0438488");

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var group = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal((DuplicateClass.SameWork, false), (group.Class, group.NeedsReview));
        Assert.Empty(group.Distinctions);
        Assert.Equal(
            [(bedroomPath, bedroom.Work.Id, nameof(MediaEdition.DirectorsCut)), (livingRoomPath, livingRoom.Work.Id, nameof(MediaEdition.DirectorsCut))],
            group.Files.Select(file => (file.File.ServicePath, Assert.Single(file.Works).Id, string.Join(' ', file.Editions))));
        // No "|Editions:": the key the two works' group always had.
        var works = string.Join('|', new[] { livingRoom.Work.Id, bedroom.Work.Id }.Select(id => id.ToString("D")).Order(StringComparer.Ordinal));
        Assert.Equal(KeyOf($"SameWork|{works}"), group.Key);
    }

    [Fact]
    public async Task List_groups_the_copies_of_one_cut_apart_from_another_cut_under_a_key_of_their_own()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // MAM-37 — Rambo: Last Blood's two cuts in the living room, as production holds them, and the bedroom's copy of
        // the Theatrical Cut: the two theatrical copies are each other's, the Extended Cut a copy of nothing.
        const string theatricalCopyPath = "/media/backup/Rambo. Last Blood (2019) Theatrical Cut.mkv";
        var livingRoom = await PlantMovieAsync(_livingRoom, "Рембо 5", 2019, RamboTheatricalPath, RamboImdbId);
        await PlantVersionAsync(livingRoom, RamboExtendedPath);
        var bedroom = await PlantMovieAsync(_bedroom, "Rambo: Last Blood", 2019, theatricalCopyPath, RamboImdbId);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // One group, nothing to review: every copy in it says the Theatrical Cut.
        var group = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal((DuplicateClass.SameWork, false), (group.Class, group.NeedsReview));
        Assert.Empty(group.Distinctions);
        Assert.Equal(
            [
                (theatricalCopyPath, bedroom.Work.Id, bedroom.Version.Id, nameof(MediaEdition.Theatrical)),
                (RamboTheatricalPath, livingRoom.Work.Id, livingRoom.Version.Id, nameof(MediaEdition.Theatrical)),
            ],
            group.Files.Select(file => (file.File.ServicePath, Assert.Single(file.Works).Id, file.Works[0].VersionId, string.Join(' ', file.Editions))));
        // Named by its editions, so it never takes the key the two works' one group would have.
        var works = string.Join('|', new[] { livingRoom.Work.Id, bedroom.Work.Id }.Select(id => id.ToString("D")).Order(StringComparer.Ordinal));
        Assert.Equal(KeyOf($"SameWork|{works}|Editions:Theatrical"), group.Key);
    }

    [Fact]
    public async Task List_groups_the_copies_saying_no_edition_beside_another_edition_under_a_none_key()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // MAM-37 — Tom & Jerry's "Feedin' the Kiddie" as production holds it in the living room, a copy whose name says no
        // edition and a Widescreen one, and the bedroom's copy saying none either: the two that say none are each other's
        // copies, the Widescreen one a copy of nothing.
        const string folder = "/media/cartoons/Tom & Jerry e001-e161 (1940-1967)";
        const string plainPath = $"{folder}/107-1. Feedin' the Kiddie (1957).mkv";
        const string widescreenPath = $"{folder}/107-2. Feedin' the Kiddie (1957) (Widescreen).mkv";
        const string plainCopyPath = "/media/backup/Feedin' the Kiddie (1957).mkv";
        var livingRoom = await PlantMovieAsync(_livingRoom, "Feedin' the Kiddie", 1957, plainPath, "tt0050388");
        await PlantVersionAsync(livingRoom, widescreenPath);
        var bedroom = await PlantMovieAsync(_bedroom, "Feedin' the Kiddie", 1957, plainCopyPath, "tt0050388");

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var group = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal((DuplicateClass.SameWork, false), (group.Class, group.NeedsReview));
        Assert.Empty(group.Distinctions);
        Assert.Equal(
            [(plainCopyPath, bedroom.Work.Id, bedroom.Version.Id), (plainPath, livingRoom.Work.Id, livingRoom.Version.Id)],
            group.Files.Select(file => (file.File.ServicePath, Assert.Single(file.Works).Id, file.Works[0].VersionId)));
        Assert.All(group.Files, file => Assert.Empty(file.Editions));
        // Named "none", beside the Widescreen set: a key of its own, never the two works' one group's.
        var works = string.Join('|', new[] { livingRoom.Work.Id, bedroom.Work.Id }.Select(id => id.ToString("D")).Order(StringComparer.Ordinal));
        Assert.Equal(KeyOf($"SameWork|{works}|Editions:none"), group.Key);
    }

    [Fact]
    public async Task List_names_a_part_whose_copies_say_several_editions_by_them_in_edition_order()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // MAM-37 — Terminator 2's "UK Remastered, Theatrical Cut" and "Special Edition" as production holds them in the
        // living room, and the bedroom's copy of the first: its two copies are one group, named by both their editions in
        // MediaEdition's order — the Theatrical Cut before Remastered, whatever order the names say them in.
        const string folder = "/media/movies/Terminator. Collection";
        const string remasteredPath = $"{folder}/[02.1] Terminator 2. Judgment Day (1991) [UK Remastered, Theatrical Cut] BDRip 1080p H.265 [4xUKR_ENG] [Hurtom].mkv";
        const string specialPath = $"{folder}/[02.2] Terminator 2. Judgment Day (1991) [Lionsgate Transfer, Special Edition] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom].mkv";
        const string remasteredCopyPath = "/media/backup/Terminator 2. Judgment Day (1991) [UK Remastered, Theatrical Cut].mkv";
        var livingRoom = await PlantMovieAsync(_livingRoom, "Термінатор 2: Судний день", 1991, remasteredPath, "tt0103064");
        await PlantVersionAsync(livingRoom, specialPath);
        var bedroom = await PlantMovieAsync(_bedroom, "Terminator 2: Judgment Day", 1991, remasteredCopyPath, "tt0103064");

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var group = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal((DuplicateClass.SameWork, false), (group.Class, group.NeedsReview));
        Assert.Equal(
            [
                (remasteredCopyPath, bedroom.Work.Id, $"{nameof(MediaEdition.Theatrical)} {nameof(MediaEdition.Remastered)}"),
                (remasteredPath, livingRoom.Work.Id, $"{nameof(MediaEdition.Theatrical)} {nameof(MediaEdition.Remastered)}"),
            ],
            group.Files.Select(file => (file.File.ServicePath, Assert.Single(file.Works).Id, string.Join(' ', file.Editions))));
        var works = string.Join('|', new[] { livingRoom.Work.Id, bedroom.Work.Id }.Select(id => id.ToString("D")).Order(StringComparer.Ordinal));
        Assert.Equal(KeyOf($"SameWork|{works}|Editions:Theatrical,Remastered"), group.Key);
    }

    [Fact]
    public async Task List_makes_no_group_of_a_copy_whose_version_name_says_a_cut_beside_one_that_says_none()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Vasyl, 2026-10-07 — an item with two versions: Jellyfin names each, and only one name says its cut; the file
        // names say nothing. MAM-37 — a labelled copy and an unlabelled one are two editions: nothing says the second is
        // the Director's Cut.
        const string labelledPath = "/media/movies/Alien (1979)/Alien.1979.1080p.mkv";
        const string unlabelledPath = "/media/movies/Alien (1979)/Alien.1979.2160p.mkv";
        var film = await PlantMovieAsync(_livingRoom, "Чужий", 1979, labelledPath, "tt0078748", editionLabel: "Alien (1979) Director's Cut");
        var version = await _versions.Create(
            film.Work.Id, film.Version.SourceItemId, Guid.NewGuid().ToString("N"), "Alien (1979) 4K", _livingRoom.Id, _livingRoom.Name);
        await _physicalFiles.Create(version.Id, unlabelledPath, sourceConnectionId: _livingRoom.Id, sourceName: _livingRoom.Name);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(0, list.TotalCount);
        Assert.Empty(list.Items);
        // The library keeps both versions with the names Jellyfin gave them.
        await _factory.WithDbAsync(async db =>
        {
            var labels = await db.MediaVersions.AsNoTracking()
                .Where(row => row.MediaWorkId == film.Work.Id)
                .Select(row => new { row.Id, row.EditionLabel })
                .ToListAsync(cancellationToken);
            Assert.Equal(
                [(version.Id, (string?)"Alien (1979) 4K"), (film.Version.Id, "Alien (1979) Director's Cut")],
                labels.Select(row => (row.Id, row.EditionLabel)).OrderBy(row => row.EditionLabel, StringComparer.Ordinal));
        });
    }

    [Fact]
    public async Task List_reads_an_edition_from_the_name_a_library_gives_a_version()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Vasyl, 2026-10-07 — an item with two versions of one cut: the first says it only in the name Jellyfin gave its
        // version, its file name saying nothing; the second only in its file name. Read from both, each is the Director's
        // Cut — read from the file names alone, they would be two editions, and no group.
        const string labelledPath = "/media/movies/Alien (1979)/Alien.1979.1080p.mkv";
        const string namedPath = "/media/movies/Alien (1979)/Alien (1979) [Director's Cut] 2160p.mkv";
        var film = await PlantMovieAsync(_livingRoom, "Чужий", 1979, labelledPath, "tt0078748", editionLabel: "Alien (1979) Director's Cut");
        var version = await _versions.Create(
            film.Work.Id, film.Version.SourceItemId, Guid.NewGuid().ToString("N"), "Alien (1979) 4K", _livingRoom.Id, _livingRoom.Name);
        await _physicalFiles.Create(version.Id, namedPath, sourceConnectionId: _livingRoom.Id, sourceName: _livingRoom.Name);

        //Act
        var response = await _factory.GetHttpRequest(SameWorkUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var group = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal((DuplicateClass.SameWork, false), (group.Class, group.NeedsReview));
        Assert.Empty(group.Distinctions);
        Assert.Equal(
            [
                (namedPath, version.Id, (string?)"Alien (1979) 4K", nameof(MediaEdition.DirectorsCut)),
                (labelledPath, film.Version.Id, "Alien (1979) Director's Cut", nameof(MediaEdition.DirectorsCut)),
            ],
            group.Files.Select(file => (file.File.ServicePath, Assert.Single(file.Works).VersionId, file.Works[0].EditionLabel, string.Join(' ', file.Editions))));
        Assert.Equal(KeyOf($"SameWork|{film.Work.Id:D}"), group.Key);
    }

    [Fact]
    public async Task List_reads_no_edition_from_the_words_of_a_works_title()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Uncut Gems in both libraries, one cut: the living room's file and version are named by the English title, whose
        // first word is an edition's; the bedroom's by the Russian one.
        const string englishPath = "/media/movies/Uncut.Gems.2019.1080p.BluRay.mkv";
        const string localizedPath = "/media/backup/Неограненные алмазы (2019).mkv";
        await PlantMovieAsync(_livingRoom, "Uncut Gems", 2019, englishPath, "tt5727208", editionLabel: "Uncut.Gems.2019.1080p.BluRay");
        await PlantMovieAsync(_bedroom, "Неограненные алмазы", 2019, localizedPath, "tt5727208");

        //Act
        var response = await _factory.GetHttpRequest(SameWorkUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // MAM-20 — a title is no label: two copies of one cut, nothing to review.
        var group = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal((DuplicateClass.SameWork, false), (group.Class, group.NeedsReview));
        Assert.Empty(group.Distinctions);
        Assert.Equal([localizedPath, englishPath], group.Files.Select(file => file.File.ServicePath));
        Assert.All(group.Files, file => Assert.Empty(file.Editions));
    }

    [Fact]
    public async Task List_flags_copies_whose_measured_dynamic_ranges_differ()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        // One film in both libraries, nothing in the names to tell them apart; the media inspector reads HDR10 in one
        // and SDR in the other.
        await PlantMovieAsync(_livingRoom, "Чужі", 1986, AliensPath, AliensImdbId);
        await PlantMovieAsync(_bedroom, "Aliens", 1986, AliensCopyPath, AliensImdbId);
        var sdr = TestMedia.Taken();
        var hdr10 = sdr with
        {
            Tracks = [.. sdr.Tracks.Select(track => track.Kind == MediaTrackKind.Video ? track with { DynamicRange = DynamicRange.Hdr10 } : track)],
        };
        _factory.Files.AddFile(AliensPath, TestMedia.File(AliensInode, AliensSizeBytes), hdr10);
        _factory.Files.AddFile(AliensCopyPath, TestMedia.File(CopyInode, CopySizeBytes), sdr);
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(SameWorkUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var group = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal((DuplicateClass.SameWork, true), (group.Class, group.NeedsReview));
        // Measured, not read off a name: the one distinction.
        Assert.Equal([new DuplicateDistinctionDto(DuplicateDistinctionKind.DynamicRange, null, false)], group.Distinctions);
        Assert.Equal([AliensCopyPath, AliensPath], group.Files.Select(file => file.File.ServicePath));
        Assert.All(group.Files, file => Assert.Empty(file.Editions));
        Assert.Equal(new LibrarySpaceDto(AliensSizeBytes + CopySizeBytes, 2, 2, 0), group.Space);
    }

    [Fact]
    public async Task List_never_merges_movies_with_similar_titles_or_years()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // ADR 0012 — a title and a year never make two works one: the same title and year under two IMDb ids, the same
        // title and year without ids, the same title a year apart, a similar title.
        await PlantMovieAsync(_livingRoom, "Чужі", 1986, AliensPath, AliensImdbId);
        await PlantMovieAsync(_bedroom, "Чужі", 1986, AliensCopyPath, "tt0103644");
        await PlantMovieAsync(_livingRoom, "Чужий", 1979, "/media/movies/Alien (1979).mkv");
        await PlantMovieAsync(_bedroom, "Чужий", 1979, "/media/backup/Alien (1979).mkv");
        await PlantMovieAsync(_bedroom, "Чужі", 1987, "/media/backup/Aliens (1987).mkv");
        await PlantMovieAsync(_bedroom, "Чужі 2", 1986, "/media/backup/Aliens 2 (1986).mkv", tmdbId: AliensTmdbId);

        //Act
        var response = await _factory.GetHttpRequest(SameWorkUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(0, list.TotalCount);
        Assert.Empty(list.Items);
    }

    [Fact]
    public async Task List_makes_no_group_of_a_work_with_one_file()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The same film in both libraries, but the bedroom's version holds no file: one copy.
        await PlantMovieAsync(_livingRoom, "Матриця", 1999, "/media/movies/The Matrix (1999).mkv", "tt0133093", "603");
        await PlantMovieAsync(_bedroom, "Матриця", 1999, path: null, "tt0133093", "603");

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(0, list.TotalCount);
        Assert.Empty(list.Items);
    }

    [Fact]
    public async Task List_makes_no_group_of_one_version_in_several_parts()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A film stacked in two parts: two files of one version are one copy, never two.
        var film = await PlantMovieAsync(_livingRoom, "Убити Білла", 2003, "/media/movies/Kill Bill (2003)/Kill Bill (2003) CD1.mkv", "tt0266697");
        await _physicalFiles.Create(
            film.Version.Id, "/media/movies/Kill Bill (2003)/Kill Bill (2003) CD2.mkv", sourceConnectionId: _livingRoom.Id, sourceName: _livingRoom.Name);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(0, list.TotalCount);
        Assert.Empty(list.Items);
    }

    [Fact]
    public async Task List_makes_no_group_of_one_file_two_libraries_hold_through_hardlinks()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        // The same film in both libraries, at two names of one file: one copy, the names of one file's class.
        await PlantMovieAsync(_livingRoom, "Чужі", 1986, AliensPath, AliensImdbId);
        await PlantMovieAsync(_bedroom, "Aliens", 1986, AliensCopyPath, AliensImdbId);
        var facts = TestMedia.File(AliensInode, AliensSizeBytes, linkCount: 2);
        _factory.Files.AddFile(AliensPath, facts, TestMedia.Taken());
        _factory.Files.AddFile(AliensCopyPath, facts, TestMedia.Taken());
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Act
        var sameWork = await _factory.GetHttpRequest(SameWorkUrl, _member.AccessToken);
        var every = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (sameWork.StatusCode, every.StatusCode));
        Assert.Equal(0, (await ReadListAsync(sameWork, cancellationToken)).TotalCount);
        var group = Assert.Single((await ReadListAsync(every, cancellationToken)).Items);
        Assert.Equal(DuplicateClass.SameFile, group.Class);
        Assert.Equal([AliensCopyPath, AliensPath], group.Files.Select(file => file.File.ServicePath));
    }

    [Fact]
    public async Task List_puts_one_file_whose_hardlinked_names_say_different_editions_in_one_group_of_copies()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        // MAM-37 — one file of Alien at two names, one in each library: the living room's name says the Director's Cut,
        // the bedroom's none. Beside it, the bedroom's other copy, saying the Director's Cut, and the living room's other
        // version, saying none. A file's names say one thing between them: it is the Director's Cut, a copy of the other
        // Director's Cut alone — never also, by its second name, of the copy that says none.
        const string directorsCutPath = "/media/movies/Alien (1979)/Alien (1979) [Director's Cut] BDRip 1080p.mkv";
        const string linkPath = "/media/backup/Alien (1979) BDRip 1080p.mkv";
        const string directorsCutCopyPath = "/media/backup/Alien (1979) [Director's Cut] 2160p.mkv";
        const string plainPath = "/media/movies/Alien (1979)/Alien (1979) 2160p.mkv";
        var livingRoom = await PlantMovieAsync(_livingRoom, "Чужий", 1979, directorsCutPath, "tt0078748");
        await PlantVersionAsync(livingRoom, plainPath);
        var bedroom = await PlantMovieAsync(_bedroom, "Alien", 1979, linkPath, "tt0078748");
        var copy = await PlantMovieAsync(_bedroom, "Alien", 1979, directorsCutCopyPath, "tt0078748");
        var linked = TestMedia.File(1_048_577, 1_000_000_001, linkCount: 2);
        _factory.Files.AddFile(directorsCutPath, linked, TestMedia.Taken());
        _factory.Files.AddFile(linkPath, linked, TestMedia.Taken());
        _factory.Files.AddFile(directorsCutCopyPath, TestMedia.File(1_048_579, 1_000_000_003), TestMedia.Taken());
        _factory.Files.AddFile(plainPath, TestMedia.File(1_048_581, 1_000_000_005), TestMedia.Taken());
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        // The two names, one file's; then one group of copies holding them both, the other Director's Cut beside them.
        Assert.Equal([DuplicateClass.SameFile, DuplicateClass.SameWork], list.Items.Select(group => group.Class));
        Assert.Equal([linkPath, directorsCutPath], list.Items[0].Files.Select(file => file.File.ServicePath));
        var group = list.Items[1];
        Assert.False(group.NeedsReview);
        Assert.Equal(
            [
                (linkPath, bedroom.Work.Id, string.Empty),
                (directorsCutCopyPath, copy.Work.Id, nameof(MediaEdition.DirectorsCut)),
                (directorsCutPath, livingRoom.Work.Id, nameof(MediaEdition.DirectorsCut)),
            ],
            group.Files.Select(file => (file.File.ServicePath, Assert.Single(file.Works).Id, string.Join(' ', file.Editions))));
        // The Director's Cut's part, its works the three holding it; the copy that says none, alone, in no group.
        var works = string.Join(
            '|', new[] { livingRoom.Work.Id, bedroom.Work.Id, copy.Work.Id }.Select(id => id.ToString("D")).Order(StringComparer.Ordinal));
        Assert.Equal(KeyOf($"SameWork|{works}|Editions:DirectorsCut"), group.Key);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    // The Penguins of Madagascar in the living room as production's library holds it: the series with its ids, and
    // S01E25 "Roger Dodger" and S01E37 "All King, No Kingdom" under one IMDb id with a TVDB id each.
    private void AddPenguinsPair()
    {
        _jellyfin.Series.Add(JellyfinSamples.SeriesWith(
            ItemId('1'), "The Penguins of Madagascar", 2008, PenguinsFolder, PenguinsImdbId, PenguinsTmdbTvId, PenguinsTvdbId));
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith(
            ItemId('a'), "Roger Dodger", ItemId('1'), 1, 25, RogerDodgerPath, RogerDodgerImdbId, RogerDodgerTvdbId));
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith(
            ItemId('b'), "All King, No Kingdom", ItemId('1'), 1, 37, AllKingNoKingdomPath, RogerDodgerImdbId, "1292941"));
    }

    // A film as a library reported it — its displayed title and year, the ids given, and one version holding the file
    // at the path given, or none — in an item of its own: what a sync stores.
    private async Task<(MediaWorkEntity Work, MediaVersionEntity Version)> PlantMovieAsync(
        IntegrationConnectionEntity library,
        string title,
        int year,
        string? path,
        string? imdbId = null,
        string? tmdbId = null,
        string? editionLabel = null)
    {
        var itemId = Guid.NewGuid().ToString("N");
        var work = await _works.Create();
        await _works.AddTitle(work.Id, title, MediaTitleKind.Display, year, library.Id, library.Name, itemId);
        if (imdbId is not null)
            await _works.AddIdentifier(work.Id, MediaIdentifierScheme.Imdb, imdbId, library.Id, library.Name, itemId);
        if (tmdbId is not null)
            await _works.AddIdentifier(work.Id, MediaIdentifierScheme.Tmdb, tmdbId, library.Id, library.Name, itemId);
        var version = await _versions.Create(work.Id, itemId, editionLabel: editionLabel, sourceConnectionId: library.Id, sourceName: library.Name);
        if (path is not null)
            await _physicalFiles.Create(version.Id, path, sourceConnectionId: library.Id, sourceName: library.Name);
        return (work, version);
    }

    // Another version of the film's item, as Jellyfin reports an item of several, holding the file at the path given.
    private async Task<MediaVersionEntity> PlantVersionAsync((MediaWorkEntity Work, MediaVersionEntity Version) film, string path)
    {
        var library = film.Version.SourceConnectionId;
        var version = await _versions.Create(
            film.Work.Id, film.Version.SourceItemId, Guid.NewGuid().ToString("N"), sourceConnectionId: library, sourceName: film.Version.SourceName);
        await _physicalFiles.Create(version.Id, path, sourceConnectionId: library, sourceName: film.Version.SourceName);
        return version;
    }

    // A series as a library reported it: its displayed title and year and its TMDb show id. It holds no file.
    private async Task<MediaWorkEntity> PlantSeriesAsync(IntegrationConnectionEntity library, string title, int year, string tmdbTvId)
    {
        var itemId = Guid.NewGuid().ToString("N");
        var series = await _works.Create(MediaWorkKind.Series);
        await _works.AddTitle(series.Id, title, MediaTitleKind.Display, year, library.Id, library.Name, itemId);
        await _works.AddIdentifier(series.Id, MediaIdentifierScheme.TmdbTv, tmdbTvId, library.Id, library.Name, itemId);
        return series;
    }

    // An episode of the series at its season and number, with the IMDb id of its own when given, and one version
    // holding the file at the path given.
    private async Task<(MediaWorkEntity Work, MediaVersionEntity Version)> PlantEpisodeAsync(
        IntegrationConnectionEntity library, MediaWorkEntity series, int season, int episode, string title, string path, string? imdbId = null)
    {
        var itemId = Guid.NewGuid().ToString("N");
        var work = await _works.Create(MediaWorkKind.Episode);
        await _works.AddTitle(work.Id, title, MediaTitleKind.Display, null, library.Id, library.Name, itemId);
        if (imdbId is not null)
            await _works.AddIdentifier(work.Id, MediaIdentifierScheme.Imdb, imdbId, library.Id, library.Name, itemId);
        await _works.AddEpisodePosition(work.Id, series.Id, season, episode, library.Id, library.Name, itemId);
        var version = await _versions.Create(work.Id, itemId, sourceConnectionId: library.Id, sourceName: library.Name);
        await _physicalFiles.Create(version.Id, path, sourceConnectionId: library.Id, sourceName: library.Name);
        return (work, version);
    }

    // A file at the path, of its own identity and size, that the media inspector measures at the duration given and
    // nothing else: no tracks, so no dynamic range tells copies apart.
    private void Measure(string path, long inode, long durationMilliseconds) =>
        _factory.Files.AddFile(path, TestMedia.File(inode, inode * 1_000), new MediaFacts(TimeSpan.FromMilliseconds(durationMilliseconds), null, []));

    // The work a synced library item landed on, read off its titles.
    private async Task<Guid> WorkOfItemAsync(string sourceItemId, CancellationToken cancellationToken)
    {
        var workId = Guid.Empty;
        await _factory.WithDbAsync(async db => workId = await db.MediaWorkTitles.AsNoTracking()
            .Where(title => title.SourceItemId == sourceItemId)
            .Select(title => title.MediaWorkId)
            .Distinct()
            .SingleAsync(cancellationToken));
        return workId;
    }

    // A 32-hex-digit item id made of one character, as Jellyfin's ids are.
    private static string ItemId(char character) => new(character, 32);

    // A work as a file lists it, but its ids.
    private static (Guid Id, MediaWorkKind Kind, string? Title, int? Year, LibraryEpisodeDto? Episode, string? SeriesTitle, Guid VersionId, string? EditionLabel, string? SourceName) Line(
        LibraryDuplicateWorkDto work) =>
        (work.Id, work.Kind, work.Title, work.Year, work.Episode, work.SeriesTitle, work.VersionId, work.EditionLabel, work.SourceName);

    // ADR 0028 — a group's key: the first 16 bytes of the SHA-256 of its canonical text, in lowercase hex.
    private static string KeyOf(string canonical) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)).AsSpan(0, 16));

    private static LibraryDuplicateListResponse ParseList(string body) =>
        JsonSerializer.Deserialize<LibraryDuplicateListResponse>(body, MediaAcquisitionManagerApiFactory.JsonOptions)!;

    private static async Task<LibraryDuplicateListResponse> ReadListAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<LibraryDuplicateListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
}
