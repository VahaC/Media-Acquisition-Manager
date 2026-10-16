using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-5 — one of the signed-in user's personal access tokens as their Profile page lists it. Never the secret,
/// nor its hash: after the create response, only <paramref name="Prefix"/> of it leaves the server.</summary>
/// <param name="Id">The token's id, which revoking it addresses.</param>
/// <param name="Name">What the user called it.</param>
/// <param name="Prefix">The token's first 14 characters (<c>mam_pat_AbC123</c>), to tell it apart from the others.</param>
/// <param name="Access">What it may do as the user.</param>
/// <param name="CreatedUtc">When it was created.</param>
/// <param name="LastUsedUtc">When a request last came in on it, to the minute; <c>null</c> until the first.</param>
/// <param name="ExpiresUtc">When it stops working; <c>null</c> when it never expires.</param>
/// <param name="IsExpired">Whether it has expired: listed until revoked, so the user sees why a script stopped.</param>
public sealed record PersonalAccessTokenDto(
    Guid Id,
    string Name,
    string Prefix,
    PersonalAccessTokenAccess Access,
    DateTime CreatedUtc,
    DateTime? LastUsedUtc,
    DateTime? ExpiresUtc,
    bool IsExpired);
