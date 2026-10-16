using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Services.PersonalAccessTokens;
using MediaAcquisitionManager.Core.Enums;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MediaAcquisitionManager.Api.Mcp;

/// <summary>
/// MAM-45 — ADR 0040's rule, per MCP tool: the access gate on <c>tools/list</c> and <c>tools/call</c>. A tool is a read
/// when its <c>[McpServerTool(ReadOnly = true)]</c> says so, open to every token; a write when its method carries
/// <see cref="PersonalAccessTokenWriteAttribute"/> — the HTTP API's own mark — open to Read and download only; a tool
/// that is neither is no token's (deny by default), as an endpoint without a mark is.
/// </summary>
/// <remarks>
/// <para>
/// Own filters rather than the SDK's <c>AddAuthorizationFilters()</c>: its policy check hands the handler an MCP
/// <c>RequestContext</c>, never the <see cref="HttpContext"/> <see cref="PersonalAccessTokenAccessHandler"/> reads the
/// endpoint from, so the default policy would refuse every token on every tool.
/// </para>
/// <para>
/// The list leaves out what the token can't call, so an assistant isn't offered a tool it would be refused. A call of
/// one anyway — a client that cached the list, or a model that guessed the name — is answered with the HTTP API's
/// sentence as an <c>isError</c> result, and recorded as the HTTP API records a refusal
/// (<see cref="IPersonalAccessTokenRefusals"/>: a warning naming <c>tools/call &lt;tool&gt;</c>, one audit row a minute).
/// A name no tool has goes on to the SDK, which answers "Unknown tool".
/// </para>
/// <para>
/// Writes win over reads: a tool marked both is gated as a write. <c>McpToolListHttpTests</c> refuses such a tool
/// anyway, and pins which tools are which.
/// </para>
/// </remarks>
public static class McpToolAccessFilter
{
    /// <summary>The <c>method</c> a refusal is recorded under; the tool's name is its path.</summary>
    public const string CallMethod = "tools/call";

    /// <summary>Drops from the list every tool the caller's token can't call.</summary>
    public static McpRequestHandler<ListToolsRequestParams, ListToolsResult> ListTools(
        McpRequestHandler<ListToolsRequestParams, ListToolsResult> next) =>
        async (context, cancellationToken) =>
        {
            var result = await next(context, cancellationToken);
            var tools = context.Server.ServerOptions.ToolCollection;
            var access = context.User?.GetPersonalAccessTokenAccess();
            result.Tools =
            [
                .. result.Tools.Where(tool =>
                    tools is not null && tools.TryGetPrimitive(tool.Name, out var serverTool) && ReasonToRefuse(serverTool, access) is null),
            ];
            return result;
        };

    /// <summary>Lets a call through to the tool the caller's token may call; refuses, records and answers the sentence
    /// otherwise. An unknown tool goes on to the SDK.</summary>
    public static McpRequestHandler<CallToolRequestParams, CallToolResult> CallTool(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        async (context, cancellationToken) =>
        {
            if (context.MatchedPrimitive is not McpServerTool tool
                || ReasonToRefuse(tool, context.User?.GetPersonalAccessTokenAccess()) is not { } reason)
                return await next(context, cancellationToken);

            // Always a token here: /mcp refuses a session and challenges an anonymous request before any tool is called.
            if (context.User?.GetPersonalAccessTokenId() is { } tokenId && context.Services is { } services)
            {
                await services.GetRequiredService<IPersonalAccessTokenRefusals>().RecordAsync(
                    tokenId, context.User.GetUserId(), reason, CallMethod, tool.ProtocolTool.Name, cancellationToken);
            }

            return McpToolResults.Error(PersonalAccessTokenFailureReason.SentenceOf(reason));
        };

    /// <summary>Why a token of <paramref name="access"/> may not call <paramref name="tool"/>; <c>null</c> when it may.
    /// An access that names nothing counts as the least, as for the HTTP API.</summary>
    private static PersonalAccessTokenRefusalReason? ReasonToRefuse(McpServerTool tool, PersonalAccessTokenAccess? access)
    {
        if (tool.Metadata.OfType<PersonalAccessTokenWriteAttribute>().Any())
            return access == PersonalAccessTokenAccess.ReadAndDownload ? null : PersonalAccessTokenRefusalReason.ReadOnly;
        if (tool.Metadata.OfType<McpServerToolAttribute>().Any(attribute => attribute.ReadOnly))
            return null;
        return PersonalAccessTokenRefusalReason.NotForTokens;
    }
}
