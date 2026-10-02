using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Downloads;

/// <summary>
/// The writes the sending makes to its job after the job is created (MAM-13), each a conditional
/// <c>UPDATE</c> of a job still <see cref="DownloadJobStatus.Created"/>: a job whose outcome is written is
/// never written again, whoever else reaches it — MAM-14's following of the client, say.
/// </summary>
/// <remarks>
/// Never cancelled, the <c>LibrarySyncRunUpdates</c> rule: once an outcome is decided it is written, even
/// while the app shuts down, or a job the client took would read as still being sent.
/// </remarks>
public static class DownloadJobUpdates
{
    /// <summary>Records the info hashes the app worked out from the payload, before the payload is sent.</summary>
    public static Task<int> RecordInfoHashesAsync(this IQueryable<DownloadJobEntity> jobs, Guid jobId, TorrentInfoHashes hashes) =>
        jobs.Where(job => job.Id == jobId && job.Status == DownloadJobStatus.Created)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(job => job.InfoHashV1, hashes.V1)
                    .SetProperty(job => job.InfoHashV2, hashes.V2),
                CancellationToken.None);

    /// <summary>
    /// Writes the job's outcome at <paramref name="changedUtc"/>: its status, the client's id of the
    /// download, and the error, cut to fit its column, with whose sentence it is; how many jobs it wrote (0
    /// or 1).
    /// </summary>
    public static Task<int> FinishAsync(
        this IQueryable<DownloadJobEntity> jobs,
        Guid jobId,
        DownloadJobStatus status,
        string? clientJobId,
        string? error,
        DownloadJobErrorOrigin? errorOrigin,
        DateTime changedUtc)
    {
        var storedError = error is null ? null : Cut(error);
        return jobs.Where(job => job.Id == jobId && job.Status == DownloadJobStatus.Created)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(job => job.Status, status)
                    .SetProperty(job => job.ClientJobId, clientJobId)
                    .SetProperty(job => job.Error, storedError)
                    .SetProperty(job => job.ErrorOrigin, errorOrigin)
                    .SetProperty(job => job.StatusChangedUtc, changedUtc),
                CancellationToken.None);
    }

    // A sentence past the column is cut with an ellipsis, so the reader sees it was cut. The column's own
    // constant, so the cut and the column can't drift apart.
    private static string Cut(string error) =>
        error.Length <= DownloadJobEntity.ErrorMaximumLength
            ? error
            : string.Concat(error.AsSpan(0, DownloadJobEntity.ErrorMaximumLength - 1), "…");
}
