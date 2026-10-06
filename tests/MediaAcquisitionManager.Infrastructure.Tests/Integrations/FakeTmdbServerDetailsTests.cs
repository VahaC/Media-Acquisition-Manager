using System.Text.Json.Nodes;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-34 — the fake TMDb's details carry what MAM-34 reads, in the shapes the live check recorded
// (VendorSamples/Tmdb/movie-details-short.json, tv-details-planet-earth.json): a film's runtime, a show's runtime as its
// latest episode's with episode_run_time empty, the overview, the alternative titles (a film's "titles", a show's
// "results") and the external ids with a show's IMDb id — the last two only when the details are asked to append them.
public sealed class FakeTmdbServerDetailsTests : IDisposable
{
    private const string Append = "append_to_response=credits,recommendations,alternative_titles,external_ids&language=uk-UA";

    private readonly RecordingHttpHandler _handler = new();
    private readonly FakeTmdbServer _server;
    private readonly HttpClient _client;

    public FakeTmdbServerDetailsTests()
    {
        _server = FakeTmdbServer.Arm(_handler);
        _client = new HttpClient(_handler) { BaseAddress = new Uri(FakeTmdbServer.BaseUrl + "/3/") };
        _client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {FakeTmdbServer.Token}");
    }

    public void Dispose() => _client.Dispose();

    [Fact]
    public async Task Film_details_carry_its_runtime_overview_alternative_titles_and_external_ids()
    {
        //Arrange
        _server.AddWork(new FakeTmdbWork(
            MediaWorkKind.Movie, 223021, "Landing Stripling", ImdbId: "tt0056164", RuntimeMinutes: 6, Overview: "Том і Джеррі.",
            AlternativeTitles: ["Посадкова смуга"]));

        //Act
        var details = await DetailsAsync($"movie/223021?{Append}");

        //Assert
        Assert.Equal((6, "Том і Джеррі.", "tt0056164"), ((int)details["runtime"]!, (string?)details["overview"], (string?)details["imdb_id"]));
        Assert.Equal("Посадкова смуга", (string?)details["alternative_titles"]!["titles"]![0]!["title"]);
        Assert.Equal("tt0056164", (string?)details["external_ids"]!["imdb_id"]);
    }

    [Fact]
    public async Task Show_details_carry_its_latest_episodes_runtime_and_its_imdb_id_among_the_external_ids()
    {
        //Arrange
        _server.AddWork(new FakeTmdbWork(
            MediaWorkKind.Series, 1044, "Планета Земля", OriginalTitle: "Planet Earth", ImdbId: "tt0795176", Seasons: [new FakeTmdbSeason(1, EpisodeCount: 11)],
            RuntimeMinutes: 53, AlternativeTitles: ["Planet Earth", "Planet Erde"]));

        //Act
        var details = await DetailsAsync($"tv/1044?{Append}");

        //Assert
        Assert.Empty(details["episode_run_time"]!.AsArray());
        Assert.Equal((53, 1, 11), ((int)details["last_episode_to_air"]!["runtime"]!, (int)details["last_episode_to_air"]!["season_number"]!, (int)details["last_episode_to_air"]!["episode_number"]!));
        // A show's details carry no imdb_id of their own: only its external ids do.
        Assert.Null(details["imdb_id"]);
        Assert.Equal("tt0795176", (string?)details["external_ids"]!["imdb_id"]);
        Assert.Equal(["Planet Earth", "Planet Erde"], details["alternative_titles"]!["results"]!.AsArray().Select(title => (string?)title!["title"]));
    }

    [Fact]
    public async Task Details_not_asked_to_append_them_carry_no_alternative_titles_or_external_ids()
    {
        //Arrange
        _server.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, 603, "Матриця", ImdbId: "tt0133093", RuntimeMinutes: 136, AlternativeTitles: ["The Matrix"]));

        //Act
        var details = await DetailsAsync("movie/603?append_to_response=credits,recommendations&language=uk-UA");

        //Assert
        Assert.False(details.ContainsKey("alternative_titles"));
        Assert.False(details.ContainsKey("external_ids"));
        Assert.Equal(136, (int)details["runtime"]!);
    }

    [Fact]
    public async Task Details_of_a_work_with_no_runtime_say_so_as_tmdb_does()
    {
        //Arrange
        _server.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, 1, "Без тривалості"));
        _server.AddWork(new FakeTmdbWork(MediaWorkKind.Series, 2, "Без серій"));

        //Act
        var film = await DetailsAsync("movie/1");
        var show = await DetailsAsync("tv/2");

        //Assert
        // TMDb's 0 for a film, and no latest episode for a show; the overview TMDb's "".
        Assert.Equal((0, ""), ((int)film["runtime"]!, (string?)film["overview"]));
        Assert.True(show.ContainsKey("last_episode_to_air"));
        Assert.Null(show["last_episode_to_air"]);
    }

    private async Task<JsonObject> DetailsAsync(string path)
    {
        var response = await _client.GetAsync(path, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!.AsObject();
    }
}
