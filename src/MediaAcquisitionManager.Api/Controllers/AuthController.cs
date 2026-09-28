using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Http;
using MediaAcquisitionManager.Api.Mapping;
using MediaAcquisitionManager.Api.RateLimiting;
using MediaAcquisitionManager.Api.Services.Auth;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Controllers;

/// <summary>Sign-in, silent refresh, sign-out, and who is signed in.</summary>
[ApiController]
[Authorize]
[Route("api/auth")]
public class AuthController(ISessionService sessions, ApplicationDbContext db, IUserMapper mapper) : ControllerBase
{
    /// <summary>MAM-4 — the one answer to every failed sign-in, byte for byte, so the response never
    /// tells which of the user name and the password was wrong, or that the account is disabled.</summary>
    private const string InvalidCredentials = "The user name or password is incorrect.";

    private const string SessionEnded = "Your session has ended. Sign in again.";
    private const string RefreshedMomentsAgo = "This session was refreshed a moment ago. Try again.";

    [HttpPost("sign-in")]
    [AllowAnonymous]
    [EnableRateLimiting(SignInRateLimitPolicy.Name)]
    public async Task<ActionResult<SignInResponse>> SignIn([FromBody] SignInRequest request, CancellationToken cancellationToken)
    {
        var result = await sessions.SignInAsync(request.UserName, request.Password, cancellationToken);
        return result.Status switch
        {
            PasswordSignInStatus.SignedIn => SignedIn(result.Grant!),
            _ => this.UnauthorizedError(InvalidCredentials),
        };
    }

    /// <summary>
    /// Anonymous: the access token has usually expired by the time the SPA calls this, and the cookie
    /// is the credential. Not rate limited: the cookie holds 256 random bits, and guessing one at any
    /// rate is hopeless, while a limit would only sign out a household behind one address whose tabs
    /// refresh together.
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<SignInResponse>> Refresh(CancellationToken cancellationToken)
    {
        var result = await sessions.RefreshAsync(RefreshTokenCookie.Read(Request), cancellationToken);
        return result.Status switch
        {
            SessionRefreshStatus.Refreshed => SignedIn(result.Grant!),
            // The cookie stays: the browser already holds (or is receiving) the rotated one.
            SessionRefreshStatus.Conflict => this.ConflictError(RefreshedMomentsAgo),
            _ => SessionHasEnded(),
        };
    }

    /// <summary>Anonymous, so an expired access token never stops anyone from signing out; the cookie
    /// names the session. Always 204 and always drops the cookie, whatever the cookie was.</summary>
    [HttpPost("sign-out")]
    [AllowAnonymous]
    public async Task<IActionResult> SignOut(CancellationToken cancellationToken)
    {
        await sessions.SignOutAsync(RefreshTokenCookie.Read(Request), cancellationToken);
        RefreshTokenCookie.Delete(HttpContext);
        return NoContent();
    }

    [HttpGet("me")]
    public async Task<ActionResult<CurrentUserResponse>> Me(CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        // The token's session check found the user a moment ago; only a delete in between gets here,
        // and to the caller that is a session that has ended.
        if (user is null)
            return Challenge();

        var isOwner = await db.Installations.AnyAsync(i => i.OwnerUserId == userId, cancellationToken);
        return Ok(mapper.ToCurrentUser(user, isOwner));
    }

    private OkObjectResult SignedIn(SessionGrant grant)
    {
        RefreshTokenCookie.Append(HttpContext, grant.RefreshToken, grant.SessionExpiresUtc);
        return Ok(grant.Response);
    }

    private ObjectResult SessionHasEnded()
    {
        // Dropped, so the SPA's next start doesn't try the same dead cookie again.
        RefreshTokenCookie.Delete(HttpContext);
        return this.UnauthorizedError(SessionEnded);
    }
}
