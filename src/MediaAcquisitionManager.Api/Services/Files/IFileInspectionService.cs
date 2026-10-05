using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Services.Files;

/// <summary>
/// Starting and reading inspections of the library's files (MAM-9): a run is queued for
/// <see cref="FileInspectionWorker"/> (202 + run row + reconciler) by the owner's Inspect files, by a
/// library sync, by a scan of the media folders (MAM-18) and by the app's start. Expected failures come
/// back as result statuses, never as exceptions.
/// </summary>
/// <remarks>
/// At most one run is queued at a time: a request that finds one waiting joins it, as that run will read
/// every file there is when it starts. A run already running doesn't stop a new one being queued behind
/// it — a sync that just finished may have named files the running one listed before it.
/// </remarks>
public interface IFileInspectionService
{
    /// <summary>
    /// Queues a run the installation owner <paramref name="requestedByUserId"/> asked for, or joins the
    /// one waiting. <see cref="FileInspectionStartStatus.NoAllowedRoots"/> when no folder is allowed;
    /// <see cref="FileInspectionStartStatus.Unavailable"/> when the queue is full.
    /// </summary>
    Task<FileInspectionStartResult> StartAsync(Guid requestedByUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues a run for <paramref name="trigger"/> — a sync's, a folder scan's or the app's start — or joins the one
    /// waiting; quietly nothing when no folder is allowed, which is the default.
    /// </summary>
    Task<FileInspectionStartResult> EnsureQueuedAsync(FileInspectionTrigger trigger, CancellationToken cancellationToken = default);

    /// <summary>The run <paramref name="runId"/>, its error and requester only when
    /// <paramref name="callerUserId"/> is the installation owner; <c>null</c> when there is no such run.</summary>
    Task<FileInspectionRunDto?> GetRunAsync(Guid runId, Guid callerUserId, CancellationToken cancellationToken = default);

    /// <summary>Where inspections stand for the Library page: whether any folder is allowed, the run under
    /// way and the latest finished one, their errors and requesters only <paramref name="forOwner"/>.</summary>
    Task<FileInspectionOverviewDto> GetOverviewAsync(bool forOwner, CancellationToken cancellationToken = default);
}
