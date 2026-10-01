using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;

namespace MediaAcquisitionManager.Api.Tests.Library;

// MAM-8 — the Library page's works (GET /api/library/works): for every signed-in user, paged by the
// server in title order — Ukrainian alphabetical order, letter case ignored (LibraryTitleOrder, ADR 0006),
// which SQLite's own code-point order gets wrong for І, Є, Ї and Ґ. Search and filters are LibraryWorksSearchHttpTests;
// what a work carries is LibraryWorksDetailsHttpTests.
public sealed class LibraryWorksHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library/works";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private ILibrarySnapshotSeeder _snapshots = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private FakeJellyfinServer _jellyfin = null!;

    public LibraryWorksHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _snapshots = new LibrarySnapshotSeeder(_factory.Scope);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        _jellyfin = new FakeJellyfinServer { ApiKey = ApiKey };
        _jellyfin.Arm(_factory.JellyfinHttp);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Works_without_a_token_is_401()
    {
        //Arrange
        await SyncAsync();

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl);

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Works_as_a_signed_in_non_owner_is_200_with_every_work_in_title_order()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncAsync();

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        // The library is the household's: every signed-in user reads it. A digit sorts before a letter,
        // and Cyrillic before Latin in Ukrainian order.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal((3, 1, 25), (list.TotalCount, list.Page, list.PageSize));
        Assert.Equal(["13-й район", "Чужі", "The Continental"], list.Items.Select(work => work.Title));
    }

    [Fact]
    public async Task Works_orders_titles_in_ukrainian_alphabetical_order()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Listed by Jellyfin in another order, so the order can only be the app's own. By code point
        // І (U+0406) would come before А, and Ґ (U+0490) after Я.
        await SyncAsync(Titled("Яга", "Інтерстеллар", "Alien", "Ґава", "Аватар", "Гнів"));

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        // Latin after Cyrillic: the Ukrainian collation's order of scripts. The invariant culture would put
        // Latin first, and Ґ with Г and Ї with І (LibraryTitleOrder, LibraryTitleOrderTests).
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(["Аватар", "Гнів", "Ґава", "Інтерстеллар", "Яга", "Alien"], list.Items.Select(work => work.Title));
    }

    [Fact]
    public async Task Works_orders_titles_ignoring_letter_case()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // By code point every capital comes before every small letter: Альфа, Гамма, бета.
        await SyncAsync(Titled("Гамма", "бета", "Альфа"));

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(["Альфа", "бета", "Гамма"], list.Items.Select(work => work.Title));
    }

    [Fact]
    public async Task Works_pages_follow_the_title_order()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncAsync(Titled("Яга", "Інтерстеллар", "Alien", "Ґава", "Аватар", "Гнів"));

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?page=2&pageSize=2", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal((6, 2, 2), (list.TotalCount, list.Page, list.PageSize));
        Assert.Equal(["Ґава", "Інтерстеллар"], list.Items.Select(work => work.Title));
    }

    [Fact]
    public async Task Works_pages_cover_every_work_once()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Eight movies, six works: renumbered copies share their titles, so the order within a title is
        // the server's tie-break, and a page boundary inside a run of equal titles must not repeat or
        // drop a work.
        await SyncAsync([.. JellyfinSamples.Library(8)]);

        //Act
        var first = await ReadListAsync(await _factory.GetHttpRequest($"{BaseUrl}?page=1&pageSize=4", _owner.AccessToken), cancellationToken);
        var second = await ReadListAsync(await _factory.GetHttpRequest($"{BaseUrl}?page=2&pageSize=4", _owner.AccessToken), cancellationToken);
        var past = await ReadListAsync(await _factory.GetHttpRequest($"{BaseUrl}?page=3&pageSize=4", _owner.AccessToken), cancellationToken);

        //Assert
        Assert.Equal((6, 4, 2), (first.TotalCount, first.Items.Count, second.Items.Count));
        Assert.Equal(6, first.Items.Concat(second.Items).Select(work => work.Id).Distinct().Count());
        Assert.Equal(
            ["13-й район", "13-й район", "Чужі", "Чужі", "The Continental", "The Continental"],
            first.Items.Concat(second.Items).Select(work => work.Title));
        // Past the last page: an empty page that still says how many there are.
        Assert.Equal((6, 3, 4), (past.TotalCount, past.Page, past.PageSize));
        Assert.Empty(past.Items);
    }

    [Fact]
    public async Task Works_with_a_page_size_of_100_is_200_and_echoes_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncAsync();

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?pageSize=100", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(100, (await ReadListAsync(response, cancellationToken)).PageSize);
    }

    [Theory]
    [InlineData("page=0", "Page must be 1 or more.")]
    [InlineData("page=-1", "Page must be 1 or more.")]
    [InlineData("pageSize=0", "Page size must be between 1 and 100.")]
    [InlineData("pageSize=101", "Page size must be between 1 and 100.")]
    public async Task Works_with_a_page_out_of_range_is_400_with_its_sentence(string query, string error)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncAsync();

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?{query}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(error, await response.ReadErrorAsync(cancellationToken));
    }

    [Fact]
    public async Task Works_before_any_sync_is_an_empty_page()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(0, list.TotalCount);
        Assert.Empty(list.Items);
    }

    // The library a sync of these movies — the samples unless given — stores for "Living room".
    private async Task SyncAsync(List<JsonObject>? movies = null)
    {
        if (movies is not null)
        {
            _jellyfin.Movies.Clear();
            _jellyfin.Movies.AddRange(movies);
        }

        var jellyfin = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        await _snapshots.Sync(jellyfin, _owner.User);
    }

    // Movies titled so, and nothing else to tell them apart by: each a renumbered "13-й район" (its own
    // ids, source and path) with that one title.
    private static List<JsonObject> Titled(params string[] titles) =>
    [
        .. titles.Select((title, index) =>
        {
            var movie = JellyfinSamples.Renumbered(JellyfinSamples.Movie(JellyfinSamples.District13Id), index + 1);
            movie["Name"] = title;
            movie.Remove("OriginalTitle");
            return movie;
        }),
    ];

    private static async Task<LibraryWorkListResponse> ReadListAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<LibraryWorkListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
}
