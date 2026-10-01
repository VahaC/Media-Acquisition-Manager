using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <summary>Creates file names of a media version directly in the database.</summary>
public interface IPhysicalFileDataFactory
{
    /// <summary>
    /// A saved name <paramref name="servicePath"/> of a file of <paramref name="versionId"/>. Reported
    /// by the library <paramref name="sourceConnectionId"/> when one is given; otherwise read by the
    /// app itself (file inspection), the only source that knows <paramref name="deviceId"/> and
    /// <paramref name="inode"/> for sure. <paramref name="libraryLocationId"/> is the library that holds
    /// it, as a sync decides it (MAM-8).
    /// </summary>
    Task<PhysicalFileEntity> Create(
        Guid versionId,
        string servicePath,
        long? sizeBytes = null,
        long? deviceId = null,
        long? inode = null,
        Guid? sourceConnectionId = null,
        string? sourceName = null,
        Guid? libraryLocationId = null);
}
