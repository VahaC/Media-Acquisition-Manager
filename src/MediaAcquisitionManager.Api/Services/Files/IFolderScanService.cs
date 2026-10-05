using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.Files;

/// <summary>
/// Starting and reading scans of the media folders for the files no library reported (MAM-18, ADR 0021):
/// a run is queued for <see cref="FolderScanWorker"/> (202 + run row + reconciler) by the owner's Scan
/// folders, and by nothing else. Expected failures come back as result statuses, never as exceptions.
/// </summary>
/// <remarks>
/// At most one scan is queued at a time: a request that finds one waiting joins it, as that scan walks
/// every folder when it starts. A scan already running doesn't stop a new one being queued behind it.
/// </remarks>
public interface IFolderScanService
{
    /// <summary>
    /// Queues a scan the installation owner <paramref name="requestedByUserId"/> asked for, or joins the
    /// one waiting. <see cref="FolderScanStartStatus.NoAllowedRoots"/> when no folder is allowed;
    /// <see cref="FolderScanStartStatus.Unavailable"/> when the queue is full.
    /// </summary>
    Task<FolderScanStartResult> StartAsync(Guid requestedByUserId, CancellationToken cancellationToken = default);

    /// <summary>The run <paramref name="runId"/>, its requester, problem and error only when
    /// <paramref name="callerUserId"/> is the installation owner; <c>null</c> when there is no such run.</summary>
    Task<FolderScanRunDto?> GetRunAsync(Guid runId, Guid callerUserId, CancellationToken cancellationToken = default);

    /// <summary>Where scans stand for the Library page: the run under way, the latest finished one and the
    /// counts of the names no library reports now, the owner-only parts only <paramref name="forOwner"/>.</summary>
    Task<FolderScanOverviewDto> GetOverviewAsync(bool forOwner, CancellationToken cancellationToken = default);
}
