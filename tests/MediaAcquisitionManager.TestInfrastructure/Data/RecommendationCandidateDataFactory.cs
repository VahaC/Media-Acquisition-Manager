using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <inheritdoc cref="IRecommendationCandidateDataFactory"/>
/// <remarks>The reasons are the candidate's own rows, so they are added through the candidate's factory.</remarks>
public sealed class RecommendationCandidateDataFactory(ApplicationDbContext db, TimeProvider time) : IRecommendationCandidateDataFactory
{
    public async Task<RecommendationCandidateEntity> Create(
        Guid catalogWorkId,
        RecommendationTab tab,
        double score = 1,
        bool requiresAvailability = false,
        string? missingSeasons = null,
        IReadOnlyList<RecommendationReasonEntity>? reasons = null)
    {
        var candidate = new RecommendationCandidateEntity
        {
            CatalogWorkId = catalogWorkId,
            Tab = tab,
            Score = score,
            RequiresAvailability = requiresAvailability,
            MissingSeasons = missingSeasons,
            CreatedUtc = time.GetUtcNow().UtcDateTime,
        };

        db.RecommendationCandidates.Add(candidate);
        var position = 0;
        foreach (var reason in reasons ?? [])
        {
            reason.RecommendationCandidateId = candidate.Id;
            reason.Position = position++;
            db.RecommendationReasons.Add(reason);
        }

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return candidate;
    }
}
