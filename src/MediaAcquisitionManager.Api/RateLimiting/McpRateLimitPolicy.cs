using System.Threading.RateLimiting;
using MediaAcquisitionManager.Api.Auth;
using Microsoft.AspNetCore.RateLimiting;

namespace MediaAcquisitionManager.Api.RateLimiting;

/// <summary>
/// MAM-45 — the limit on the MCP endpoint (<c>/mcp</c>): requests a minute per personal access token. Applied with
/// <c>RequireRateLimiting(McpRateLimitPolicy.Name)</c> on the endpoint.
/// </summary>
/// <remarks>
/// <para>
/// Partitioned by the token, not by address or user. An assistant gone into a loop — or a leaked token — should run
/// into its own limit and leave the user's other assistants, and everyone behind the same home router, untouched;
/// the token is the one thing every request of that loop shares. Guessing tokens is the sign-in limit's business
/// (ADR 0040): a wrong token is challenged before this policy runs, so it never counts here.
/// </para>
/// <para>
/// In stateless mode every JSON-RPC message is a POST of its own, so connecting, listing the tools and each call all
/// count. The token comes from authentication, so this relies on <c>UseAuthentication</c> and <c>UseAuthorization</c>
/// running before <c>UseRateLimiter</c>: a session is refused and an anonymous request challenged before it gets here.
/// A fixed window with no queue, as for the other policies.
/// <c>McpEndpointHttpTests.Post_beyond_sixty_a_minute_with_one_token_is_429_with_the_mcp_sentence_while_another_token_gets_through</c>
/// pins it.
/// </para>
/// </remarks>
public static class McpRateLimitPolicy
{
    public const string Name = "mcp";

    /// <summary>
    /// Sixty requests a minute per token: a conversation's connect, tool list and a few dozen calls fit, while an
    /// assistant calling in a loop is stopped within the minute. A search asks every release source and takes its
    /// time anyway, so nobody waits on this limit in normal use.
    /// </summary>
    public const int PermitLimit = 60;

    /// <summary>The window the limit counts in. One minute, as every other policy, so "wait a minute" holds.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    /// <summary>The refusal's sentence, in place of the sign-in's "Too many attempts": an assistant's requests aren't
    /// attempts, and the sentence names the limit, so whoever reads it in the assistant's log knows what to change.</summary>
    public const string TooManyRequests = "Too many requests with this access token: at most 60 a minute. Wait a minute and try again.";

    public static void Add(RateLimiterOptions options) =>
        options.AddPolicy(Name, context => RateLimitPartition.GetFixedWindowLimiter(
            // Throws for a request with no token: only possible if the policy were put on an endpoint that lets a session
            // or an anonymous request through, a wiring mistake that should fail loudly rather than share one partition.
            context.User.GetPersonalAccessTokenId()
                ?? throw new InvalidOperationException($"The {Name} rate limit counts per personal access token; the request carries none."),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = PermitLimit,
                Window = Window,
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
}
