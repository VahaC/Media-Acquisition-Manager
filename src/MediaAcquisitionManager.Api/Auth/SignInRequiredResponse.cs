using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace MediaAcquisitionManager.Api.Auth;

/// <summary>
/// The one 401 every refused credential gets: <c>WWW-Authenticate: Bearer</c> and
/// <c>{ "error": "Sign in to continue." }</c>.
/// </summary>
/// <remarks>
/// MAM-5 — written by both the session JWT's challenge (<see cref="SessionCheckingJwtBearerEvents"/>) and the personal
/// access token's (<see cref="PersonalAccessTokenAuthenticationHandler"/>), so the answer is the same bytes by
/// construction: a wrong, unknown, malformed, revoked or expired token, or one of a disabled or deleted user, can't be
/// told apart from an expired session, and a caller probing tokens learns nothing about which check failed (ADR 0040).
/// </remarks>
public static class SignInRequiredResponse
{
    /// <summary>Writes the 401 to <paramref name="context"/>'s response.</summary>
    public static Task WriteAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = JwtBearerDefaults.AuthenticationScheme;
        return context.Response.WriteAsJsonAsync(new { error = SessionCheckingJwtBearerEvents.SignInRequired }, context.RequestAborted);
    }
}
