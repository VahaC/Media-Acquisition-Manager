using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <summary>File inspection runs for a test (MAM-9), planted in any status without running anything.</summary>
public interface IFileInspectionRunSeeder
{
    /// <summary>
    /// A run in <paramref name="status"/> for <paramref name="trigger"/>, asked for by
    /// <paramref name="requestedBy"/> (id and user name) at <paramref name="createdUtc"/> — the clock's now
    /// when <c>null</c>; give runs explicit times when a test compares them.
    /// </summary>
    Task<FileInspectionRunEntity> Create(
        FileInspectionRunStatus status,
        FileInspectionTrigger trigger = FileInspectionTrigger.Requested,
        UserEntity? requestedBy = null,
        DateTime? createdUtc = null,
        string? error = null,
        int? filesTotal = null,
        int filesChecked = 0,
        int filesProbed = 0);
}
