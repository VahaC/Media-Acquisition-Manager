using MediaAcquisitionManager.Core.Entities;

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
}
