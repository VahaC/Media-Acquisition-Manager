using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <summary>MAM-33 — recommendation candidates for a test, planted as a run would have stored them, without running
/// anything.</summary>
public interface IRecommendationCandidateSeeder
{
    /// <summary>
    /// A candidate of <paramref name="work"/> in <paramref name="tab"/> scored <paramref name="score"/>, with
    /// <paramref name="reasons"/> in order (a Recommended reason with no titles when none are given).
    /// </summary>
    Task<RecommendationCandidateEntity> Create(
        CatalogWorkEntity work,
        RecommendationTab tab,
        double score = 1,
        bool requiresAvailability = false,
        string? missingSeasons = null,
        IReadOnlyList<RecommendationReasonEntity>? reasons = null);
}
