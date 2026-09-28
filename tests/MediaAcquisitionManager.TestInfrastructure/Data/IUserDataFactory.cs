using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <summary>Creates user rows directly in the database.</summary>
public interface IUserDataFactory
{
    /// <summary>A saved user named <paramref name="userName"/> whose password is
    /// <paramref name="password"/>, hashed by the host's own hasher.</summary>
    Task<UserEntity> Create(string userName, string password, bool isDisabled = false);
}
