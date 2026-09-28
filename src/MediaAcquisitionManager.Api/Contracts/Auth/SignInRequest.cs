namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>A sign-in with a user name and a password.</summary>
/// <remarks>
/// MAM-4 — both nullable and without <c>[Required]</c> on purpose: a blank field is a failed sign-in
/// and gets the same 401 as a wrong password, not a 400 that answers differently.
/// </remarks>
/// <param name="UserName">The user name, in any letter case.</param>
/// <param name="Password">The password, exactly as typed.</param>
public sealed record SignInRequest(string? UserName, string? Password);
