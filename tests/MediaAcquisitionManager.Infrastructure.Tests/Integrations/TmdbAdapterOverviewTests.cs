using System.Net;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Integrations.Tmdb;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// A work's overview in one language, for the Recommendations page's Description dialog: the details asked in that
// language with nothing appended, of which the overview alone is read — TMDb's "" no overview, cleaned, masked and cut as
// the details' own; a work TMDb doesn't have; and a kind, an id or a language that is a caller's mistake.
public sealed class TmdbAdapterOverviewTests : IDisposable
{
    private const string BaseUrl = "https://api.themoviedb.test";
    private const string Token = "eyJhbGciOiJIUzI1NiJ9.eyJhdWQiOiJvdmVydmlldyJ9.c2lnbmF0dXJlLW9mLW92ZXJ2aWV3";

    private static readonly IntegrationConnectionSettings Settings = new(null, BaseUrl, null, null, Token);

    private readonly IntegrationAdapterHarness _harness = new();

    private IMetadataCatalog Catalog => _harness.Adapter<IMetadataCatalog>(TmdbAdapter.TypeId);

    public void Dispose() => _harness.Dispose();

    [Theory]
    [InlineData(MediaWorkKind.Movie, "en", "movie/118340?language=en")]
    [InlineData(MediaWorkKind.Movie, "uk-UA", "movie/118340?language=uk-UA")]
    [InlineData(MediaWorkKind.Series, "en", "tv/118340?language=en")]
    public async Task GetOverview_asks_for_the_details_in_that_language_with_nothing_appended(MediaWorkKind kind, string language, string pathAndQuery)
    {
        //Arrange
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Sample(TmdbSamples.MovieDetailsFile, TmdbSamples.MediaType);

        //Act
        await Catalog.GetOverviewAsync(Settings, kind, "118340", language, TestContext.Current.CancellationToken);

        //Assert
        var request = Assert.Single(_harness.TmdbHttp.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal($"{BaseUrl}/3/{pathAndQuery}", request.Uri.AbsoluteUri);
        Assert.Equal([$"Bearer {Token}"], request.HeaderValues("Authorization"));
    }

    [Fact]
    public async Task GetOverview_reads_the_details_samples_overview()
    {
        //Arrange
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Sample(TmdbSamples.MovieDetailsFile, TmdbSamples.MediaType);
        var expected = (string)JsonNode.Parse(TmdbSamples.MovieDetails())!["overview"]!;

        //Act
        var result = await Catalog.GetOverviewAsync(Settings, MediaWorkKind.Movie, TmdbSamples.MovieDetailsId, "uk-UA", TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(expected));
        Assert.Equal(new CatalogOverview(expected), result.Value);
    }

    [Fact]
    public async Task GetOverview_of_a_work_tmdb_has_no_overview_of_in_that_language_is_none()
    {
        //Arrange
        // The live check's short: TMDb's "" in Ukrainian (VendorSamples/Tmdb/movie-details-short.json).
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Sample(TmdbSamples.MovieDetailsShortFile, TmdbSamples.MediaType);

        //Act
        var result = await Catalog.GetOverviewAsync(Settings, MediaWorkKind.Movie, TmdbSamples.MovieDetailsShortId, "uk-UA", TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(new CatalogOverview(null), result.Value);
    }

    [Fact]
    public async Task GetOverview_cleans_masks_and_cuts_the_overview_as_the_details_do()
    {
        //Arrange
        var details = JsonNode.Parse(TmdbSamples.MovieDetailsShort())!.AsObject();
        details["overview"] = $"Tom‮\r\n  and Jerry {Token} " + new string('y', 3000);
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Text(details.ToJsonString(), HttpStatusCode.OK, "application/json");

        //Act
        var result = await Catalog.GetOverviewAsync(Settings, MediaWorkKind.Movie, TmdbSamples.MovieDetailsShortId, "en", TestContext.Current.CancellationToken);

        //Assert
        var overview = result.Value!.Text!;
        Assert.StartsWith("Tom and Jerry *** yy", overview, StringComparison.Ordinal);
        Assert.Equal((2000, '…'), (overview.Length, overview[^1]));
    }

    [Theory]
    [InlineData(MediaWorkKind.Movie, "TMDb has no movie with the id 999999.")]
    [InlineData(MediaWorkKind.Series, "TMDb has no show with the id 999999.")]
    public async Task GetOverview_of_a_work_tmdb_does_not_have_is_not_found(MediaWorkKind kind, string expectedError)
    {
        //Arrange
        FakeTmdbServer.Arm(_harness.TmdbHttp);
        var settings = new IntegrationConnectionSettings(null, FakeTmdbServer.BaseUrl, null, null, FakeTmdbServer.Token);

        //Act
        var result = await Catalog.GetOverviewAsync(settings, kind, "999999", "en", TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.NotFound, result.Status);
        Assert.Equal(expectedError, result.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("english")]
    [InlineData("EN")]
    [InlineData("uk-ua")]
    [InlineData("en-US-x")]
    [InlineData("en&append_to_response=credits")]
    [InlineData("en\n")]
    public async Task GetOverview_in_a_language_that_is_not_a_language_code_throws_without_sending_anything(string language)
    {
        //Arrange

        //Act
        var exception = await Record.ExceptionAsync(
            () => Catalog.GetOverviewAsync(Settings, MediaWorkKind.Movie, "118340", language, TestContext.Current.CancellationToken));

        //Assert
        Assert.IsType<ArgumentException>(exception);
        Assert.Empty(_harness.TmdbHttp.Requests);
    }

    [Theory]
    [InlineData(MediaWorkKind.Movie, "tt2015381")]
    [InlineData(MediaWorkKind.Movie, "118340?append_to_response=images")]
    [InlineData(MediaWorkKind.Episode, "118340")]
    public async Task GetOverview_of_an_episode_or_an_id_that_is_not_tmdbs_throws_without_sending_anything(MediaWorkKind kind, string id)
    {
        //Arrange

        //Act
        var exception = await Record.ExceptionAsync(
            () => Catalog.GetOverviewAsync(Settings, kind, id, "en", TestContext.Current.CancellationToken));

        //Assert
        Assert.IsType<ArgumentException>(exception);
        Assert.Empty(_harness.TmdbHttp.Requests);
    }
}
