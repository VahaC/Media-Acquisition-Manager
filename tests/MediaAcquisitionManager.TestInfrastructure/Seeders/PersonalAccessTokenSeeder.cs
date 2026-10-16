using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Data;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <inheritdoc cref="IPersonalAccessTokenSeeder"/>
public sealed class PersonalAccessTokenSeeder(IServiceProvider services) : SeederBase(services), IPersonalAccessTokenSeeder
{
    /// <summary>Every seeded token's name unless a test picks one.</summary>
    public const string DefaultName = "Script";

    private IPersonalAccessTokenDataFactory Tokens => new PersonalAccessTokenDataFactory(Db, Resolve<TimeProvider>());

    private DateTime Now => Resolve<TimeProvider>().GetUtcNow().UtcDateTime;

    public Task<IssuedPersonalAccessToken> Create(
        UserEntity user,
        PersonalAccessTokenAccess access = PersonalAccessTokenAccess.ReadOnly,
        DateTime? expiresUtc = null,
        string? name = null)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Tokens.Create(user.Id, access, name ?? DefaultName, expiresUtc);
    }

    public Task<IssuedPersonalAccessToken> CreateRevoked(
        UserEntity user,
        PersonalAccessTokenAccess access = PersonalAccessTokenAccess.ReadOnly,
        string? name = null)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Tokens.Create(user.Id, access, name ?? DefaultName, expiresUtc: null, revokedUtc: Now);
    }

    public Task<IssuedPersonalAccessToken> CreateExpired(
        UserEntity user,
        PersonalAccessTokenAccess access = PersonalAccessTokenAccess.ReadOnly,
        string? name = null)
    {
        ArgumentNullException.ThrowIfNull(user);
        var now = Now;
        return Tokens.Create(user.Id, access, name ?? DefaultName, expiresUtc: now, createdUtc: now.AddDays(-30));
    }
}
