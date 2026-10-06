using System.Net;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Infrastructure.Integrations.Tmdb;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-34 follow-up (Vasyl's decision of 2026-10-06) — one episode of a show with its own IMDb id
// (IMetadataCatalog.GetEpisodeAsync), the id an episode's link writes into a film item: the request as the live check
// sent it, the answer read field by field — numbers, title, air date, runtime and external_ids.imdb_id —, TMDb's
// 404 a missing episode, and a caller's mistake sent nowhere; from the answers Vasyl's live check recorded on 2026-10-06
// (VendorSamples/README.md).
public sealed class TmdbAdapterEpisodesTests : IDisposable
{
    private const string BaseUrl = "https://api.themoviedb.test";
    private const string Token = "eyJhbGciOiJIUzI1NiJ9.eyJhdWQiOiJlcGlzb2RlcyJ9.c2lnbmF0dXJlLW9mLWVwaXNvZGVz";

    private const string NotTmdbError =
        "The service at this address answered, but not the way TMDb's API does. Check the address: TMDb's is https://api.themoviedb.org.";

    private static readonly IntegrationConnectionSettings Settings = new(null, BaseUrl, null, null, Token);

    private readonly IntegrationAdapterHarness _harness = new();

    private IMetadataCatalog Catalog => _harness.Adapter<IMetadataCatalog>(TmdbAdapter.TypeId);

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task GetEpisode_asks_for_the_episode_with_its_external_ids_in_ukrainian_in_one_request()
    {
        //Arrange
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Sample(TmdbSamples.TvEpisodeWwiiFile, TmdbSamples.MediaType);

        //Act
        await Catalog.GetEpisodeAsync(Settings, TmdbSamples.TvEpisodeWwiiShowId, 1, 1, TestContext.Current.CancellationToken);

        //Assert
        var request = Assert.Single(_harness.TmdbHttp.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal($"{BaseUrl}/3/tv/91724/season/1/episode/1?append_to_response=external_ids&language=uk-UA", request.Uri.AbsoluteUri);
        Assert.Equal([$"Bearer {Token}"], request.HeaderValues("Authorization"));
        Assert.DoesNotContain(Token, request.Uri.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetEpisode_reads_the_episodes_numbers_title_air_date_runtime_and_its_own_imdb_id()
    {
        //Arrange
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Sample(TmdbSamples.TvEpisodeWwiiFile, TmdbSamples.MediaType);

        //Act
        var result = await Catalog.GetEpisodeAsync(Settings, TmdbSamples.TvEpisodeWwiiShowId, 1, 1, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        // The IMDb id Vasyl set by hand in Jellyfin on 2026-10-06, after which its lookup found the episode. TMDb has no
        // Ukrainian title for it: "Серія 1" is its own fallback.
        Assert.Equal(new CatalogEpisode(1, 1, "Серія 1", new DateOnly(2019, 7, 12), 50, TmdbSamples.TvEpisodeWwiiImdbId), result.Value);
    }

    [Fact]
    public async Task GetEpisode_reads_planet_earths_jungles_with_its_imdb_id()
    {
        //Arrange
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Sample(TmdbSamples.TvEpisodePlanetEarthFile, TmdbSamples.MediaType);

        //Act
        var result = await Catalog.GetEpisodeAsync(Settings, "1044", 1, 8, TestContext.Current.CancellationToken);

        //Assert
        // "Planet_Earth_E08 - Jungles", the movie item MAM-34's acceptance links to this episode: its IMDb id is what the
        // app writes there.
        Assert.Equal(new CatalogEpisode(1, 8, "Джунглі", new DateOnly(2006, 11, 19), 51, TmdbSamples.TvEpisodePlanetEarthImdbId), result.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nm0001")]
    public async Task GetEpisode_whose_external_ids_name_no_imdb_id_has_none(string? imdbId)
    {
        //Arrange
        var answer = JsonNode.Parse(TmdbSamples.TvEpisodeWwii())!.AsObject();
        answer["external_ids"]!["imdb_id"] = imdbId;
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Text(answer.ToJsonString(), HttpStatusCode.OK, "application/json");

        //Act
        var result = await Catalog.GetEpisodeAsync(Settings, TmdbSamples.TvEpisodeWwiiShowId, 1, 1, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Null(result.Value!.ImdbId);
    }

    [Fact]
    public async Task GetEpisode_without_external_ids_and_with_no_title_date_or_runtime_has_none_of_them()
    {
        //Arrange
        var answer = JsonNode.Parse(TmdbSamples.TvEpisodeWwii())!.AsObject();
        answer.Remove("external_ids");
        answer["name"] = "";
        answer["air_date"] = null;
        answer["runtime"] = null;
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Text(answer.ToJsonString(), HttpStatusCode.OK, "application/json");

        //Act
        var result = await Catalog.GetEpisodeAsync(Settings, TmdbSamples.TvEpisodeWwiiShowId, 1, 1, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(new CatalogEpisode(1, 1, null, null, null, null), result.Value);
    }

    [Fact]
    public async Task GetEpisode_tmdb_does_not_know_is_not_found_with_its_sentence()
    {
        //Arrange
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Recorded(TmdbSamples.TvEpisodeNotFoundResponseFile);

        //Act
        var result = await Catalog.GetEpisodeAsync(Settings, TmdbSamples.TvEpisodeWwiiShowId, 9, 99, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(
            (IntegrationCallStatus.NotFound, "TMDb has no episode 99 of season 9 of the show 91724."),
            (result.Status, result.Error));
        Assert.Single(_harness.TmdbHttp.Requests);
    }

    [Fact]
    public async Task GetEpisode_answered_404_without_tmdbs_body_is_an_unexpected_response_not_a_missing_episode()
    {
        //Arrange
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Text("<html>Not Found</html>", HttpStatusCode.NotFound, "text/html");

        //Act
        var result = await Catalog.GetEpisodeAsync(Settings, TmdbSamples.TvEpisodeWwiiShowId, 1, 1, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
    }

    [Fact]
    public async Task GetEpisode_with_a_token_tmdb_refuses_is_an_authentication_failure()
    {
        //Arrange
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Recorded(TmdbSamples.WrongKeyResponseFile);

        //Act
        var result = await Catalog.GetEpisodeAsync(Settings, TmdbSamples.TvEpisodeWwiiShowId, 1, 1, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.AuthenticationFailed, result.Status);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"season_number":1}""")]
    [InlineData("""{"season_number":1,"episode_number":-1}""")]
    [InlineData("""{"season_number":"1","episode_number":1}""")]
    [InlineData("[]")]
    public async Task GetEpisode_of_a_body_without_the_episodes_numbers_is_an_unexpected_response(string body)
    {
        //Arrange
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Text(body, HttpStatusCode.OK, "application/json");

        //Act
        var result = await Catalog.GetEpisodeAsync(Settings, TmdbSamples.TvEpisodeWwiiShowId, 1, 1, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal((IntegrationCallStatus.UnexpectedResponse, NotTmdbError), (result.Status, result.Error));
    }

    [Theory]
    [InlineData("tt9103946", 1, 1)]
    [InlineData("../91724", 1, 1)]
    [InlineData("91724?append_to_response=images", 1, 1)]
    [InlineData("91724", -1, 1)]
    [InlineData("91724", 1, -1)]
    public async Task GetEpisode_with_a_callers_mistake_throws_without_sending_anything(string showId, int seasonNumber, int episodeNumber)
    {
        //Arrange

        //Act
        var exception = await Record.ExceptionAsync(
            () => Catalog.GetEpisodeAsync(Settings, showId, seasonNumber, episodeNumber, TestContext.Current.CancellationToken));

        //Assert
        Assert.IsAssignableFrom<ArgumentException>(exception);
        Assert.Empty(_harness.TmdbHttp.Requests);
    }

    [Fact]
    public async Task GetEpisode_against_the_fake_reads_the_episode_it_holds_and_misses_another()
    {
        //Arrange
        var tmdb = FakeTmdbServer.Arm(_harness.TmdbHttp);
        tmdb.AddEpisode(new FakeTmdbEpisode(1044, 1, 8, "Джунглі", "tt1000008", "2006-11-26", 51));
        var settings = new IntegrationConnectionSettings(null, FakeTmdbServer.BaseUrl, null, null, FakeTmdbServer.Token);
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var held = await Catalog.GetEpisodeAsync(settings, "1044", 1, 8, cancellationToken);
        var missing = await Catalog.GetEpisodeAsync(settings, "1044", 1, 9, cancellationToken);

        //Assert
        Assert.Equal(new CatalogEpisode(1, 8, "Джунглі", new DateOnly(2006, 11, 26), 51, "tt1000008"), held.Value);
        Assert.Equal(IntegrationCallStatus.NotFound, missing.Status);
    }
}
