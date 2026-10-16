using System.Globalization;
using System.Threading.RateLimiting;
using MediaAcquisitionManager.Api.RateLimiting;

namespace MediaAcquisitionManager.Api.Extensions;

public static class RateLimitingServiceCollectionExtensions
{
    /// <summary>The sentence of every refusal over a limit. MAM-5 — public, as the personal access token handler answers
    /// with it too while an address's sign-in attempts are used up.</summary>
    public const string TooManyAttempts = "Too many attempts. Wait a minute and try again.";

    /// <summary>
    /// The rate-limit policies endpoints opt into, and the answer to a refused request:
    /// 429 <c>{ "error": "…" }</c> with <c>Retry-After</c>.
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

            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            // The default rejection is an empty 503, which the SPA can only show as "the server is
            // down". The sentence holds for every policy there is, as each counts in a one-minute
            // window; a policy with another window will need its own. Retry-After comes from the
            // limiter itself — for a fixed window, one whole window: never earlier than the next
            // attempt can succeed.
            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                await context.HttpContext.Response.WriteAsJsonAsync(new { error = TooManyAttempts }, cancellationToken);
            };
        });
    }
}
