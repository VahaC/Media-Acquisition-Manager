using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Replacements;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Replacements;

// The files' stage: checks 1 and 2 and the other downloads, then the one request to look, while the library wasn't asked.
public sealed partial class ReplacementVerifier
{
    /// <summary>
    /// Checks <paramref name="job"/>'s new files and, once they pass, asks their library to look at them; or stops the
    /// job, or keeps what was found for the next pass. A wait that ran out (<see cref="ReplacementVerificationRules.FilesWait"/>
    /// from the check's start) stops it for review with what is still missing.
    /// </summary>
    private async Task CheckFilesAsync(VerifyingJob job, DateTime now, CancellationToken cancellationToken)
    {
        var replacementJobId = job.Replacement.Id;
        // Every ticked copy, a dropped one too (MAM-27): it is still in the library, and a new file that is one of them is
        // still no new copy.
        var copies = await db.ReplacementJobFiles
            .AsNoTracking()
            .Where(copy => copy.ReplacementJobId == replacementJobId)
            .OrderBy(copy => copy.Position)
            .ToListAsync(cancellationToken);
        var check = await newFiles.CheckAsync(replacementJobId, job.Download, copies, now, cancellationToken);
        var (status, reason) = (check.Status, check.Reason);
        if (status == ReplacementCheckStatus.Pass)
        {
            var asked = await AskLibrariesAsync(check.NewFiles, cancellationToken);
            if (asked.Status == ReplacementCheckStatus.Pass)
            {
                // The library's hour runs from when it took the request (ADR 0035), not from this pass's start: the files'
                // checks before it — every new file read by the media inspector — may have taken minutes.
                await AskedAsync(replacementJobId, check.NewFiles, time.GetUtcNow().UtcDateTime);
                return;
            }

            (status, reason) = (asked.Status, asked.Reason);
        }

        // A row older than the column reckons from its state's time: never a wait without an end.
        var startedUtc = job.Replacement.InspectionStartedUtc ?? job.Replacement.StateChangedUtc;
        if (status == ReplacementCheckStatus.Wait && ReplacementVerificationRules.FilesWaitPassed(startedUtc, now))
            status = ReplacementCheckStatus.Review;

        Task WriteAsync() => ReplaceNewFilesAsync(replacementJobId, check.NewFiles);
        switch (status)
        {
            case ReplacementCheckStatus.Wait:
                await KeepAsync(replacementJobId, WriteAsync);
                break;
            case ReplacementCheckStatus.Failed:
                await FinishAsync(job, ReplacementJobState.Failed, reason, now, WriteAsync);
                break;
            default:
                await FinishAsync(job, ReplacementJobState.NeedsReview, reason, now, WriteAsync);
                break;
        }
    }

    /// <summary>
    /// Asks each library holding the passed <paramref name="checkedFiles"/> to look at its own paths of them, one call per
    /// library in the order of their first file; the first library's answer that isn't a pass — a review before a wait —,
    /// or a pass once every library took its request.
    /// </summary>
    private async Task<ReplacementLibraryAnswer> AskLibrariesAsync(
        IReadOnlyList<ReplacementJobNewFileEntity> checkedFiles, CancellationToken cancellationToken)
    {
        var answers = new List<ReplacementLibraryAnswer>();
        // Every file that passed has its library: the files' checks stop one that has none.
        foreach (var libraryFiles in checkedFiles.GroupBy(file => file.LibraryIntegrationId!.Value))
            answers.Add(await library.AskAsync(libraryFiles.Key, [.. libraryFiles.Select(file => file.LibraryPath!)], cancellationToken));

        return answers.FirstOrDefault(answer => answer.Status == ReplacementCheckStatus.Review)
            ?? answers.FirstOrDefault(answer => answer.Status != ReplacementCheckStatus.Pass)
            ?? answers[0];
    }

    /// <summary>
    /// Keeps the files' evidence and <paramref name="askedUtc"/>, when the libraries took the request to look, while the
    /// job is still Inspecting: from then on its passes read the library. A library asked again after a restart before
    /// this write is harmless.
    /// </summary>
    private async Task AskedAsync(Guid replacementJobId, IReadOnlyList<ReplacementJobNewFileEntity> checkedFiles, DateTime askedUtc)
    {
        var kept = await KeepAsync(replacementJobId, async () =>
        {
            await ReplaceNewFilesAsync(replacementJobId, checkedFiles);
            await db.ReplacementJobs.RecordLibraryAskedAsync(replacementJobId, askedUtc);
        });
        if (kept)
            logger.LogInformation("Replacement job {ReplacementJobId} asked its library to look at {Count} new files.", replacementJobId, checkedFiles.Count);
    }

    /// <summary>
    /// Replaces the job's new-file rows whole with <paramref name="checkedFiles"/> (their ids go with them), and takes the
    /// old copies' pairs and problems of an earlier check away: a check of the files starts the pairing over. MAM-27 — the
    /// owner's Retry of a stopped check forgets the same way (<see cref="ReplacementJobUpdates.ForgetCheckAsync"/>), so a
    /// copy's drop-only mark goes with its problem and a dropped copy keeps both as its history. Staged in the caller's
    /// transaction; the rows are added to the context, saved by its <c>SaveChanges</c>.
    /// </summary>
    private async Task ReplaceNewFilesAsync(Guid replacementJobId, IReadOnlyList<ReplacementJobNewFileEntity> checkedFiles)
    {
        await db.ForgetCheckAsync(replacementJobId);
        db.ReplacementJobNewFiles.AddRange(checkedFiles);
    }
}
