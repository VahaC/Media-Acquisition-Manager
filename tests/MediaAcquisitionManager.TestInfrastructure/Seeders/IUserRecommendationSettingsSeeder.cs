using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <summary>MAM-33 — a user's saved recommendation settings for a test (Vasyl's decision 5); a user with none has the
/// defaults, every tab on.</summary>
public interface IUserRecommendationSettingsSeeder
{
    /// <summary>Saves <paramref name="user"/>'s settings with exactly <paramref name="tabs"/> on, and
    /// <paramref name="dailySearchLimit"/> searches a day.</summary>
    Task<UserRecommendationSettingsEntity> Save(UserEntity user, IReadOnlyCollection<RecommendationTab> tabs, int dailySearchLimit = 60);
}
