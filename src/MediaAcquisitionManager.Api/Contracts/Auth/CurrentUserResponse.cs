namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>The signed-in user.</summary>
/// <param name="Id">The user's id.</param>
/// <param name="UserName">The user name as it was created.</param>
/// <param name="IsOwner">Whether the user is the installation owner, who manages users and global
/// settings.</param>
/// <param name="Language">MAM-46 — the language the user saved for the web app (<c>en</c>, <c>uk</c>), which it switches
/// to as soon as the session is set — every start of the app comes through a refresh, so the language travels here, not
/// only on <c>GET /api/auth/me</c>; <c>null</c> when the user follows the browser's.</param>
public sealed record CurrentUserResponse(Guid Id, string UserName, bool IsOwner, string? Language);
