using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.RateLimiting;

namespace MediaAcquisitionManager.Api.Mcp;

public static class McpEndpointExtensions
{
    /// <summary>The MCP endpoint's path: what an assistant is configured with, after the app's address.</summary>
    public const string Path = "/mcp";

    /// <summary>
    /// MAM-45 — maps the MCP endpoint, <c>POST /mcp</c> (Streamable HTTP, stateless): for personal access tokens alone,
    /// each tool call judged by the token's access (<see cref="McpToolAccessFilter"/>), at most
    /// <see cref="McpRateLimitPolicy.PermitLimit"/> requests a minute per token. Map it before the SPA fallback, which
    /// would otherwise answer the paths below.
    /// </summary>
    public static WebApplication MapMediaAcquisitionManagerMcp(this WebApplication app)
    {
        app.MapMcp(Path)
            // The default policy, ADR 0040's requirement included: no header or a refused token gets the one 401.
            .RequireAuthorization()
            // Tokens alone, of either access: a session gets 403, and the tools apply the access per call.
            .WithMetadata(new PersonalAccessTokenOnlyAttribute())
            .RequireRateLimiting(McpRateLimitPolicy.Name);

        // Stateless MapMcp maps POST only; without these the SPA fallback answers GET /mcp with 200 HTML, which an MCP
        // client that opens its GET stream after initialize would read as an event stream. 405 is what the
        // specification gives a server with no stream, and both the C# and the TypeScript clients then carry on
        // without one. Anonymous, as the routing 405 it stands in for comes before authorization too.
        app.MapMethods(Path, [HttpMethods.Get, HttpMethods.Head, HttpMethods.Delete], context =>
            {
                context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                context.Response.Headers.Allow = HttpMethods.Post;
                return Task.CompletedTask;
            })
            .AllowAnonymous();

        // After a 401 an MCP client looks for OAuth metadata here (RFC 9728, then RFC 8414); the SPA fallback would answer
        // 200 HTML, which the client would try to parse. MAM has no OAuth: a token goes in the Authorization header.
        // TypedResults: inside this namespace "Results" names the tools' result records.
        app.Map("/.well-known/{**path}", () => TypedResults.NotFound()).AllowAnonymous();

        return app;
    }
}
