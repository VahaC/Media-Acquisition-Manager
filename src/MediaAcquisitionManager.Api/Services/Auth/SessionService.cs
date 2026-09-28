using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using MediaAcquisitionManager.Api.Mapping;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Accounts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Auth;

/// <inheritdoc cref="ISessionService"/>
public sealed class SessionService(
    ApplicationDbContext db,
    IPasswordHasher hasher,
    UnknownUserPasswordHash unknownUserPasswordHash,
    IAccessTokenService accessTokens,
    IUserMapper mapper,
    TimeProvider time,
    ILogger<SessionService> logger) : ISessionService
{
    /// <summary>
    /// MAM-4 — a session ends 30 days after it was last used, so someone who opens the app now and
    /// then stays signed in, while a device left behind signs itself out within a month.
    /// </summary>
    private static readonly TimeSpan IdleLifetime = TimeSpan.FromDays(30);

    /// <summary>
    /// MAM-4 — how long a just-replaced refresh token still counts as a race rather than a replay. Two
    /// tabs refreshing together send the same cookie; the loser's request can arrive a little after
    /// the winner's rotation, and its browser receives the new cookie within the same moment. Thirty
    /// seconds covers a slow network by far, and is too short for a stolen token to be of use.
    /// </summary>
    private static readonly TimeSpan RefreshReplayGrace = TimeSpan.FromSeconds(30);

    /// <summary>256 bits: unguessable, so the token needs no rate limit and its hash no salt.</summary>
    private const int RefreshTokenBytes = 32;

    public async Task<PasswordSignInResult> SignInAsync(string? userName, string? password, CancellationToken cancellationToken = default)
    {
        UserEntity? user = null;
        if (!string.IsNullOrWhiteSpace(userName))
        {
            var normalized = UserNameRules.Normalize(userName);
            user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.NormalizedUserName == normalized, cancellationToken);
        }

        // MAM-4 — exactly one password hash per attempt, whatever fails: an unknown name is checked
        // against a hash nobody has the password to, a disabled user's password is still checked,
        // so the answer takes as long either way. A password no rule would have let anyone set can't
        // match; it is swapped for an empty one so the hash still runs, but never over a megabyte
        // someone posted.
        var passwordIsPossible = !string.IsNullOrEmpty(password) && password.Length <= PasswordRules.MaximumLength;
        var passwordMatches = hasher.Verify(passwordIsPossible ? password! : "", user?.PasswordHash ?? unknownUserPasswordHash.Value);

        if (user is not { IsDisabled: false } || !passwordIsPossible || !passwordMatches)
            return Refused();

        // MAM-4 — the check above ran on a copy read a few hundred milliseconds of PBKDF2 ago. A
        // password reset (how the owner locks out a leaked password), a disable or a delete that
        // committed since must win, or this sign-in would start a session the reset's or disable's
        // revocation never saw. So the sign-in is claimed with a conditional update that only matches
        // the user as verified — same hash, still enabled, still there — and the session is saved in
        // the same transaction. EF's BeginTransaction on SQLite is BEGIN IMMEDIATE: it takes the write
        // lock at once, like the reset's, disable's and delete's writes do, so none of them can commit
        // between the claim and the session; whichever runs first, the other sees its result.
        var now = time.GetUtcNow().UtcDateTime;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var claimed = await db.Users
            .Where(u => u.Id == user.Id && u.PasswordHash == user.PasswordHash && !u.IsDisabled)
            .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.LastSignInUtc, (DateTime?)now), cancellationToken) == 1;
        // Changed or gone since the check: the same refusal as any other, never a 500.
        if (!claimed)
            return Refused();

        var grant = await StartAsync(user, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("{UserName} signed in.", user.UserName);
        return PasswordSignInResult.SignedIn(grant);
    }

    public async Task<SessionGrant> StartAsync(UserEntity user, CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var refreshToken = NewRefreshToken();
        var session = new UserSessionEntity
        {
            UserId = user.Id,
            RefreshTokenHash = HashOf(refreshToken),
            CreatedUtc = now,
            LastUsedUtc = now,
            ExpiresUtc = now + IdleLifetime,
        };

        db.UserSessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);

        return await GrantAsync(user, session.Id, refreshToken, session.ExpiresUtc, cancellationToken);
    }

    public async Task<SessionRefreshResult> RefreshAsync(string? refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(refreshToken))
            return SessionRefreshResult.Ended;

        var presentedHash = HashOf(refreshToken);
        var now = time.GetUtcNow().UtcDateTime;

        var session = await db.UserSessions.AsNoTracking()
            .SingleOrDefaultAsync(s => s.RefreshTokenHash == presentedHash, cancellationToken);
        if (session is null)
            return await RefreshWithReplacedTokenAsync(presentedHash, now, cancellationToken);

        if (session.RevokedUtc is not null || session.ExpiresUtc <= now)
            return SessionRefreshResult.Ended;

        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == session.UserId, cancellationToken);
        if (user is not { IsDisabled: false })
            return SessionRefreshResult.Ended;

        var newToken = NewRefreshToken();
        var newHash = HashOf(newToken);
        var expiresUtc = now + IdleLifetime;

        // MAM-4 — the rotation is a conditional update that only succeeds while the row still holds
        // the presented hash: of two requests racing with one cookie, exactly one gets a row back,
        // with no lock and no read-modify-write window between them.
        var rotated = await db.UserSessions
            .Where(s => s.Id == session.Id && s.RefreshTokenHash == presentedHash && s.RevokedUtc == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.RefreshTokenHash, newHash)
                .SetProperty(s => s.PreviousRefreshTokenHash, presentedHash)
                .SetProperty(s => s.RotatedUtc, now)
                .SetProperty(s => s.LastUsedUtc, now)
                .SetProperty(s => s.ExpiresUtc, expiresUtc),
                cancellationToken) == 1;
        if (!rotated)
            return SessionRefreshResult.Conflict;

        return SessionRefreshResult.Refreshed(await GrantAsync(user, session.Id, newToken, expiresUtc, cancellationToken));
    }

    public async Task SignOutAsync(string? refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(refreshToken))
            return;

        var hash = HashOf(refreshToken);
        var now = time.GetUtcNow().UtcDateTime;
        // MAM-4 — the replaced token counts too. A sign-out racing a refresh from another tab carries
        // the cookie that refresh just rotated away; matching the current token only would revoke
        // nothing, and the rotated cookie, arriving after the sign-out's deletion, would keep the
        // browser signed in. Ending a session through its previous token grants nothing: a refresh
        // with that token ends the session too once the grace has passed.
        await db.UserSessions
            .Where(s => (s.RefreshTokenHash == hash || s.PreviousRefreshTokenHash == hash) && s.RevokedUtc == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.RevokedUtc, now), cancellationToken);
    }

    public async Task<int> RevokeAllForUserAsync(Guid userId, Guid? exceptSessionId = null, CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var sessions = db.UserSessions.Where(s => s.UserId == userId && s.RevokedUtc == null);
        if (exceptSessionId is { } keptSessionId)
            sessions = sessions.Where(s => s.Id != keptSessionId);

        return await sessions.ExecuteUpdateAsync(setters => setters.SetProperty(s => s.RevokedUtc, now), cancellationToken);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    // Information, not Warning: a mistyped password is routine. The name isn't logged — people type
    // their password into the user-name field often enough.
    private PasswordSignInResult Refused()
    {
        logger.LogInformation("A sign-in attempt was refused.");
        return PasswordSignInResult.Refused;
    }

    // A token that is no session's current one may be the one a refresh just replaced.
    private async Task<SessionRefreshResult> RefreshWithReplacedTokenAsync(string presentedHash, DateTime now, CancellationToken cancellationToken)
    {
        var session = await db.UserSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.PreviousRefreshTokenHash == presentedHash, cancellationToken);
        if (session is null || session.RevokedUtc is not null)
            return SessionRefreshResult.Ended;

        // Another tab won the race a moment ago; this browser already has (or is about to get) the
        // new cookie, so a retry succeeds.
        if (session.RotatedUtc is { } rotatedUtc && now - rotatedUtc <= RefreshReplayGrace)
            return SessionRefreshResult.Conflict;

        // MAM-4 — an old token coming back long after it was replaced means two parties hold this
        // session's tokens: the user and someone who copied the cookie. There is no telling which
        // request is whose, so the session ends for both and the user signs in again.
        await db.UserSessions
            .Where(s => s.Id == session.Id && s.RevokedUtc == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.RevokedUtc, now), cancellationToken);

        // Warning, so it ships off the box: this is what a stolen refresh token looks like.
        logger.LogWarning(
            "A replaced refresh token of session {SessionId} (user {UserId}) was presented again; the session is revoked in case the token was stolen.",
            session.Id,
            session.UserId);
        return SessionRefreshResult.Ended;
    }

    private async Task<SessionGrant> GrantAsync(
        UserEntity user, Guid sessionId, string refreshToken, DateTime sessionExpiresUtc, CancellationToken cancellationToken)
    {
        var isOwner = await db.Installations.AnyAsync(i => i.OwnerUserId == user.Id, cancellationToken);
        var response = mapper.ToSignInResponse(user, isOwner, accessTokens.Issue(user, sessionId));
        return new SessionGrant(response, sessionId, refreshToken, sessionExpiresUtc);
    }

    // Base64url: safe in a cookie value as is, no encoding on the way out or back.
    private static string NewRefreshToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(RefreshTokenBytes));

    private static string HashOf(string refreshToken) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));
}
