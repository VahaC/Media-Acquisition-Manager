using MediaAcquisitionManager.Api.Http;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace MediaAcquisitionManager.Api.Auth;

/// <summary>
/// The one 401 every refused credential gets: <c>WWW-Authenticate: Bearer</c> and
/// <c>{ "error": "Sign in to continue.", "code": "account.sign-in-required" }</c>.
/// </summary>
/// <remarks>
/// MAM-5 — written by both the session JWT's challenge (<see cref="SessionCheckingJwtBearerEvents"/>) and the personal
/// access token's (<see cref="PersonalAccessTokenAuthenticationHandler"/>), so the answer is the same bytes by
/// construction: a wrong, unknown, malformed, revoked or expired token, or one of a disabled or deleted user, can't be
/// told apart from an expired session, and a caller probing tokens learns nothing about which check failed (ADR 0040).
/// MAM-47 (ADR 0042) — the sentence is in the language the request's own <c>Accept-Language</c> asks for, never the
/// account's: so the bytes still depend on nothing but the request, whichever check failed.
/// </remarks>
public static class SignInRequiredResponse
{
    /// <summary>Writes the 401 to <paramref name="context"/>'s response.</summary>
    public static Task WriteAsync(HttpContext context)
    {
        context.Response.Headers.WWWAuthenticate = JwtBearerDefaults.AuthenticationScheme;
        return ApiErrorBody.WriteAsync(context, StatusCodes.Status401Unauthorized, SessionCheckingJwtBearerEvents.SignInRequired);
    }
}
