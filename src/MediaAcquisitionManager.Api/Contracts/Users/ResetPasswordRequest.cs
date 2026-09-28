namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>A new password for a user, set by the installation owner.</summary>
/// <remarks>
/// MAM-4 — nullable and without <c>[Required]</c> on purpose (a non-nullable <c>string</c> would bring
/// MVC's implicit <c>[Required]</c> back): a blank or missing password must reach the password rules,
/// whose sentence the Reset password dialog shows, not stop at a validation body with no
/// <c>error</c> in it.
/// </remarks>
/// <param name="Password">The new password; see the password rules.</param>
public sealed record ResetPasswordRequest(string? Password);
