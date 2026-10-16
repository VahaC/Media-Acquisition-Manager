using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <summary>
/// MAM-5 — personal access tokens for a test, planted without the API and without an audit row. A <c>null</c> name is
/// <see cref="PersonalAccessTokenSeeder.DefaultName"/>; every token is created now, by the test clock.
/// </summary>
public interface IPersonalAccessTokenSeeder
{
    /// <summary>An active token of <paramref name="user"/> with <paramref name="access"/>, expiring at
    /// <paramref name="expiresUtc"/> (<c>null</c>: never).</summary>
    Task<IssuedPersonalAccessToken> Create(
        UserEntity user,
        PersonalAccessTokenAccess access = PersonalAccessTokenAccess.ReadOnly,
        DateTime? expiresUtc = null,
        string? name = null);

    /// <summary>A token of <paramref name="user"/> revoked now, which never expires.</summary>
    Task<IssuedPersonalAccessToken> CreateRevoked(
        UserEntity user,
        PersonalAccessTokenAccess access = PersonalAccessTokenAccess.ReadOnly,
        string? name = null);

    /// <summary>A token of <paramref name="user"/> that expires exactly now — expired, as a token is from its
    /// <c>ExpiresUtc</c> on — and was created 30 days ago.</summary>
    Task<IssuedPersonalAccessToken> CreateExpired(
        UserEntity user,
        PersonalAccessTokenAccess access = PersonalAccessTokenAccess.ReadOnly,
        string? name = null);
}
