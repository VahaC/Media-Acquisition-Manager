using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Infrastructure.Persistence;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <inheritdoc cref="IUserRecommendationSettingsDataFactory"/>
public sealed class UserRecommendationSettingsDataFactory(ApplicationDbContext db, TimeProvider time) : IUserRecommendationSettingsDataFactory
{
    public async Task<UserRecommendationSettingsEntity> Create(
        Guid userId, bool movies, bool series, bool cartoons, bool documentaries, int dailySearchLimit)
    {
        var settings = new UserRecommendationSettingsEntity
        {
            UserId = userId,
            MoviesEnabled = movies,
            SeriesEnabled = series,
            CartoonsEnabled = cartoons,
            DocumentariesEnabled = documentaries,
            DailySearchLimit = dailySearchLimit,
            CreatedUtc = time.GetUtcNow().UtcDateTime,
        };

        db.UserRecommendationSettings.Add(settings);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return settings;
    }
}
