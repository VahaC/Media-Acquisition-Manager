using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <summary>MAM-33 — creates rows of the recommendations' search log (the daily budget's) directly in the
/// database.</summary>
public interface IRecommendationSearchDataFactory
{
    /// <summary><paramref name="count"/> saved searches of <paramref name="userId"/>'s <paramref name="tab"/> list run
    /// at <paramref name="searchedUtc"/>, each of a catalog work of its own that needn't exist (the log has no foreign
    /// key to the catalog).</summary>
    Task<IReadOnlyList<RecommendationSearchEntity>> Create(Guid userId, RecommendationTab tab, int count, DateTime searchedUtc);
}
