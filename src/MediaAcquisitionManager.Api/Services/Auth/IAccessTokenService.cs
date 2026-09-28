using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.Api.Services.Auth;

/// <summary>Issues the short-lived access tokens every API call carries.</summary>
public interface IAccessTokenService
{
    /// <summary>
    /// A token for <paramref name="user"/> in session <paramref name="sessionId"/>, valid for
    /// <see cref="Api.Auth.JwtAccessTokens.Lifetime"/> from now. It works only while that session is
    /// active, whatever its own expiry says.
    /// </summary>
    AccessToken Issue(UserEntity user, Guid sessionId);
}
