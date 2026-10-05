using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <summary>MAM-33 — creates a user's "Not interested" marks directly in the database.</summary>
public interface IRecommendationDismissalDataFactory
{
    /// <summary>A saved mark of <paramref name="userId"/> on the catalog work <paramref name="kind"/>
    /// <paramref name="catalogId"/>.</summary>
    Task<RecommendationDismissalEntity> Create(Guid userId, MediaWorkKind kind, string catalogId);
}
