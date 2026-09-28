namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>A new user, created by the installation owner.</summary>
/// <remarks>
/// MAM-4 — both nullable and without <c>[Required]</c> on purpose (a non-nullable <c>string</c> would
/// bring MVC's implicit <c>[Required]</c> back): a blank or missing field must reach the user-name and
/// password rules, whose sentence the Add user dialog shows, not stop at a validation body with no
/// <c>error</c> in it.
/// </remarks>
/// <param name="UserName">The new user's name; see the user-name rules.</param>
/// <param name="Password">Their first password, which the owner passes on to them; see the password
/// rules.</param>
public sealed record CreateUserRequest(string? UserName, string? Password);
