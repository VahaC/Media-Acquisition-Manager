using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Library;

// MAM-32, ADR 0019 — a series' episodes on the Library page (GET /api/library/works/{id}/episodes): for every
// signed-in user, paged by the server by season — the regular ones, then the specials, then none, as the series
// lists its seasons —, then number — a missing one last —, then title; a season at a time, the specials as
// season 0 and the episodes the library gives no season as "None"; searched and filtered
// as the works are, a search or the missing-ids filter the series itself answers keeping every episode. Each
// episode carries its place, versions and files as a work does. Only a series has episodes: any other id is a
// bare 404.
public sealed class LibraryEpisodesHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library/works";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";

    /// <summary>A series without ids, so two of its episodes at one place stay two works (ADR 0017), and
    /// whose title is in none of its paths.</summary>
    private const string Penguins = "Пінгвіни з Мадагаскару";

    /// <summary>Every episode of <see cref="Penguins"/> in episode order: season 1 by number with two episodes
    /// at S01E03 by title and one without a number last, season 2, the specials, then no season.</summary>
    private static readonly string[] PenguinsEpisodes =
    [
        "Gone in a Flash",
        "Popcorn Panic",
        "Arctic Instincts",
        "Operation: Get Shorty",
        "Penguin Shorts",
        "Haunted Habitat",
        "Christmas Caper",
        "Untitled Short",
    ];

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private FakeJellyfinServer _jellyfin = null!;
    private IntegrationConnectionEntity _livingRoom = null!;

    public LibraryEpisodesHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    /// <summary>A season and the titles of the episodes it lists, in episode order.</summary>
    public static TheoryData<string, string[]> Seasons => new()
    {
        { "1", ["Gone in a Flash", "Popcorn Panic", "Arctic Instincts", "Operation: Get Shorty", "Penguin Shorts"] },
        { " 2 ", ["Haunted Habitat"] },
        // The specials.
        { "0", ["Christmas Caper"] },
        { "00", ["Christmas Caper"] },
        // The episodes the library gives no season, in any letter case.
        { "None", ["Untitled Short"] },
        { "none", ["Untitled Short"] },
        // A season the series doesn't have, and the highest one asked for.
        { "7", [] },
        { "9999", [] },
        // Blank: every season.
        { "", PenguinsEpisodes },
    };

    /// <summary>A search term and the titles of the episodes it lists, in episode order.</summary>
    public static TheoryData<string, string[]> Searches => new()
    {
        // The series' own title: every episode, whatever it is called.
        { "пінгвіни", PenguinsEpisodes },
        // An episode's title, id or file path: that episode alone.
        { "POPCORN", ["Popcorn Panic"] },
        { "tt0000102", ["Popcorn Panic"] },
        { "s02e01", ["Haunted Habitat"] },
        // Nothing of the series.
        { "yellowstone", [] },
    };

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        _jellyfin = new FakeJellyfinServer { ApiKey = ApiKey };
        _jellyfin.Arm(_factory.JellyfinHttp);
        // The Penguins' episodes, listed out of order so the order can only be the app's own; their second
        // season is kept in the TV library, the rest in the cartoon one.
        _jellyfin.Series.Add(JellyfinSamples.SeriesWith(ItemId("a"), Penguins, 2008, "/media/cartoons/Penguins"));
        _jellyfin.Episodes.Add(Episode("b", "Popcorn Panic", "a", 1, 2, "/media/cartoons/Penguins/S01E02.mkv", "tt0000102", "100102"));
        _jellyfin.Episodes.Add(Episode("c", "Untitled Short", "a", null, null, "/media/cartoons/Penguins/short.mkv", "tt0000109", "100109"));
        _jellyfin.Episodes.Add(Episode("d", "Operation: Get Shorty", "a", 1, 3, "/media/cartoons/Penguins/S01E03b.mkv", "tt0000104", "100104"));
        _jellyfin.Episodes.Add(Episode("e", "Haunted Habitat", "a", 2, 1, "/media/tv-shows/Penguins S02/S02E01.mkv", "tt0000201", null));
        _jellyfin.Episodes.Add(Episode("f", "Penguin Shorts", "a", 1, null, "/media/cartoons/Penguins/S01.extra.mkv", "tt0000108", "100108"));
        _jellyfin.Episodes.Add(Episode("g", "Gone in a Flash", "a", 1, 1, "/media/cartoons/Penguins/S01E01.mkv", "tt0000101", "100101"));
        _jellyfin.Episodes.Add(Episode("h", "Christmas Caper", "a", 0, 1, "/media/cartoons/Penguins/S00E01.mkv", "tt0000100", "100100"));
        _jellyfin.Episodes.Add(Episode("i", "Arctic Instincts", "a", 1, 3, "/media/cartoons/Penguins/S01E03a.mkv", "tt0000103", "100103"));
        // Yellowstone with every id; one of its two episodes lacks its TVDB id.
        _jellyfin.Series.Add(JellyfinSamples.SeriesWith(ItemId("j"), "Yellowstone", 2018, "/media/tv-shows/Yellowstone", "tt4236770", "73586", "341164"));
        _jellyfin.Episodes.Add(Episode("k", "Daybreak", "j", 1, 1, "/media/tv-shows/Yellowstone/S01E01.mkv", "tt7529124", "6507316"));
        _jellyfin.Episodes.Add(Episode("l", "Kill the Messenger", "j", 1, 2, "/media/tv-shows/Yellowstone/S01E02.mkv", "tt7529126", null));
        // An episode of no series, beside the four sample movies.
        _jellyfin.Episodes.Add(Episode("m", "Flippin Fido", null, 1, 4, "/media/cartoons/Tom & Jerry Kids/1.mkv", null, null));
        // The movies' library mapped, the cartoons' and TV shows' not: no episode file has an app path.
        _livingRoom = await new IntegrationConnectionSeeder(_factory.Scope).Create(
            "jellyfin", "Living room", apiKey: ApiKey, pathMappings: [new PathMappingPair("/media/movies", "/data/movies")]);
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(_livingRoom, _owner.User);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Episodes_without_a_token_is_401()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var penguins = await WorkIdAsync(Penguins, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{penguins}/episodes");

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Episodes_as_a_signed_in_non_owner_is_200_with_every_episode_in_season_then_number_then_title_order()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var penguins = await WorkIdAsync(Penguins, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{penguins}/episodes", _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal((8, 1, 25), (list.TotalCount, list.Page, list.PageSize));
        Assert.Equal(
            [
                ("Gone in a Flash", new LibraryEpisodeDto(penguins, 1, 1)),
                ("Popcorn Panic", new LibraryEpisodeDto(penguins, 1, 2)),
                ("Arctic Instincts", new LibraryEpisodeDto(penguins, 1, 3)),
                ("Operation: Get Shorty", new LibraryEpisodeDto(penguins, 1, 3)),
                ("Penguin Shorts", new LibraryEpisodeDto(penguins, 1, null)),
                ("Haunted Habitat", new LibraryEpisodeDto(penguins, 2, 1)),
                ("Christmas Caper", new LibraryEpisodeDto(penguins, 0, 1)),
                ("Untitled Short", new LibraryEpisodeDto(penguins, null, null)),
            ],
            list.Items.Select(work => (work.Title!, work.Episode!)));
        Assert.All(list.Items, work => Assert.Equal((MediaWorkKind.Episode, (IReadOnlyList<LibrarySeasonDto>?)null), (work.Kind, work.Seasons)));
    }

    [Fact]
    public async Task Episodes_gives_an_episode_its_ids_missing_schemes_versions_and_files()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var penguins = await WorkIdAsync(Penguins, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{penguins}/episodes?season=2", _owner.AccessToken);

        //Assert
        // An episode needs an IMDb and a TVDB id; its file is in the TV library, which no mapping covers.
        var work = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal([new LibraryIdentifierDto(MediaIdentifierScheme.Imdb, "tt0000201", "Living room")], work.Identifiers);
        Assert.Equal([MediaIdentifierScheme.Tvdb], work.MissingIdentifiers);
        var version = Assert.Single(work.Versions);
        Assert.Equal(ItemId("e"), version.SourceItemId);
        var file = Assert.Single(version.Files);
        Assert.Equal(
            ("/media/tv-shows/Penguins S02/S02E01.mkv", PathMapStatus.NotCovered, (string?)null, "Серіали"),
            (file.ServicePath, file.PathStatus, file.AppPath, file.LocationName));
        Assert.Equal("No path mapping of this integration covers “/media/tv-shows/Penguins S02/S02E01.mkv”.", file.PathProblem);
    }

    [Fact]
    public async Task Episodes_as_a_signed_in_non_owner_gives_a_file_without_an_app_path_its_status_but_not_why()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var penguins = await WorkIdAsync(Penguins, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{penguins}/episodes", _member.AccessToken);

        //Assert
        // MAM-8 — the reason quotes the owner's path mappings, so it is the owner's; the status is everyone's.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.DoesNotContain("No path mapping", body, StringComparison.Ordinal);
        var files = (await ReadListAsync(response, cancellationToken)).Items.SelectMany(work => work.Versions).SelectMany(version => version.Files).ToList();
        Assert.Equal(8, files.Count);
        Assert.All(files, file => Assert.Equal((PathMapStatus.NotCovered, (string?)null, (string?)null), (file.PathStatus, file.AppPath, file.PathProblem)));
    }

    [Fact]
    public async Task Episodes_lists_an_episode_of_two_items_once_with_both_versions()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The library synced again as the sample Mickey Mouse Clubhouse: the two halves of Road Rally (S03E09)
        // share their ids and place.
        _jellyfin.Series.Clear();
        _jellyfin.Episodes.Clear();
        _jellyfin.Series.Add(JellyfinSamples.Series(JellyfinSamples.MickeyMouseClubhouseId));
        _jellyfin.Episodes.AddRange(JellyfinSamples.Episodes().Where(episode =>
            (string?)episode["SeriesId"] == JellyfinSamples.MickeyMouseClubhouseId));
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(_livingRoom, _owner.User);
        var mickey = await WorkIdAsync("Mickey Mouse Clubhouse", cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{mickey}/episodes", _owner.AccessToken);

        //Assert
        // The virtual special (S00E01) is stored as nothing, so it is not listed.
        var work = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal(("Road Rally", new LibraryEpisodeDto(mickey, 3, 9)), (work.Title, work.Episode));
        Assert.Equal(
            [JellyfinSamples.RoadRallyAId, JellyfinSamples.RoadRallyBId],
            work.Versions.Select(version => version.SourceItemId).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Episodes_pages_follow_the_episode_order()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var penguins = await WorkIdAsync(Penguins, cancellationToken);

        //Act
        var first = await ReadListAsync(await _factory.GetHttpRequest($"{BaseUrl}/{penguins}/episodes?page=1&pageSize=3", _owner.AccessToken), cancellationToken);
        var second = await ReadListAsync(await _factory.GetHttpRequest($"{BaseUrl}/{penguins}/episodes?page=2&pageSize=3", _owner.AccessToken), cancellationToken);
        var third = await ReadListAsync(await _factory.GetHttpRequest($"{BaseUrl}/{penguins}/episodes?page=3&pageSize=3", _owner.AccessToken), cancellationToken);
        var past = await ReadListAsync(await _factory.GetHttpRequest($"{BaseUrl}/{penguins}/episodes?page=4&pageSize=3", _owner.AccessToken), cancellationToken);

        //Assert
        // A page boundary between the two episodes at S01E03 neither repeats nor drops one.
        Assert.Equal((8, 2, 3), (second.TotalCount, second.Page, second.PageSize));
        Assert.Equal(["Gone in a Flash", "Popcorn Panic", "Arctic Instincts"], first.Items.Select(work => work.Title));
        Assert.Equal(["Operation: Get Shorty", "Penguin Shorts", "Haunted Habitat"], second.Items.Select(work => work.Title));
        Assert.Equal(["Christmas Caper", "Untitled Short"], third.Items.Select(work => work.Title));
        // Past the last page: an empty page that still says how many there are.
        Assert.Equal((8, 4, 3), (past.TotalCount, past.Page, past.PageSize));
        Assert.Empty(past.Items);
    }

    [Theory]
    [InlineData("page=0", "Page must be 1 or more.")]
    [InlineData("pageSize=0", "Page size must be between 1 and 100.")]
    [InlineData("pageSize=101", "Page size must be between 1 and 100.")]
    public async Task Episodes_with_a_page_out_of_range_is_400_with_its_sentence(string query, string error)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var penguins = await WorkIdAsync(Penguins, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{penguins}/episodes?{query}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(error, await response.ReadErrorAsync(cancellationToken));
    }

    [Theory]
    [MemberData(nameof(Seasons))]
    public async Task Episodes_of_a_season_lists_only_its_episodes(string season, string[] titles)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var penguins = await WorkIdAsync(Penguins, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{penguins}/episodes?season={Uri.EscapeDataString(season)}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(titles, list.Items.Select(work => work.Title));
        Assert.Equal(titles.Length, list.TotalCount);
    }

    [Theory]
    [InlineData("abc", "“abc” is not a season. Use a season number, or None for the episodes without one.")]
    [InlineData(" -1 ", "“-1” is not a season. Use a season number, or None for the episodes without one.")]
    [InlineData("+1", "“+1” is not a season. Use a season number, or None for the episodes without one.")]
    [InlineData("1.5", "“1.5” is not a season. Use a season number, or None for the episodes without one.")]
    [InlineData("10000", "“10000” is not a season. Use a season number, or None for the episodes without one.")]
    [InlineData("99999999999", "“99999999999” is not a season. Use a season number, or None for the episodes without one.")]
    public async Task Episodes_of_a_season_that_is_not_one_is_400_with_its_sentence(string season, string error)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var penguins = await WorkIdAsync(Penguins, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{penguins}/episodes?season={Uri.EscapeDataString(season)}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(error, await response.ReadErrorAsync(cancellationToken));
    }

    [Fact]
    public async Task Episodes_of_a_season_that_is_a_long_text_echoes_only_its_start()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var penguins = await WorkIdAsync(Penguins, cancellationToken);
        var pasted = new string('x', 500);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{penguins}/episodes?season={pasted}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            $"“{new string('x', 32)}…” is not a season. Use a season number, or None for the episodes without one.",
            await response.ReadErrorAsync(cancellationToken));
    }

    [Theory]
    [InlineData("movie")]
    [InlineData("episode of a series")]
    [InlineData("episode of no series")]
    [InlineData("no work")]
    public async Task Episodes_of_anything_but_a_series_is_a_bare_404(string what)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var id = what switch
        {
            "movie" => await WorkIdAsync("Чужі", cancellationToken),
            "episode of a series" => await WorkIdAsync("Popcorn Panic", cancellationToken),
            "episode of no series" => await WorkIdAsync("Flippin Fido", cancellationToken),
            _ => Guid.NewGuid(),
        };

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{id}/episodes", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("", await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task Episodes_of_an_unknown_id_with_a_bad_season_and_page_is_still_a_bare_404()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{Guid.NewGuid()}/episodes?season=abc&page=0", _owner.AccessToken);

        //Assert
        // What isn't there has no episodes to ask about: the id is answered first.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("", await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Theory]
    [MemberData(nameof(Searches))]
    public async Task Episodes_search_lists_every_episode_when_it_finds_the_series_and_else_the_episodes_it_finds(string search, string[] titles)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var penguins = await WorkIdAsync(Penguins, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{penguins}/episodes?search={Uri.EscapeDataString(search)}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(titles, list.Items.Select(work => work.Title));
        Assert.Equal(titles.Length, list.TotalCount);
    }

    [Fact]
    public async Task Episodes_search_and_season_apply_together()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var penguins = await WorkIdAsync(Penguins, cancellationToken);

        //Act
        var inSeason = await ReadListAsync(
            await _factory.GetHttpRequest($"{BaseUrl}/{penguins}/episodes?season=1&search=popcorn", _owner.AccessToken), cancellationToken);
        var elsewhere = await ReadListAsync(
            await _factory.GetHttpRequest($"{BaseUrl}/{penguins}/episodes?season=2&search=popcorn", _owner.AccessToken), cancellationToken);

        //Assert
        Assert.Equal(["Popcorn Panic"], inSeason.Items.Select(work => work.Title));
        Assert.Equal(0, elsewhere.TotalCount);
    }

    [Fact]
    public async Task Episodes_in_a_library_lists_only_the_episodes_with_a_file_in_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var penguins = await WorkIdAsync(Penguins, cancellationToken);
        var shows = await LocationIdAsync(JellyfinSamples.TvShowsLocationId, cancellationToken);
        var movies = await LocationIdAsync(JellyfinSamples.MoviesLocationId, cancellationToken);

        //Act
        var inShows = await ReadListAsync(await _factory.GetHttpRequest($"{BaseUrl}/{penguins}/episodes?locationId={shows}", _owner.AccessToken), cancellationToken);
        var inMovies = await ReadListAsync(await _factory.GetHttpRequest($"{BaseUrl}/{penguins}/episodes?locationId={movies}", _owner.AccessToken), cancellationToken);
        var inNone = await ReadListAsync(await _factory.GetHttpRequest($"{BaseUrl}/{penguins}/episodes?locationId={Guid.NewGuid()}", _owner.AccessToken), cancellationToken);

        //Assert
        Assert.Equal(["Haunted Habitat"], inShows.Items.Select(work => work.Title));
        Assert.Equal((0, 0), (inMovies.TotalCount, inNone.TotalCount));
    }

    [Fact]
    public async Task Episodes_in_a_library_that_is_not_an_id_is_400_with_its_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var penguins = await WorkIdAsync(Penguins, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{penguins}/episodes?locationId=abc", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("“abc” is not a library.", await response.ReadErrorAsync(cancellationToken));
    }

    [Fact]
    public async Task Episodes_missing_an_identifier_lists_every_episode_of_a_series_lacking_one_and_else_the_episodes_lacking_one()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var penguins = await WorkIdAsync(Penguins, cancellationToken);
        var yellowstone = await WorkIdAsync("Yellowstone", cancellationToken);

        //Act
        var ofPenguins = await ReadListAsync(
            await _factory.GetHttpRequest($"{BaseUrl}/{penguins}/episodes?identifiers=Missing", _owner.AccessToken), cancellationToken);
        var ofYellowstone = await ReadListAsync(
            await _factory.GetHttpRequest($"{BaseUrl}/{yellowstone}/episodes?identifiers=missing", _owner.AccessToken), cancellationToken);

        //Assert
        // The Penguins lack every id a series needs: a release can't be matched to them reliably, nor to any
        // of their episodes. Yellowstone lacks none, so only its episode without a TVDB id is listed.
        Assert.Equal(PenguinsEpisodes, ofPenguins.Items.Select(work => work.Title));
        Assert.Equal(
            [("Kill the Messenger", (IReadOnlyList<MediaIdentifierScheme>)[MediaIdentifierScheme.Tvdb])],
            ofYellowstone.Items.Select(work => (work.Title!, work.MissingIdentifiers)));
    }

    [Fact]
    public async Task Episodes_with_an_unknown_identifier_filter_is_400_with_its_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var penguins = await WorkIdAsync(Penguins, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{penguins}/episodes?identifiers=Present", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("“Present” is not an identifier filter. Use Missing.", await response.ReadErrorAsync(cancellationToken));
    }

    // The stored id of the one work titled so.
    private async Task<Guid> WorkIdAsync(string title, CancellationToken cancellationToken)
    {
        var id = Guid.Empty;
        await _factory.WithDbAsync(async db => id = await db.MediaWorkTitles
            .Where(stored => stored.Text == title)
            .Select(stored => stored.MediaWorkId)
            .Distinct()
            .SingleAsync(cancellationToken));
        return id;
    }

    // The stored id of the library Jellyfin calls sourceLocationId.
    private async Task<Guid> LocationIdAsync(string sourceLocationId, CancellationToken cancellationToken)
    {
        var id = Guid.Empty;
        await _factory.WithDbAsync(async db => id = await db.LibraryLocations
            .Where(location => location.SourceItemId == sourceLocationId)
            .Select(location => location.Id)
            .SingleAsync(cancellationToken));
        return id;
    }

    // A 32-character item id made of one letter.
    private static string ItemId(string letter) => new(letter[0], 32);

    // An episode of the series item seriesLetter (none when null) with these facts.
    private static JsonObject Episode(
        string letter, string title, string? seriesLetter, int? season, int? episode, string path, string? imdbId, string? tvdbId) =>
        JellyfinSamples.EpisodeWith(ItemId(letter), title, seriesLetter is null ? null : ItemId(seriesLetter), season, episode, path, imdbId, tvdbId);

    private static async Task<LibraryWorkListResponse> ReadListAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<LibraryWorkListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
}
