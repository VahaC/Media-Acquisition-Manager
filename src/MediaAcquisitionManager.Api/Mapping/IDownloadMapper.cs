using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.Api.Mapping;

/// <summary>Turns download jobs and the clients a release can go to into the contracts of the Download
/// dialog (MAM-13), and jobs and their files into those of the Jobs page (MAM-14).</summary>
public interface IDownloadMapper
{
    /// <summary>
    /// A job as the dialog shows it, with its release's <paramref name="releaseTitle"/> and
    /// <paramref name="sourceName"/>, read with it by the caller. A service's sentence of why it failed only
    /// when the reader is the installation owner (<paramref name="forOwner"/>); anyone else reads the app's
    /// sentence naming that service. MAM-14 — the same for the client's sentence of why it isn't moving on.
    /// </summary>
    DownloadJobDto ToDto(DownloadJobEntity job, string releaseTitle, string sourceName, bool forOwner);

    /// <summary>MAM-14 — a file of a job as its details page lists it.</summary>
    DownloadJobFileDto ToDto(DownloadJobFileEntity file);

    /// <summary>
    /// MAM-24 — the replacement a job carries, as the Jobs page shows it under the job: its state and why it stopped,
    /// with what the caller counted — its <paramref name="copyCount"/> copies, whether replacement is
    /// <paramref name="switchedOff"/> now, and how many copies lie outside a ticked folder now
    /// (<paramref name="copiesOutsideTickedFolders"/>). MAM-26 — and its id, which the owner's actions are addressed by, how
    /// many of its copies have their video in the quarantine (<paramref name="quarantinedCopyCount"/>), how many companion
    /// files stayed in their folder (<paramref name="stayedFileCount"/>), and whether the owner's Retry is offered.
    /// </summary>
    DownloadJobReplacementDto ToDto(
        ReplacementJobEntity replacement,
        int copyCount,
        bool switchedOff,
        int copiesOutsideTickedFolders,
        int quarantinedCopyCount,
        int stayedFileCount);

    /// <summary>A client the dialog offers: its name, what it is (<paramref name="typeName"/>) and its
    /// <paramref name="destinations"/>, in the order given.</summary>
    DownloadClientOptionDto ToOption(IntegrationConnectionEntity client, string typeName, IReadOnlyList<DestinationMappingEntity> destinations);
}
