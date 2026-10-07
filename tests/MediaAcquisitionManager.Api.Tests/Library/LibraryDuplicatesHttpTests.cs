using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.Extensions.DependencyInjection;

namespace MediaAcquisitionManager.Api.Tests.Library;

// MAM-20 — the library's duplicates as one list (GET /api/library/duplicates; ADR 0028): every signed-in user reads it,
// like the Library it is worked out from, with the owner-only sentences (a path a mapping doesn't cover) null for
// everyone else. A class filter by name in any letter case, never a number; paging with the Library's sentences,
// checked first, and an empty page past the end that keeps the total. Groups in class order (specification §3.3's
// rows), then by the title of their first work, each once across the pages; classes, distinctions, editions and gaps
// travel by name. What makes each class is the class files': LibraryDuplicatesSameFileHttpTests,
// LibraryDuplicatesByteIdenticalHttpTests, LibraryDuplicatesSameWorkHttpTests and LibraryDuplicatesPossibleHttpTests.
// MAM-21 — a decision filter by name with the class filter's rules, checked after it; what the owner's decisions do to
// the list is LibraryDuplicateDecisionsHttpTests'.
public sealed class LibraryDuplicatesHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library/duplicates";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string Root = "/media";

    /// <summary>A second Jellyfin, beside the factory's default address.</summary>
    private const string BedroomBaseUrl = "http://127.0.0.2:9";

    /// <summary>A third, whose path mapping covers none of its files.</summary>
    private const string AtticBaseUrl = "http://127.0.0.3:9";

    private const string AlienImdbId = "tt0078748";
    private const string GattacaImdbId = "tt0119177";
    private const string HeatImdbId = "tt0113277";
    private const string BladeRunnerImdbId = "tt0083658";

    /// <summary>Where the attic's Jellyfin sees its copy of Alien: under no prefix its mapping knows.</summary>
    private const string AtticAlienPath = "/srv/films/Alien (1979).mkv";

    // Spelled out rather than read from the product code: these are what the owner reads on the page.
    private const string AtticAlienPathProblem = "No path mapping of this integration covers “/srv/films/Alien (1979).mkv”.";

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

    public LibraryDuplicatesHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public static TheoryData<string, string> UnknownClasses => new()
    {
        { "Hardlink", NotAClass("Hardlink") },
        // By name only: the number of a class is not its name.
        { "3", NotAClass("3") },
        { "SameWork,Possible", NotAClass("SameWork,Possible") },
        // A pasted page of text comes back cut, never whole.
        { new string('x', 500), NotAClass(new string('x', 32) + "…") },
    };

    // MAM-21 — the decision filter's, with the class filter's rules.
    public static TheoryData<string, string> UnknownDecisions => new()
    {
        { "Maybe", NotADecision("Maybe") },
        // By name only: the number of a decision is not its name.
        { "1", NotADecision("1") },
        { "Confirmed,Dismissed", NotADecision("Confirmed,Dismissed") },
        // A pasted page of text comes back cut, never whole.
        { new string('x', 500), NotADecision(new string('x', 32) + "…") },
    };

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
        // Two Jellyfins over one disk, with no path mapping: the app looks for each file where its library says it is.
        _livingRoom = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        _bedroom = await _integrations.Create("jellyfin", "Bedroom", BedroomBaseUrl, apiKey: ApiKey);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task List_without_a_token_is_401()
    {
        //Arrange
        await PlantCopiesAsync("Alien", 1979, AlienImdbId);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl);

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task List_before_any_sync_is_an_empty_page()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var owners = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);
        var members = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (owners.StatusCode, members.StatusCode));
        foreach (var list in new[] { await ReadListAsync(owners, cancellationToken), await ReadListAsync(members, cancellationToken) })
        {
            Assert.Equal((0, 1, 25), (list.TotalCount, list.Page, list.PageSize));
            Assert.Empty(list.Items);
        }

        // No byte-identical group, so no file system to ask about.
        Assert.Empty(_factory.Files.DescribedDevices);
    }

    [Theory]
    [MemberData(nameof(UnknownClasses))]
    public async Task List_with_an_unknown_class_is_400_naming_the_classes(string duplicateClass, string error)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantCopiesAsync("Alien", 1979, AlienImdbId);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?class={Uri.EscapeDataString(duplicateClass)}", _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(error, await response.ReadErrorAsync(cancellationToken));
    }

    [Theory]
    [MemberData(nameof(UnknownDecisions))]
    public async Task List_with_an_unknown_decision_is_400_naming_the_decisions(string decision, string error)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantPossibleMatchAsync("Arrival", 2016);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?decision={Uri.EscapeDataString(decision)}", _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(error, await response.ReadErrorAsync(cancellationToken));
    }

    [Theory]
    [InlineData("page=0", "Page must be 1 or more.")]
    [InlineData("page=-1", "Page must be 1 or more.")]
    [InlineData("pageSize=0", "Page size must be between 1 and 100.")]
    [InlineData("pageSize=101", "Page size must be between 1 and 100.")]
    // Paging is checked first: one sentence, the same as the Library's.
    [InlineData("page=0&class=Hardlink", "Page must be 1 or more.")]
    // MAM-21 — then the class, then the decision.
    [InlineData("pageSize=101&decision=Maybe", "Page size must be between 1 and 100.")]
    [InlineData("class=Hardlink&decision=Maybe", "“Hardlink” is not a duplicate class. Use SameFile, ByteIdentical, SameWork or Possible.")]
    public async Task List_with_a_page_or_page_size_out_of_range_is_400(string query, string error)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantCopiesAsync("Alien", 1979, AlienImdbId);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?{query}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(error, await response.ReadErrorAsync(cancellationToken));
    }

    [Theory]
    [InlineData("SameWork")]
    [InlineData("samework")]
    [InlineData("SAMEWORK")]
    [InlineData("sAmEwOrK")]
    public async Task List_takes_a_class_in_any_letter_case(string duplicateClass)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantCopiesAsync("Alien", 1979, AlienImdbId);
        await PlantPossibleMatchAsync("Arrival", 2016);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?class={duplicateClass}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(1, list.TotalCount);
        var group = Assert.Single(list.Items);
        Assert.Equal((DuplicateClass.SameWork, "Alien"), (group.Class, TitleOf(group)));
    }

    [Fact]
    public async Task List_filters_by_class_and_pages_every_group_once()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Three works with a copy in each library, and two pairs of id-less works with one title and year.
        await PlantCopiesAsync("Heat", 1995, HeatImdbId);
        await PlantCopiesAsync("Alien", 1979, AlienImdbId);
        await PlantCopiesAsync("Gattaca", 1997, GattacaImdbId);
        await PlantPossibleMatchAsync("Brazil", 1985);
        await PlantPossibleMatchAsync("Arrival", 2016);

        //Act
        var first = await ReadListAsync(await _factory.GetHttpRequest($"{BaseUrl}?class=SameWork&page=1&pageSize=2", _member.AccessToken), cancellationToken);
        var second = await ReadListAsync(await _factory.GetHttpRequest($"{BaseUrl}?class=SameWork&page=2&pageSize=2", _member.AccessToken), cancellationToken);
        var past = await ReadListAsync(await _factory.GetHttpRequest($"{BaseUrl}?class=SameWork&page=3&pageSize=2", _member.AccessToken), cancellationToken);
        var possible = await ReadListAsync(await _factory.GetHttpRequest($"{BaseUrl}?class=Possible", _member.AccessToken), cancellationToken);
        var all = await ReadListAsync(await _factory.GetHttpRequest(BaseUrl, _member.AccessToken), cancellationToken);

        //Assert
        // Each page of one class's groups carries their total; past the last, an empty page still does.
        Assert.Equal((3, 1, 2, 2), (first.TotalCount, first.Page, first.PageSize, first.Items.Count));
        Assert.Equal((3, 2, 2, 1), (second.TotalCount, second.Page, second.PageSize, second.Items.Count));
        Assert.Equal((3, 3, 2, 0), (past.TotalCount, past.Page, past.PageSize, past.Items.Count));
        List<LibraryDuplicateGroupDto> paged = [.. first.Items, .. second.Items];
        Assert.All(paged, group => Assert.Equal(DuplicateClass.SameWork, group.Class));
        // Every group once, in title order, across the pages.
        Assert.Equal(["Alien", "Gattaca", "Heat"], paged.Select(TitleOf));
        Assert.Equal(3, paged.Select(group => group.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(2, possible.TotalCount);
        Assert.All(possible.Items, group => Assert.Equal(DuplicateClass.Possible, group.Class));
        Assert.Equal(["Arrival", "Brazil"], possible.Items.Select(TitleOf));
        // No filter: both classes, the same groups by the same keys.
        Assert.Equal(5, all.TotalCount);
        Assert.Equal(paged.Concat(possible.Items).Select(group => group.Key), all.Items.Select(group => group.Key));
    }

    [Fact]
    public async Task List_orders_groups_by_class_then_title()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        // Planted out of order, and each class's titles before the one above it in the alphabet: the class comes
        // first, the title only inside it.
        await PlantPossibleMatchAsync("Arrival", 2016);
        await PlantCopiesAsync("Gattaca", 1997, GattacaImdbId);
        await PlantCopiesAsync("Alien", 1979, AlienImdbId);
        await PlantByteIdenticalCopiesAsync("Brazil", 1985, 1_048_603, 1_048_605, 1_000_000_003);
        await PlantHardlinksAsync("Zodiac", 2007, 1_048_607, 1_000_000_007);
        await PlantHardlinksAsync("Memento", 2000, 1_048_601, 1_000_000_001);
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(
            [
                (DuplicateClass.SameFile, "Memento"),
                (DuplicateClass.SameFile, "Zodiac"),
                (DuplicateClass.ByteIdentical, "Brazil"),
                (DuplicateClass.SameWork, "Alien"),
                (DuplicateClass.SameWork, "Gattaca"),
                (DuplicateClass.Possible, "Arrival"),
            ],
            list.Items.Select(group => (group.Class, TitleOf(group))));
        Assert.Equal(6, list.TotalCount);
    }

    // MAM-21 — an episode's group is headed by its series, then where the episode sits, so it is ordered by them too:
    // by the episodes' own titles, Yellowstone's would come in the order A Thundering, Behind the Story, Daybreak, Kill
    // the Messenger, with Breaking Bad's Pilot after them all.
    [Fact]
    public async Task List_orders_episode_groups_by_their_series_then_season_and_episode()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var yellowstone = await PlantSeriesAsync(_livingRoom, "Yellowstone");
        var breakingBad = await PlantSeriesAsync(_livingRoom, "Breaking Bad");
        await PlantEpisodeCopiesAsync(yellowstone, 2, 1, "A Thundering", "tt8526530");
        await PlantEpisodeCopiesAsync(yellowstone, 1, 2, "Kill the Messenger", "tt7999740");
        await PlantEpisodeCopiesAsync(yellowstone, 0, 1, "Behind the Story", "tt9022356");
        await PlantEpisodeCopiesAsync(breakingBad, 1, 1, "Pilot", "tt0959621");
        await PlantEpisodeCopiesAsync(yellowstone, 1, 1, "Daybreak", "tt4236770");

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?class=SameWork", _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        // The specials after the numbered seasons, as the Library lists a show's seasons.
        Assert.Equal(
            [
                ("Breaking Bad", 1, 1, "Pilot"),
                ("Yellowstone", 1, 1, "Daybreak"),
                ("Yellowstone", 1, 2, "Kill the Messenger"),
                ("Yellowstone", 2, 1, "A Thundering"),
                ("Yellowstone", 0, 1, "Behind the Story"),
            ],
            list.Items.Select(group =>
            {
                var work = group.Files.SelectMany(file => file.Works).DistinctBy(work => (work.SeriesTitle, work.Title)).Single();
                return (work.SeriesTitle, work.Episode!.SeasonNumber, work.Episode.EpisodeNumber, work.Title);
            }));
    }

    [Fact]
    public async Task List_gives_a_member_the_groups_without_the_owners_path_problems()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A copy of Alien in a library whose mapping covers none of its files: the reason quotes the owner's paths.
        var attic = await _integrations.Create(
            "jellyfin", "Attic", AtticBaseUrl, apiKey: ApiKey, pathMappings: [new PathMappingPair("/data", Root)]);
        await PlantMovieAsync(_livingRoom, "Alien", 1979, MoviePath("Alien", 1979), AlienImdbId);
        await PlantMovieAsync(attic, "Alien", 1979, AtticAlienPath, AlienImdbId);
        await PlantPossibleMatchAsync("Arrival", 2016);

        //Act
        var owners = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);
        var members = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (owners.StatusCode, members.StatusCode));
        var ownersList = await ReadListAsync(owners, cancellationToken);
        var membersList = await ReadListAsync(members, cancellationToken);
        // Everyone sees that the attic's file has no app path, and why only the owner.
        var ownersFile = ownersList.Items.SelectMany(group => group.Files).Single(file => file.File.ServicePath == AtticAlienPath);
        var membersFile = membersList.Items.SelectMany(group => group.Files).Single(file => file.File.ServicePath == AtticAlienPath);
        Assert.Equal((PathMapStatus.NotCovered, (string?)null, (string?)AtticAlienPathProblem), (ownersFile.File.PathStatus, ownersFile.File.AppPath, ownersFile.File.PathProblem));
        Assert.Equal((PathMapStatus.NotCovered, (string?)null, (string?)null), (membersFile.File.PathStatus, membersFile.File.AppPath, membersFile.File.PathProblem));
        // Otherwise the same groups, field for field.
        Assert.Equal(
            [(DuplicateClass.SameWork, "Alien"), (DuplicateClass.Possible, "Arrival")],
            membersList.Items.Select(group => (group.Class, TitleOf(group))));
        var ownersBody = JsonNode.Parse(await owners.Content.ReadAsStringAsync(cancellationToken))!;
        foreach (var file in ownersBody["items"]!.AsArray().SelectMany(group => group!["files"]!.AsArray()))
            file!["file"]!["pathProblem"] = null;
        Assert.True(JsonNode.DeepEquals(ownersBody, JsonNode.Parse(await members.Content.ReadAsStringAsync(cancellationToken))));
    }

    [Fact]
    public async Task List_writes_classes_distinctions_and_editions_by_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        await PlantHardlinksAsync("Memento", 2000, 1_048_601, 1_000_000_001);
        await PlantByteIdenticalCopiesAsync("Brazil", 1985, 1_048_603, 1_048_605, 1_000_000_003);
        // One IMDb id, two TMDb ids, and the bedroom's copy the Final Cut by its file name.
        await PlantMovieAsync(_livingRoom, "Blade Runner", 1982, MoviePath("Blade Runner", 1982), BladeRunnerImdbId, "78");
        await PlantMovieAsync(_bedroom, "Blade Runner", 1982, "/media/backup/Blade Runner (1982) Final Cut.mkv", BladeRunnerImdbId, "79");
        // On a file system that can share blocks, so the estimate's gap shows.
        _factory.Files.SetFileSystem(TestMedia.DeviceId, "btrfs");
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        // What the Duplicates page (MAM-21) reads, by these names: enums travel as their names, never numbers.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))!;
        Assert.Equal(["items", "totalCount", "page", "pageSize"], body.AsObject().Select(property => property.Key));
        var items = body["items"]!.AsArray();
        Assert.Equal(["SameFile", "ByteIdentical", "SameWork"], items.Select(item => item!["class"]!.GetValue<string>()));
        Assert.All(items, item =>
        {
            Assert.Equal(
                // MAM-21 — the owner's decision last, null while undecided and for every class but a possible match.
                ["key", "class", "needsReview", "distinctions", "evidence", "files", "space", "recoverable", "decision"],
                item!.AsObject().Select(property => property.Key));
            Assert.Matches("^[0-9a-f]{32}$", item["key"]!.GetValue<string>());
            Assert.Equal(
                ["identity", "sha256", "identifiers", "titles", "durationsAgree"],
                item["evidence"]!.AsObject().Select(property => property.Key));
            Assert.Equal(["sizeBytes", "fileCount", "nameCount", "unmeasuredCount"], item["space"]!.AsObject().Select(property => property.Key));
            Assert.Null(item["decision"]);
        });

        var sameFile = items[0]!;
        Assert.Equal(["deviceId", "inode", "linkCount", "nameCount"], sameFile["evidence"]!["identity"]!.AsObject().Select(property => property.Key));
        Assert.Equal(["sizeBytes", "gap"], sameFile["recoverable"]!.AsObject().Select(property => property.Key));
        Assert.Equal(0L, sameFile["recoverable"]!["sizeBytes"]!.GetValue<long>());
        Assert.Null(sameFile["recoverable"]!["gap"]);

        var byteIdentical = items[1]!;
        Assert.Matches("^[0-9a-f]{64}$", byteIdentical["evidence"]!["sha256"]!.GetValue<string>());
        Assert.Null(byteIdentical["recoverable"]!["sizeBytes"]);
        Assert.Equal("SharingFileSystem", byteIdentical["recoverable"]!["gap"]!.GetValue<string>());

        var sameWork = items[2]!;
        Assert.True(sameWork["needsReview"]!.GetValue<bool>());
        Assert.Equal(
            new (string, string?, bool)[] { ("IdentifierConflict", "Tmdb", false), ("Edition", null, false) },
            sameWork["distinctions"]!.AsArray().Select(distinction => (
                distinction!["kind"]!.GetValue<string>(),
                distinction["scheme"]?.GetValue<string>(),
                distinction["ofSeries"]!.GetValue<bool>())));
        var identifier = Assert.Single(sameWork["evidence"]!["identifiers"]!.AsArray())!;
        Assert.Equal(["scheme", "value", "seasonNumber", "episodeNumber"], identifier.AsObject().Select(property => property.Key));
        Assert.Equal(("Imdb", BladeRunnerImdbId), (identifier["scheme"]!.GetValue<string>(), identifier["value"]!.GetValue<string>()));
        Assert.Null(sameWork["recoverable"]);

        // The files in path order: the bedroom's Final Cut first, the living room's unlabelled one after.
        var files = sameWork["files"]!.AsArray();
        Assert.All(files, file =>
            Assert.Equal(
                ["file", "readSizeBytes", "deviceId", "inode", "linkCount", "sha256", "editions", "works"],
                file!.AsObject().Select(property => property.Key)));
        Assert.Equal(["FinalCut"], files[0]!["editions"]!.AsArray().Select(edition => edition!.GetValue<string>()));
        Assert.Empty(files[1]!["editions"]!.AsArray());
        var work = Assert.Single(files[0]!["works"]!.AsArray())!;
        Assert.Equal(
            ["id", "kind", "title", "year", "episode", "seriesTitle", "versionId", "editionLabel", "sourceName", "identifiers"],
            work.AsObject().Select(property => property.Key));
        Assert.Equal(("Movie", "Blade Runner", "Bedroom"), (work["kind"]!.GetValue<string>(), work["title"]!.GetValue<string>(), work["sourceName"]!.GetValue<string>()));
        Assert.Equal(["Imdb", "Tmdb"], work["identifiers"]!.AsArray().Select(id => id!["scheme"]!.GetValue<string>()));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static string NotAClass(string echo) =>
        $"“{echo}” is not a duplicate class. Use SameFile, ByteIdentical, SameWork or Possible.";

    private static string NotADecision(string echo) => $"“{echo}” is not a decision. Use Confirmed or Dismissed.";

    private static string MoviePath(string title, int year) => $"/media/movies/{title} ({year})/{title} ({year}).mkv";

    // A movie a library reported in an item of its own — its title and year, the ids given, one version and one
    // name — what a sync stores.
    private async Task<Planted> PlantMovieAsync(
        IntegrationConnectionEntity library, string title, int year, string servicePath, string? imdbId = null, string? tmdbId = null)
    {
        var itemId = Guid.NewGuid().ToString("N");
        var work = await _works.Create();
        await _works.AddTitle(work.Id, title, MediaTitleKind.Display, year, library.Id, library.Name, itemId);
        if (imdbId is not null)
            await _works.AddIdentifier(work.Id, MediaIdentifierScheme.Imdb, imdbId, library.Id, library.Name, itemId);
        if (tmdbId is not null)
            await _works.AddIdentifier(work.Id, MediaIdentifierScheme.Tmdb, tmdbId, library.Id, library.Name, itemId);
        var version = await _versions.Create(work.Id, itemId, sourceConnectionId: library.Id, sourceName: library.Name);
        var file = await _physicalFiles.Create(version.Id, servicePath, sourceConnectionId: library.Id, sourceName: library.Name);
        return new Planted(work.Id, version.Id, file.Id);
    }

    // A SameWork group: the movie in each library under one IMDb id, at two paths the app hasn't looked at.
    private async Task PlantCopiesAsync(string title, int year, string imdbId)
    {
        await PlantMovieAsync(_livingRoom, title, year, MoviePath(title, year), imdbId);
        await PlantMovieAsync(_bedroom, title, year, $"/media/films/{title} ({year}).mkv", imdbId);
    }

    // A Possible group: the movie in each library with one title and year and no id at all.
    private async Task PlantPossibleMatchAsync(string title, int year)
    {
        await PlantMovieAsync(_livingRoom, title, year, MoviePath(title, year));
        await PlantMovieAsync(_bedroom, title, year, $"/media/films/{title} ({year}).mkv");
    }

    // A SameFile group: one id-less movie of the living room whose version names one file twice — its own name and
    // the download's — for the next inspection to read.
    private async Task PlantHardlinksAsync(string title, int year, long inode, long sizeBytes)
    {
        var movie = await PlantMovieAsync(_livingRoom, title, year, MoviePath(title, year));
        var linkPath = $"/media/downloads/{title} ({year}).mkv";
        await _physicalFiles.Create(movie.VersionId, linkPath, sourceConnectionId: _livingRoom.Id, sourceName: _livingRoom.Name);
        var facts = TestMedia.File(inode, sizeBytes, linkCount: 2);
        _factory.Files.AddFile(MoviePath(title, year), facts, TestMedia.Taken());
        _factory.Files.AddFile(linkPath, facts, TestMedia.Taken());
    }

    // A ByteIdentical group: one id-less movie of the living room whose version names two files, copied rather than
    // linked, with every byte the same — for the next inspection to read and hash.
    private async Task PlantByteIdenticalCopiesAsync(string title, int year, long inode, long copyInode, long sizeBytes)
    {
        var movie = await PlantMovieAsync(_livingRoom, title, year, MoviePath(title, year));
        var copyPath = $"/media/backup/{title} ({year}).mkv";
        await _physicalFiles.Create(movie.VersionId, copyPath, sourceConnectionId: _livingRoom.Id, sourceName: _livingRoom.Name);
        var facts = TestMedia.File(inode, sizeBytes);
        var copyFacts = TestMedia.File(copyInode, sizeBytes, TestMedia.ModifiedUtc.AddDays(3));
        _factory.Files.AddFile(MoviePath(title, year), facts, TestMedia.Taken());
        _factory.Files.AddFile(copyPath, copyFacts, TestMedia.Taken(), hash: FakeFileInventory.HashOf(copyFacts, FakeFileInventory.DefaultSha256(facts)));
    }

    // A series item of the living room, with no file and no id: its episodes are known through their places.
    private async Task<MediaWorkEntity> PlantSeriesAsync(IntegrationConnectionEntity library, string title)
    {
        var series = await _works.Create(MediaWorkKind.Series);
        await _works.AddTitle(series.Id, title, MediaTitleKind.Display, null, library.Id, library.Name, Guid.NewGuid().ToString("N"));
        return series;
    }

    // A SameWork group of an episode: one item of it in each library under the episode's IMDb id, at the series' season
    // and number, each with one name.
    private async Task PlantEpisodeCopiesAsync(MediaWorkEntity series, int season, int episode, string title, string imdbId)
    {
        foreach (var (library, folder) in new[] { (_livingRoom, "series"), (_bedroom, "tv") })
        {
            var itemId = Guid.NewGuid().ToString("N");
            var work = await _works.Create(MediaWorkKind.Episode);
            await _works.AddTitle(work.Id, title, MediaTitleKind.Display, null, library.Id, library.Name, itemId);
            await _works.AddIdentifier(work.Id, MediaIdentifierScheme.Imdb, imdbId, library.Id, library.Name, itemId);
            await _works.AddEpisodePosition(work.Id, series.Id, season, episode, library.Id, library.Name, itemId);
            var version = await _versions.Create(work.Id, itemId, sourceConnectionId: library.Id, sourceName: library.Name);
            await _physicalFiles.Create(
                version.Id, $"/media/{folder}/S{season:00}E{episode:00} {title}.mkv", sourceConnectionId: library.Id, sourceName: library.Name);
        }
    }

    // The title a group is ordered by, as its first file's first work shows it: every planted group's works share one.
    private static string? TitleOf(LibraryDuplicateGroupDto group) => group.Files[0].Works[0].Title;

    private static async Task<LibraryDuplicateListResponse> ReadListAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<LibraryDuplicateListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;

    /// <summary>A planted movie: its work, its version and its one name.</summary>
    private sealed record Planted(Guid WorkId, Guid VersionId, Guid FileId);
}
