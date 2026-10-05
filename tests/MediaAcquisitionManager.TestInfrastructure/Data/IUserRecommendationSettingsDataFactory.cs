using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <summary>MAM-33 — creates a user's saved recommendation settings directly in the database.</summary>
public interface IUserRecommendationSettingsDataFactory
{
    /// <summary>The saved settings row of <paramref name="userId"/>, as a Profile save would store it; values are
    /// stored as given.</summary>
    Task<UserRecommendationSettingsEntity> Create(
        Guid userId, bool movies, bool series, bool cartoons, bool documentaries, int dailySearchLimit);
}
