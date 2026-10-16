namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>A user as the Users settings page shows them.</summary>
/// <param name="Id">The user's id.</param>
/// <param name="UserName">The user name as it was created.</param>
/// <param name="IsOwner">Whether this is the installation owner, who can't be disabled or deleted.</param>
/// <param name="IsDisabled">Whether the user is disabled: they can't sign in, and their sessions have
/// ended.</param>
/// <param name="CreatedUtc">When the account was created.</param>
/// <param name="LastSignInUtc">The last successful sign-in; <c>null</c> until the first one.</param>
/// <param name="ActiveTokenCount">MAM-5 — how many of the user's personal access tokens are active: neither revoked nor
/// expired. A disabled user's are revoked, so theirs is 0.</param>
public sealed record UserDto(
    Guid Id,
    string UserName,
    bool IsOwner,
    bool IsDisabled,
    DateTime CreatedUtc,
    DateTime? LastSignInUtc,
    int ActiveTokenCount);
