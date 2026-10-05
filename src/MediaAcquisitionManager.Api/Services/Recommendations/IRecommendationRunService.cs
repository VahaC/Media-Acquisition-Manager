using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Services.Recommendations;

/// <summary>
/// MAM-33 — queuing and reading computations of the recommendations: a run is queued for
/// <see cref="RecommendationRunWorker"/> (202 + run row + reconciler) by a successful library sync, a metadata
/// catalog connected or switched back on, the owner's Recompute now, a tab switched on that nobody else had on,
/// and the newest releases read. Expected failures come back as result statuses, never as exceptions.
/// </summary>
/// <remarks>
/// At most one run is queued at a time: a trigger that finds one waiting joins it, as that run reads the library
/// and the settings as they are when it starts. A run already running doesn't stop a new one being queued behind
/// it — a sync that just finished may hold works the running one read before it.
/// </remarks>
public interface IRecommendationRunService
{
    /// <summary>
    /// Queues a run the installation owner <paramref name="requestedByUserId"/> asked for, or joins the one
    /// waiting. <see cref="RecommendationRunStartStatus.NoCatalog"/> when no catalog is connected;
    /// <see cref="RecommendationRunStartStatus.Unavailable"/> when the queue is full.
    /// </summary>
    Task<RecommendationRunStartResult> StartAsync(Guid requestedByUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues a run for <paramref name="trigger"/>, or joins the one waiting; <paramref name="requestedByUserId"/>
    /// names the user whose action queued it (a Profile save's tab), when there is one, and is copied with their
    /// name. Quietly nothing — <see cref="RecommendationRunStartStatus.NoCatalog"/> — when no catalog is
    /// connected, which is the default.
    /// </summary>
    Task<RecommendationRunStartResult> EnsureQueuedAsync(
        RecommendationRunTrigger trigger, Guid? requestedByUserId = null, CancellationToken cancellationToken = default);

    /// <summary>The running run when there is one, else the newest by when it was queued (a queued one included); its
    /// error and requester only when <paramref name="callerUserId"/> is the installation owner; <c>null</c> before the
    /// first.</summary>
    Task<RecommendationRunDto?> GetLatestAsync(Guid callerUserId, CancellationToken cancellationToken = default);
}
