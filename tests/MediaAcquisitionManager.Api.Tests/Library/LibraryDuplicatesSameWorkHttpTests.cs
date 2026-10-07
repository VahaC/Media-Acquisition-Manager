using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediaAcquisitionManager.Api.Contracts;
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
// or more. Works whose ids disagree, and copies whose editions or measured dynamic ranges differ, stay one group flagged
// for review with the disagreement named, each file listing its own work with its own ids (Vasyl, 2026-10-07); a group
// of one work's copies is never redundant, so it carries no estimate. Works come from real syncs through the fake
// Jellyfin where its samples hold the case, else are planted as a sync stores them; identities and dynamic ranges from
// real inspections through the fake file inventory. Access, validation, order and wire names are
// LibraryDuplicatesHttpTests'; names of one file LibraryDuplicatesSameFileHttpTests'; byte-identical files
// LibraryDuplicatesByteIdenticalHttpTests'; matches by title and year LibraryDuplicatesPossibleHttpTests'.
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
        // The key that makes them one; no identity, hash, title or duration is this class's evidence.
        Assert.Equal([new DuplicateIdentifierDto(MediaIdentifierScheme.Imdb, AliensImdbId, null, null)], group.Evidence.Identifiers);
        Assert.Equal(((DuplicateIdentityDto?)null, (string?)null, (bool?)null), (group.Evidence.Identity, group.Evidence.Sha256, group.Evidence.DurationsAgree));
        Assert.Empty(group.Evidence.Titles);
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
    public async Task List_groups_the_two_halves_of_an_episode_as_versions_of_one_work()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Mickey Mouse Clubhouse S03E09, "Road Rally": two items, the a and b halves, with the episode's ids and place —
        // one work of two versions, each holding a file of its own.
        _jellyfin.Series.Add(JellyfinSamples.Series(JellyfinSamples.MickeyMouseClubhouseId));
        _jellyfin.Episodes.Add(JellyfinSamples.Episode(JellyfinSamples.RoadRallyAId));
        _jellyfin.Episodes.Add(JellyfinSamples.Episode(JellyfinSamples.RoadRallyBId));
        await _factory.SyncLibraryAsync(_livingRoom.Id, _owner.AccessToken, cancellationToken);
        var episodeId = await WorkOfItemAsync(JellyfinSamples.RoadRallyAId, cancellationToken);
        var seriesId = await WorkOfItemAsync(JellyfinSamples.MickeyMouseClubhouseId, cancellationToken);
        var halfA = (string)JellyfinSamples.Episode(JellyfinSamples.RoadRallyAId)["Path"]!;
        var halfB = (string)JellyfinSamples.Episode(JellyfinSamples.RoadRallyBId)["Path"]!;

        //Act
        var response = await _factory.GetHttpRequest(SameWorkUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var group = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal((DuplicateClass.SameWork, false), (group.Class, group.NeedsReview));
        Assert.Empty(group.Distinctions);
        Assert.Equal([halfA, halfB], group.Files.Select(file => file.File.ServicePath));
        Assert.All(
            group.Files,
            file => Assert.Equal(
                (episodeId, MediaWorkKind.Episode, (string?)"Road Rally", (LibraryEpisodeDto?)new LibraryEpisodeDto(seriesId, 3, 9), (string?)"Mickey Mouse Clubhouse"),
                (Assert.Single(file.Works).Id, file.Works[0].Kind, file.Works[0].Title, file.Works[0].Episode, file.Works[0].SeriesTitle)));
        Assert.NotEqual(group.Files[0].Works[0].VersionId, group.Files[1].Works[0].VersionId);
        // One work: every key it holds — its own IMDb and TVDB ids, and the show's ids with its place.
        Assert.Equal(
            [
                new DuplicateIdentifierDto(MediaIdentifierScheme.Imdb, "tt1715023", null, null),
                new DuplicateIdentifierDto(MediaIdentifierScheme.Imdb, "tt0784896", 3, 9),
                new DuplicateIdentifierDto(MediaIdentifierScheme.Tvdb, "2804541", null, null),
                new DuplicateIdentifierDto(MediaIdentifierScheme.Tvdb, "79854", 3, 9),
                new DuplicateIdentifierDto(MediaIdentifierScheme.TmdbTv, "3934", 3, 9),
            ],
            group.Evidence.Identifiers);
        Assert.Null(group.Recoverable);
    }

    [Fact]
    public async Task List_flags_two_episodes_with_one_episodes_ids_under_different_numbers_for_review()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // ADR 0017 — "The Golden Boo Boo" (S03E31) carries the ids of "Donald Hatches an Egg" (S03E33): two works, as
        // their places differ, one episode's ids between them.
        _jellyfin.Series.Add(JellyfinSamples.Series(JellyfinSamples.MickeyMouseClubhouseId));
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith(
            ItemId('c'), "The Golden Boo Boo", JellyfinSamples.MickeyMouseClubhouseId, 3, 31, "/media/cartoons/Mickey/S03E31.mkv", "tt5625312", "4354865"));
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith(
            ItemId('d'), "Donald Hatches an Egg", JellyfinSamples.MickeyMouseClubhouseId, 3, 33, "/media/cartoons/Mickey/S03E33.mkv", "tt5625312", "4354865"));
        await _factory.SyncLibraryAsync(_livingRoom.Id, _owner.AccessToken, cancellationToken);
        var goldenBooBoo = await WorkOfItemAsync(ItemId('c'), cancellationToken);
        var donald = await WorkOfItemAsync(ItemId('d'), cancellationToken);
        var seriesId = await WorkOfItemAsync(JellyfinSamples.MickeyMouseClubhouseId, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(SameWorkUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var group = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        // One group, for the owner to tell which file is which: the numbers disagree.
        Assert.Equal((DuplicateClass.SameWork, true), (group.Class, group.NeedsReview));
        Assert.Equal([new DuplicateDistinctionDto(DuplicateDistinctionKind.EpisodeNumbers, null, false)], group.Distinctions);
        Assert.Equal(
            [
                new DuplicateIdentifierDto(MediaIdentifierScheme.Imdb, "tt5625312", null, null),
                new DuplicateIdentifierDto(MediaIdentifierScheme.Tvdb, "4354865", null, null),
            ],
            group.Evidence.Identifiers);
        Assert.NotEqual(goldenBooBoo, donald);
        Assert.Equal(
            [
                ("/media/cartoons/Mickey/S03E31.mkv", goldenBooBoo, (LibraryEpisodeDto?)new LibraryEpisodeDto(seriesId, 3, 31)),
                ("/media/cartoons/Mickey/S03E33.mkv", donald, new LibraryEpisodeDto(seriesId, 3, 33)),
            ],
            group.Files.Select(file => (file.File.ServicePath, Assert.Single(file.Works).Id, file.Works[0].Episode)));
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
    public async Task List_keeps_different_cuts_apart_and_flags_their_editions_for_review()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // One film, two cuts: the names say which.
        const string directorsCutPath = "/media/movies/Alien (1979)/Alien (1979) [Director's Cut] BDRip 1080p.mkv";
        const string theatricalCutPath = "/media/backup/Alien (1979) [Theatrical Cut] BDRip 1080p.mkv";
        var livingRoom = await PlantMovieAsync(_livingRoom, "Чужий", 1979, directorsCutPath, "tt0078748");
        var bedroom = await PlantMovieAsync(_bedroom, "Alien", 1979, theatricalCutPath, "tt0078748");

        //Act
        var response = await _factory.GetHttpRequest(SameWorkUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Copies of one work, but not of one cut: flagged, and each file says its own.
        var group = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal((DuplicateClass.SameWork, true), (group.Class, group.NeedsReview));
        Assert.Equal([new DuplicateDistinctionDto(DuplicateDistinctionKind.Edition, null, false)], group.Distinctions);
        Assert.Equal(
            [
                (theatricalCutPath, bedroom.Work.Id, nameof(MediaEdition.Theatrical)),
                (directorsCutPath, livingRoom.Work.Id, nameof(MediaEdition.DirectorsCut)),
            ],
            group.Files.Select(file => (file.File.ServicePath, Assert.Single(file.Works).Id, string.Join(' ', file.Editions))));
        Assert.Null(group.Recoverable);
    }

    [Fact]
    public async Task List_reads_an_edition_from_the_name_a_library_gives_a_version()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Vasyl, 2026-10-07 — an item with two versions: Jellyfin names each, and only one name says its cut; the file
        // names say nothing.
        const string labelledPath = "/media/movies/Alien (1979)/Alien.1979.1080p.mkv";
        const string unlabelledPath = "/media/movies/Alien (1979)/Alien.1979.2160p.mkv";
        var film = await PlantMovieAsync(_livingRoom, "Чужий", 1979, labelledPath, "tt0078748", editionLabel: "Alien (1979) Director's Cut");
        var version = await _versions.Create(
            film.Work.Id, film.Version.SourceItemId, Guid.NewGuid().ToString("N"), "Alien (1979) 4K", _livingRoom.Id, _livingRoom.Name);
        await _physicalFiles.Create(version.Id, unlabelledPath, sourceConnectionId: _livingRoom.Id, sourceName: _livingRoom.Name);

        //Act
        var response = await _factory.GetHttpRequest(SameWorkUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // A labelled copy and an unlabelled one differ too.
        var group = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal((DuplicateClass.SameWork, true), (group.Class, group.NeedsReview));
        Assert.Equal([new DuplicateDistinctionDto(DuplicateDistinctionKind.Edition, null, false)], group.Distinctions);
        Assert.Equal(
            [
                (labelledPath, film.Version.Id, (string?)"Alien (1979) Director's Cut", nameof(MediaEdition.DirectorsCut)),
                (unlabelledPath, version.Id, "Alien (1979) 4K", ""),
            ],
            group.Files.Select(file => (file.File.ServicePath, Assert.Single(file.Works).VersionId, file.Works[0].EditionLabel, string.Join(' ', file.Editions))));
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

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

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

    private static LibraryDuplicateListResponse ParseList(string body) =>
        JsonSerializer.Deserialize<LibraryDuplicateListResponse>(body, MediaAcquisitionManagerApiFactory.JsonOptions)!;

    private static async Task<LibraryDuplicateListResponse> ReadListAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<LibraryDuplicateListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
}
