using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Replacements;
using MediaAcquisitionManager.Api.Texts;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Replacements;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Mapping;

/// <inheritdoc cref="IDownloadMapper"/>
public sealed class DownloadMapper(IRequestTexts texts) : IDownloadMapper
{
    public DownloadJobDto ToDto(DownloadJobEntity job, string releaseTitle, string sourceName, bool forOwner) =>
        new(
            job.Id,
            job.ReleaseId,
            releaseTitle,
            sourceName,
            job.DownloadClientName,
            job.DestinationName,
            // MAM-30, D13 — a folder is the owner's business, as his path mappings are: a member reads a folder job's
            // destination by its name alone.
            job.DestinationKind == DownloadDestinationKind.Folder && !forOwner ? null : job.ClientDestinationKey,
            job.Status,
            job.Progress,
            job.ClientJobId,
            texts.Render(ErrorOf(job, sourceName, forOwner)),
            texts.Render(BlockReasonOf(job, forOwner)),
            job.OverwriteCheck,
            job.CreatedUtc,
            job.StatusChangedUtc,
            DestinationKind: job.DestinationKind);

    public DownloadJobFileDto ToDto(DownloadJobFileEntity file) =>
        new(file.Position, file.RelativePath, file.SizeBytes, file.Progress, file.IsWanted);

    // MAM-24 — the reason is the app's own sentence, naming nothing of a service's, and the job is its sender's — the
    // owner's, as only he schedules —, so it is shown as is. MAM-26 — Retry by the one rule its action keeps too. MAM-27 —
    // Keep both too, and the stop, the stage and the wait from the row's own times, by the rules the job's page reads: the
    // line never tells the owner something its details or the action then contradict. MAM-41 — Restore and Delete for good
    // the same way, so the Jobs row offers them by the rules the job's page and the actions apply. MAM-47 — the reason
    // rendered in the request's language (WP9).
    public DownloadJobReplacementDto ToDto(ReplacementJobEntity replacement, ReplacementLineCounts counts, bool switchedOff)
    {
        var stop = ReplacementJobProgress.StopOf(replacement.State, replacement.InspectionStartedUtc, replacement.QuarantineCheckedUtc);
        return new(replacement.State, counts.CopyCount, texts.Render(replacement.Reason), switchedOff, counts.CopiesOutsideTickedFolders)
        {
            Id = replacement.Id,
            QuarantinedCopyCount = counts.QuarantinedCopyCount,
            StayedFileCount = counts.StayedFileCount,
            CanRetry = ReplacementJobOffers.CanRetry(stop, counts.DropOnlyCopyCount),
            DroppedCopyCount = counts.DroppedCopyCount,
            DropOnlyCopyCount = counts.DropOnlyCopyCount,
            CanCancel = ReplacementJobOffers.CanCancel(replacement.State, counts.HoldsTakenNames),
            Stop = stop,
            CheckStage = ReplacementJobProgress.StageOf(replacement.State, replacement.LibraryAskedUtc),
            CheckWaitsUntilUtc = ReplacementJobProgress.CheckWaitsUntilUtc(
                replacement.State, replacement.InspectionStartedUtc, replacement.LibraryAskedUtc),
            CanRestore = ReplacementJobOffers.CanRestore(replacement.State, counts.HoldsQuarantinedNames, counts.VideoDeleted),
            CanDeleteForGood = ReplacementJobOffers.CanDeleteForGood(replacement.State, counts.HoldsQuarantinedNames),
        };
    }

    public DownloadClientOptionDto ToOption(
        IntegrationConnectionEntity client, string typeName, IReadOnlyList<DestinationMappingEntity> destinations, DownloadDestinationKind destinationKind) =>
        new(
            client.Id,
            client.Name,
            typeName,
            // MAM-30, D13 — never a folder, whoever opens the dialog: members send from it too.
            [.. destinations.Select(destination => new DownloadDestinationOptionDto(
                destination.Id,
                destination.Name,
                destinationKind == DownloadDestinationKind.Folder ? null : destination.ClientDestinationKey,
                destination.IsDefault))],
            destinationKind);

    /// <summary>
    /// MAM-10's rule for a service's sentence, as the Search and Library pages apply it: it can name the
    /// service's address or a redirect target, and the integrations are the owner's, so anyone else reads one
    /// the app writes, naming the service without what it said. The app's own sentences name nothing of the
    /// owner's and are everyone's. MAM-47 — the member's sentence is a key of its own (<c>downloads.member.*</c>),
    /// picked here and rendered in the reader's language.
    /// </summary>
    public Message? ErrorOf(DownloadJobEntity job, string sourceName, bool forOwner) =>
        job.Error is null || forOwner
            ? job.Error
            : job.ErrorOrigin switch
            {
                DownloadJobErrorOrigin.ReleaseSource => Message.Of("downloads.member.source-failed", ("source", sourceName)),
                DownloadJobErrorOrigin.DownloadClient => Message.Of("downloads.member.client-failed", ("client", job.DownloadClientName)),
                _ => job.Error,
            };

    /// <summary>
    /// MAM-14 — <see cref="ErrorOf"/>'s rule for why a followed job isn't moving on: a client's own sentence
    /// (it couldn't be asked: unreachable, refused, an answer the app couldn't read) can name its address, so
    /// anyone but the owner reads one naming the client without what it said. Every job is its sender's
    /// (MAM-14's decision 1), so this is a member reading their own job. The app's reasons — the client
    /// removed, switched off, reporting an error, no longer listing the download — are everyone's.
    /// </summary>
    public Message? BlockReasonOf(DownloadJobEntity job, bool forOwner) =>
        job.BlockReason is null || forOwner || job.BlockReasonOrigin != DownloadJobErrorOrigin.DownloadClient
            ? job.BlockReason
            : Message.Of("downloads.member.client-not-asked", ("client", job.DownloadClientName));
}
