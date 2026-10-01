using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <summary>Creates file inspection runs directly in the database (MAM-9).</summary>
public interface IFileInspectionRunDataFactory
{
    /// <summary>
    /// A saved run in <paramref name="status"/> for <paramref name="trigger"/>, queued at
    /// <paramref name="createdUtc"/> (the clock's now when <c>null</c>): started at that instant unless it
    /// is still queued, and finished at it when it succeeded or failed. Values are stored as given, so a
    /// test can plant a row the app would never write.
    /// </summary>
    Task<FileInspectionRunEntity> Create(
        FileInspectionRunStatus status,
        FileInspectionTrigger trigger = FileInspectionTrigger.Requested,
        Guid? requestedByUserId = null,
        string? requestedByUserName = null,
        DateTime? createdUtc = null,
        string? error = null,
        int? filesTotal = null,
        int filesChecked = 0,
        int filesProbed = 0);
}
