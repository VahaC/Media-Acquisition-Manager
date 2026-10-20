using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Mcp.Results;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace MediaAcquisitionManager.Api.Tests.Mcp;

// MAM-45 — list_recommendations and dismiss_recommendation: the Recommendations page (GET /api/recommendations) and its
// Not interested (PUT /api/recommendations/dismissals/{kind}/{catalogId}) as tools, for the caller by their user, never
// by the token. The list is the page's JSON, 20 works unless asked otherwise, with every filter and the sort passed as
// the page sends them; the caller's own tabs (one switched off is the list's sentence, marked isError) and their own Not
// interested marks left out, nobody else's. The mark writes one RecommendationDismissals row and no audit row, again
// changes nothing, and the work then leaves the caller's list; a kind or an id a mark doesn't take is the rules' sentence
// with nothing written. A read-only token's refusal is McpToolAccessHttpTests'.
public sealed class McpRecommendationToolsHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string ListTool = "list_recommendations";
    private const string DismissTool = "dismiss_recommendation";
    private const string ListUrl = "/api/recommendations";

    // Spelled out rather than read from the product code: this is what the user is told.
    private const string TabSwitchedOffError = "This tab is switched off in your profile.";

    /// <summary>The catalog's id (TMDb's) of the drama genre.</summary>
    private const int Drama = 18;

    private const int Adventure = 12;

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IPersonalAccessTokenSeeder _tokens = null!;
    private AuthorizedUser _anna = null!;
    private AuthorizedUser _bohdan = null!;
    private CatalogWorkEntity _matrix = null!;
    private CatalogWorkEntity _interstellar = null!;
    private CatalogWorkEntity _arrival = null!;
    private CatalogWorkEntity _breakingBad = null!;

    public McpRecommendationToolsHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var users = new UserSeeder(_factory.Scope);
        _tokens = new PersonalAccessTokenSeeder(_factory.Scope);
        // Two members: a mark is its user's alone.
        _anna = await users.CreateAuthorized("anna");
        _bohdan = await users.CreateAuthorized("bohdan");
        var genres = new CatalogGenreSeeder(_factory.Scope);
        await genres.Create(MediaWorkKind.Movie, Drama, "Драма");
        await genres.Create(MediaWorkKind.Movie, Adventure, "Пригоди");
        // Three films, best scored first, and a show on its own tab.
        var works = new CatalogWorkSeeder(_factory.Scope);
        var candidates = new RecommendationCandidateSeeder(_factory.Scope);
        _matrix = await works.Create(MediaWorkKind.Movie, "603", "Матриця", new DateOnly(1999, 3, 31), [Drama], originalTitle: "The Matrix");
        await candidates.Create(_matrix, RecommendationTab.Movies, score: 3, reasons: [new RecommendationReasonEntity { Kind = RecommendationReasonKind.Director }]);
        _interstellar = await works.Create(MediaWorkKind.Movie, "157336", "Інтерстеллар", new DateOnly(2014, 11, 5), [Adventure], originalTitle: "Interstellar");
        await candidates.Create(_interstellar, RecommendationTab.Movies, score: 2);
        _arrival = await works.Create(MediaWorkKind.Movie, "329865", "Прибуття", new DateOnly(2016, 11, 10), [Drama], originalTitle: "Arrival");
        await candidates.Create(_arrival, RecommendationTab.Movies, score: 1);
        _breakingBad = await works.Create(MediaWorkKind.Series, "1396", "Пуститися берега", new DateOnly(2008, 1, 20), originalTitle: "Breaking Bad");
        await candidates.Create(_breakingBad, RecommendationTab.Series);
        // The release sources carry The Matrix; a check of Arrival found nothing; Interstellar was never checked.
        var checks = new ReleaseAvailabilitySeeder(_factory.Scope);
        await checks.Create(_matrix, releaseCount: 3, bestResolution: 2160, hasUkrainianAudio: true, maxSeeders: 41);
        await checks.Create(_arrival, releaseCount: 0);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task ListRecommendations_gives_the_tabs_json_as_the_recommendations_page_reads_it_and_writes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The tool's page size is 20, the page's 25: the same page is asked of both.
        var page = await GetAsync($"{ListUrl}?tab=Movies&pageSize=20", _anna, cancellationToken);
        var token = await _tokens.Create(_anna.User, PersonalAccessTokenAccess.ReadOnly);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await client.CallToolAsync(ListTool, new Dictionary<string, object?> { ["tab"] = "Movies" }, cancellationToken: cancellationToken);

        //Assert
        Assert.False(result.IsError);
        Assert.Equal("3 recommendations on the Movies tab in all; page 1 lists 3.", result.ReadText());
        // Field for field what the HTTP API gives the same user, enums by name.
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(page), JsonNode.Parse(result.StructuredContent!.Value.GetRawText())));
        var list = result.ReadStructured<RecommendationListResponse>();
        Assert.Equal([_matrix.Id, _interstellar.Id, _arrival.Id], list.Items.Select(item => item.Id));
        Assert.Equal((3, 1, 20), (list.TotalCount, list.Page, list.PageSize));
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.RecommendationDismissals.CountAsync(cancellationToken));
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    [Theory]
    [InlineData("reason", "Director", "reason=Director")]
    [InlineData("availableOnly", true, "available=true")]
    [InlineData("genreId", Drama, "genreId=18")]
    [InlineData("decade", 2010, "decade=2010")]
    [InlineData("kind", "Series", "kind=Series")]
    [InlineData("sort", "Title", "sort=Title")]
    [InlineData("pageSize", 1, "pageSize=1")]
    public async Task ListRecommendations_passes_each_filter_the_sort_and_the_paging_as_the_page_sends_them(string argument, object value, string query)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var page = ReadList(await GetAsync($"{ListUrl}?tab=Movies&{query}", _anna, cancellationToken));
        var token = await _tokens.Create(_anna.User);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        // The page's own size of 25, unless the row asks for another.
        var filtered = await client.CallToolAsync(
            ListTool, new Dictionary<string, object?> { ["tab"] = "Movies", ["pageSize"] = 25, [argument] = value }, cancellationToken: cancellationToken);

        //Assert
        var list = filtered.ReadStructured<RecommendationListResponse>();
        Assert.Equal(page.Items.Select(item => item.Id), list.Items.Select(item => item.Id));
        Assert.Equal((page.TotalCount, page.PageSize), (list.TotalCount, list.PageSize));
        // Each changes what the unfiltered tab lists, so the argument reached the list.
        Assert.NotEqual([_matrix.Id, _interstellar.Id, _arrival.Id], list.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task ListRecommendations_of_a_tab_the_user_switched_off_is_an_error_with_the_lists_sentence_while_another_user_reads_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await new UserRecommendationSettingsSeeder(_factory.Scope).Save(_anna.User, [RecommendationTab.Series]);
        var annasToken = await _tokens.Create(_anna.User);
        var bohdansToken = await _tokens.Create(_bohdan.User);
        await using var annasClient = await _factory.ConnectMcpAsync(annasToken.Secret, cancellationToken: cancellationToken);
        await using var bohdansClient = await _factory.ConnectMcpAsync(bohdansToken.Secret, cancellationToken: cancellationToken);

        //Act
        var annas = await annasClient.CallToolAsync(ListTool, new Dictionary<string, object?> { ["tab"] = "Movies" }, cancellationToken: cancellationToken);
        var annasSeries = await annasClient.CallToolAsync(ListTool, new Dictionary<string, object?> { ["tab"] = "Series" }, cancellationToken: cancellationToken);
        var bohdans = await bohdansClient.CallToolAsync(ListTool, new Dictionary<string, object?> { ["tab"] = "Movies" }, cancellationToken: cancellationToken);

        //Assert
        // Her tabs (Vasyl's decision 5): one she switched off is no list of hers. His are the defaults.
        Assert.True(annas.IsError);
        Assert.Equal(TabSwitchedOffError, annas.ReadText());
        Assert.Null(annas.StructuredContent);
        Assert.Equal([_breakingBad.Id], annasSeries.ReadStructured<RecommendationListResponse>().Items.Select(item => item.Id));
        Assert.Equal(3, bohdans.ReadStructured<RecommendationListResponse>().TotalCount);
    }

    [Fact]
    public async Task ListRecommendations_leaves_out_the_users_own_not_interested_marks_only()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await new RecommendationDismissalSeeder(_factory.Scope).Dismiss(_anna.User, _matrix);
        var annasToken = await _tokens.Create(_anna.User);
        var bohdansToken = await _tokens.Create(_bohdan.User);
        await using var annasClient = await _factory.ConnectMcpAsync(annasToken.Secret, cancellationToken: cancellationToken);
        await using var bohdansClient = await _factory.ConnectMcpAsync(bohdansToken.Secret, cancellationToken: cancellationToken);

        //Act
        var annas = await annasClient.CallToolAsync(ListTool, new Dictionary<string, object?> { ["tab"] = "Movies" }, cancellationToken: cancellationToken);
        var bohdans = await bohdansClient.CallToolAsync(ListTool, new Dictionary<string, object?> { ["tab"] = "Movies" }, cancellationToken: cancellationToken);

        //Assert
        Assert.Equal([_interstellar.Id, _arrival.Id], annas.ReadStructured<RecommendationListResponse>().Items.Select(item => item.Id));
        Assert.Equal("2 recommendations on the Movies tab in all; page 1 lists 2.", annas.ReadText());
        Assert.Equal([_matrix.Id, _interstellar.Id, _arrival.Id], bohdans.ReadStructured<RecommendationListResponse>().Items.Select(item => item.Id));
    }

    [Theory]
    [InlineData("genreId", -18, "“-18” is not a genre id, which is a number such as 18.")]
    [InlineData("decade", 1995, "“1995” is not a decade. Use its first year, such as 1990.")]
    [InlineData("kind", "Episode", "“Episode” is not a kind of work. Use Movie or Series.")]
    [InlineData("page", 0, "Page must be 1 or more.")]
    [InlineData("page", -1, "Page must be 1 or more.")]
    [InlineData("pageSize", 101, "Page size must be between 1 and 100.")]
    public async Task ListRecommendations_with_a_value_the_list_doesnt_take_is_an_error_with_the_lists_sentence(string argument, object value, string error)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_anna.User);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await client.CallToolAsync(
            ListTool, new Dictionary<string, object?> { ["tab"] = "Movies", [argument] = value }, cancellationToken: cancellationToken);

        //Assert
        Assert.True(result.IsError);
        Assert.Equal(error, result.ReadText());
        Assert.Null(result.StructuredContent);
    }

    [Fact]
    public async Task DismissRecommendation_marks_the_work_for_the_caller_with_no_audit_row_and_it_leaves_their_list_only()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var token = await _tokens.Create(_anna.User, PersonalAccessTokenAccess.ReadAndDownload);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await DismissAsync(client, "Movie", "603", cancellationToken);
        var annas = await client.CallToolAsync(ListTool, new Dictionary<string, object?> { ["tab"] = "Movies" }, cancellationToken: cancellationToken);
        var bohdans = ReadList(await GetAsync($"{ListUrl}?tab=Movies", _bohdan, cancellationToken));

        //Assert
        Assert.False(result.IsError);
        Assert.Equal(new RecommendationDismissalToolResult("Movie", "603"), result.ReadStructured<RecommendationDismissalToolResult>());
        Assert.Equal("Movie 603 is marked Not interested and leaves your recommendations; the web app can undo it.", result.ReadText());
        Assert.Equal([_interstellar.Id, _arrival.Id], annas.ReadStructured<RecommendationListResponse>().Items.Select(item => item.Id));
        Assert.Equal([_matrix.Id, _interstellar.Id, _arrival.Id], bohdans.Items.Select(item => item.Id));
        await _factory.WithDbAsync(async db =>
        {
            // Hers, by her user — not by the token that asked.
            var row = await db.RecommendationDismissals.SingleAsync(cancellationToken);
            Assert.Equal((_anna.User.Id, MediaWorkKind.Movie, "603", now), (row.UserId, row.Kind, row.CatalogId, row.CreatedUtc));
            // A preference, not an action on the household's things.
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task ListRecommendations_of_a_tab_with_one_work_says_one_recommendation()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_anna.User, PersonalAccessTokenAccess.ReadOnly);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await client.CallToolAsync(ListTool, new Dictionary<string, object?> { ["tab"] = "Series" }, cancellationToken: cancellationToken);

        //Assert
        // MAM-47 (ADR 0042) — the summary's singular, a plural form of its own.
        Assert.False(result.IsError);
        Assert.Equal("1 recommendation on the Series tab in all; page 1 lists 1.", result.ReadText());
        Assert.Equal([_breakingBad.Id], result.ReadStructured<RecommendationListResponse>().Items.Select(item => item.Id));
    }

    [Fact]
    public async Task DismissRecommendation_of_a_series_names_it_a_series()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_anna.User, PersonalAccessTokenAccess.ReadAndDownload);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await DismissAsync(client, "series", "1396", cancellationToken);

        //Assert
        // MAM-47 (ADR 0042) — a summary of its own per kind, with the kind as the rules keep it.
        Assert.False(result.IsError);
        Assert.Equal(new RecommendationDismissalToolResult("Series", "1396"), result.ReadStructured<RecommendationDismissalToolResult>());
        Assert.Equal("Series 1396 is marked Not interested and leaves your recommendations; the web app can undo it.", result.ReadText());
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.RecommendationDismissals.SingleAsync(cancellationToken);
            Assert.Equal((_anna.User.Id, MediaWorkKind.Series, "1396"), (row.UserId, row.Kind, row.CatalogId));
        });
    }

    [Fact]
    public async Task DismissRecommendation_twice_is_a_success_both_times_and_keeps_one_mark()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_anna.User, PersonalAccessTokenAccess.ReadAndDownload);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var first = await DismissAsync(client, "Movie", "603", cancellationToken);
        var second = await DismissAsync(client, "Movie", "603", cancellationToken);

        //Assert
        Assert.Equal((false, false), (first.IsError ?? false, second.IsError ?? false));
        Assert.Equal(first.ReadStructured<RecommendationDismissalToolResult>(), second.ReadStructured<RecommendationDismissalToolResult>());
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(1, await db.RecommendationDismissals.CountAsync(cancellationToken));
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    [Theory]
    [InlineData("series", " 1396 ", MediaWorkKind.Series, "1396")]
    [InlineData("MOVIE", "603", MediaWorkKind.Movie, "603")]
    public async Task DismissRecommendation_reads_the_kind_in_any_letter_case_and_answers_the_mark_as_it_is_kept(
        string kind, string catalogId, MediaWorkKind keptKind, string keptCatalogId)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_anna.User, PersonalAccessTokenAccess.ReadAndDownload);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await DismissAsync(client, kind, catalogId, cancellationToken);

        //Assert
        Assert.Equal(new RecommendationDismissalToolResult(keptKind.ToString(), keptCatalogId), result.ReadStructured<RecommendationDismissalToolResult>());
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.RecommendationDismissals.SingleAsync(cancellationToken);
            Assert.Equal((keptKind, keptCatalogId), (row.Kind, row.CatalogId));
        });
    }

    [Theory]
    [InlineData("Episode", "603", "“Episode” is not a kind of work. Use Movie or Series.")]
    [InlineData("Movie", "tt0133093", "“tt0133093” is not a catalog id, which is a number such as 603.")]
    public async Task DismissRecommendation_with_a_kind_or_an_id_a_mark_doesnt_take_is_an_error_and_writes_nothing(string kind, string catalogId, string error)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_anna.User, PersonalAccessTokenAccess.ReadAndDownload);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await DismissAsync(client, kind, catalogId, cancellationToken);

        //Assert
        Assert.True(result.IsError);
        Assert.Equal(error, result.ReadText());
        Assert.Null(result.StructuredContent);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.RecommendationDismissals.CountAsync(cancellationToken));
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static ValueTask<CallToolResult> DismissAsync(McpClient client, string kind, string catalogId, CancellationToken cancellationToken) =>
        client.CallToolAsync(
            DismissTool, new Dictionary<string, object?> { ["kind"] = kind, ["catalogId"] = catalogId }, cancellationToken: cancellationToken);

    /// <summary>The body the web app reads, through the HTTP API with <paramref name="user"/>'s session.</summary>
    private async Task<string> GetAsync(string url, AuthorizedUser user, CancellationToken cancellationToken)
    {
        var response = await _factory.GetHttpRequest(url, user.AccessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static RecommendationListResponse ReadList(string body) =>
        JsonSerializer.Deserialize<RecommendationListResponse>(body, MediaAcquisitionManagerApiFactory.JsonOptions)!;
}
