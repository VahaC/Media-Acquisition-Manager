using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.Api.Services.Auth;

/// <summary>
/// Signed-in sessions: signing in with a password, starting a session, rotating its refresh token,
/// and ending sessions. Expected failures come back as result statuses, never as exceptions.
/// </summary>
public interface ISessionService
{
    /// <summary>
    /// Checks <paramref name="userName"/> (any letter case) and <paramref name="password"/>, and on a
    /// match with an enabled user records the sign-in and starts a session. Every failure — unknown
    /// user, wrong password, disabled user, a blank field — is the same
    /// <see cref="PasswordSignInStatus.Refused"/> after the same amount of work; so is a user whose
    /// password was reset, or who was disabled or deleted, while the password was being checked.
    /// </summary>
    Task<PasswordSignInResult> SignInAsync(string? userName, string? password, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a session for <paramref name="user"/> and saves it — inside the caller's transaction
    /// when there is one, which is how a password sign-in keeps its claim on the user and the new
    /// session together. Checks nothing: the caller has already decided the user may sign in.
    /// </summary>
    Task<SessionGrant> StartAsync(UserEntity user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Trades <paramref name="refreshToken"/> for a new one and a new access token. The swap is
    /// atomic: of two requests presenting the same token, one rotates it and the other gets
    /// <see cref="SessionRefreshStatus.Conflict"/>. A token presented again shortly after it was
    /// rotated is a <see cref="SessionRefreshStatus.Conflict"/> too; later than that it is treated as
    /// stolen and its session is revoked.
    /// </summary>
    Task<SessionRefreshResult> RefreshAsync(string? refreshToken, CancellationToken cancellationToken = default);

    /// <summary>Revokes the session <paramref name="refreshToken"/> belongs to, as its current token
    /// or the one its last refresh replaced; nothing when it belongs to none.</summary>
    Task SignOutAsync(string? refreshToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes every active session of <paramref name="userId"/> except
    /// <paramref name="exceptSessionId"/>, at once — in its own statement, not in the caller's next
    /// save. Their access tokens stop working with the next request. Returns how many were revoked.
    /// </summary>
    Task<int> RevokeAllForUserAsync(Guid userId, Guid? exceptSessionId = null, CancellationToken cancellationToken = default);
}
