using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-25 — the fake Jellyfin's side of a replacement's check, which the adapter's and the verifier's tests stand on: a request
// to look at paths is recorded and answered 204 at once, as Jellyfin 12.2's source answers it, and a planted film or episode
// sits under the id its path computes, refreshed or not yet. The id formula the fake files them by is pinned here against
// every movie and episode file the vendor samples recorded, so the tests can't agree with a drifted formula.
public sealed class FakeJellyfinServerFilesTests : IDisposable
{
    private const string BaseUrl = "http://jellyfin.test:8096";
    private const string ApiKey = "4f1d0b9c2e7a4c3b8d6e5f7a9b0c1d2e";

    private const string FilmPath = "/media/movies/Alien (1979)/Alien.1979.2160p.UHD.BDRemux.mkv";
    private const string EpisodePath = "/media/tv-shows/Yellowstone/Yellowstone.2018.S02.1080p/Yellowstone.2018.S02E05.1080p.mkv";

    private readonly RecordingHttpHandler _handler = new();
    private readonly FakeJellyfinServer _server = new(BaseUrl) { ApiKey = ApiKey };
    private readonly HttpClient _client;

    public FakeJellyfinServerFilesTests()
    {
        _server.Arm(_handler);
        _client = new HttpClient(_handler) { BaseAddress = new Uri(BaseUrl + "/") };
        _client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"MediaBrowser Token=\"{ApiKey}\"");
    }

    public void Dispose() => _client.Dispose();

    [Fact]
    public void Of_gives_every_recorded_movie_and_episode_file_the_id_jellyfin_gave_it()
    {
        //Arrange
        // Every item the samples recorded with a file of its own: the movie and episode pages, the item read whole, and the
        // hidden alternate version read on 12.2.0. A virtual episode has no path, and no id of this kind.
        var items = JellyfinSamples.Movies()
            .Concat(JellyfinSamples.Episodes())
            .Concat([JellyfinSamples.Item(), JellyfinSamples.EpisodeAlternateVersionItem()])
            .Where(item => item["Path"] is JsonValue)
            .ToList();

        //Act
        var computed = items
            .Select(item => (
                Recorded: (string)item["Id"]!,
                Computed: JellyfinItemIds.Of((string?)item["Type"] == "Episode" ? JellyfinItemIds.EpisodeType : JellyfinItemIds.MovieType, (string)item["Path"]!)))
            .ToList();

        //Assert
        // Guard the guard: four movies, five episode files, the item and the alternate.
        Assert.Equal(11, computed.Count);
        Assert.All(computed, pair => Assert.Equal(pair.Recorded, pair.Computed));
    }

    [Fact]
    public async Task Media_updated_records_each_requests_paths_in_order_and_answers_204_at_once()
    {
        //Arrange

        //Act
        var first = await PostAsync(Body(FilmPath, EpisodePath));
        var second = await PostAsync(Body(EpisodePath));

        //Assert
        Assert.Equal((HttpStatusCode.NoContent, HttpStatusCode.NoContent), (first.StatusCode, second.StatusCode));
        Assert.Collection(
            _server.ScanRequests,
            paths => Assert.Equal([FilmPath, EpisodePath], paths),
            paths => Assert.Equal([EpisodePath], paths));
        // Jellyfin answers before it looks: nothing is listed until a test plants it.
        Assert.Empty(_server.Episodes);
    }

    [Fact]
    public async Task Media_updated_runs_the_hook_whose_planted_item_is_read_by_the_id_its_path_computes()
    {
        //Arrange
        _server.OnScanRequested = paths => _server.PlantFilm(paths[0], "Alien", 1979, imdbId: "tt0078748", tmdbId: "348");

        //Act
        var posted = await PostAsync(Body(FilmPath));
        var read = await _client.GetAsync(
            $"Items/{JellyfinItemIds.Of(JellyfinItemIds.MovieType, FilmPath)}?userId={JellyfinSamples.AdministratorUserId}", TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, posted.StatusCode);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var item = JsonNode.Parse(await read.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
        Assert.Equal(FilmPath, (string?)item["Path"]);
        Assert.Equal("348", (string?)item["ProviderIds"]!["Tmdb"]);
    }

    [Fact]
    public async Task Media_updated_with_a_refusal_answers_it_records_the_paths_and_runs_no_hook()
    {
        //Arrange
        var hookRan = false;
        _server.ScanRefusal = HttpStatusCode.ServiceUnavailable;
        _server.OnScanRequested = _ => hookRan = true;

        //Act
        var response = await PostAsync(Body(FilmPath));

        //Assert
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal([FilmPath], Assert.Single(_server.ScanRequests));
        Assert.False(hookRan);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("""{"Updates":[{"UpdateType":"Created"}]}""")]
    [InlineData("""{"Updates":[{"Path":null,"UpdateType":"Created"}]}""")]
    public async Task Media_updated_without_a_path_for_every_update_is_400_and_records_nothing(string body)
    {
        //Arrange

        //Act
        var response = await PostAsync(body);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_server.ScanRequests);
    }

    [Fact]
    public async Task Media_updated_read_with_get_or_a_wrong_key_is_not_taken()
    {
        //Arrange
        _client.DefaultRequestHeaders.Remove("Authorization");
        _client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "MediaBrowser Token=\"another-key\"");

        //Act
        var wrongKey = await PostAsync(Body(FilmPath));
        _server.ApiKey = null;
        var get = await _client.GetAsync("Library/Media/Updated", TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal((HttpStatusCode.Unauthorized, HttpStatusCode.NotFound), (wrongKey.StatusCode, get.StatusCode));
        Assert.Empty(_server.ScanRequests);
    }

    [Fact]
    public void Planted_items_before_their_refresh_carry_no_running_time_ids_or_numbers()
    {
        //Arrange

        //Act
        var film = _server.PlantFilm(FilmPath, "Alien", 1979, imdbId: "tt0078748", tmdbId: "348", refreshed: false);
        var episode = _server.PlantEpisode(EpisodePath, "Episode 5", JellyfinSamples.YellowstoneId, 2, 5, tvdbId: "3005", refreshed: false);

        //Assert
        Assert.Equal(JellyfinItemIds.Of(JellyfinItemIds.MovieType, FilmPath), (string?)film["Id"]);
        Assert.Equal(JellyfinItemIds.Of(JellyfinItemIds.EpisodeType, EpisodePath), (string?)episode["Id"]);
        JsonObject[] planted = [film, episode];
        Assert.All(planted, item =>
        {
            Assert.False(item.ContainsKey("RunTimeTicks"));
            Assert.False(item["MediaSources"]![0]!.AsObject().ContainsKey("RunTimeTicks"));
            Assert.Empty(item["ProviderIds"]!.AsObject());
            Assert.False(item.ContainsKey("IndexNumber"));
            Assert.False(item.ContainsKey("ParentIndexNumber"));
        });
        // The series is set as the item is made; the numbers come with the refresh.
        Assert.Equal(JellyfinSamples.YellowstoneId, (string?)episode["SeriesId"]);
        Assert.Contains(film, _server.Movies);
        Assert.Contains(episode, _server.Episodes);
    }

    [Fact]
    public void Planting_an_item_again_replaces_the_one_under_its_id()
    {
        //Arrange
        _server.PlantEpisode(EpisodePath, "Episode 5", JellyfinSamples.YellowstoneId, 2, 5, refreshed: false);

        //Act
        var refreshed = _server.PlantEpisode(EpisodePath, "Two Parts", JellyfinSamples.YellowstoneId, 2, 5, episodeEnd: 6, tvdbId: "3005");

        //Assert
        var stored = Assert.Single(_server.Episodes);
        Assert.Same(refreshed, stored);
        Assert.Equal((2, 5, 6), ((int)stored["ParentIndexNumber"]!, (int)stored["IndexNumber"]!, (int)stored["IndexNumberEnd"]!));
        Assert.True((long)stored["RunTimeTicks"]! > 0);
    }

    [Fact]
    public async Task Items_by_ids_answers_a_series_with_its_provider_ids_to_the_key_alone()
    {
        //Arrange
        _server.Series.Add(JellyfinSamples.Series(JellyfinSamples.YellowstoneId));

        //Act
        var response = await _client.GetAsync(
            $"Items?ids={JellyfinSamples.YellowstoneId}&fields=ProviderIds&enableImages=false&enableUserData=false", TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var series = Assert.Single(JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!["Items"]!.AsArray())!;
        Assert.Equal("Series", (string?)series["Type"]);
        Assert.Equal("73586", (string?)series["ProviderIds"]!["Tmdb"]);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<HttpResponseMessage> PostAsync(string body)
    {
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        return await _client.PostAsync("Library/Media/Updated", content, TestContext.Current.CancellationToken);
    }

    /// <summary>The body the adapter sends: each path as a file that appeared.</summary>
    private static string Body(params string[] paths) =>
        new JsonObject
        {
            ["Updates"] = new JsonArray([.. paths.Select(path => (JsonNode)new JsonObject { ["Path"] = path, ["UpdateType"] = "Created" })]),
        }.ToJsonString();
}
