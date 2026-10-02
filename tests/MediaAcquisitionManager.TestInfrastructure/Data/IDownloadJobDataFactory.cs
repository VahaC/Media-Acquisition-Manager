using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <summary>Creates download jobs directly in the database.</summary>
public interface IDownloadJobDataFactory
{
    /// <summary>
    /// A saved job that sent the release <paramref name="releaseId"/> to a client, with the client's
    /// name — and, given a destination, its name and client key — copied onto the row the way job
    /// history keeps them. MAM-13 — given a user, sent by them, under the request id given or one of its
    /// own, with the info hashes worked out from what it sent. MAM-14 — and as a previous run of the app left
    /// it: the client's id of it, how far the client said it had come, and why it wasn't moving on. Created and
    /// changed at the test clock's now; advance the clock to age it.
    /// </summary>
    Task<DownloadJobEntity> Create(
        Guid releaseId,
        Guid? downloadClientConnectionId,
        string downloadClientName,
        Guid? destinationMappingId = null,
        DownloadJobStatus status = DownloadJobStatus.Submitted,
        Guid? requestedByUserId = null,
        string? infoHashV1 = null,
        Guid? requestId = null,
        string? infoHashV2 = null,
        string? clientJobId = null,
        double? progress = null,
        string? blockReason = null,
        DownloadJobErrorOrigin? blockReasonOrigin = null);

    /// <summary>
    /// MAM-14 — gives the job <paramref name="downloadJobId"/> <paramref name="files"/> as its client last listed
    /// them, in the order given: each one's job and position are set here. Returns them saved.
    /// </summary>
    Task<IReadOnlyList<DownloadJobFileEntity>> AddFiles(Guid downloadJobId, IReadOnlyList<DownloadJobFileEntity> files);
}
