using System.Linq.Expressions;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Replacements;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <inheritdoc cref="IReplacementJobFollower"/>
/// <remarks>
/// <para>
/// R6's mapping. A download Cancelled cancels its replacement from any followed state. A replacement waiting for review
/// ends Failed when its download fails, and waits otherwise. Then, for a download the strict check let through: Created
/// stays Scheduled, Submitted, Downloading, Paused and Downloaded are mirrored, Failed fails it, NeedsLinking needs
/// review. For one it didn't: Created stays Scheduled, Failed fails it as refused before it was sent, and any other —
/// a send the poller adopted after the app stopped — fails it as unchecked.
/// </para>
/// <para>
/// Information: a replacement's id and its states, never a path, a title or a sentence.
/// </para>
/// </remarks>
public sealed class ReplacementJobFollower(ApplicationDbContext db, TimeProvider time, ILogger<ReplacementJobFollower> logger)
    : IReplacementJobFollower
{
    private const string DownloadCancelledReason = "The download was cancelled, so nothing will be replaced.";

    private const string DownloadFailedReason = "The download failed, so nothing will be replaced.";

    private const string RefusedBeforeSentReason = "The download was refused before it was sent, so nothing will be replaced.";

    private const string InterruptedReason =
        "The app stopped while sending this download, before it checked where the new files would land, so nothing will be replaced.";

    private const string NeedsLinkingReason =
        "qBittorrent's downloads couldn't be told apart for this release, so the app can't follow it to verify the new file.";

    public async Task SyncAsync(CancellationToken cancellationToken = default)
    {
        var followed = await FollowedAsync(_ => true, cancellationToken);
        var now = time.GetUtcNow().UtcDateTime;
        var changes = 0;
        foreach (var replacement in followed)
        {
            // One each: a bug on one row costs that row this pass, never the others' (the poller's rule per client).
            try
            {
                if (await FollowAsync(replacement, now))
                    changes++;
            }
            catch (Exception exception) when (!(exception is OperationCanceledException && cancellationToken.IsCancellationRequested))
            {
                logger.LogError(
                    "Following replacement job {ReplacementJobId} threw {ExceptionType} {StackTrace}",
                    replacement.Id,
                    exception.GetType().FullName,
                    exception.StackTrace);
            }
        }

        // Debug, not Information: a pass runs every 2 s while anything is followed.
        if (followed.Count > 0)
            logger.LogDebug("Followed {Count} replacement jobs; {Changes} changed state.", followed.Count, changes);
    }

    public async Task SyncAsync(Guid downloadJobId, CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow().UtcDateTime;
        foreach (var replacement in await FollowedAsync(job => job.DownloadJobId == downloadJobId, cancellationToken))
            await FollowAsync(replacement, now);
    }

    /// <summary>
    /// The replacements <paramref name="which"/> picks that are still followed, each with its download's status and how
    /// that was checked before it was sent; oldest first, so a pass reads the same way every time. Filtered before the
    /// join, where EF Core can translate it, and by the followed states inlined as constants: they are then
    /// IX_ReplacementJobs_Followed's own filter, which SQLite uses only for a query naming them.
    /// </summary>
    private Task<List<FollowedReplacement>> FollowedAsync(Expression<Func<ReplacementJobEntity, bool>> which, CancellationToken cancellationToken) =>
        db.ReplacementJobs
            .AsNoTracking()
            .Where(job => EF.Constant(ReplacementJobStates.Followed).Contains(job.State))
            .Where(which)
            .Join(
                db.DownloadJobs.AsNoTracking(),
                replacement => replacement.DownloadJobId,
                download => download.Id,
                (replacement, download) => new { Replacement = replacement, download.Status, download.OverwriteCheck })
            .OrderBy(row => row.Replacement.CreatedUtc)
            .ThenBy(row => row.Replacement.Id)
            .Select(row => new FollowedReplacement(row.Replacement.Id, row.Replacement.State, row.Status, row.OverwriteCheck))
            .ToListAsync(cancellationToken);

    /// <summary>Writes where <paramref name="replacement"/> stands now, when that differs; whether it did — a cancel that
    /// got there first wins.</summary>
    private async Task<bool> FollowAsync(FollowedReplacement replacement, DateTime now)
    {
        if (Next(replacement.State, replacement.DownloadStatus, replacement.OverwriteCheck) is not { } next)
            return false;
        if (await db.ReplacementJobs.MoveAsync(replacement.Id, replacement.State, next.State, next.Reason, now) == 0)
            return false;

        logger.LogInformation("Replacement job {ReplacementJobId} went from {From} to {To}.", replacement.Id, replacement.State, next.State);
        return true;
    }

    /// <summary>
    /// Where a replacement now <paramref name="current"/> stands with its download <paramref name="download"/>, checked
    /// before it was sent as <paramref name="check"/>; <c>null</c> when that is where it is.
    /// </summary>
    private static (ReplacementJobState State, string? Reason)? Next(
        ReplacementJobState current, DownloadJobStatus download, DownloadOverwriteCheck? check)
    {
        (ReplacementJobState State, string? Reason) next = download == DownloadJobStatus.Cancelled
            ? (ReplacementJobState.Cancelled, DownloadCancelledReason)
            : current == ReplacementJobState.NeedsReview
                ? download == DownloadJobStatus.Failed ? (ReplacementJobState.Failed, DownloadFailedReason) : (current, null)
                : check == DownloadOverwriteCheck.Checked
                    ? Checked(download)
                    : Unchecked(download);
        return next.State == current ? null : next;
    }

    /// <summary>A download the strict check let through: mirrored.</summary>
    private static (ReplacementJobState State, string? Reason) Checked(DownloadJobStatus download) => download switch
    {
        DownloadJobStatus.Submitted => (ReplacementJobState.Submitted, null),
        DownloadJobStatus.Downloading => (ReplacementJobState.Downloading, null),
        DownloadJobStatus.Paused => (ReplacementJobState.Paused, null),
        DownloadJobStatus.Downloaded => (ReplacementJobState.Downloaded, null),
        DownloadJobStatus.Failed => (ReplacementJobState.Failed, DownloadFailedReason),
        DownloadJobStatus.NeedsLinking => (ReplacementJobState.NeedsReview, NeedsLinkingReason),
        // Created: checked, and its submission still under way.
        _ => (ReplacementJobState.Scheduled, null),
    };

    /// <summary>A download the strict check didn't let through — refused, or never checked at all.</summary>
    private static (ReplacementJobState State, string? Reason) Unchecked(DownloadJobStatus download) => download switch
    {
        // Still being sent: the check may come yet.
        DownloadJobStatus.Created => (ReplacementJobState.Scheduled, null),
        DownloadJobStatus.Failed => (ReplacementJobState.Failed, RefusedBeforeSentReason),
        // Taken by the client with nothing checked: a send the poller adopted after the app stopped.
        _ => (ReplacementJobState.Failed, InterruptedReason),
    };

    /// <summary>One followed replacement with what its download says.</summary>
    private sealed record FollowedReplacement(
        Guid Id, ReplacementJobState State, DownloadJobStatus DownloadStatus, DownloadOverwriteCheck? OverwriteCheck);
}
