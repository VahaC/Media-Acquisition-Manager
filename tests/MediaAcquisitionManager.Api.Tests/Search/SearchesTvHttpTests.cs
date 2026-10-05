using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Search;

// MAM-32 — a stored search of a show over real HTTP, against the real Torznab adapter with the network
// faked by the real Toloka answers: the season it takes (stored, echoed on every read, sent to the TV
// search alone), the TV releases it keeps beside the movies, listed with a show's ids, and a source whose
// TV query fails, stored as failed with none of its releases.
public sealed class SearchesTvHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/searches";
    private const string TolokaAddress = "http://prowlarr.test:9696/1/api";
    private const string ApiKey = "planted-torznab-key-0001";

    private const string UnreachableError =
        $"Couldn't reach Torznab at {TolokaAddress}: nothing accepted the connection. Check the address and port, and that Torznab is running.";

    private static readonly int SampleMovieCount = TorznabSamples.ItemCount - TorznabSamples.NonMovieItemCount;

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private FakeTorznabServer _toloka = null!;

    public SearchesTvHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        _toloka = new FakeTorznabServer(TolokaAddress, ApiKey);
        _toloka.Arm(_factory.TorznabHttp);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Create_of_a_season_stores_and_echoes_it_and_asks_the_tv_search_alone()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        _toloka.TvSearchXml = TorznabSamples.TvSeasonSearch(TolokaAddress, ApiKey);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new SearchRequest("Yellowstone", Season: " 1 "));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await ReadAsync(response, cancellationToken);
        Assert.Equal(1, created.Season);
        var read = await _factory.GetHttpRequest(response.Headers.Location!.AbsolutePath, _member.AccessToken);
        Assert.Equal(1, (await ReadAsync(read, cancellationToken)).Season);
        Assert.Equal(
            [
                $"{TolokaAddress}?t=caps&apikey={ApiKey}",
                $"{TolokaAddress}?t=tvsearch&apikey={ApiKey}&q=Yellowstone&season=1&cat=5000,5040,5050,5060,5070,5080&limit=100",
            ],
            _factory.TorznabHttp.Requests.Select(request => request.Uri.AbsoluteUri));
        var source = Assert.Single(created.Sources);
        Assert.Equal(TorznabSamples.TvSeasonItemCount, source.ReleaseCount);
        await _factory.WithDbAsync(async db =>
        {
            var search = Assert.Single(await db.Searches.AsNoTracking().ToListAsync(cancellationToken));
            Assert.Equal(1, search.Season);
            var releases = await db.Releases.AsNoTracking().ToListAsync(cancellationToken);
            Assert.Equal(TorznabSamples.TvSeasonItemCount, releases.Count);
            Assert.All(releases, release =>
            {
                Assert.Equal(ReleaseKind.Tv, release.Kind);
                Assert.Equal(source.Id, release.SearchSourceId);
            });
        });
    }

    [Fact]
    public async Task Create_without_a_season_stores_none_and_asks_for_movies_and_tv()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        _toloka.TvSearchXml = TorznabSamples.TvSearch(TolokaAddress, ApiKey);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new SearchRequest("Yellowstone", Season: " "));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await ReadAsync(response, cancellationToken);
        Assert.Null(created.Season);
        Assert.Equal(SampleMovieCount + TorznabSamples.TvItemCount, Assert.Single(created.Sources).ReleaseCount);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Null(Assert.Single(await db.Searches.AsNoTracking().ToListAsync(cancellationToken)).Season);
            // MAM-33 — the TV answer's films in TV/Documentary, naming no season, are movies (ADR 0020).
            Assert.Equal(
                TorznabSamples.TvItemCount - TorznabSamples.TvFilmItemCount,
                await db.Releases.CountAsync(release => release.Kind == ReleaseKind.Tv, cancellationToken));
            Assert.Equal(
                SampleMovieCount + TorznabSamples.TvFilmItemCount,
                await db.Releases.CountAsync(release => release.Kind == ReleaseKind.Movie, cancellationToken));
        });
    }

    [Theory]
    [InlineData("one", "“one” is not a season. Use a season number, such as 1, or 0 for the specials.")]
    [InlineData("S01", "“S01” is not a season. Use a season number, such as 1, or 0 for the specials.")]
    [InlineData("-1", "“-1” is not a season. Use a season number, such as 1, or 0 for the specials.")]
    [InlineData("10000", "“10000” is not a season. Use a season number, such as 1, or 0 for the specials.")]
    public async Task Create_of_a_season_that_is_not_one_is_400_with_its_sentence_stores_nothing_and_asks_no_source(
        string season, string expectedError)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new SearchRequest("Yellowstone", Season: season));

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedError, await response.ReadErrorAsync(cancellationToken));
        Assert.Empty(_factory.TorznabHttp.Requests);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.Searches.CountAsync(cancellationToken));
            Assert.Equal(0, await db.Releases.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Releases_lists_a_tv_release_beside_the_movies_with_a_shows_ids()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        _toloka.TvSearchXml = TorznabSamples.Feed(
            TorznabSamples.Item(
                "Yellowstone (S1) (2018) 1080p",
                $"{TorznabSamples.DownloadBaseFor(TolokaAddress)}download?file=tv",
                "t2",
                extra: """<category>5040</category><torznab:attr name="seeders" value="99999" /><torznab:attr name="imdbid" value="tt4236770" /><torznab:attr name="tmdbid" value="73586" /><torznab:attr name="tvdbid" value="341164" />"""));
        var created = await ReadAsync(
            await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new SearchRequest("Yellowstone")), cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{created.Id}/releases?pageSize=1", _member.AccessToken);

        //Assert
        // The most seeded first, and the whole list counts the movies too. Its TMDb id is the show's
        // (ADR 0017), never one a movie's could equal.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = (await response.Content.ReadFromJsonAsync<SearchReleaseListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal(SampleMovieCount + 1, list.TotalCount);
        var release = Assert.Single(list.Items);
        Assert.Equal("Yellowstone (S1) (2018) 1080p", release.Title);
        Assert.Equal(
            [
                new ReleaseIdentifierDto(MediaIdentifierScheme.Imdb, "tt4236770"),
                new ReleaseIdentifierDto(MediaIdentifierScheme.TmdbTv, "73586"),
                new ReleaseIdentifierDto(MediaIdentifierScheme.Tvdb, "341164"),
            ],
            release.Identifiers);
    }

    [Fact]
    public async Task Create_whose_tv_query_fails_stores_the_source_as_failed_with_none_of_its_releases()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        _toloka.Intercept = request => request.RequestUri!.Query.Contains("t=tvsearch", StringComparison.Ordinal)
            ? throw new HttpRequestException(HttpRequestError.ConnectionError, "refused")
            : null;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new SearchRequest("Yellowstone"));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var source = Assert.Single((await ReadAsync(response, cancellationToken)).Sources);
        Assert.Equal((IntegrationCallStatus?)IntegrationCallStatus.Unreachable, source.Status);
        Assert.Equal(UnreachableError, source.Error);
        Assert.Equal(0, source.ReleaseCount);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(IntegrationCallStatus.Unreachable, Assert.Single(await db.SearchSources.AsNoTracking().ToListAsync(cancellationToken)).Status);
            Assert.Equal(0, await db.Releases.CountAsync(cancellationToken));
        });
    }

    private static async Task<SearchResponse> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<SearchResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
}
