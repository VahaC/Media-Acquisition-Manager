namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>The signed-in user.</summary>
/// <param name="Id">The user's id.</param>
/// <param name="UserName">The user name as it was created.</param>
/// <param name="IsOwner">Whether the user is the installation owner, who manages users and global
/// settings.</param>
public sealed record CurrentUserResponse(Guid Id, string UserName, bool IsOwner);
