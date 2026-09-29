using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <summary>Creates download jobs directly in the database.</summary>
public interface IDownloadJobDataFactory
{
    /// <summary>
    /// A saved job that sent the release <paramref name="releaseId"/> to a client, with the client's
    /// name — and, given a destination, its name and client key — copied onto the row the way job
    /// history keeps them.
    /// </summary>
    Task<DownloadJobEntity> Create(
        Guid releaseId,
        Guid? downloadClientConnectionId,
        string downloadClientName,
        Guid? destinationMappingId = null,
        DownloadJobStatus status = DownloadJobStatus.Submitted);
}
