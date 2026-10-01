using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <inheritdoc cref="ILibrarySyncRunDataFactory"/>
public sealed class LibrarySyncRunDataFactory(ApplicationDbContext db, TimeProvider time) : ILibrarySyncRunDataFactory
{
    public async Task<LibrarySyncRunEntity> Create(
        Guid? integrationId,
        string integrationName,
        LibrarySyncStatus status,
        Guid? requestedByUserId = null,
        string? requestedByUserName = null,
        DateTime? createdUtc = null,
        string? error = null)
    {
        var requested = createdUtc ?? time.GetUtcNow().UtcDateTime;
        var finished = status is LibrarySyncStatus.Succeeded or LibrarySyncStatus.Failed;
        var run = new LibrarySyncRunEntity
        {
            IntegrationConnectionId = integrationId,
            IntegrationName = integrationName,
            RequestedByUserId = requestedByUserId,
            RequestedByUserName = requestedByUserName,
            Status = status,
            StartedUtc = status == LibrarySyncStatus.Queued ? null : requested,
            FinishedUtc = finished ? requested : null,
            Error = error,
            CreatedUtc = requested,
        };

        db.LibrarySyncRuns.Add(run);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return run;
    }
}
