using System.Globalization;
using System.Threading.RateLimiting;
using MediaAcquisitionManager.Api.Http;
using MediaAcquisitionManager.Api.RateLimiting;
using MediaAcquisitionManager.Core.Texts;
using Microsoft.AspNetCore.RateLimiting;

namespace MediaAcquisitionManager.Api.Extensions;

public static class RateLimitingServiceCollectionExtensions
{
    /// <summary>The message of every refusal over a limit. MAM-5 — public, as the personal access token handler answers
    /// with it too while an address's sign-in attempts are used up.</summary>
    public static readonly Message TooManyAttempts = Message.Of("account.too-many-attempts");

    /// <summary>
    /// The rate-limit policies endpoints opt into, and the answer to a refused request:
    /// 429 <c>{ "error": "…", "code": "…" }</c> with <c>Retry-After</c>.
    /// </summary>
    public static IServiceCollection AddMediaAcquisitionManagerRateLimiting(this IServiceCollection services)
    {
        // MAM-5 — one per process: the sign-in attempts per address, which the sign-in policy's partitions and the
        // personal access token handler both count against.
        services.AddSingleton<SignInAttemptLimiter>();
        return services.AddRateLimiter(options =>
        {
            SignInRateLimitPolicy.Add(options);
            PasswordChangeRateLimitPolicy.Add(options);
            McpRateLimitPolicy.Add(options);

            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            // The default rejection is an empty 503, which the SPA can only show as "the server is
            // down". The sentence's "wait a minute" holds for every policy there is, as each counts in a
            // one-minute window; a policy with another window will need its own. MAM-45 — the MCP endpoint's
            // has its own wording all the same: an assistant's requests aren't attempts. Retry-After comes
            // from the limiter itself — for a fixed window, one whole window: never earlier than the next
            // attempt can succeed.
            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                var policy = context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
                var message = policy == McpRateLimitPolicy.Name ? McpRateLimitPolicy.TooManyRequests : TooManyAttempts;
                // MAM-47 (ADR 0042) — through the one writer of error bodies, in the request's own language: the 429 of a
                // guessed password or token depends on nothing but the request, as the 401 does.
                await ApiErrorBody.WriteAsync(context.HttpContext, StatusCodes.Status429TooManyRequests, message);
            };
        });
    }
}
