using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>
/// MAM-34 — queuing and reading the preparations of the TMDb suggestions for the library works without a TMDb id: a run
/// is queued for <see cref="LibraryLinkSuggestionRunWorker"/> (202 + run row + reconciler) by a successful library sync, a
/// metadata catalog connected or switched back on, and the owner's request. Expected failures come back as result
/// statuses, never as exceptions.
/// </summary>
/// <remarks>
/// The <c>IRecommendationRunService</c> shape: at most one run is queued at a time, and a trigger that finds one waiting
/// joins it, as that run reads the library as it is when it starts. A run already running doesn't stop a new one being
/// queued behind it — a sync that just finished may hold works the running one read before it. Queuing only writes the
/// run row: it never asks the catalog anything.
/// </remarks>
public interface ILibraryLinkSuggestionRunService
{
    /// <summary>
    /// Queues a run the installation owner <paramref name="requestedByUserId"/> asked for — R21: one that searches every
    /// work again, its cache bypassed — or joins the one waiting, which then searches again too.
    /// <see cref="LibraryLinkSuggestionRunStartStatus.NoCatalog"/> when no catalog is connected;
    /// <see cref="LibraryLinkSuggestionRunStartStatus.Unavailable"/> when the queue is full.
    /// </summary>
    Task<LibraryLinkSuggestionRunStartResult> StartAsync(Guid requestedByUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues a run for <paramref name="trigger"/>, or joins the one waiting. Quietly nothing —
    /// <see cref="LibraryLinkSuggestionRunStartStatus.NoCatalog"/> — when no catalog is connected, which is the default.
    /// </summary>
    Task<LibraryLinkSuggestionRunStartResult> EnsureQueuedAsync(LibraryLinkSuggestionRunTrigger trigger, CancellationToken cancellationToken = default);

    /// <summary>The running run when there is one, else the newest by when it was queued (a queued one included);
    /// <c>null</c> before the first.</summary>
    Task<LibraryLinkSuggestionRunDto?> GetLatestAsync(CancellationToken cancellationToken = default);
}
