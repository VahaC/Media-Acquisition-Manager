using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.Recommendations;

/// <summary>
/// MAM-33 — a user's own recommendation settings, from their Profile (Vasyl's decision 5): which tabs they see and how
/// many release-source searches the app may run for their lists in a day. Every user reads and saves their own, nobody
/// else's. Expected failures come back as result statuses, never as exceptions.
/// </summary>
/// <remarks>
/// A per-user preference: a save writes no audit row (the README's Audit trail lists what does), like "Not interested".
/// </remarks>
public interface IRecommendationSettingsService
{
    /// <summary><paramref name="userId"/>'s settings — the defaults, every tab on and 60 searches, until they save — with
    /// the searches run for them in the last 24 hours.</summary>
    Task<RecommendationSettingsDto> GetAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves <paramref name="request"/> as <paramref name="userId"/>'s settings; every field is required. A save that
    /// switches on a tab no other enabled user has on queues a computation (<c>TabEnabled</c>): nobody's list held that
    /// tab's works, so the last run computed none. The queuing never fails the save.
    /// </summary>
    Task<RecommendationSettingsSaveResult> SaveAsync(RecommendationSettingsRequest? request, Guid userId, CancellationToken cancellationToken = default);
}
