using MediaAcquisitionManager.Core.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace MediaAcquisitionManager.Api.Auth;

/// <summary>
/// How access tokens are made and checked: one place for the issuer, audience, lifetime, key and
/// algorithm, so the token service and the bearer handler can't drift apart.
/// </summary>
public static class JwtAccessTokens
{
    /// <summary>Who issues the tokens, and who they are for: this app, on both ends.</summary>
    public const string Issuer = "media-acquisition-manager";

    /// <inheritdoc cref="Issuer"/>
    public const string Audience = "media-acquisition-manager";

    /// <summary>
    /// MAM-4 — 15 minutes. The token is replayable by whoever holds it until it expires, so it should
    /// be short; the SPA refreshes it silently through the refresh cookie, so short costs the user
    /// nothing. (Sign-out, disable and delete don't wait for it: every request checks its session.)
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Tolerance for a clock that differs between issuing and checking. Tokens are issued and
    /// checked by the same process, so this only has to absorb rounding (a JWT's times are whole
    /// seconds); well under the framework's 5-minute default, which would stretch a 15-minute token
    /// by a third.
    /// </summary>
    public static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    /// <summary>
    /// MAM-4 — HS256 needs a key at least as long as its 256-bit hash. IdentityModel refuses a
    /// shorter one too, but only at the first sign-in, with a message (IDX10720) that says nothing
    /// about which setting to fix.
    /// </summary>
    private const int MinimumKeyBytes = 32;

    /// <summary>The HMAC key from <see cref="JwtOptions.Secret"/>.</summary>
    /// <exception cref="InvalidOperationException">The secret is missing, isn't base64, or is shorter
    /// than 32 bytes.</exception>
    public static SymmetricSecurityKey CreateSigningKey(JwtOptions options)
    {
        // Normally unreachable: the first start generates the secret when nothing configures it.
        // Worded for the operator who set MAM_Jwt__Secret by hand and got it wrong.
        if (string.IsNullOrWhiteSpace(options.Secret))
            throw new InvalidOperationException(
                "Jwt:Secret has no value, so access tokens can't be signed. Leave MAM_Jwt__Secret unset to let the app generate one, or set it to base64 of 64 random bytes.");

        byte[] key;
        try
        {
            key = Convert.FromBase64String(options.Secret);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                "Jwt:Secret is not valid base64, so access tokens can't be signed. Set MAM_Jwt__Secret to base64 of 64 random bytes.",
                exception);
        }

        if (key.Length < MinimumKeyBytes)
            throw new InvalidOperationException(
                "Jwt:Secret is shorter than 32 bytes, too short to sign HS256 tokens. Set MAM_Jwt__Secret to base64 of 64 random bytes, or leave it unset to let the app generate one.");

        return new SymmetricSecurityKey(key);
    }

    /// <summary>
    /// What the bearer handler checks: this app's issuer and audience, an HS256 signature with this
    /// installation's key, and a lifetime measured on <paramref name="time"/>.
    /// </summary>
    public static TokenValidationParameters CreateValidationParameters(JwtOptions options, TimeProvider time) => new()
    {
        ValidIssuer = Issuer,
        ValidAudience = Audience,
        IssuerSigningKey = CreateSigningKey(options),
        // Pinned, so a token can never pick its own algorithm; this app only ever signs with HS256.
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        // MAM-4 — the injected clock, not the system one. Tokens are stamped with TimeProvider when
        // issued, so they must be judged by it too; and it is what lets a test age a token out by
        // advancing the test clock instead of waiting 15 minutes.
        LifetimeValidator = (notBefore, expires, _, _) => IsWithinLifetime(notBefore, expires, time.GetUtcNow().UtcDateTime),
        // User.Identity.Name is the user name, as the token names it.
        NameClaimType = JwtRegisteredClaimNames.Name,
    };

    // No expiry means no lifetime at all: refused, like the framework's own check does.
    private static bool IsWithinLifetime(DateTime? notBefore, DateTime? expires, DateTime nowUtc) =>
        expires is { } expiresUtc
        && nowUtc < expiresUtc + ClockSkew
        && (notBefore is not { } notBeforeUtc || nowUtc >= notBeforeUtc - ClockSkew);
}
