using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-5 — a personal access token to create for the signed-in user; every field is required.</summary>
/// <remarks>
/// Every field nullable and without <c>[Required]</c> on purpose, as in <see cref="ChangePasswordRequest"/>: a blank or
/// missing field reaches the service, whose sentence the Create token dialog shows under it, instead of stopping at a
/// validation body with no <c>error</c> in it. An access or lifetime that isn't one of the names is a body the formatter
/// can't read, answered with its own sentence.
/// </remarks>
/// <param name="Name">What to call it, such as "Claude on the laptop": up to 100 characters, trimmed.</param>
/// <param name="Access">What it may do: <c>ReadOnly</c> or <c>ReadAndDownload</c>.</param>
/// <param name="Lifetime">When it expires: <c>Days30</c>, <c>Days90</c>, <c>OneYear</c> or <c>Never</c>.</param>
public sealed record CreatePersonalAccessTokenRequest(
    string? Name,
    PersonalAccessTokenAccess? Access,
    PersonalAccessTokenLifetime? Lifetime);
