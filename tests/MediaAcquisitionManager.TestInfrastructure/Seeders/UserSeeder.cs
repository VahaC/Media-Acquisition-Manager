using MediaAcquisitionManager.Api.Services.Auth;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Data;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <inheritdoc cref="IUserSeeder"/>
public sealed class UserSeeder(IServiceProvider services) : SeederBase(services), IUserSeeder
{
    /// <summary>Every seeded user's password unless a test picks one; passes the password rules.</summary>
    public const string DefaultPassword = "correct-horse-battery";

    private IUserDataFactory Users => new UserDataFactory(Db, Resolve<IPasswordHasher>(), Resolve<TimeProvider>());

    private IInstallationDataFactory Installations => new InstallationDataFactory(Db, Resolve<TimeProvider>());

    public Task<UserEntity> Create(string? userName = null, string password = DefaultPassword, bool isDisabled = false) =>
        Users.Create(userName ?? NewUserName(), password, isDisabled);

    public async Task<UserEntity> CreateOwner(string? userName = null, string password = DefaultPassword)
    {
        var owner = await Create(userName, password);
        await Installations.SetOwner(owner.Id);
        return owner;
    }

    public async Task<AuthorizedUser> CreateAuthorized(string? userName = null, string password = DefaultPassword, bool isDisabled = false) =>
        await StartSession(await Create(userName, password, isDisabled));

    public async Task<AuthorizedUser> CreateAuthorizedOwner(string? userName = null, string password = DefaultPassword) =>
        await StartSession(await CreateOwner(userName, password));

    public Task<AuthorizedUser> CreateSession(UserEntity user) => StartSession(user);

    // The app's own session service, so the token, the cookie value and the session row are exactly
    // what a real sign-in produces.
    private async Task<AuthorizedUser> StartSession(UserEntity user)
    {
        var grant = await Resolve<ISessionService>().StartAsync(user);
        Db.ChangeTracker.Clear();
        return new AuthorizedUser(user, grant.Response.AccessToken, grant.SessionId, grant.RefreshToken);
    }

    // Unique, and within the user-name rules (3–32 of letters, digits, '.', '-', '_').
    private static string NewUserName() => $"user-{Guid.NewGuid():N}"[..17];
}
