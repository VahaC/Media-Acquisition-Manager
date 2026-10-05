using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <summary>MAM-33 — a user's "Not interested" marks for a test (Vasyl's decision 3), planted without the API.</summary>
public interface IRecommendationDismissalSeeder
{
    /// <summary>Marks <paramref name="work"/> "Not interested" for <paramref name="user"/>, by its kind and catalog
    /// id.</summary>
    Task<RecommendationDismissalEntity> Dismiss(UserEntity user, CatalogWorkEntity work);
}
