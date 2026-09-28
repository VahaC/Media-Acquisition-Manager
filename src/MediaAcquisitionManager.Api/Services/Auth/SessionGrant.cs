using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.Auth;

/// <summary>A session that has just started or been refreshed: the body for the client and the
/// refresh token for its cookie.</summary>
/// <param name="Response">The access token and the signed-in user.</param>
/// <param name="SessionId">The session's id, which the access token names.</param>
/// <param name="RefreshToken">The new refresh token. Only its hash is stored; this is the only copy.</param>
/// <param name="SessionExpiresUtc">When the session ends unless it is refreshed before.</param>
public sealed record SessionGrant(SignInResponse Response, Guid SessionId, string RefreshToken, DateTime SessionExpiresUtc);
