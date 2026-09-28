using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Auth;
using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.Api.Mapping;

/// <summary>Turns user rows into the contracts that describe them.</summary>
public interface IUserMapper
{
    /// <summary>The signed-in user as the SPA sees them. Whether they own the installation is not on
    /// the user row, so the caller supplies it.</summary>
    CurrentUserResponse ToCurrentUser(UserEntity user, bool isOwner);

    /// <summary>A signed-in session's answer: <paramref name="accessToken"/> for <paramref name="user"/>.</summary>
    SignInResponse ToSignInResponse(UserEntity user, bool isOwner, AccessToken accessToken);

    /// <summary>The user as the owner's user management shows them; the caller supplies whether they
    /// own the installation, as for <see cref="ToCurrentUser"/>.</summary>
    UserDto ToDto(UserEntity user, bool isOwner);
}
