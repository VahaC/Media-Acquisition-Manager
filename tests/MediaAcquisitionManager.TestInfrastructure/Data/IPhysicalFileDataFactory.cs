using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <summary>Creates file names of a media version, or ones a scan of the media folders found, directly in
/// the database.</summary>
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

    /// <summary>
    /// A saved name <paramref name="appPath"/> as a scan of the media folders stores one no library reported
    /// (MAM-18): the app as its source, with no connection, no version, no library and no size as reported;
    /// the path is the app's own, and it was first and last seen at the factory's now.
    /// </summary>
    Task<PhysicalFileEntity> CreateUnreported(string appPath);

    /// <summary>
    /// Writes on the name <paramref name="fileId"/> what an inspection run writes when it read the file (MAM-9):
    /// <paramref name="status"/>, the reading <paramref name="readingId"/> it points at, and the factory's now.
    /// </summary>
    Task Link(Guid fileId, Guid readingId, FileInspectionStatus status = FileInspectionStatus.Inspected);
}
