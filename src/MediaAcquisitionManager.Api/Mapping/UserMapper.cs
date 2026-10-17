using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Auth;
using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.Api.Mapping;

/// <inheritdoc cref="IUserMapper"/>
public sealed class UserMapper : IUserMapper
{
    public CurrentUserResponse ToCurrentUser(UserEntity user, bool isOwner) =>
        new(user.Id, user.UserName, isOwner, user.Language);

    public CurrentUserProfileResponse ToProfile(UserEntity user, bool isOwner, DateTime sessionExpiresUtc) =>
        new(user.Id, user.UserName, isOwner, user.CreatedUtc, user.LastSignInUtc, sessionExpiresUtc, user.Language);

    public SignInResponse ToSignInResponse(UserEntity user, bool isOwner, AccessToken accessToken) =>
        new(accessToken.Token, accessToken.ExpiresUtc, ToCurrentUser(user, isOwner));

    public UserDto ToDto(UserEntity user, bool isOwner, int activeTokenCount) =>
        new(user.Id, user.UserName, isOwner, user.IsDisabled, user.CreatedUtc, user.LastSignInUtc, activeTokenCount);
}
