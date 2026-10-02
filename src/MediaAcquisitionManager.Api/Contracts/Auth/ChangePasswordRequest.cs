namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>A signed-in user's new password, with the current one that proves it is them.</summary>
/// <remarks>
/// MAM-16 — both nullable and without <c>[Required]</c> on purpose, as in
/// <see cref="ResetPasswordRequest"/> (a non-nullable <c>string</c> would bring MVC's implicit
/// <c>[Required]</c> back): a blank or missing field must reach the checks, whose sentence the
/// Profile page shows, not stop at a validation body with no <c>error</c> in it.
/// </remarks>
/// <param name="CurrentPassword">The password the user signs in with now.</param>
/// <param name="NewPassword">The password to sign in with from now on; see the password rules.</param>
public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);
