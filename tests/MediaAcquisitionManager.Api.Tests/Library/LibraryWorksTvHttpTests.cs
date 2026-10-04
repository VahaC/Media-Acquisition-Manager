using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Library;

// MAM-32, ADR 0019 — the Library page's works with series in the library (GET /api/library/works): a series
// is one work among the movies, in title order, with its seasons counted from its episodes' places and no
// versions of its own; its episodes are never cards of their own, but an episode no series holds is; a series
// none of whose episodes is stored is not listed; and the kind filter picks movies, or series and the episodes
// of no series. Search and filters reaching into a series are LibraryWorksTvSearchHttpTests; a series'
// episodes are LibraryEpisodesHttpTests.
public sealed class LibraryWorksTvHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library/works";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";

    /// <summary>The title of the Tom &amp; Jerry Kids episode item, the sample episode of no series.</summary>
    private const string TomAndJerryKids = "1. Flippin Fido; Dakota Droopy & the Lost";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private FakeJellyfinServer _jellyfin = null!;

    public LibraryWorksTvHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        _jellyfin = new FakeJellyfinServer { ApiKey = ApiKey };
        _jellyfin.Arm(_factory.JellyfinHttp);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Works_as_a_signed_in_non_owner_lists_each_series_with_episodes_once_among_the_movies_in_title_order()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The four sample movies (three works), the five sample series and the six sample episodes.
        await SyncSamplesAsync();

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        // Clarkson's Farm and the Penguins have no episode stored, so nothing of them is in the library;
        // the episodes of the other three are inside their series, and Tom & Jerry Kids, of no series, is a
        // work of its own.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(7, list.TotalCount);
        Assert.Equal(
            [
                (TomAndJerryKids, MediaWorkKind.Episode),
                ("13-й район", MediaWorkKind.Movie),
                ("Чужі", MediaWorkKind.Movie),
                ("How the Universe Works", MediaWorkKind.Series),
                ("Mickey Mouse Clubhouse", MediaWorkKind.Series),
                ("The Continental", MediaWorkKind.Movie),
                ("Yellowstone", MediaWorkKind.Series),
            ],
            list.Items.Select(work => (work.Title!, work.Kind)));
    }

    [Fact]
    public async Task Works_gives_a_series_its_ids_and_seasons_and_no_versions_or_place()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncSamplesAsync();

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?search=yellowstone", _owner.AccessToken);

        //Assert
        // A show's TMDb id is a TMDb TV id (ADR 0017); the series has all three schemes a series needs.
        var work = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal(
            (MediaWorkKind.Series, "Yellowstone", "Yellowstone", 2018),
            (work.Kind, work.Title, work.OriginalTitle, work.Year));
        Assert.Equal(
            [
                new LibraryIdentifierDto(MediaIdentifierScheme.Imdb, "tt4236770", "Living room"),
                new LibraryIdentifierDto(MediaIdentifierScheme.Tvdb, "341164", "Living room"),
                new LibraryIdentifierDto(MediaIdentifierScheme.TmdbTv, "73586", "Living room"),
            ],
            work.Identifiers);
        Assert.Empty(work.MissingIdentifiers);
        Assert.Empty(work.Versions);
        Assert.Null(work.Episode);
        Assert.Equal([new LibrarySeasonDto(1, 1)], work.Seasons);
    }

    [Fact]
    public async Task Works_counts_an_episode_of_two_items_once_and_leaves_the_virtual_special_out_of_the_seasons()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncSamplesAsync();

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?search=mickey", _owner.AccessToken);

        //Assert
        // The two halves of Road Rally are one work at S03E09; the special Jellyfin only lists (S00E01) is
        // stored as nothing, so there are no specials.
        var work = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal("Mickey Mouse Clubhouse", work.Title);
        Assert.Equal([new LibrarySeasonDto(3, 1)], work.Seasons);
    }

    [Fact]
    public async Task Works_lists_the_seasons_regular_first_then_the_specials_then_the_episodes_without_one()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _jellyfin.Series.Add(JellyfinSamples.SeriesWith(ItemId("a"), "Пінгвіни з Мадагаскару", 2008, "/media/cartoons/Penguins"));
        // Listed out of order, so the order can only be the app's own.
        _jellyfin.Episodes.Add(Episode("b", 2, 1));
        _jellyfin.Episodes.Add(Episode("c", null, null));
        _jellyfin.Episodes.Add(Episode("d", 0, 1));
        _jellyfin.Episodes.Add(Episode("e", 2, 2));
        _jellyfin.Episodes.Add(Episode("f", 1, 1));
        _jellyfin.Episodes.Add(Episode("g", 10, 1));
        await SyncAsync(movies: []);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        // Seasons by number, not as text (10 after 2), the specials after them, then no season: the order
        // Jellyfin's own pages show.
        var work = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal(
            [new LibrarySeasonDto(1, 1), new LibrarySeasonDto(2, 2), new LibrarySeasonDto(10, 1), new LibrarySeasonDto(0, 1), new LibrarySeasonDto(null, 1)],
            work.Seasons);
    }

    [Fact]
    public async Task Works_lists_an_episode_of_no_series_as_a_work_of_its_own_with_its_place()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncSamplesAsync();

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?search=flippin", _owner.AccessToken);

        //Assert
        // The Tom & Jerry Kids item: season 1, episode 4 of no series, its folder's 65 files each a version,
        // and no id — an episode needs an IMDb and a TVDB one.
        var work = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal((MediaWorkKind.Episode, TomAndJerryKids), (work.Kind, work.Title));
        Assert.Equal(new LibraryEpisodeDto(null, 1, 4), work.Episode);
        Assert.Null(work.Seasons);
        Assert.Equal(65, work.Versions.Count);
        Assert.Equal([MediaIdentifierScheme.Imdb, MediaIdentifierScheme.Tvdb], work.MissingIdentifiers);
    }

    [Fact]
    public async Task Works_lists_an_episode_whose_series_is_gone_as_a_work_of_its_own()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _jellyfin.Series.Add(JellyfinSamples.Series(JellyfinSamples.YellowstoneId));
        _jellyfin.Episodes.Add(JellyfinSamples.Episode(JellyfinSamples.YellowstoneS01E01Id));
        var jellyfin = await SyncAsync(movies: []);
        // The next sync finds the series item gone and the episode still there.
        _jellyfin.Series.Clear();
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(jellyfin, _owner.User);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        var work = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal((MediaWorkKind.Episode, "Daybreak"), (work.Kind, work.Title));
        Assert.Equal(new LibraryEpisodeDto(null, 1, 1), work.Episode);
    }

    [Fact]
    public async Task Works_gives_a_movie_no_place_and_no_seasons()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncSamplesAsync();

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?search=чужі", _owner.AccessToken);

        //Assert
        var work = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal(MediaWorkKind.Movie, work.Kind);
        Assert.Equal(((LibraryEpisodeDto?)null, (IReadOnlyList<LibrarySeasonDto>?)null), (work.Episode, work.Seasons));
    }

    [Theory]
    [InlineData("Movie", new[] { "13-й район", "Чужі", "The Continental" })]
    [InlineData("movie", new[] { "13-й район", "Чужі", "The Continental" })]
    [InlineData("Series", new[] { TomAndJerryKids, "How the Universe Works", "Mickey Mouse Clubhouse", "Yellowstone" })]
    [InlineData(" SERIES ", new[] { TomAndJerryKids, "How the Universe Works", "Mickey Mouse Clubhouse", "Yellowstone" })]
    // Blank: both kinds.
    [InlineData(" ", new[] { TomAndJerryKids, "13-й район", "Чужі", "How the Universe Works", "Mickey Mouse Clubhouse", "The Continental", "Yellowstone" })]
    public async Task Works_of_a_kind_lists_only_the_works_of_that_kind(string kind, string[] titles)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncSamplesAsync();

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?kind={Uri.EscapeDataString(kind)}", _owner.AccessToken);

        //Assert
        // Series: the series and the episodes of no series — what the TV libraries hold.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(titles, list.Items.Select(work => work.Title));
        Assert.Equal(titles.Length, list.TotalCount);
    }

    [Theory]
    [InlineData("Episode", "“Episode” is not a kind filter. Use Movie or Series.")]
    [InlineData(" tv ", "“tv” is not a kind filter. Use Movie or Series.")]
    [InlineData("3", "“3” is not a kind filter. Use Movie or Series.")]
    public async Task Works_of_a_kind_that_is_not_one_is_400_with_its_sentence(string kind, string error)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?kind={Uri.EscapeDataString(kind)}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(error, await response.ReadErrorAsync(cancellationToken));
    }

    [Fact]
    public async Task Works_of_a_kind_that_is_a_long_text_echoes_only_its_start()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var pasted = new string('x', 500);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?kind={pasted}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal($"“{new string('x', 32)}…” is not a kind filter. Use Movie or Series.", await response.ReadErrorAsync(cancellationToken));
    }

    [Fact]
    public async Task Works_pages_count_a_series_once_however_many_episodes_it_has()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Yellowstone with three episodes, among the three movie works.
        _jellyfin.Series.Add(JellyfinSamples.Series(JellyfinSamples.YellowstoneId));
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith(ItemId("b"), "Daybreak", JellyfinSamples.YellowstoneId, 1, 1, "/media/tv-shows/Yellowstone/S01E01.mkv"));
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith(ItemId("c"), "Kill the Messenger", JellyfinSamples.YellowstoneId, 1, 2, "/media/tv-shows/Yellowstone/S01E02.mkv"));
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith(ItemId("d"), "A Thundering", JellyfinSamples.YellowstoneId, 2, 1, "/media/tv-shows/Yellowstone/S02E01.mkv"));
        await SyncAsync();

        //Act
        var first = await ReadListAsync(await _factory.GetHttpRequest($"{BaseUrl}?page=1&pageSize=3", _owner.AccessToken), cancellationToken);
        var second = await ReadListAsync(await _factory.GetHttpRequest($"{BaseUrl}?page=2&pageSize=3", _owner.AccessToken), cancellationToken);

        //Assert
        // Seven works stored — the three movies, the series and its three episodes —, four listed.
        Assert.Equal((4, 4), (first.TotalCount, second.TotalCount));
        Assert.Equal(["13-й район", "Чужі", "The Continental"], first.Items.Select(work => work.Title));
        var series = Assert.Single(second.Items);
        Assert.Equal("Yellowstone", series.Title);
        Assert.Equal([new LibrarySeasonDto(1, 2), new LibrarySeasonDto(2, 1)], series.Seasons);
        await _factory.WithDbAsync(async db =>
            Assert.Equal(7, await db.MediaWorks.CountAsync(cancellationToken)));
    }

    // The library a sync of the sample movies, series and episodes stores for "Living room".
    private async Task SyncSamplesAsync()
    {
        _jellyfin.Series.AddRange(JellyfinSamples.Series());
        _jellyfin.Episodes.AddRange(JellyfinSamples.Episodes());
        await SyncAsync();
    }

    // The library a sync stores for "Living room": these movies (the samples unless given) and the series and
    // episodes the test put on the server.
    private async Task<IntegrationConnectionEntity> SyncAsync(List<JsonObject>? movies = null)
    {
        if (movies is not null)
        {
            _jellyfin.Movies.Clear();
            _jellyfin.Movies.AddRange(movies);
        }

        var jellyfin = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(jellyfin, _owner.User);
        return jellyfin;
    }

    // A 32-character item id made of one letter.
    private static string ItemId(string letter) => new(letter[0], 32);

    // An episode of the series "a" at this place, with a file of its own in the cartoon library.
    private static JsonObject Episode(string letter, int? season, int? episode) =>
        JellyfinSamples.EpisodeWith(ItemId(letter), $"Episode {letter}", ItemId("a"), season, episode, $"/media/cartoons/Penguins/{letter}.mkv");

    private static async Task<LibraryWorkListResponse> ReadListAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<LibraryWorkListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
}
