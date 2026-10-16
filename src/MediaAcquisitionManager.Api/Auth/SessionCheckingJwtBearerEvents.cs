using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;

namespace MediaAcquisitionManager.Api.Auth;

/// <summary>
/// The bearer handler's events: every access token is checked against its session, and a missing or
/// refused token answers 401 <c>{ "error": "Sign in to continue." }</c>.
/// </summary>
/// <remarks>
/// <para>
/// MAM-4 — a valid signature and lifetime are not enough: the token's session (<c>sid</c>) must still
/// exist for its user (<c>sub</c>), not be revoked or expired, and the user must not be disabled.
/// Without this, signing out, disabling or deleting a user, or resetting their password would take
/// effect only when their last access token expired, up to 15 minutes later — long enough for
/// someone who was just locked out to keep going. The price is one indexed lookup by primary key per
/// authenticated request, which SQLite answers from a page already in cache.
/// ADR 0002 — records this decision and the options it rejected.
/// </para>
/// <para>
/// Scoped and resolved per request (<c>JwtBearerOptions.EventsType</c>), so it gets the request's
/// own DbContext.
/// </para>
/// </remarks>
public sealed class SessionCheckingJwtBearerEvents(ApplicationDbContext db, TimeProvider time) : JwtBearerEvents
{
    public const string SignInRequired = "Sign in to continue.";

    public override async Task TokenValidated(TokenValidatedContext context)
    {
        var principal = context.Principal;
        if (!Guid.TryParse(principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var userId)
            || !Guid.TryParse(principal?.FindFirst(JwtRegisteredClaimNames.Sid)?.Value, out var sessionId))
        {
            context.Fail("The access token names no user or session.");
            return;
        }

        var now = time.GetUtcNow().UtcDateTime;
        var sessionIsActive = await db.UserSessions.AnyAsync(
            session => session.Id == sessionId
                && session.UserId == userId
                && session.RevokedUtc == null
                && session.ExpiresUtc > now
                && db.Users.Any(user => user.Id == userId && !user.IsDisabled),
            context.HttpContext.RequestAborted);

        if (!sessionIsActive)
            context.Fail("The session behind this access token has ended.");
    }

    public override async Task Challenge(JwtBearerChallengeContext context)
    {
        // The default challenge is an empty 401 whose WWW-Authenticate header explains why (expired,
        // bad signature). The SPA needs a sentence in the shape every other failure has, and a
        // caller probing tokens gets no hint which check failed. MAM-5 — written by the one writer
        // the personal access token's challenge uses too, so both 401s are the same bytes.
        context.HandleResponse();
        await SignInRequiredResponse.WriteAsync(context.HttpContext);
    }
}
