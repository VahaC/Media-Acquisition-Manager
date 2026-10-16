using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Http;
using MediaAcquisitionManager.Api.Mapping;
using MediaAcquisitionManager.Api.RateLimiting;
using MediaAcquisitionManager.Api.Services.Accounts;
using MediaAcquisitionManager.Api.Services.Auth;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Controllers;

/// <summary>Sign-in, silent refresh, sign-out, who is signed in, and a user changing their own
/// password.</summary>
[ApiController]
[Authorize]
[Route("api/auth")]
public class AuthController(
    ISessionService sessions,
    IUserAccountService accounts,
    ApplicationDbContext db,
    IUserMapper mapper) : ControllerBase
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

    /// <summary>MAM-16 — the signed-in user, with when the account was created, when it last signed
    /// in and when this session ends, for the Profile page. The web app can't read the session's end
    /// from the refresh cookie, which is <c>HttpOnly</c>. MAM-5 — refused to personal access tokens, a GET though it is:
    /// it describes the account's session, which a token has none of (ADR 0040).</summary>
    [HttpGet("me")]
    [DenyPersonalAccessToken]
    public async Task<ActionResult<CurrentUserProfileResponse>> Me(CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        var sessionId = User.GetSessionId();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        // The session the token names (sid), the one the session check has just let this request in on.
        var sessionExpiresUtc = await db.UserSessions
            .Where(s => s.Id == sessionId && s.UserId == userId)
            .Select(s => (DateTime?)s.ExpiresUtc)
            .SingleOrDefaultAsync(cancellationToken);
        // The token's session check found both a moment ago; only a delete in between gets here (the
        // sessions go with the user), and to the caller that is a session that has ended.
        if (user is null || sessionExpiresUtc is not { } expiresUtc)
            return Challenge();

        var isOwner = await db.Installations.AnyAsync(i => i.OwnerUserId == userId, cancellationToken);
        return Ok(mapper.ToProfile(user, isOwner, expiresUtc));
    }

    /// <summary>
    /// MAM-16 — any signed-in user sets a new password for their own account, giving the current one.
    /// Every other session of theirs ends at once and this one stays (ADR 0016). Rate limited per user.
    /// </summary>
    /// <remarks>
    /// A wrong current password is a 400 with its sentence, never a 401: the web app reads a 401 as an
    /// expired token, and would refresh and send the change again. The session kept is the token's
    /// (<c>sid</c>). The refresh cookie the browser also sends to /api/auth is never read, so the
    /// bearer header, which another site can't make a browser send, is the only credential here.
    /// </remarks>
    [HttpPost("password")]
    [EnableRateLimiting(PasswordChangeRateLimitPolicy.Name)]
    // MAM-5 — the account's password is never a personal access token's (ADR 0040). On the actions, not the class:
    // sign-in, refresh and sign-out are anonymous and read no token.
    [DenyPersonalAccessToken]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        // A missing field is a blank one: the checks answer both with the same sentence.
        var caller = new AccountCaller(User.GetUserId(), User.GetSessionId());
        var result = await accounts.ChangeOwnPasswordAsync(caller, request.CurrentPassword ?? "", request.NewPassword ?? "", cancellationToken);
        return result.Status switch
        {
            UserAccountStatus.Succeeded => NoContent(),
            // Deleted since the session check let the request in: to the caller, an ended session.
            UserAccountStatus.NotFound => Challenge(),
            _ => this.BadRequestError(result.Error),
        };
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
