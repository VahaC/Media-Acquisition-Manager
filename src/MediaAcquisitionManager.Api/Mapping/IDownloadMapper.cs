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

    /// <summary>A client the dialog offers: its name, what it is (<paramref name="typeName"/>) and its
    /// <paramref name="destinations"/>, in the order given.</summary>
    DownloadClientOptionDto ToOption(IntegrationConnectionEntity client, string typeName, IReadOnlyList<DestinationMappingEntity> destinations);
}
