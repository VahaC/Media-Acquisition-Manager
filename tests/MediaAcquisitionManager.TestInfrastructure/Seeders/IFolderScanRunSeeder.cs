using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <summary>Scans of the media folders for a test (MAM-18), planted in any status without walking
/// anything.</summary>
public interface IFolderScanRunSeeder
{
    /// <summary>
    /// A scan in <paramref name="status"/>, asked for by <paramref name="requestedBy"/> (id and user name)
    /// at <paramref name="createdUtc"/> — the clock's now when <c>null</c>; give scans explicit times when a
    /// test compares them.
    /// </summary>
    Task<FolderScanRunEntity> Create(
        FolderScanStatus status,
        UserEntity? requestedBy = null,
        DateTime? createdUtc = null,
        int filesFound = 0,
        int? filesUnreported = null,
        int problemCount = 0,
        string? problem = null,
        string? error = null);
}
