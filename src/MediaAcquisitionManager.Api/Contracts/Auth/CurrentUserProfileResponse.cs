namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>The signed-in user and the session the request came through, as the Profile page shows
/// them.</summary>
/// <remarks>
/// MAM-16 — <c>GET /api/auth/me</c> only. Sign-in, refresh and setup keep answering
/// <see cref="CurrentUserResponse"/>: sign-in reads the user before it records the sign-in, so these
/// fields would be stale there, and the session's end already travels in the refresh cookie the same
/// response sets — which is <c>HttpOnly</c>, so the web app asks here instead.
/// </remarks>
/// <param name="Id">The user's id.</param>
/// <param name="UserName">The user name as it was created.</param>
/// <param name="IsOwner">Whether the user is the installation owner, who manages users and global
/// settings.</param>
/// <param name="CreatedUtc">When the account was created.</param>
/// <param name="LastSignInUtc">The user's last successful sign-in on any device, which is not
/// necessarily this session's; <c>null</c> until the first one.</param>
/// <param name="SessionExpiresUtc">When the session this request came through ends unless it is used
/// again: every refresh moves it to 30 days later.</param>
/// <param name="Language">MAM-46 — the language the user saved for the web app (<c>en</c>, <c>uk</c>), which the Profile
/// page's Language card shows; <c>null</c> when the user follows the browser's.</param>
public sealed record CurrentUserProfileResponse(
    Guid Id,
    string UserName,
    bool IsOwner,
    DateTime CreatedUtc,
    DateTime? LastSignInUtc,
    DateTime SessionExpiresUtc,
    string? Language);
