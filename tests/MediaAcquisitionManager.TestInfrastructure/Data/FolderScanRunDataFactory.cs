using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.Infrastructure.Persistence;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <inheritdoc cref="IFolderScanRunDataFactory"/>
public sealed class FolderScanRunDataFactory(ApplicationDbContext db, TimeProvider time) : IFolderScanRunDataFactory
{
    public async Task<FolderScanRunEntity> Create(
        FolderScanStatus status,
        Guid? requestedByUserId = null,
        string? requestedByUserName = null,
        DateTime? createdUtc = null,
        int filesFound = 0,
        int? filesUnreported = null,
        int problemCount = 0,
        Message? problem = null,
        Message? error = null)
    {
        var queued = createdUtc ?? time.GetUtcNow().UtcDateTime;
        var run = new FolderScanRunEntity
        {
            RequestedByUserId = requestedByUserId,
            RequestedByUserName = requestedByUserName,
            Status = status,
            StartedUtc = status == FolderScanStatus.Queued ? null : queued,
            FinishedUtc = status is FolderScanStatus.Succeeded or FolderScanStatus.Failed ? queued : null,
            FilesFound = filesFound,
            FilesUnreported = filesUnreported,
            ProblemCount = problemCount,
            Problem = problem,
            Error = error,
            CreatedUtc = queued,
        };

        db.FolderScanRuns.Add(run);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return run;
    }
}
