using System.Globalization;
using System.Threading.RateLimiting;
using MediaAcquisitionManager.Api.RateLimiting;

namespace MediaAcquisitionManager.Api.Extensions;

public static class RateLimitingServiceCollectionExtensions
{
    private const string TooManyAttempts = "Too many attempts. Wait a minute and try again.";

    /// <summary>
    /// The rate-limit policies endpoints opt into, and the answer to a refused request:
    /// 429 <c>{ "error": "…" }</c> with <c>Retry-After</c>.
    /// </summary>
    public static IServiceCollection AddMediaAcquisitionManagerRateLimiting(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
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
