using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <summary>MAM-33 — recommendation runs for a test, planted in any status without running anything.</summary>
public interface IRecommendationRunSeeder
{
    /// <summary>
    /// A run in <paramref name="status"/> for <paramref name="trigger"/>, asked for by <paramref name="requestedBy"/>
    /// (id and user name) at <paramref name="createdUtc"/> — the clock's now when <c>null</c>; give runs explicit times
    /// when a test compares them.
    /// </summary>
    Task<RecommendationRunEntity> Create(
        RecommendationRunStatus status,
        RecommendationRunTrigger trigger = RecommendationRunTrigger.Requested,
        UserEntity? requestedBy = null,
        DateTime? createdUtc = null,
        int? seedCount = null,
        int? candidateCount = null,
        string? error = null);
}
