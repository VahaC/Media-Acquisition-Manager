using MediaAcquisitionManager.Core.Recommendations;

namespace MediaAcquisitionManager.Api.Services.Recommendations;

/// <summary>
/// MAM-33 — the recommendation settings that hold (Vasyl's decision 5): a user's saved row, or
/// <see cref="RecommendationSettingsRules.Default"/> for a user who never saved. Reads only.
/// </summary>
public interface IRecommendationSettingsReader
{
    /// <summary>The settings of <paramref name="userId"/>; the defaults when they never saved, or when there is no
    /// such user.</summary>
    Task<RecommendationSettings> ReadAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The settings of every user who can sign in, in user-name order: a disabled user sees no list, so their tabs
    /// ask nothing of the catalog and their budget runs no search.
    /// </summary>
    Task<IReadOnlyList<UserRecommendationSettings>> ReadEnabledUsersAsync(CancellationToken cancellationToken = default);
}
