using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Data;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <inheritdoc cref="IRecommendationCandidateSeeder"/>
public sealed class RecommendationCandidateSeeder(IServiceProvider services) : SeederBase(services), IRecommendationCandidateSeeder
{
    private IRecommendationCandidateDataFactory Candidates => new RecommendationCandidateDataFactory(Db, Resolve<TimeProvider>());

    public Task<RecommendationCandidateEntity> Create(
        CatalogWorkEntity work,
        RecommendationTab tab,
        double score = 1,
        bool requiresAvailability = false,
        string? missingSeasons = null,
        IReadOnlyList<RecommendationReasonEntity>? reasons = null)
    {
        ArgumentNullException.ThrowIfNull(work);
        return Candidates.Create(
            work.Id,
            tab,
            score,
            requiresAvailability,
            missingSeasons,
            reasons ?? [new RecommendationReasonEntity { Kind = RecommendationReasonKind.Recommended, Held = 1, Titles = "" }]);
    }
}
