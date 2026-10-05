namespace MediaAcquisitionManager.Api.Services.Recommendations;

/// <summary>
/// MAM-33 — "Not interested" (Vasyl's decision 3): a work a user marks is left out of their lists, and of the
/// availability searches run for them; nobody else's. Both calls are idempotent, so a repeated click or a retry
/// changes nothing more. Expected failures come back as result statuses, never as exceptions.
/// </summary>
/// <remarks>
/// A per-user preference, like the Profile's settings: it writes no audit row (the README's Audit trail lists what
/// does). The mark names the work by its kind and catalog id, not by its catalog row, so it outlives the row.
/// </remarks>
public interface IRecommendationDismissalService
{
    /// <summary>Marks the work <paramref name="kind"/> <paramref name="catalogId"/> "Not interested" for
    /// <paramref name="userId"/>; <see cref="RecommendationDismissalStatus.Done"/> also when it already was.</summary>
    Task<RecommendationDismissalResult> DismissAsync(string? kind, string? catalogId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Undoes <paramref name="userId"/>'s mark on the work <paramref name="kind"/> <paramref name="catalogId"/>;
    /// <see cref="RecommendationDismissalStatus.Done"/> also when there was none.</summary>
    Task<RecommendationDismissalResult> UndismissAsync(string? kind, string? catalogId, Guid userId, CancellationToken cancellationToken = default);
}
