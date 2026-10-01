using MediaAcquisitionManager.Core.Abstractions.Integrations;

namespace MediaAcquisitionManager.Core.Abstractions.Downloads;

/// <summary>
/// A client that downloads a release the owner picked. First implemented for qBittorrent by MAM-11, as
/// a further interface of its <see cref="IIntegrationAdapter"/> class. Phase A members only.
/// </summary>
/// <remarks>
/// <para>
/// All methods return result types and never throw on expected failures (unreachable, refused,
/// unexpected answer); only a cancellation by the caller propagates, as
/// <see cref="OperationCanceledException"/>. Every message is one readable English sentence with the
/// call's secrets already masked.
/// </para>
/// <para>
/// MAM-11 — only what MAM-11 implements: the capabilities and the destinations. Submitting a release
/// (MAM-13) and reading the client's jobs and their files (MAM-14) join with the tickets that implement
/// them, rather than as members every client would have to stub until then; the records they take and
/// give (<see cref="DownloadSubmission"/>, <see cref="SubmissionReceipt"/>, <see cref="ClientJob"/>,
/// <see cref="ClientJobFile"/>, <see cref="ClientJobState"/>) already wait here for them.
/// </para>
/// </remarks>
public interface IDownloadClient
{
    /// <summary>What this type of client can take and report.</summary>
    DownloadClientCapabilities Capabilities { get; }

    /// <summary>The client's own destinations (its categories).</summary>
    Task<IntegrationCallResult<IReadOnlyList<ClientDestination>>> ListDestinationsAsync(IntegrationConnectionSettings settings, CancellationToken cancellationToken = default);
}
