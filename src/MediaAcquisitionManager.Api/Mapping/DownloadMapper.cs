using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Replacements;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Mapping;

/// <inheritdoc cref="IDownloadMapper"/>
public sealed class DownloadMapper : IDownloadMapper
{
    public DownloadJobDto ToDto(DownloadJobEntity job, string releaseTitle, string sourceName, bool forOwner) =>
        new(
            job.Id,
            job.ReleaseId,
            releaseTitle,
            sourceName,
            job.DownloadClientName,
            job.DestinationName,
            job.ClientDestinationKey,
            job.Status,
            job.Progress,
            job.ClientJobId,
            ErrorFor(job, sourceName, forOwner),
            BlockReasonFor(job, forOwner),
            job.OverwriteCheck,
            job.CreatedUtc,
            job.StatusChangedUtc);

    public DownloadJobFileDto ToDto(DownloadJobFileEntity file) =>
        new(file.Position, file.RelativePath, file.SizeBytes, file.Progress, file.IsWanted);

    // MAM-24 — the reason is the app's own sentence, naming nothing of a service's, and the job is its sender's — the
    // owner's, as only he schedules —, so it is shown as is. MAM-26 — Retry by the one rule its action keeps too.
    public DownloadJobReplacementDto ToDto(
        ReplacementJobEntity replacement,
        int copyCount,
        bool switchedOff,
        int copiesOutsideTickedFolders,
        int quarantinedCopyCount,
        int stayedFileCount) =>
        new(replacement.State, copyCount, replacement.Reason, switchedOff, copiesOutsideTickedFolders)
        {
            Id = replacement.Id,
            QuarantinedCopyCount = quarantinedCopyCount,
            StayedFileCount = stayedFileCount,
            CanRetry = ReplacementJobOffers.CanRetry(replacement.State, replacement.QuarantineCheckedUtc),
        };

    public DownloadClientOptionDto ToOption(IntegrationConnectionEntity client, string typeName, IReadOnlyList<DestinationMappingEntity> destinations) =>
        new(
            client.Id,
            client.Name,
            typeName,
            [.. destinations.Select(destination =>
                new DownloadDestinationOptionDto(destination.Id, destination.Name, destination.ClientDestinationKey, destination.IsDefault))]);

    /// <summary>
    /// MAM-10's rule for a service's sentence, as the Search and Library pages apply it: it can name the
    /// service's address or a redirect target, and the integrations are the owner's, so anyone else reads one
    /// the app writes, naming the service without what it said. The app's own sentences name nothing of the
    /// owner's and are everyone's.
    /// </summary>
    private static string? ErrorFor(DownloadJobEntity job, string sourceName, bool forOwner) =>
        job.Error is null || forOwner
            ? job.Error
            : job.ErrorOrigin switch
            {
                DownloadJobErrorOrigin.ReleaseSource =>
                    $"“{sourceName}” couldn't hand over the release, so nothing was sent. Ask the owner to check “{sourceName}” under Integrations.",
                DownloadJobErrorOrigin.DownloadClient =>
                    $"“{job.DownloadClientName}” couldn't take the release, so nothing was sent. Ask the owner to check “{job.DownloadClientName}” under Integrations.",
                _ => job.Error,
            };

    /// <summary>
    /// MAM-14 — <see cref="ErrorFor"/>'s rule for why a followed job isn't moving on: a client's own sentence
    /// (it couldn't be asked: unreachable, refused, an answer the app couldn't read) can name its address, so
    /// anyone but the owner reads one naming the client without what it said. Every job is its sender's
    /// (MAM-14's decision 1), so this is a member reading their own job. The app's reasons — the client
    /// removed, switched off, reporting an error, no longer listing the download — are everyone's.
    /// </summary>
    private static string? BlockReasonFor(DownloadJobEntity job, bool forOwner) =>
        job.BlockReason is null || forOwner || job.BlockReasonOrigin != DownloadJobErrorOrigin.DownloadClient
            ? job.BlockReason
            : $"“{job.DownloadClientName}” couldn't be asked about this download. Ask the owner to check “{job.DownloadClientName}” under Integrations.";
}
