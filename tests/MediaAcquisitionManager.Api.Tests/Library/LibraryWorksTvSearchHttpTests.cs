using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Library;

// MAM-32, ADR 0019 — the Library page's search and filters reaching into a series: a series is listed when
// one of its episodes passes, and its seasons count only the episodes that do. A search or the missing-ids
// filter the series itself answers keeps all its episodes; one only an episode answers keeps that episode
// alone. A library holds files, so only the episodes' files put a series in one. An episode of no series is
// searched and filtered as a movie is; each kind lacks the ids of its own kind.
public sealed class LibraryWorksTvSearchHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library/works";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";

    /// <summary>A series with every id, whose episode "Kill the Messenger" lacks its TVDB id and whose
    /// second season is kept in the documentary library.</summary>
    private const string Yellowstone = "Yellowstone";

    /// <summary>A series without ids, whose title is in none of its paths.</summary>
    private const string Penguins = "Пінгвіни з Мадагаскару";

    /// <summary>A series and its one episode, every id of both known.</summary>
    private const string ClarksonsFarm = "Clarkson's Farm";

    /// <summary>An episode of no series, without ids.</summary>
    private const string FlippinFido = "Flippin Fido";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private AuthorizedUser _owner = null!;
    private FakeJellyfinServer _jellyfin = null!;

    public LibraryWorksTvSearchHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    /// <summary>A search term and the works it lists, in title order: each title with a series' seasons as
    /// "number:episodes".</summary>
    public static TheoryData<string, string[]> Searches => new()
    {
        // The series' own title, id or year: every episode of it, whatever they are called.
        { "пінгвіни", [$"{Penguins} [1:2]"] },
        { "2008", [$"{Penguins} [1:2]"] },
        { "341164", [$"{Yellowstone} [1:2, 2:1]"] },
        // An episode's title, id or file path: the series, with that episode alone.
        { "MESSENGER", [$"{Yellowstone} [1:1]"] },
        { "tt7529126", [$"{Yellowstone} [1:1]"] },
        { "s02e01", [$"{Yellowstone} [2:1]"] },
        // An episode of no series: itself.
        { "flippin", [FlippinFido] },
        // Never an episode of a series as a work of its own.
        { "daybreak", [$"{Yellowstone} [1:1]"] },
    };

    /// <summary>The kind with another filter, and the works they list.</summary>
    public static TheoryData<string, string[]> KindsWithFilters => new()
    {
        { "kind=Series&identifiers=Missing", [$"{Penguins} [1:2]", FlippinFido, $"{Yellowstone} [1:1]"] },
        { "kind=Movie&identifiers=Missing", ["The Continental"] },
        { "kind=Movie&search=yellowstone", [] },
        { "kind=Series&search=collection", [] },
    };

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        _jellyfin = new FakeJellyfinServer { ApiKey = ApiKey };
        _jellyfin.Arm(_factory.JellyfinHttp);
        // The four sample movies (three works, The Continental without a TMDb id), and three series.
        _jellyfin.Series.Add(JellyfinSamples.SeriesWith(ItemId("a"), Yellowstone, 2018, "/media/tv-shows/Yellowstone", "tt4236770", "73586", "341164"));
        _jellyfin.Episodes.Add(Episode("b", "Daybreak", "a", 1, 1, "/media/tv-shows/Yellowstone/S01E01.mkv", "tt7529124", "6507316"));
        _jellyfin.Episodes.Add(Episode("c", "Kill the Messenger", "a", 1, 2, "/media/tv-shows/Yellowstone/S01E02.mkv", "tt7529126", null));
        _jellyfin.Episodes.Add(Episode("d", "A Thundering", "a", 2, 1, "/media/documentary/Yellowstone S02/S02E01.mkv", "tt8000001", "7000001"));
        _jellyfin.Series.Add(JellyfinSamples.SeriesWith(ItemId("e"), Penguins, 2008, "/media/cartoons/Penguins"));
        _jellyfin.Episodes.Add(Episode("f", "Gone in a Flash", "e", 1, 1, "/media/cartoons/Penguins/S01E01.mkv", "tt0000101", "100101"));
        _jellyfin.Episodes.Add(Episode("g", "Popcorn Panic", "e", 1, 2, "/media/cartoons/Penguins/S01E02.mkv", "tt0000102", "100102"));
        _jellyfin.Series.Add(JellyfinSamples.SeriesWith(ItemId("h"), ClarksonsFarm, 2021, "/media/tv-shows/Clarksons Farm", "tt10541088", "117648", "378165"));
        _jellyfin.Episodes.Add(Episode("i", "Tractoring", "h", 1, 1, "/media/tv-shows/Clarksons Farm/S01E01.mkv", "tt0000201", "200201"));
        _jellyfin.Episodes.Add(Episode("j", FlippinFido, null, 1, 4, "/media/cartoons/Tom & Jerry Kids/1.mkv", null, null));
        var jellyfin = await new IntegrationConnectionSeeder(_factory.Scope).Create("jellyfin", "Living room", apiKey: ApiKey);
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(jellyfin, _owner.User);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Theory]
    [MemberData(nameof(Searches))]
    public async Task Works_search_lists_a_series_with_the_episodes_it_finds_or_all_when_it_finds_the_series(string search, string[] works)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?search={Uri.EscapeDataString(search)}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(works, list.Items.Select(Card));
        Assert.Equal(works.Length, list.TotalCount);
    }

    [Fact]
    public async Task Works_in_a_library_lists_a_series_by_its_episodes_files_counting_only_those()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var shows = await LocationIdAsync(JellyfinSamples.TvShowsLocationId, cancellationToken);
        var documentary = await LocationIdAsync(JellyfinSamples.DocumentaryLocationId, cancellationToken);
        var cartoons = await LocationIdAsync(JellyfinSamples.CartoonsLocationId, cancellationToken);
        var movies = await LocationIdAsync(JellyfinSamples.MoviesLocationId, cancellationToken);

        //Act
        var inShows = await ReadListAsync(await _factory.GetHttpRequest($"{BaseUrl}?locationId={shows}", _owner.AccessToken), cancellationToken);
        var inDocumentary = await ReadListAsync(await _factory.GetHttpRequest($"{BaseUrl}?locationId={documentary}", _owner.AccessToken), cancellationToken);
        var inCartoons = await ReadListAsync(await _factory.GetHttpRequest($"{BaseUrl}?locationId={cartoons}", _owner.AccessToken), cancellationToken);
        var inMovies = await ReadListAsync(await _factory.GetHttpRequest($"{BaseUrl}?locationId={movies}", _owner.AccessToken), cancellationToken);

        //Assert
        // Yellowstone in both libraries its seasons are kept in, each time with only that library's.
        Assert.Equal([$"{ClarksonsFarm} [1:1]", $"{Yellowstone} [1:2]"], inShows.Items.Select(Card));
        Assert.Equal([$"{Yellowstone} [2:1]"], inDocumentary.Items.Select(Card));
        Assert.Equal([$"{Penguins} [1:2]", FlippinFido], inCartoons.Items.Select(Card));
        Assert.Equal(["13-й район", "Чужі", "The Continental"], inMovies.Items.Select(Card));
        Assert.Equal((2, 1, 2, 3), (inShows.TotalCount, inDocumentary.TotalCount, inCartoons.TotalCount, inMovies.TotalCount));
    }

    [Fact]
    public async Task Works_missing_an_identifier_lists_each_work_lacking_an_id_of_its_kind_and_each_series_with_such_an_episode()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?identifiers=Missing", _owner.AccessToken);

        //Assert
        // The Penguins lack every id a series needs, so all their episodes stay; Yellowstone lacks none,
        // and only its episode without a TVDB id stays. Clarkson's Farm and its episode lack nothing.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(
            [
                ($"{Penguins} [1:2]", (IReadOnlyList<MediaIdentifierScheme>)[MediaIdentifierScheme.Imdb, MediaIdentifierScheme.TmdbTv, MediaIdentifierScheme.Tvdb]),
                (FlippinFido, [MediaIdentifierScheme.Imdb, MediaIdentifierScheme.Tvdb]),
                ("The Continental", [MediaIdentifierScheme.Tmdb]),
                ($"{Yellowstone} [1:1]", []),
            ],
            list.Items.Select(work => (Card(work), work.MissingIdentifiers)));
        Assert.Equal(4, list.TotalCount);
    }

    [Fact]
    public async Task Works_search_and_a_library_apply_together_to_a_series()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var documentary = await LocationIdAsync(JellyfinSamples.DocumentaryLocationId, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?search=yellowstone&locationId={documentary}", _owner.AccessToken);

        //Assert
        // The series' title keeps all its episodes; the library only the one with a file in it.
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal([$"{Yellowstone} [2:1]"], list.Items.Select(Card));
    }

    [Theory]
    [MemberData(nameof(KindsWithFilters))]
    public async Task Works_kind_applies_with_the_search_and_filters(string query, string[] works)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?{query}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(works, list.Items.Select(Card));
        Assert.Equal(works.Length, list.TotalCount);
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

    // A work as the theories name it: its title, and a series' seasons as "number:episodes".
    private static string Card(LibraryWorkDto work) =>
        work.Seasons is null
            ? work.Title!
            : $"{work.Title} [{string.Join(", ", work.Seasons.Select(season => $"{season.Number}:{season.EpisodeCount}"))}]";

    // A 32-character item id made of one letter.
    private static string ItemId(string letter) => new(letter[0], 32);

    // An episode of the series item seriesLetter (none when null) with these facts.
    private static JsonObject Episode(
        string letter, string title, string? seriesLetter, int season, int episode, string path, string? imdbId, string? tvdbId) =>
        JellyfinSamples.EpisodeWith(ItemId(letter), title, seriesLetter is null ? null : ItemId(seriesLetter), season, episode, path, imdbId, tvdbId);

    private static async Task<LibraryWorkListResponse> ReadListAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<LibraryWorkListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
}
