using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace MediaAcquisitionManager.Api.RateLimiting;

/// <summary>
/// The limit on the anonymous endpoints that take a password: sign-in and the first-run setup.
/// Applied with <c>[EnableRateLimiting(SignInRateLimitPolicy.Name)]</c>. A signed-in user changing
/// their own password is counted per user instead, by <see cref="PasswordChangeRateLimitPolicy"/>.
/// </summary>
/// <remarks>
/// MAM-4 — partitioned by the connection's remote address, never by a header the client sends
/// (<c>X-Forwarded-For</c> is whatever the attacker writes, a new partition per request). A fixed
/// window with no queue: an attempt over the limit is refused at once rather than held open.
/// </remarks>
public static class SignInRateLimitPolicy
{
    public const string Name = "sign-in";

    /// <summary>
    /// Ten attempts a minute from one address: more than a person mistyping a password ever needs,
    /// and it holds a guessing run to 14,400 attempts a day, each costing the server one PBKDF2 run.
    /// Per address rather than per user name: a per-name limit would let anyone lock a user out by
    /// failing on purpose, and wouldn't slow a run that tries one password against many names.
    /// </summary>
    public const int PermitLimit = 10;

    /// <summary>The window the limit counts in; the rejection tells the user to wait this long.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    public static void Add(RateLimiterOptions options) =>
        options.AddPolicy(Name, context => RateLimitPartition.GetFixedWindowLimiter(
            // Kestrel always knows the peer; only an in-memory test server leaves it empty, and
            // then every request shares one partition.
            context.Connection.RemoteIpAddress?.ToString() ?? "",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = PermitLimit,
                Window = Window,
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
}
