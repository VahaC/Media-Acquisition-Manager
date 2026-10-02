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
        DownloadJobStatus status = DownloadJobStatus.Submitted,
        Guid? requestedByUserId = null,
        string? infoHashV1 = null,
        Guid? requestId = null,
        string? infoHashV2 = null,
        string? clientJobId = null,
        double? progress = null,
        string? blockReason = null,
        DownloadJobErrorOrigin? blockReasonOrigin = null)
    {
        // Read from the mapping itself, so the copied values are the ones a test can later see
        // outliving it.
        var destination = destinationMappingId is { } id
            ? await db.DestinationMappings.AsNoTracking().SingleAsync(x => x.Id == id)
            : null;

        var now = time.GetUtcNow().UtcDateTime;
        var requestedBy = requestedByUserId is { } userId
            ? await db.Users.AsNoTracking().SingleAsync(x => x.Id == userId)
            : null;

        var job = new DownloadJobEntity
        {
            // A request of its own unless the test names one: one user's request ids are unique (MAM-13).
            RequestId = requestId ?? Guid.NewGuid(),
            RequestedByUserId = requestedBy?.Id,
            RequestedByUserName = requestedBy?.UserName,
            InfoHashV1 = infoHashV1,
            InfoHashV2 = infoHashV2,
            ClientJobId = clientJobId,
            Progress = progress,
            BlockReason = blockReason,
            BlockReasonOrigin = blockReasonOrigin,
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

    public async Task<IReadOnlyList<DownloadJobFileEntity>> AddFiles(Guid downloadJobId, IReadOnlyList<DownloadJobFileEntity> files)
    {
        for (var position = 0; position < files.Count; position++)
        {
            files[position].DownloadJobId = downloadJobId;
            files[position].Position = position;
        }

        db.DownloadJobFiles.AddRange(files);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return files;
    }
}
