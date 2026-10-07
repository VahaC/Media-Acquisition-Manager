using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Search;

// MAM-35, ADR 0027 — "In your library" shows a work exactly as the Library page does (ADR 0006), its picture included: a
// search's library block carries each work's posterUrl, the same URL the work's Library card carries, and none for a work
// whose items have no image. No release source is set up here.
public sealed class SearchesLibraryPostersHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/searches";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private FakeJellyfinServer _jellyfin = null!;

    public SearchesLibraryPostersHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        // The samples as Jellyfin listed them with their images' tags, but 13-й район, whose image went.
        _jellyfin = new FakeJellyfinServer { ApiKey = ApiKey };
        _jellyfin.Movies.Clear();
        _jellyfin.Movies.AddRange(JellyfinSamples.MoviesWithImages());
        _jellyfin.Movies.Single(movie => (string?)movie["Id"] == JellyfinSamples.District13Id).Remove("ImageTags");
        _jellyfin.Series.AddRange(JellyfinSamples.SeriesWithImages());
        _jellyfin.Episodes.AddRange(JellyfinSamples.EpisodesWithImages());
        _jellyfin.Arm(_factory.JellyfinHttp);
        var livingRoom = await new IntegrationConnectionSeeder(_factory.Scope).Create("jellyfin", "Living room", apiKey: ApiKey);
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(livingRoom, _owner.User);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Theory]
    [InlineData("The Continental")]
    [InlineData("Yellowstone")]
    [InlineData("Flippin Fido")]
    public async Task Create_lists_a_library_work_with_the_poster_url_of_its_library_card(string words)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new SearchRequest(words));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var search = (await response.Content.ReadFromJsonAsync<SearchResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        var match = Assert.Single(search.Library);
        var card = await LibraryCardAsync(match.Work.Id, cancellationToken);
        Assert.NotNull(match.Work.PosterUrl);
        Assert.Equal(card.PosterUrl, match.Work.PosterUrl);
        // The poster row of the item the card is named after.
        await _factory.WithDbAsync(async db =>
        {
            var poster = await db.MediaWorkPosters.SingleAsync(row => row.MediaWorkId == match.Work.Id, cancellationToken);
            Assert.Equal($"/api/library/posters/{poster.SourceItemId}-{poster.Tag}.jpg", match.Work.PosterUrl);
        });
    }

    [Fact]
    public async Task Create_lists_a_library_work_whose_items_have_no_image_without_a_poster_url()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new SearchRequest("13-й район"));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var search = (await response.Content.ReadFromJsonAsync<SearchResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        var match = Assert.Single(search.Library);
        Assert.Equal("13-й район", match.Work.Title);
        Assert.Null(match.Work.PosterUrl);
        await _factory.WithDbAsync(async db =>
            Assert.False(await db.MediaWorkPosters.AnyAsync(row => row.MediaWorkId == match.Work.Id, cancellationToken)));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    // The work's card as the Library page lists it to the same member.
    private async Task<LibraryWorkDto> LibraryCardAsync(Guid workId, CancellationToken cancellationToken)
    {
        var response = await _factory.GetHttpRequest("/api/library/works?pageSize=100", _member.AccessToken);
        var list = (await response.Content.ReadFromJsonAsync<LibraryWorkListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        return list.Items.Single(work => work.Id == workId);
    }
}
