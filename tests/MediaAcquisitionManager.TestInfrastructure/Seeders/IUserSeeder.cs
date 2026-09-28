using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.TestInfrastructure.Auth;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <summary>
/// Users for a test: plain, owner, and signed in. A <c>null</c> user name makes a unique valid one;
/// the password defaults to <see cref="UserSeeder.DefaultPassword"/>.
/// </summary>
public interface IUserSeeder
{
    /// <summary>A user who has never signed in.</summary>
    Task<UserEntity> Create(string? userName = null, string password = UserSeeder.DefaultPassword, bool isDisabled = false);

    /// <summary>A user who is the installation owner, so setup counts as completed.</summary>
    Task<UserEntity> CreateOwner(string? userName = null, string password = UserSeeder.DefaultPassword);

    /// <summary>
    /// A user with a live session, made through the app's real session and token services.
    /// <paramref name="isDisabled"/> makes the user who was disabled after signing in, still holding
    /// the session.
    /// </summary>
    Task<AuthorizedUser> CreateAuthorized(string? userName = null, string password = UserSeeder.DefaultPassword, bool isDisabled = false);

    /// <summary>The installation owner with a live session.</summary>
    Task<AuthorizedUser> CreateAuthorizedOwner(string? userName = null, string password = UserSeeder.DefaultPassword);

    /// <summary>Another live session for an existing <paramref name="user"/> — the same account signed
    /// in on a second browser or device.</summary>
    Task<AuthorizedUser> CreateSession(UserEntity user);
}
