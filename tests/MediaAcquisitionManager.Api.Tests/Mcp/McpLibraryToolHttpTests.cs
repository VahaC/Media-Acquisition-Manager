using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Mcp;

// MAM-45 — search_library: the Library page's list for an assistant, with no source asked and nothing stored. A text
// finds what the page's search finds, a kind and an order go by the page's names, and the paging is the page's, 10 works
// a page unless asked otherwise; each work comes with its versions and files as the page gives them to that user — a
// file's path problem, which quotes the owner's path mappings, to the owner alone. The page's sentences come back as
// isError results, and a summary says how many matched.
public sealed class McpLibraryToolHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string Tool = "search_library";
    private const string LibraryUrl = "/api/library/works";
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";

    /// <summary>The library's one path mapping: it covers none of the seeded files, which lie under <c>/media</c>, so each
    /// has a path problem for the owner to read.</summary>
    private static readonly PathMappingPair UncoveringMapping = new("/data", "/mnt/data");

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IPersonalAccessTokenSeeder _tokens = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;

    public McpLibraryToolHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    /// <summary>An order by the page's name — none for the default — and the titles it lists, every work on one page.</summary>
    public static TheoryData<string?, string[]> Orders => new()
    {
        // By the displayed title in Ukrainian alphabetical order: Д, І, Й.
        { null, ["Дюна", "Дюна: Частина друга", "Інтерстеллар", "Йеллоустоун"] },
        { "Title", ["Дюна", "Дюна: Частина друга", "Інтерстеллар", "Йеллоустоун"] },
        { "TitleDescending", ["Йеллоустоун", "Інтерстеллар", "Дюна: Частина друга", "Дюна"] },
        { "Newest", ["Дюна: Частина друга", "Дюна", "Йеллоустоун", "Інтерстеллар"] },
        { "Oldest", ["Інтерстеллар", "Йеллоустоун", "Дюна", "Дюна: Частина друга"] },
        // A series by its episodes' files: two of 2 GB.
        { "Largest", ["Інтерстеллар", "Дюна: Частина друга", "Дюна", "Йеллоустоун"] },
    };

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var users = new UserSeeder(_factory.Scope);
        _tokens = new PersonalAccessTokenSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        var livingRoom = await new IntegrationConnectionSeeder(_factory.Scope).Create(
            "jellyfin", "Living room", apiKey: JellyfinApiKey, pathMappings: [UncoveringMapping]);
        var works = new MediaWorkSeeder(_factory.Scope);
        await works.CreateMovie(livingRoom, "Інтерстеллар", 2014, "157336", "tt0816692", "Interstellar", [40_000_000_000]);
        await works.CreateMovie(livingRoom, "Дюна", 2021, "438631", "tt1160419", "Dune", [20_000_000_000]);
        await works.CreateMovie(livingRoom, "Дюна: Частина друга", 2024, "693134", "tt15239678", "Dune: Part Two", [30_000_000_000]);
        await works.CreateSeries(livingRoom, "Йеллоустоун", 2018, "73586", "tt4236770", [1, 2], 2_000_000_000);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SearchLibrary_answers_what_the_library_page_lists_for_the_same_user_with_path_problems_for_the_owner_only(bool asTheOwner)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var user = asTheOwner ? _owner : _member;
        var token = await _tokens.Create(user.User);
        var page = await ReadAsync($"{LibraryUrl}?search=dune&pageSize=10", user, cancellationToken);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await client.CallToolAsync(Tool, new Dictionary<string, object?> { ["text"] = "dune" }, cancellationToken: cancellationToken);

        //Assert
        Assert.False(result.IsError);
        var list = result.ReadStructured<LibraryWorkListResponse>();
        Assert.Equivalent(page, list, strict: true);
        Assert.Equal(["Дюна", "Дюна: Частина друга"], list.Items.Select(work => work.Title));
        // Ten a page by default: a list costs the model's context.
        Assert.Equal((2, 1, 10), (list.TotalCount, list.Page, list.PageSize));
        var files = list.Items.SelectMany(work => work.Versions).SelectMany(version => version.Files).ToList();
        Assert.Equal(2, files.Count);
        Assert.All(files, file => Assert.Equal(
            asTheOwner ? $"No path mapping of this integration covers “{file.ServicePath}”." : null,
            file.PathProblem));
        Assert.Equal("2 works match “dune”; page 1 lists 2.", result.ReadText());
    }

    [Fact]
    public async Task SearchLibrary_gives_a_work_with_its_versions_and_files_and_writes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_member.User);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await client.CallToolAsync(Tool, new Dictionary<string, object?> { ["text"] = " tt0816692 " }, cancellationToken: cancellationToken);

        //Assert
        var work = Assert.Single(result.ReadStructured<LibraryWorkListResponse>().Items);
        Assert.Equal(("Інтерстеллар", "Interstellar", (int?)2014), (work.Title, work.OriginalTitle, work.Year));
        var version = Assert.Single(work.Versions);
        Assert.Equal("Living room", version.SourceName);
        var file = Assert.Single(version.Files);
        Assert.Equal((40_000_000_000L, PathMapStatus.NotCovered), (file.SizeBytes!.Value, file.PathStatus));
        Assert.StartsWith("/media/", file.ServicePath, StringComparison.Ordinal);
        Assert.Equal("1 work matches “tt0816692”; page 1 lists 1.", result.ReadText());
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
            Assert.Equal(0, await db.Searches.CountAsync(cancellationToken));
        });
        Assert.Empty(_factory.JellyfinHttp.Requests);
    }

    [Fact]
    public async Task SearchLibrary_of_a_text_nothing_matches_is_an_empty_page_not_an_error()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_member.User);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await client.CallToolAsync(Tool, new Dictionary<string, object?> { ["text"] = "Ghostbusters" }, cancellationToken: cancellationToken);

        //Assert
        Assert.False(result.IsError);
        var list = result.ReadStructured<LibraryWorkListResponse>();
        Assert.Equal(0, list.TotalCount);
        Assert.Empty(list.Items);
        Assert.Equal("0 works match “Ghostbusters”; page 1 lists 0.", result.ReadText());
    }

    [Theory]
    [MemberData(nameof(Orders))]
    public async Task SearchLibrary_without_a_text_lists_the_whole_library_in_the_order_asked_for(string? sort, string[] titles)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_member.User);
        var arguments = sort is null ? new Dictionary<string, object?>() : new Dictionary<string, object?> { ["sort"] = sort };
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await client.CallToolAsync(Tool, arguments, cancellationToken: cancellationToken);

        //Assert
        var list = result.ReadStructured<LibraryWorkListResponse>();
        Assert.Equal(titles, list.Items.Select(work => work.Title));
        Assert.Equal("The library lists 4 works; page 1 lists 4.", result.ReadText());
    }

    [Theory]
    [InlineData(1, new[] { "Інтерстеллар", "Дюна: Частина друга" })]
    [InlineData(2, new[] { "Дюна", "Йеллоустоун" })]
    [InlineData(3, new string[0])]
    public async Task SearchLibrary_pages_the_ordered_library_with_the_whole_count(int page, string[] titles)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_member.User);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await client.CallToolAsync(
            Tool,
            new Dictionary<string, object?> { ["sort"] = "Largest", ["page"] = page, ["pageSize"] = 2 },
            cancellationToken: cancellationToken);

        //Assert
        var list = result.ReadStructured<LibraryWorkListResponse>();
        Assert.Equal(titles, list.Items.Select(work => work.Title));
        Assert.Equal((4, page, 2), (list.TotalCount, list.Page, list.PageSize));
        Assert.Equal($"The library lists 4 works; page {page} lists {titles.Length}.", result.ReadText());
    }

    [Theory]
    [InlineData("Movie", new[] { "Дюна", "Дюна: Частина друга", "Інтерстеллар" })]
    [InlineData("Series", new[] { "Йеллоустоун" })]
    [InlineData(" series ", new[] { "Йеллоустоун" })]
    public async Task SearchLibrary_with_a_kind_keeps_the_works_of_that_kind(string kind, string[] titles)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_member.User);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await client.CallToolAsync(Tool, new Dictionary<string, object?> { ["kind"] = kind }, cancellationToken: cancellationToken);

        //Assert
        var list = result.ReadStructured<LibraryWorkListResponse>();
        Assert.Equal(titles, list.Items.Select(work => work.Title));
        Assert.Equal(titles.Length, list.TotalCount);
    }

    [Theory]
    [InlineData("kind", "Episode", "“Episode” is not a kind filter. Use Movie or Series.")]
    [InlineData("page", 0, "Page must be 1 or more.")]
    [InlineData("pageSize", 0, "Page size must be between 1 and 100.")]
    [InlineData("pageSize", 101, "Page size must be between 1 and 100.")]
    public async Task SearchLibrary_with_a_value_the_list_does_not_take_is_an_error_with_the_library_pages_sentence(string argument, object value, string expected)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_member.User);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await client.CallToolAsync(Tool, new Dictionary<string, object?> { [argument] = value }, cancellationToken: cancellationToken);

        //Assert
        Assert.True(result.IsError);
        Assert.Equal(expected, result.ReadText());
        Assert.Null(result.StructuredContent);
    }

    [Fact]
    public async Task SearchLibrary_with_an_order_the_page_does_not_have_is_the_argument_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_member.User);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await client.CallToolAsync(Tool, new Dictionary<string, object?> { ["sort"] = "Popular" }, cancellationToken: cancellationToken);

        //Assert
        // An enum: the schema lists the page's orders, and anything else never reaches the service.
        Assert.True(result.IsError);
        Assert.Equal(
            "The arguments don't fit search_library: The JSON value could not be converted to System.Nullable`1[MediaAcquisitionManager.Core.Enums.LibrarySort]. Path: $ | LineNumber: 0 | BytePositionInLine: 9. Check them against the tool's input schema and call it again.",
            result.ReadText());
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>The page as the web app reads it, through the HTTP API with <paramref name="user"/>'s session; throws
    /// unless it was read, as a test that meant to read it arranged something else.</summary>
    private async Task<LibraryWorkListResponse> ReadAsync(string url, AuthorizedUser user, CancellationToken cancellationToken)
    {
        var response = await _factory.GetHttpRequest(url, user.AccessToken);
        if (response.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"The library answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellationToken)}");
        return (await response.Content.ReadFromJsonAsync<LibraryWorkListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }
}
