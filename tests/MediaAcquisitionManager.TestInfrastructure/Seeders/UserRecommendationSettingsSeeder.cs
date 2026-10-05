using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Data;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <inheritdoc cref="IUserRecommendationSettingsSeeder"/>
public sealed class UserRecommendationSettingsSeeder(IServiceProvider services) : SeederBase(services), IUserRecommendationSettingsSeeder
{
    private IUserRecommendationSettingsDataFactory Settings => new UserRecommendationSettingsDataFactory(Db, Resolve<TimeProvider>());

    public Task<UserRecommendationSettingsEntity> Save(UserEntity user, IReadOnlyCollection<RecommendationTab> tabs, int dailySearchLimit = 60)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(tabs);
        return Settings.Create(
            user.Id,
            tabs.Contains(RecommendationTab.Movies),
            tabs.Contains(RecommendationTab.Series),
            tabs.Contains(RecommendationTab.Cartoons),
            tabs.Contains(RecommendationTab.Documentaries),
            dailySearchLimit);
    }
}
