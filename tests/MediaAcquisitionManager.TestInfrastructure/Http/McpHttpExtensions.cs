using System.Text;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace MediaAcquisitionManager.TestInfrastructure.Http;

/// <summary>
/// MAM-45 — the MCP endpoint the way an assistant uses it: the SDK's own client over the test server (Streamable HTTP,
/// a personal access token in the <c>Authorization</c> header), and the reads of a tool's answer.
/// </summary>
public static class McpHttpExtensions
{
    /// <summary>The MCP endpoint's path.</summary>
    public const string McpUrl = "/mcp";

    /// <summary>
    /// The protocol revision Claude Code (2.1.220) and mcp-remote speak: the <c>initialize</c> handshake. Every test
    /// connects with it unless it says otherwise; <c>null</c> is the SDK client's default, 2026-07-28's per-request
    /// <c>server/discover</c>.
    /// </summary>
    public const string ClaudeCodeProtocolVersion = "2025-11-25";

    /// <summary>
    /// Connects an MCP client to the factory's server with <paramref name="token"/> as the bearer, speaking
    /// <paramref name="protocolVersion"/> (<see cref="ClaudeCodeProtocolVersion"/> by default; <c>null</c> for the SDK
    /// client's latest). The caller disposes the client.
    /// </summary>
    /// <remarks>
    /// The token rides on the transport, not on a shared client, so nothing carries over from one test to the next — the
    /// factory's own rule. Streamable HTTP only, not AutoDetect: a refused connect then throws the server's own 401 or
    /// 403, not the SSE fallback's.
    /// </remarks>
    public static async Task<McpClient> ConnectMcpAsync(
        this MediaAcquisitionManagerApiFactory factory,
        string token,
        string? protocolVersion = ClaudeCodeProtocolVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);

        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(factory.Server.BaseAddress, McpUrl),
                TransportMode = HttpTransportMode.StreamableHttp,
                AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + token },
            },
            new HttpClient(factory.Server.CreateHandler()) { BaseAddress = factory.Server.BaseAddress },
            ownsHttpClient: true);
        return await McpClient.CreateAsync(
            transport, new McpClientOptions { ProtocolVersion = protocolVersion }, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Posts one raw JSON-RPC message to <c>/mcp</c> as a client would — both media types accepted, JSON content — with
    /// <paramref name="token"/> as the bearer (none = anonymous), for the tests about the HTTP answer itself: the 401,
    /// the 403, the 429.
    /// </summary>
    public static Task<HttpResponseMessage> PostMcpMessageAsync(
        this MediaAcquisitionManagerApiFactory factory, string? token, string message, string url = McpUrl)
    {
        ArgumentNullException.ThrowIfNull(factory);

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(message, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
        if (token is not null)
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        return factory.SendHttpRequest(request);
    }

    /// <summary>
    /// A tool's structured content read as <typeparamref name="T"/> with the factory's JSON options, which mirror the
    /// API's: the same names, enums and instants a script reads from the HTTP API.
    /// </summary>
    /// <exception cref="InvalidOperationException">The result has none — an error, whose sentence the message quotes, so a
    /// failing test says what the tool answered.</exception>
    public static T ReadStructured<T>(this CallToolResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.StructuredContent is not { } structured)
            throw new InvalidOperationException($"The tool answered no structured content (isError {result.IsError}): {result.ReadText()}");
        return structured.Deserialize<T>(MediaAcquisitionManagerApiFactory.JsonOptions)
            ?? throw new InvalidOperationException("The tool's structured content is null.");
    }

    /// <summary>
    /// The first text block of a tool's answer: a success's one-sentence summary, or an error's sentence. A success's
    /// second block holds the same JSON as its structured content.
    /// </summary>
    public static string ReadText(this CallToolResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text
            ?? throw new InvalidOperationException("The tool answered no text.");
    }
}
