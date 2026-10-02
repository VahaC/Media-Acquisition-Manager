using System.Threading.RateLimiting;
using MediaAcquisitionManager.Api.Auth;
using Microsoft.AspNetCore.RateLimiting;

namespace MediaAcquisitionManager.Api.RateLimiting;

/// <summary>
/// The limit on a signed-in user changing their own password (MAM-16). Applied with
/// <c>[EnableRateLimiting(PasswordChangeRateLimitPolicy.Name)]</c>.
/// </summary>
/// <remarks>
/// <para>
/// Partitioned by the signed-in user, not by address as <see cref="SignInRateLimitPolicy"/> is. The
/// threat here is someone holding a user's access token (a tab left open, a copied token) guessing
/// the current password to take the account over: a per-address limit would let them spread the
/// guesses over many addresses, while the user is the one thing every guess shares. And a user
/// mistyping their current password must not use up the sign-in attempts of everyone behind the same
/// home router. Locking a user out by failing on purpose, the reason sign-in can't count per user,
/// needs that user's own token here.
/// </para>
/// <para>
/// The user comes from the access token, so this policy relies on <c>UseAuthentication</c> running
/// before <c>UseRateLimiter</c>; and on <c>UseAuthorization</c> too, which challenges an anonymous
/// request before it gets here. A fixed window with no queue, as for sign-in.
/// <c>AuthPasswordHttpTests.ChangePassword_is_rate_limited_per_user_whatever_the_client_address</c>
/// and <c>AuthPasswordHttpTests.ChangePassword_without_a_token_is_401_with_the_sign_in_sentence</c>
/// pin the two.
/// </para>
/// </remarks>
public static class PasswordChangeRateLimitPolicy
{
    public const string Name = "password-change";

    /// <summary>
    /// Ten attempts a minute per user, the same as sign-in allows an address: more than a person
    /// mistyping their current password ever needs, and each attempt costs the server one PBKDF2 run.
    /// </summary>
    public const int PermitLimit = 10;

    /// <summary>The window the limit counts in. One minute, as for sign-in, so the rejection's "wait a
    /// minute" holds for both.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    public static void Add(RateLimiterOptions options) =>
        options.AddPolicy(Name, context => RateLimitPartition.GetFixedWindowLimiter(
            // Throws for a request with no user: only possible if the policy were put on an anonymous
            // endpoint, a wiring mistake that should fail loudly rather than share one partition.
            context.User.GetUserId(),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = PermitLimit,
                Window = Window,
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
}
