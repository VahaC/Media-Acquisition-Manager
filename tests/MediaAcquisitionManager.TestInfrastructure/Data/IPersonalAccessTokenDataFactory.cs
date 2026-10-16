using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <summary>
/// MAM-5 — creates personal access token rows directly in the database, in any state, with a fresh well-formed secret.
/// Writes no audit row — unlike creating one through the API — so a test that counts audit rows counts only its own.
/// </summary>
public interface IPersonalAccessTokenDataFactory
{
    /// <summary>
    /// A saved token of <paramref name="userId"/> named <paramref name="name"/> with <paramref name="access"/>, expiring
    /// at <paramref name="expiresUtc"/> (<c>null</c>: never), revoked at <paramref name="revokedUtc"/> and last used at
    /// <paramref name="lastUsedUtc"/> (<c>null</c>: not), created at <paramref name="createdUtc"/> (<c>null</c>: now).
    /// </summary>
    Task<IssuedPersonalAccessToken> Create(
        Guid userId,
        PersonalAccessTokenAccess access,
        string name,
        DateTime? expiresUtc,
        DateTime? revokedUtc = null,
        DateTime? lastUsedUtc = null,
        DateTime? createdUtc = null);
}
