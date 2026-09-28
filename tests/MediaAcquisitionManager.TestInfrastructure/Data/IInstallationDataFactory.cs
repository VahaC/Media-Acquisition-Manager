using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <summary>Creates or changes the installation row directly in the database.</summary>
public interface IInstallationDataFactory
{
    /// <summary>Makes <paramref name="ownerUserId"/> the installation owner, completing the setup if
    /// it isn't yet.</summary>
    Task<InstallationEntity> SetOwner(Guid ownerUserId);
}
