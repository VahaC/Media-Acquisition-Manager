using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <summary>MAM-33 — creates recommendation runs directly in the database.</summary>
public interface IRecommendationRunDataFactory
{
    /// <summary>
    /// A saved run in <paramref name="status"/> for <paramref name="trigger"/>, queued at
    /// <paramref name="createdUtc"/> (the clock's now when <c>null</c>): started at that instant unless it is still
    /// queued, and finished at it when it succeeded or failed. Values are stored as given, so a test can plant a row
    /// the app would never write.
    /// </summary>
    Task<RecommendationRunEntity> Create(
        RecommendationRunStatus status,
        RecommendationRunTrigger trigger = RecommendationRunTrigger.Requested,
        Guid? requestedByUserId = null,
        string? requestedByUserName = null,
        DateTime? createdUtc = null,
        int? seedCount = null,
        int? candidateCount = null,
        string? error = null);
}
