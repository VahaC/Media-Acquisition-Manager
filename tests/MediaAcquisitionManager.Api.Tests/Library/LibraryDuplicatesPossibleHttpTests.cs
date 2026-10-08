using System.Net;
using System.Net.Http.Json;
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

// MAM-20 — possible matches on the Duplicates list (GET /api/library/duplicates?class=Possible; spec §3.3 row 4,
// ADR 0028): works, and files no library reported, that share a title's match key with that title's own year —
// ADR 0012's title-and-year tier, never a distance, a year ± 1 or a title alone. A work's titles count from every
// source, displayed or original; a scan-found file's from its file name, then its folder's name, and a name with no
// year claims nothing, nor a name that names seasons or episodes (each episode claims its show's title and year). Not
// offered: a film and an episode (the library says what each is), works whose ids decide (ids of one number space
// shared — already one work — or not shared — two works, The Continental's three items, ADR 0017), two episodes at
// different places of their shows, even under one episode's ids (MAM-37), and a work with itself; what may be any of
// several such works is offered beside each of them, never in one group with them all. Each is for the owner to confirm (NeedsReview), with whether the measured
// durations agree as evidence, and none is redundant. The other classes are LibraryDuplicatesSameFileHttpTests',
// LibraryDuplicatesByteIdenticalHttpTests' and LibraryDuplicatesSameWorkHttpTests'; access, paging and order
// LibraryDuplicatesHttpTests'.
public sealed class LibraryDuplicatesPossibleHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library/duplicates";
    private const string PossibleUrl = $"{BaseUrl}?class=Possible";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string Root = "/media";

    /// <summary>A second Jellyfin, beside the factory's default address.</summary>
    private const string BedroomBaseUrl = "http://127.0.0.2:9";

    // In path order, the order a group lists its files in.
    private const string GonePath = "/media/films/Gone (2001)/Gone (2001) BDRip 1080p.mkv";
    private const string PropalaPath = "/media/movies/Пропала (2001)/Пропала (2001) WEB-DL 1080p.mkv";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private IAllowedRootSeeder _roots = null!;
    private IMediaInspectionSeeder _inspections = null!;
    private IMediaWorkDataFactory _works = null!;
    private IMediaVersionDataFactory _versions = null!;
    private IPhysicalFileDataFactory _physicalFiles = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private IntegrationConnectionEntity _livingRoom = null!;

    public LibraryDuplicatesPossibleHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _roots = new AllowedRootSeeder(_factory.Scope);
        _inspections = new MediaInspectionSeeder(_factory.Scope);
        var db = _factory.Scope.GetRequiredService<ApplicationDbContext>();
        _works = new MediaWorkDataFactory(db, _factory.Time);
        _versions = new MediaVersionDataFactory(db, _factory.Time);
        _physicalFiles = new PhysicalFileDataFactory(db, _factory.Time);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        // The library most works come from; with no path mapping, a name's app path is where the library says it is.
        _livingRoom = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task List_offers_two_works_without_ids_with_one_title_and_year_as_a_possible_match()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var bedroom = await _integrations.Create("jellyfin", "Bedroom", BedroomBaseUrl, apiKey: ApiKey);
        // Neither library knew an id: one shows the English title, the other the Ukrainian one with the English as its
        // original — one title of one year, whichever kind and source says it.
        var gone = await PlantWorkAsync(_livingRoom, GonePath, "Gone", 2001);
        var propala = await PlantWorkAsync(bedroom, PropalaPath, "Пропала", 2001, originalTitle: "Gone");
        // A minute apart: another rip of one cut, well inside the 2 minutes or 10 % (MAM-34's tolerance).
        await _inspections.Measure(GonePath, TimeSpan.FromMinutes(117));
        await _inspections.Measure(PropalaPath, TimeSpan.FromMinutes(118));

        //Act
        var owners = await _factory.GetHttpRequest(PossibleUrl, _owner.AccessToken);
        var members = await _factory.GetHttpRequest(PossibleUrl, _member.AccessToken);

        //Assert
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (owners.StatusCode, members.StatusCode));
        var list = await ReadListAsync(owners, cancellationToken);
        Assert.Equal(1, list.TotalCount);
        var group = Assert.Single(list.Items);
        // Spec §3.3 — a title and a year are never proof: the owner confirms, nothing is redundant.
        Assert.Equal((DuplicateClass.Possible, true), (group.Class, group.NeedsReview));
        Assert.Empty(group.Distinctions);
        Assert.Null(group.Recoverable);
        Assert.Equal([GonePath, PropalaPath], group.Files.Select(file => file.File.ServicePath));
        Assert.Equal(Sorted(gone.Id, propala.Id), WorkIdsOf(group));
        // The evidence: the title both share, and that the measured durations agree; no identity, hash or id.
        Assert.Equal([new DuplicateTitleDto("Gone", 2001)], group.Evidence.Titles);
        Assert.True(group.Evidence.DurationsAgree);
        Assert.Equal(((DuplicateIdentityDto?)null, (string?)null), (group.Evidence.Identity, group.Evidence.Sha256));
        Assert.Empty(group.Evidence.Identifiers);
        // Everyone's to read, alike.
        Assert.Equal([group.Key], (await ReadListAsync(members, cancellationToken)).Items.Select(item => item.Key));
        // An offer only: the library still holds two works, and nothing links them.
        await _factory.WithDbAsync(async db =>
            Assert.Equal(
                (2, 0),
                (await db.MediaWorks.CountAsync(cancellationToken), await db.LibraryItemLinks.CountAsync(cancellationToken))));
    }

    [Fact]
    public async Task List_never_offers_works_with_a_similar_title_or_another_year()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // One letter apart in one year, one title a year apart, and one title thirty-seven years apart: ADR 0012 matches
        // exact keys, with the title's own year — no year ± 1 — and a remake is another film.
        await PlantWorkAsync(_livingRoom, "/media/movies/Alien (1979)/Alien (1979).mkv", "Alien", 1979);
        await PlantWorkAsync(_livingRoom, "/media/movies/Aliens (1979)/Aliens (1979).mkv", "Aliens", 1979);
        await PlantWorkAsync(_livingRoom, GonePath, "Gone", 2001);
        await PlantWorkAsync(_livingRoom, "/media/movies/Gone (2002)/Gone (2002).mkv", "Gone", 2002);
        await PlantWorkAsync(_livingRoom, "/media/movies/Dune (1984)/Dune (1984).mkv", "Dune", 1984);
        await PlantWorkAsync(_livingRoom, "/media/movies/Dune (2021)/Dune (2021).mkv", "Dune", 2021);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(0, list.TotalCount);
        Assert.Empty(list.Items);
    }

    [Fact]
    public async Task List_never_offers_works_whose_ids_differ()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The Continental (ADR 0017): three items of one title and year, each with an IMDb id of its own — the ids
        // say they are three works, and a title can't say otherwise.
        await PlantWorkAsync(_livingRoom, "/media/tv/The Continental (2023)/The Continental S01E01.mkv", "The Continental", 2023, imdbId: "tt23899004");
        await PlantWorkAsync(_livingRoom, "/media/tv/The Continental (2023)/The Continental S01E02.mkv", "The Continental", 2023, imdbId: "tt23899006");
        await PlantWorkAsync(_livingRoom, "/media/tv/The Continental (2023)/The Continental S01E03.mkv", "The Continental", 2023, imdbId: "tt23899008");

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        // No possible match, and no copies of one work either.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(0, list.TotalCount);
        Assert.Empty(list.Items);
    }

    [Fact]
    public async Task List_offers_works_whose_ids_share_no_scheme()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var bedroom = await _integrations.Create("jellyfin", "Bedroom", BedroomBaseUrl, apiKey: ApiKey);
        // One library knew only the IMDb id, the other only the TMDb one: no number space in common, so the ids
        // decide nothing and the title and year are all there is.
        var gone = await PlantWorkAsync(_livingRoom, GonePath, "Gone", 2001, imdbId: "tt0245562");
        var propala = await PlantWorkAsync(bedroom, PropalaPath, "Gone", 2001, tmdbId: "13809");

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        // A possible match only: no shared id, so no copies of one work.
        var group = Assert.Single(list.Items);
        Assert.Equal((DuplicateClass.Possible, true), (group.Class, group.NeedsReview));
        Assert.Equal(Sorted(gone.Id, propala.Id), WorkIdsOf(group));
        Assert.Equal([new DuplicateTitleDto("Gone", 2001)], group.Evidence.Titles);
        // Nothing measured: no word on the durations.
        Assert.Null(group.Evidence.DurationsAgree);
        // Each file shows its own work's ids.
        Assert.Equal(
            [(gone.Id, MediaIdentifierScheme.Imdb, "tt0245562"), (propala.Id, MediaIdentifierScheme.Tmdb, "13809")],
            group.Files
                .SelectMany(file => file.Works)
                .SelectMany(work => work.Identifiers.Select(identifier => (work.Id, identifier.Scheme, identifier.Value)))
                .OrderBy(identifier => identifier.Scheme));
    }

    [Fact]
    public async Task List_never_offers_a_movie_and_an_episode_with_one_title_and_year()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var gone = await PlantWorkAsync(_livingRoom, GonePath, "Gone", 2001);
        var copy = await PlantWorkAsync(_livingRoom, "/media/backup/Gone (2001).mkv", "Gone", 2001);
        // An episode called "Gone", aired the same year: the library says it is an episode, not the film.
        var series = await _works.Create(MediaWorkKind.Series);
        await _works.AddTitle(series.Id, "Night Visions", MediaTitleKind.Display, 2001, _livingRoom.Id, _livingRoom.Name, "series");
        var episode = await PlantWorkAsync(_livingRoom, "/media/tv/Night Visions/Season 1/Night Visions S01E05.mkv", "Gone", 2001, kind: MediaWorkKind.Episode);
        await _works.AddEpisodePosition(episode.Id, series.Id, 1, 5, _livingRoom.Id, _livingRoom.Name, "episode");

        //Act
        var response = await _factory.GetHttpRequest(PossibleUrl, _owner.AccessToken);

        //Assert
        // The two films are offered; the episode is in no group.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var group = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal(Sorted(gone.Id, copy.Id), WorkIdsOf(group));
        Assert.DoesNotContain(group.Files, file => file.File.ServicePath.StartsWith("/media/tv/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task List_offers_a_scanned_file_named_with_a_works_title_and_year()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        const string libraryPath = "/media/movies/Alien (1979)/Alien (1979) BDRip 1080p.mkv";
        const string scannedPath = "/media/downloads/Alien (1979).mkv";
        // A work the library knows by its id, and a file in the media folders no library reports.
        var alien = await PlantWorkAsync(_livingRoom, libraryPath, "Alien", 1979, imdbId: "tt0078748");
        _factory.Files.AddFile(libraryPath, TestMedia.File(1_048_577, 7_000_000_000), TestMedia.Taken());
        _factory.Files.AddFile(scannedPath, TestMedia.File(1_048_579, 4_000_000_000), TestMedia.Taken());
        var scan = await _factory.ScanFoldersAsync(_owner.AccessToken, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(PossibleUrl, _owner.AccessToken);

        //Assert
        Assert.Equal((FolderScanStatus.Succeeded, 2, (int?)1), (scan.Status, scan.FilesFound, scan.FilesUnreported));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // The file's name claims the work's title and year: offered beside the library's copy, held by no work.
        var group = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal((DuplicateClass.Possible, true), (group.Class, group.NeedsReview));
        Assert.Equal([scannedPath, libraryPath], group.Files.Select(file => file.File.ServicePath));
        Assert.Empty(group.Files[0].Works);
        Assert.Equal([alien.Id], WorkIdsOf(group));
        Assert.Equal([new DuplicateTitleDto("Alien", 1979)], group.Evidence.Titles);
    }

    [Fact]
    public async Task List_offers_a_scanned_file_by_its_folders_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        const string libraryPath = "/media/movies/Aliens (1986)/Aliens (1986) BDRip 1080p.mkv";
        const string scannedPath = "/media/downloads/Aliens (1986)/aliens.mkv";
        var aliens = await PlantWorkAsync(_livingRoom, libraryPath, "Aliens", 1986, tmdbId: "679");
        // The file's own name has no year; the folder it sits in names the film.
        await _physicalFiles.CreateUnreported(scannedPath);

        //Act
        var response = await _factory.GetHttpRequest(PossibleUrl, _owner.AccessToken);
        var every = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (response.StatusCode, every.StatusCode));
        var group = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal([scannedPath, libraryPath], group.Files.Select(file => file.File.ServicePath));
        Assert.Equal([aliens.Id], WorkIdsOf(group));
        Assert.Empty(group.Files[0].Works);
        Assert.Equal([new DuplicateTitleDto("Aliens", 1986)], group.Evidence.Titles);
        // A file no library reported can only be a possible match: in no other class, whatever its name claims.
        var list = await ReadListAsync(every, cancellationToken);
        Assert.Equal(1, list.TotalCount);
        Assert.Equal([(DuplicateClass.Possible, group.Key)], list.Items.Select(item => (item.Class, item.Key)));
    }

    [Fact]
    public async Task List_offers_two_scanned_files_named_with_one_title_and_year()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // No library reports either: a download and a backup, each named by the film.
        const string downloadPath = "/media/downloads/Gone (2001).mkv";
        const string backupPath = "/media/backup/Gone (2001)/gone.mkv";
        await _physicalFiles.CreateUnreported(downloadPath);
        await _physicalFiles.CreateUnreported(backupPath);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // A group of names alone: held by no work, its title what both claim.
        var group = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal((DuplicateClass.Possible, true), (group.Class, group.NeedsReview));
        Assert.Equal([backupPath, downloadPath], group.Files.Select(file => file.File.ServicePath));
        Assert.All(group.Files, file => Assert.Empty(file.Works));
        Assert.Equal([new DuplicateTitleDto("Gone", 2001)], group.Evidence.Titles);
    }

    [Fact]
    public async Task List_never_offers_the_episodes_of_a_show_no_library_reports_as_one_work()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A season the scan found before any library reported it, named the Sonarr way and the scene way: every name
        // claims the show's one title and year, each file is another episode.
        string[] paths =
        [
            "/media/tv/Severance (2022)/Season 01/Severance (2022) - S01E01 - Good News About Hell.mkv",
            "/media/tv/Severance (2022)/Season 01/Severance (2022) - S01E02 - Half Loop.mkv",
            "/media/tv/Severance (2022)/Season 01/Severance (2022) - S01E03 - In Perpetuity.mkv",
            "/media/downloads/Severance.S01.2022.1080p/Severance.S01E01.mkv",
            "/media/downloads/Severance.S01.2022.1080p/Severance.S01E02.mkv",
        ];
        foreach (var path in paths)
            await _physicalFiles.CreateUnreported(path);
        // And a film of that title and year a library holds, which no episode's name may be offered as.
        await PlantWorkAsync(_livingRoom, "/media/movies/Severance (2022).mkv", "Severance", 2022);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(0, list.TotalCount);
        Assert.Empty(list.Items);
    }

    [Fact]
    public async Task List_offers_a_file_beside_each_work_whose_ids_differ_never_the_works_together()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var bedroom = await _integrations.Create("jellyfin", "Bedroom", BedroomBaseUrl, apiKey: ApiKey);
        // The Continental's three items with three IMDb ids (three works, ADR 0017), the bedroom's item of that title
        // and year with no id, and a file no library reports named the same: the last two could be any of the three.
        var first = await PlantWorkAsync(_livingRoom, "/media/tv/The Continental (2023)/Night 1.mkv", "The Continental", 2023, imdbId: "tt23899004");
        var second = await PlantWorkAsync(_livingRoom, "/media/tv/The Continental (2023)/Night 2.mkv", "The Continental", 2023, imdbId: "tt23899006");
        var third = await PlantWorkAsync(_livingRoom, "/media/tv/The Continental (2023)/Night 3.mkv", "The Continental", 2023, imdbId: "tt23899008");
        var withoutIds = await PlantWorkAsync(bedroom, "/media/backup/The Continental (2023).mkv", "The Continental", 2023);
        const string scannedPath = "/media/downloads/The.Continental.2023.1080p.mkv";
        await _physicalFiles.CreateUnreported(scannedPath);

        //Act
        var response = await _factory.GetHttpRequest(PossibleUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // ADR 0028 — one group per item, each with what may be it; never one saying the three may be one work.
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(3, list.TotalCount);
        Assert.All(list.Items, group =>
        {
            Assert.Equal((DuplicateClass.Possible, true), (group.Class, group.NeedsReview));
            // One item with an IMDb id, the item without, and the scanned file.
            Assert.Equal(2, WorkIdsOf(group).Count);
            Assert.Contains(withoutIds.Id, WorkIdsOf(group));
            Assert.Equal(3, group.Files.Count);
            Assert.Contains(scannedPath, group.Files.Select(file => file.File.ServicePath));
            Assert.Equal([new DuplicateTitleDto("The Continental", 2023)], group.Evidence.Titles);
        });
        Assert.Equal(
            Sorted(first.Id, second.Id, third.Id),
            list.Items.Select(group => WorkIdsOf(group).Single(id => id != withoutIds.Id)).Order());
        Assert.Equal(3, list.Items.Select(group => group.Key).Distinct().Count());
    }

    [Fact]
    public async Task List_never_offers_episodes_of_two_shows_or_two_places_with_one_title_and_year()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var bedroom = await _integrations.Create("jellyfin", "Bedroom", BedroomBaseUrl, apiKey: ApiKey);
        // "Episode 1" of 2023 is what TMDb and Jellyfin call an episode with no title of its own: here the first episode
        // of two shows, one known by its TVDB id, the other by its TMDb id, which share no number space.
        var firstShow = await PlantSeriesAsync(_livingRoom, "Frieren", 2023, MediaIdentifierScheme.Tvdb, "424536");
        var secondShow = await PlantSeriesAsync(bedroom, "Pluto", 2023, MediaIdentifierScheme.TmdbTv, "99071");
        await PlantEpisodeAsync(_livingRoom, firstShow, 1, 1, "/media/anime/Frieren/S01E01.mkv", "Episode 1", 2023);
        await PlantEpisodeAsync(bedroom, secondShow, 1, 1, "/media/anime/Pluto/S01E01.mkv", "Episode 1", 2023);
        // And two parts of one show with no ids, the first of each season, aired in one year.
        var thirdShow = await PlantSeriesAsync(_livingRoom, "Anthology", 2023);
        await PlantEpisodeAsync(_livingRoom, thirdShow, 1, 1, "/media/tv/Anthology/S01E01.mkv", "Part 1", 2023);
        await PlantEpisodeAsync(_livingRoom, thirdShow, 2, 1, "/media/tv/Anthology/S02E01.mkv", "Part 1", 2023);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        // An episode is known by its show and its place, which the library gives here: they say these are different.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(0, list.TotalCount);
        Assert.Empty(list.Items);
    }

    [Fact]
    public async Task List_never_offers_two_episodes_with_one_imdb_id_at_different_places_with_one_title_and_year()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // MAM-37 — Mickey Mouse Clubhouse's S03E31 carries S03E33's IMDb id, and production's library titles both "Donald
        // Hatches an Egg" of 2010: two episodes at different places, so neither one work nor a possible match.
        const string goldenBooBooPath = "/media/cartoons/Mickey Mouse Clubhouse/Mickey Mouse Clubhouse_S03E31_-_The Golden Boo Boo [ukr_eng] WEB-DL 1080p [Hurtom].mkv";
        const string donaldPath = "/media/cartoons/Mickey Mouse Clubhouse/Mickey Mouse Clubhouse_S03E33_-_Donald Hatches an Egg [ukr_eng] WEB-DL 1080p [Hurtom].mkv";
        var show = await PlantSeriesAsync(_livingRoom, "Mickey Mouse Clubhouse", 2006, MediaIdentifierScheme.Tvdb, "79854");
        var goldenBooBoo = await PlantEpisodeAsync(_livingRoom, show, 3, 31, goldenBooBooPath, "Donald Hatches an Egg", 2010, "tt5625312");
        var donald = await PlantEpisodeAsync(_livingRoom, show, 3, 33, donaldPath, "Donald Hatches an Egg", 2010, "tt5625312");

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(0, list.TotalCount);
        Assert.Empty(list.Items);
        // The library keeps them as it has them: two episodes, each at its place, one IMDb id between them.
        await _factory.WithDbAsync(async db =>
        {
            var places = await db.MediaEpisodePositions.AsNoTracking()
                .Where(position => position.SeriesWorkId == show.Id)
                .Select(position => new { position.MediaWorkId, position.SeasonNumber, position.EpisodeNumber })
                .ToListAsync(cancellationToken);
            Assert.Equal(
                [(goldenBooBoo.Id, (int?)3, (int?)31), (donald.Id, 3, 33)],
                places.Select(place => (place.MediaWorkId, place.SeasonNumber, place.EpisodeNumber)).OrderBy(place => place.EpisodeNumber));
        });
    }

    [Fact]
    public async Task List_offers_one_episode_of_a_show_without_ids_in_two_libraries()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var bedroom = await _integrations.Create("jellyfin", "Bedroom", BedroomBaseUrl, apiKey: ApiKey);
        // Neither library knows an id of the show or the episode: one title, one year, one place in one show's title.
        var livingRoomShow = await PlantSeriesAsync(_livingRoom, "Anthology", 2023);
        var bedroomShow = await PlantSeriesAsync(bedroom, "Anthology", 2023);
        var livingRoom = await PlantEpisodeAsync(_livingRoom, livingRoomShow, 1, 1, "/media/tv/Anthology/S01E01.mkv", "Part 1", 2023);
        var copy = await PlantEpisodeAsync(bedroom, bedroomShow, 1, 1, "/media/backup/Anthology/S01E01.mkv", "Part 1", 2023);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var group = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal((DuplicateClass.Possible, true), (group.Class, group.NeedsReview));
        Assert.Equal(Sorted(livingRoom.Id, copy.Id), WorkIdsOf(group));
        Assert.Equal([new DuplicateTitleDto("Part 1", 2023)], group.Evidence.Titles);
    }

    [Fact]
    public async Task List_flags_a_possible_matchs_files_whose_editions_differ()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var bedroom = await _integrations.Create("jellyfin", "Bedroom", BedroomBaseUrl, apiKey: ApiKey);
        // Two works without ids, one title and year; the names say two cuts.
        const string directorsCutPath = "/media/backup/Alien (1979) [Director's Cut].mkv";
        const string theatricalPath = "/media/movies/Alien (1979) [Theatrical Cut].mkv";
        await PlantWorkAsync(_livingRoom, theatricalPath, "Alien", 1979);
        await PlantWorkAsync(bedroom, directorsCutPath, "Alien", 1979);

        //Act
        var response = await _factory.GetHttpRequest(PossibleUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var group = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal((DuplicateClass.Possible, true), (group.Class, group.NeedsReview));
        Assert.Equal([new DuplicateDistinctionDto(DuplicateDistinctionKind.Edition, null, false)], group.Distinctions);
        Assert.Equal(
            [(directorsCutPath, nameof(MediaEdition.DirectorsCut)), (theatricalPath, nameof(MediaEdition.Theatrical))],
            group.Files.Select(file => (file.File.ServicePath, string.Join(' ', file.Editions))));
    }

    [Fact]
    public async Task List_never_offers_a_scanned_file_whose_name_has_no_year()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantWorkAsync(_livingRoom, "/media/movies/Alien (1979)/Alien (1979) BDRip 1080p.mkv", "Alien", 1979);
        // No year in the file's name or its folder's: nothing tells where the title ends, so the name claims nothing.
        await _physicalFiles.CreateUnreported("/media/downloads/Alien BDRip 1080p.mkv");
        await _physicalFiles.CreateUnreported("/media/downloads/Alien/alien.mkv");

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(0, list.TotalCount);
        Assert.Empty(list.Items);
    }

    [Theory]
    [InlineData(90, 140, false)]
    [InlineData(117, 125, true)]
    [InlineData(117, null, null)]
    public async Task List_says_whether_the_durations_agree(int goneMinutes, int? propalaMinutes, bool? agree)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        await PlantWorkAsync(_livingRoom, GonePath, "Gone", 2001);
        await PlantWorkAsync(_livingRoom, PropalaPath, "Gone", 2001);
        // Measured by a real inspection; 50 minutes apart is another film or another cut, 8 of 125 another rip. A file
        // the inspection can't find has no duration.
        _factory.Files.AddFile(GonePath, TestMedia.File(1_048_577, 7_000_000_000), new MediaFacts(TimeSpan.FromMinutes(goneMinutes), null, []));
        if (propalaMinutes is { } minutes)
            _factory.Files.AddFile(PropalaPath, TestMedia.File(1_048_579, 4_000_000_000), new MediaFacts(TimeSpan.FromMinutes(minutes), null, []));
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(PossibleUrl, _owner.AccessToken);

        //Assert
        // Evidence for the owner, never a verdict: offered either way.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var group = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal((DuplicateClass.Possible, true, agree), (group.Class, group.NeedsReview, group.Evidence.DurationsAgree));
    }

    [Fact]
    public async Task List_gives_a_possible_match_no_running_times_distinction_whatever_its_durations()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        // MAM-37 — «Ніж у воді» and its featurette as two works without ids under one title and year: 27.9 % of the film,
        // which would flag copies of one work. A possible match keeps its own evidence, the 2 minutes or 10 %.
        const string filmPath = "/media/movies/Nóż w wodzie (1962)/Nóż w wodzie (1962).mkv";
        const string featurettePath = "/media/movies/Nóż w wodzie (1962)/Polanski+Skolimowski o Noże.mkv";
        await PlantWorkAsync(_livingRoom, filmPath, "Nóż w wodzie", 1962);
        await PlantWorkAsync(_livingRoom, featurettePath, "Nóż w wodzie", 1962);
        _factory.Files.AddFile(filmPath, TestMedia.File(1_048_577, 7_000_000_000), new MediaFacts(TimeSpan.FromMilliseconds(5_731_959), null, []));
        _factory.Files.AddFile(featurettePath, TestMedia.File(1_048_579, 1_000_000_000), new MediaFacts(TimeSpan.FromMilliseconds(1_600_699), null, []));
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(PossibleUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var group = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal((DuplicateClass.Possible, true), (group.Class, group.NeedsReview));
        Assert.Empty(group.Distinctions);
        Assert.False(group.Evidence.DurationsAgree);
        Assert.Null(group.Evidence.RunningTimes);
        Assert.Equal([filmPath, featurettePath], group.Files.Select(file => file.File.ServicePath));
    }

    [Fact]
    public async Task List_never_offers_a_work_with_itself()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // One work under two titles of one year, with two files: nothing to match it with.
        var gone = await PlantWorkAsync(_livingRoom, GonePath, "Gone", 2001, originalTitle: "Gone");
        var version = await _versions.Create(
            gone.Id, sourceItemId: Guid.NewGuid().ToString("N"), sourceConnectionId: _livingRoom.Id, sourceName: _livingRoom.Name);
        await _physicalFiles.Create(version.Id, "/media/films/Gone (2001)/Gone (2001) WEB-DL 720p.mkv", sourceConnectionId: _livingRoom.Id, sourceName: _livingRoom.Name);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(0, list.TotalCount);
        Assert.Empty(list.Items);
    }

    [Fact]
    public async Task List_never_offers_two_works_already_grouped_as_one_work()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var bedroom = await _integrations.Create("jellyfin", "Bedroom", BedroomBaseUrl, apiKey: ApiKey);
        // One IMDb id in two libraries: copies of one work, which a title and a year have nothing to add to.
        var gone = await PlantWorkAsync(_livingRoom, GonePath, "Gone", 2001, imdbId: "tt0245562");
        var propala = await PlantWorkAsync(bedroom, PropalaPath, "Gone", 2001, imdbId: "tt0245562");

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var group = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal(DuplicateClass.SameWork, group.Class);
        Assert.Equal(Sorted(gone.Id, propala.Id), WorkIdsOf(group));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    // A work as library reported it, with one version naming servicePath — what a sync stores: its displayed title of
    // year, an original title and the ids when given.
    private async Task<MediaWorkEntity> PlantWorkAsync(
        IntegrationConnectionEntity library,
        string servicePath,
        string title,
        int year,
        string? imdbId = null,
        string? tmdbId = null,
        string? originalTitle = null,
        MediaWorkKind kind = MediaWorkKind.Movie)
    {
        var itemId = Guid.NewGuid().ToString("N");
        var work = await _works.Create(kind);
        await _works.AddTitle(work.Id, title, MediaTitleKind.Display, year, library.Id, library.Name, itemId);
        if (originalTitle is not null)
            await _works.AddTitle(work.Id, originalTitle, MediaTitleKind.Original, year, library.Id, library.Name, itemId);
        if (imdbId is not null)
            await _works.AddIdentifier(work.Id, MediaIdentifierScheme.Imdb, imdbId, library.Id, library.Name, itemId);
        if (tmdbId is not null)
            await _works.AddIdentifier(work.Id, MediaIdentifierScheme.Tmdb, tmdbId, library.Id, library.Name, itemId);
        var version = await _versions.Create(work.Id, itemId, sourceConnectionId: library.Id, sourceName: library.Name);
        await _physicalFiles.Create(version.Id, servicePath, sourceConnectionId: library.Id, sourceName: library.Name);
        return work;
    }

    // A series as a library reported it: its displayed title and year, and its one id when given. It holds no file.
    private async Task<MediaWorkEntity> PlantSeriesAsync(
        IntegrationConnectionEntity library, string title, int year, MediaIdentifierScheme? scheme = null, string? value = null)
    {
        var itemId = Guid.NewGuid().ToString("N");
        var series = await _works.Create(MediaWorkKind.Series);
        await _works.AddTitle(series.Id, title, MediaTitleKind.Display, year, library.Id, library.Name, itemId);
        if (scheme is { } registry && value is not null)
            await _works.AddIdentifier(series.Id, registry, value, library.Id, library.Name, itemId);
        return series;
    }

    // An episode of the series at its season and number, titled with the year it aired, with the IMDb id of its own
    // when given, and one version holding the file at the path given.
    private async Task<MediaWorkEntity> PlantEpisodeAsync(
        IntegrationConnectionEntity library,
        MediaWorkEntity series,
        int season,
        int episode,
        string servicePath,
        string title,
        int year,
        string? imdbId = null)
    {
        var work = await PlantWorkAsync(library, servicePath, title, year, imdbId, kind: MediaWorkKind.Episode);
        await _works.AddEpisodePosition(work.Id, series.Id, season, episode, library.Id, library.Name, Guid.NewGuid().ToString("N"));
        return work;
    }

    // The works a group's files are held by, each once, in id order.
    private static List<Guid> WorkIdsOf(LibraryDuplicateGroupDto group) =>
        [.. group.Files.SelectMany(file => file.Works).Select(work => work.Id).Distinct().Order()];

    private static List<Guid> Sorted(params Guid[] ids) => [.. ids.Order()];

    private static async Task<LibraryDuplicateListResponse> ReadListAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<LibraryDuplicateListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
}
