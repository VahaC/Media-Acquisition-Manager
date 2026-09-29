using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <inheritdoc cref="IDownloadJobDataFactory"/>
public sealed class DownloadJobDataFactory(ApplicationDbContext db, TimeProvider time) : IDownloadJobDataFactory
{
    public async Task<DownloadJobEntity> Create(
        Guid releaseId,
        Guid? downloadClientConnectionId,
        string downloadClientName,
        Guid? destinationMappingId = null,
        DownloadJobStatus status = DownloadJobStatus.Submitted)
    {
        // Read from the mapping itself, so the copied values are the ones a test can later see
        // outliving it.
        var destination = destinationMappingId is { } id
            ? await db.DestinationMappings.AsNoTracking().SingleAsync(x => x.Id == id)
            : null;

        var now = time.GetUtcNow().UtcDateTime;
        var job = new DownloadJobEntity
        {
            ReleaseId = releaseId,
            DownloadClientConnectionId = downloadClientConnectionId,
            DownloadClientName = downloadClientName,
            DestinationMappingId = destination?.Id,
            DestinationName = destination?.Name,
            ClientDestinationKey = destination?.ClientDestinationKey,
            Status = status,
            StatusChangedUtc = now,
            CreatedUtc = now,
        };

        db.DownloadJobs.Add(job);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return job;
    }
}
