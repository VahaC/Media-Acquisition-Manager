using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <summary>Creates scans of the media folders directly in the database (MAM-18).</summary>
public interface IFolderScanRunDataFactory
{
    /// <summary>
    /// A saved scan in <paramref name="status"/>, queued at <paramref name="createdUtc"/> (the clock's now
    /// when <c>null</c>): started at that instant unless it is still queued, and finished at it when it
    /// succeeded or failed. Values are stored as given, so a test can plant a row the app would never write.
    /// </summary>
    Task<FolderScanRunEntity> Create(
        FolderScanStatus status,
        Guid? requestedByUserId = null,
        string? requestedByUserName = null,
        DateTime? createdUtc = null,
        int filesFound = 0,
        int? filesUnreported = null,
        int problemCount = 0,
        Message? problem = null,
        Message? error = null);
}
