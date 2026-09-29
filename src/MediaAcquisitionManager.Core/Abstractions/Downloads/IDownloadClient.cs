using MediaAcquisitionManager.Core.Abstractions.Integrations;

namespace MediaAcquisitionManager.Core.Abstractions.Downloads;

/// <summary>
/// A client that downloads a release the owner picked. First implemented for qBittorrent by MAM-11,
/// 13 and 14, as a further interface of its <see cref="IIntegrationAdapter"/> class. Phase A members
/// only.
/// </summary>
/// <remarks>
/// All methods return result types and never throw on expected failures (unreachable, refused,
/// unexpected answer); only a cancellation by the caller propagates, as
/// <see cref="OperationCanceledException"/>. Every message is one readable English sentence with the
/// call's secrets already masked.
/// </remarks>
public interface IDownloadClient
{
    /// <summary>What this type of client can take and report.</summary>
    DownloadClientCapabilities Capabilities { get; }

    /// <summary>The client's own destinations (its categories).</summary>
    Task<IntegrationCallResult<IReadOnlyList<ClientDestination>>> ListDestinationsAsync(IntegrationConnectionSettings settings, CancellationToken cancellationToken = default);

    /// <summary>Hands the client a release for one of its destinations.</summary>
    Task<IntegrationCallResult<SubmissionReceipt>> SubmitAsync(IntegrationConnectionSettings settings, DownloadSubmission submission, CancellationToken cancellationToken = default);

    /// <summary>
    /// The client's jobs with these ids; <paramref name="clientJobIds"/> <c>null</c> = every job, the
    /// list correlation walks when the client gave no id (specification §3.2, step 6).
    /// </summary>
    Task<IntegrationCallResult<IReadOnlyList<ClientJob>>> ListJobsAsync(IntegrationConnectionSettings settings, IReadOnlyCollection<string>? clientJobIds, CancellationToken cancellationToken = default);

    /// <summary>The files of one job, as the client sees them.</summary>
    Task<IntegrationCallResult<IReadOnlyList<ClientJobFile>>> ListJobFilesAsync(IntegrationConnectionSettings settings, string clientJobId, CancellationToken cancellationToken = default);
}
