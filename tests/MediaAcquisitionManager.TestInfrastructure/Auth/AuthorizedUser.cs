using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.TestInfrastructure.Auth;

/// <summary>A seeded user with a live session, as a browser that just signed in holds it.</summary>
/// <param name="User">The user row.</param>
/// <param name="AccessToken">A valid bearer token for the session.</param>
/// <param name="SessionId">The session's id (the token's <c>sid</c>).</param>
/// <param name="RefreshToken">The session's refresh token — the <c>mam_refresh</c> cookie's value.</param>
public sealed record AuthorizedUser(UserEntity User, string AccessToken, Guid SessionId, string RefreshToken)
{
    /// <summary>The <c>Cookie</c> request header carrying <see cref="RefreshToken"/>, for the
    /// factory's <c>cookie</c> argument.</summary>
    public string RefreshCookie => $"{RefreshTokenCookie.Name}={RefreshToken}";
}
