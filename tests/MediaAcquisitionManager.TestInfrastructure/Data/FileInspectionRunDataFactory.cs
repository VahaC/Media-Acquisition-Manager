using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <inheritdoc cref="IFileInspectionRunDataFactory"/>
public sealed class FileInspectionRunDataFactory(ApplicationDbContext db, TimeProvider time) : IFileInspectionRunDataFactory
{
    public async Task<FileInspectionRunEntity> Create(
        FileInspectionRunStatus status,
        FileInspectionTrigger trigger = FileInspectionTrigger.Requested,
        Guid? requestedByUserId = null,
        string? requestedByUserName = null,
        DateTime? createdUtc = null,
        string? error = null,
        int? filesTotal = null,
        int filesChecked = 0,
        int filesProbed = 0)
    {
        var queued = createdUtc ?? time.GetUtcNow().UtcDateTime;
        var run = new FileInspectionRunEntity
        {
            Trigger = trigger,
            RequestedByUserId = requestedByUserId,
            RequestedByUserName = requestedByUserName,
            Status = status,
            StartedUtc = status == FileInspectionRunStatus.Queued ? null : queued,
            FinishedUtc = status is FileInspectionRunStatus.Succeeded or FileInspectionRunStatus.Failed ? queued : null,
            FilesTotal = filesTotal,
            FilesChecked = filesChecked,
            FilesProbed = filesProbed,
            Error = error,
            CreatedUtc = queued,
        };

        db.FileInspectionRuns.Add(run);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return run;
    }
}
