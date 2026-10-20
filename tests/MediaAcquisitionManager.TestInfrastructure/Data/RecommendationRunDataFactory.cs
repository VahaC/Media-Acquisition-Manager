using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.Infrastructure.Persistence;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <inheritdoc cref="IRecommendationRunDataFactory"/>
public sealed class RecommendationRunDataFactory(ApplicationDbContext db, TimeProvider time) : IRecommendationRunDataFactory
{
    public async Task<RecommendationRunEntity> Create(
        RecommendationRunStatus status,
        RecommendationRunTrigger trigger = RecommendationRunTrigger.Requested,
        Guid? requestedByUserId = null,
        string? requestedByUserName = null,
        DateTime? createdUtc = null,
        int? seedCount = null,
        int? candidateCount = null,
        Message? error = null)
    {
        var queued = createdUtc ?? time.GetUtcNow().UtcDateTime;
        var run = new RecommendationRunEntity
        {
            Trigger = trigger,
            RequestedByUserId = requestedByUserId,
            RequestedByUserName = requestedByUserName,
            Status = status,
            StartedUtc = status == RecommendationRunStatus.Queued ? null : queued,
            FinishedUtc = status is RecommendationRunStatus.Succeeded or RecommendationRunStatus.Failed ? queued : null,
            SeedCount = seedCount,
            CandidateCount = candidateCount,
            Error = error,
            CreatedUtc = queued,
        };

        db.RecommendationRuns.Add(run);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return run;
    }
}
