using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <summary>Download jobs for a test — history of releases sent to a download client.</summary>
public interface IDownloadJobSeeder
{
    /// <summary>
    /// A job that sent a release snapshot to <paramref name="client"/>, into a destination of that
    /// client (a saved destination mapping), with the client's and the destination's names copied onto
    /// the job the way job history keeps them.
    /// </summary>
    Task<DownloadJobEntity> Create(IntegrationConnectionEntity client);

    /// <summary>
    /// A job that sent the stored release <paramref name="releaseId"/> — one a search found, say — to
    /// <paramref name="client"/>, the same way (MAM-12). MAM-33 — standing at <paramref name="status"/>, sent by
    /// <paramref name="requestedBy"/> or by nobody named.
    /// </summary>
    Task<DownloadJobEntity> Create(
        IntegrationConnectionEntity client,
        Guid releaseId,
        DownloadJobStatus status = DownloadJobStatus.Submitted,
        UserEntity? requestedBy = null);
}
