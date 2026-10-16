using System.Reflection;
using System.Text.Json;
using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using ModelContextProtocol.Server;

namespace MediaAcquisitionManager.Api.Tests.Mcp;

// MAM-45 — which tools a token is offered (ADR 0040, per tool): the nine reads to any token, the three writes to Read and
// download alone, owner and member alike; each read and write marked as such for the client, send_download asking the
// user every time (Vasyl's decision), every tool with an output schema and every parameter described. The lists are
// fixed here and found again by reflection over the app's tool classes, so a tool added later fails until it is listed
// on purpose — the way PersonalAccessTokenWritesHttpTests keeps the HTTP API's list.
public sealed class McpToolListHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    /// <summary>Every token's tools.</summary>
    private static readonly string[] Reads =
    [
        "get_status",
        "search_library",
        "search_releases",
        "list_search_releases",
        "get_download_options",
        "list_jobs",
        "get_job",
        "list_duplicates",
        "list_recommendations",
    ];

    /// <summary>The three writes Read and download opens, the HTTP API's three.</summary>
    private static readonly string[] Writes = ["send_download", "cancel_job", "dismiss_recommendation"];

    /// <summary>
    /// What each tool tells the client about itself: read only, destructive, idempotent, open world. Only the searches of
    /// the release sources, the overwrite check and the send reach beyond MAM's own data.
    /// </summary>
    private static readonly Dictionary<string, (bool ReadOnly, bool Destructive, bool Idempotent, bool OpenWorld)> Annotations = new()
    {
        ["get_status"] = (true, false, true, false),
        ["search_library"] = (true, false, true, false),
        ["search_releases"] = (true, false, true, true),
        ["list_search_releases"] = (true, false, true, false),
        ["get_download_options"] = (true, false, true, true),
        ["list_jobs"] = (true, false, true, false),
        ["get_job"] = (true, false, true, false),
        ["list_duplicates"] = (true, false, true, false),
        ["list_recommendations"] = (true, false, true, false),
        ["send_download"] = (false, false, false, true),
        ["cancel_job"] = (false, false, true, false),
        ["dismiss_recommendation"] = (false, false, true, false),
    };

    private const string RequiresUserInteraction = "anthropic/requiresUserInteraction";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IPersonalAccessTokenSeeder _tokens = null!;
    private UserEntity _owner = null!;
    private UserEntity _member = null!;

    public McpToolListHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var users = new UserSeeder(_factory.Scope);
        _tokens = new PersonalAccessTokenSeeder(_factory.Scope);
        _owner = await users.CreateOwner("owner");
        _member = await users.Create("member");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task List_with_a_read_only_token_is_exactly_the_nine_reads_for_the_owner_and_a_member(bool ofTheOwner)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(ofTheOwner ? _owner : _member, PersonalAccessTokenAccess.ReadOnly);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);

        //Assert
        Assert.Equal(Reads.Order(StringComparer.Ordinal), tools.Select(tool => tool.Name).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task List_with_a_read_and_download_token_is_exactly_the_twelve_tools_for_the_owner_and_a_member(bool ofTheOwner)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(ofTheOwner ? _owner : _member, PersonalAccessTokenAccess.ReadAndDownload);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);

        //Assert
        Assert.Equal(Reads.Concat(Writes).Order(StringComparer.Ordinal), tools.Select(tool => tool.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task List_marks_each_tool_read_only_or_not_with_its_title_and_every_hint_set()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_member, PersonalAccessTokenAccess.ReadAndDownload);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);

        //Assert
        Assert.All(tools, tool =>
        {
            var annotations = tool.ProtocolTool.Annotations;
            Assert.NotNull(annotations);
            Assert.Equal(
                (tool.Name, (bool?)Annotations[tool.Name].ReadOnly, (bool?)Annotations[tool.Name].Destructive, (bool?)Annotations[tool.Name].Idempotent, (bool?)Annotations[tool.Name].OpenWorld),
                (tool.Name, annotations.ReadOnlyHint, annotations.DestructiveHint, annotations.IdempotentHint, annotations.OpenWorldHint));
            Assert.False(string.IsNullOrWhiteSpace(tool.Title), $"{tool.Name} has no title.");
            Assert.Equal(tool.Title, annotations.Title);
        });
    }

    [Fact]
    public async Task List_asks_for_the_users_interaction_on_send_download_and_no_other_tool()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_member, PersonalAccessTokenAccess.ReadAndDownload);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);

        //Assert
        // Claude Code then asks before every send, even with mam's tools allowed "always".
        var asking = tools.Where(tool => tool.ProtocolTool.Meta?[RequiresUserInteraction] is not null).ToList();
        var send = Assert.Single(asking);
        Assert.Equal("send_download", send.Name);
        Assert.True(send.ProtocolTool.Meta![RequiresUserInteraction]!.GetValue<bool>());
    }

    [Fact]
    public async Task List_gives_every_tool_a_description_an_object_output_schema_and_a_description_of_every_parameter_but_no_caller()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_member, PersonalAccessTokenAccess.ReadAndDownload);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);

        //Assert
        Assert.All(tools, tool =>
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.Description), $"{tool.Name} has no description.");
            // An object, as a 2025-11-25 client requires, so the structured content is checked as it is, unwrapped.
            Assert.True(tool.ProtocolTool.OutputSchema is { } schema && schema.GetProperty("type").GetString() == "object", $"{tool.Name} has no object output schema.");
            // The caller is bound from the request, never an argument (McpToolRegistration says why this is checked).
            Assert.False(tool.ProtocolTool.InputSchema.GetProperty("properties").TryGetProperty("user", out _), $"{tool.Name} asks for the caller.");
            foreach (var parameter in tool.ProtocolTool.InputSchema.GetProperty("properties").EnumerateObject())
            {
                Assert.True(
                    parameter.Value.TryGetProperty("description", out var description) && description.ValueKind == JsonValueKind.String && description.GetString()!.Length > 0,
                    $"{tool.Name}'s {parameter.Name} has no description.");
            }
        });
    }

    [Fact]
    public async Task List_describes_every_property_of_every_schema_as_an_object_and_a_time_as_a_date_time_string()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_member, PersonalAccessTokenAccess.ReadAndDownload);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);

        //Assert
        // The MCP TypeScript SDK (Claude Code, mcp-remote) refuses the whole tools/list answer when a property's schema is
        // `true` instead of an object — what the exporter writes for a DateTime, whose converter it can't see through.
        var times = new List<JsonElement>();
        Assert.All(tools, tool =>
        {
            Assert.Empty(PropertiesNotDescribedAsObjects(tool.ProtocolTool.InputSchema, $"{tool.Name}.input", times));
            Assert.Empty(PropertiesNotDescribedAsObjects(tool.ProtocolTool.OutputSchema!.Value, $"{tool.Name}.output", times));
        });
        // Every instant is a *Utc DateTime (the house rule): a date-time string, or null where it may be.
        Assert.NotEmpty(times);
        Assert.All(times, time => Assert.Equal("date-time", time.GetProperty("format").GetString()));
    }

    [Fact]
    public void Discovery_finds_exactly_the_twelve_tools_each_a_read_or_one_of_the_three_writes_and_never_both()
    {
        //Arrange
        var tools = typeof(Program).Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Select(method => (Method: method, Tool: method.GetCustomAttribute<McpServerToolAttribute>()))
            .Where(found => found.Tool is not null)
            .ToList();

        //Act
        var reads = tools.Where(found => found.Tool!.ReadOnly).Select(found => found.Tool!.Name).ToList();
        var writes = tools.Where(found => found.Method.GetCustomAttribute<PersonalAccessTokenWriteAttribute>() is not null).Select(found => found.Tool!.Name).ToList();

        //Assert
        // Guards the guard: the scan found the app's tools at all.
        Assert.Equal(12, tools.Count);
        Assert.Equal(Reads.Order(StringComparer.Ordinal), reads.Order(StringComparer.Ordinal));
        Assert.Equal(Writes.Order(StringComparer.Ordinal), writes.Order(StringComparer.Ordinal));
        // Never both, never neither: a tool that is neither is no token's, and one that is both says two things.
        Assert.Empty(reads.Intersect(writes, StringComparer.Ordinal));
        Assert.All(tools, found => Assert.False(string.IsNullOrEmpty(found.Tool!.Name), $"{found.Method.Name} has no explicit name."));
    }

    // ── helpers ──

    /// <summary>The path of every property, anywhere in <paramref name="schema"/>, whose schema isn't an object; the schema
    /// of every property named <c>…Utc</c> goes to <paramref name="times"/> on the way.</summary>
    private static List<string> PropertiesNotDescribedAsObjects(JsonElement schema, string path, List<JsonElement> times)
    {
        var refused = new List<string>();
        Walk(schema, path);
        return refused;

        void Walk(JsonElement node, string at)
        {
            if (node.ValueKind == JsonValueKind.Array)
            {
                foreach (var (item, index) in node.EnumerateArray().Select((item, index) => (item, index)))
                    Walk(item, $"{at}[{index}]");
                return;
            }

            if (node.ValueKind != JsonValueKind.Object)
                return;

            foreach (var member in node.EnumerateObject())
            {
                if (member.Name == "properties" && member.Value.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in member.Value.EnumerateObject())
                    {
                        if (property.Value.ValueKind != JsonValueKind.Object)
                            refused.Add($"{at}.{property.Name}");
                        else if (property.Name.EndsWith("Utc", StringComparison.Ordinal))
                            times.Add(property.Value);
                    }
                }

                Walk(member.Value, $"{at}.{member.Name}");
            }
        }
    }
}
