using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Recommendations;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Recommendations;

/// <inheritdoc cref="IRecommendationSettingsReader"/>
public sealed class RecommendationSettingsReader(ApplicationDbContext db) : IRecommendationSettingsReader
{
    public async Task<RecommendationSettings> ReadAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var row = await db.UserRecommendationSettings.AsNoTracking().SingleOrDefaultAsync(settings => settings.UserId == userId, cancellationToken);
        return ToSettings(row);
    }

    public async Task<IReadOnlyList<UserRecommendationSettings>> ReadEnabledUsersAsync(CancellationToken cancellationToken = default)
    {
        // Two queries, joined in memory: a handful of users, each with a row or none.
        var users = await db.Users.AsNoTracking()
            .Where(user => !user.IsDisabled)
            .OrderBy(user => user.NormalizedUserName)
            .Select(user => user.Id)
            .ToListAsync(cancellationToken);
        var rows = await db.UserRecommendationSettings.AsNoTracking()
            .Where(settings => users.Contains(settings.UserId))
            .ToDictionaryAsync(settings => settings.UserId, cancellationToken);
        return [.. users.Select(userId => new UserRecommendationSettings(userId, ToSettings(rows.GetValueOrDefault(userId))))];
    }

    private static RecommendationSettings ToSettings(UserRecommendationSettingsEntity? row) =>
        row is null
            ? RecommendationSettingsRules.Default
            : new RecommendationSettings(row.MoviesEnabled, row.SeriesEnabled, row.CartoonsEnabled, row.DocumentariesEnabled, row.DailySearchLimit);
}
