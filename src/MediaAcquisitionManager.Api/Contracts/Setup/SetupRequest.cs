namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>The first-run setup: the installation owner's account.</summary>
/// <remarks>
/// MAM-4 — both nullable and without <c>[Required]</c> on purpose (a non-nullable <c>string</c> would
/// bring MVC's implicit <c>[Required]</c> back): a blank or missing field must reach the user-name and
/// password rules, whose sentence the setup page shows, not stop at a validation body with no
/// <c>error</c> in it.
/// </remarks>
/// <param name="UserName">The owner's user name; see the user-name rules.</param>
/// <param name="Password">The owner's password; see the password rules.</param>
public sealed record SetupRequest(string? UserName, string? Password);
