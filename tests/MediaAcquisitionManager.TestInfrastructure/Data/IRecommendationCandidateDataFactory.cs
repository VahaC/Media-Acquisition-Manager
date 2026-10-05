using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <summary>MAM-33 — creates recommendation candidates, with their reasons, directly in the database.</summary>
public interface IRecommendationCandidateDataFactory
{
    /// <summary>
    /// A saved candidate of the catalog work <paramref name="catalogWorkId"/> in <paramref name="tab"/>, with
    /// <paramref name="reasons"/> stored in the order given — their candidate and positions are set here, everything
    /// else is stored as given.
    /// </summary>
    Task<RecommendationCandidateEntity> Create(
        Guid catalogWorkId,
        RecommendationTab tab,
        double score = 1,
        bool requiresAvailability = false,
        string? missingSeasons = null,
        IReadOnlyList<RecommendationReasonEntity>? reasons = null);
}
