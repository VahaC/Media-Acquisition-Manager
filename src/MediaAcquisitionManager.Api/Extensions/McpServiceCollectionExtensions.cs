using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Mcp;
using ModelContextProtocol.AspNetCore;

namespace MediaAcquisitionManager.Api.Extensions;

public static class McpServiceCollectionExtensions
{
    /// <summary>MAM-45 — the MCP server an AI assistant connects to: its identity, the instructions for the calling
    /// token, the access gate and the argument filter, and every tool of this assembly's <c>[McpServerToolType]</c>
    /// classes. The endpoint itself is mapped by <see cref="McpEndpointExtensions.MapMediaAcquisitionManagerMcp"/>.</summary>
    public static IServiceCollection AddMcpFeatures(this IServiceCollection services)
    {
        // One instance for this host, shared by the registration and the tools' results; never a static one (McpJson).
        var json = McpJson.CreateOptions();
        // Singleton: it holds nothing but the options, and every tool call of every request answers through it.
        services.AddSingleton(new McpToolResults(json));

        services.AddMcpServer(options => options.ServerInfo = McpServerIdentity.Create())
            .WithHttpTransport(options =>
            {
                // Stateless, the 2.2.0 default, set so it doesn't change under us: each POST is served on its own, with
                // the request's own scope, user and DbContext, and nothing is kept between requests — no session for a
                // revoked token to outlive, and no GET stream or server-to-client request MAM would need.
                options.SessionMode = HttpServerSessionMode.Stateless;
                // Per POST in stateless mode: the instructions say what THIS token may do (the initialize, or the
                // server/discover, answer carries them).
                options.ConfigureSessionOptions = (httpContext, serverOptions, cancellationToken) =>
                {
                    serverOptions.ServerInstructions = McpInstructions.For(httpContext.User.GetPersonalAccessTokenAccess());
                    return Task.CompletedTask;
                };
            })
            .WithRequestFilters(filters =>
            {
                // The first filter registered is the outermost: the argument filter wraps the gate and the tool.
                filters.AddCallToolFilter(McpArgumentErrorFilter.CallTool);
                filters.AddListToolsFilter(McpToolAccessFilter.ListTools);
                filters.AddCallToolFilter(McpToolAccessFilter.CallTool);
            })
            // Not the SDK's WithToolsFromAssembly: see McpToolRegistration for the schema it can get wrong.
            .WithMediaAcquisitionManagerTools(json);

        return services;
    }
}
