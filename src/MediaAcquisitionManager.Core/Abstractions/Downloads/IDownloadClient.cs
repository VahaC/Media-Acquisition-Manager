using MediaAcquisitionManager.Core.Abstractions.Integrations;

namespace MediaAcquisitionManager.Core.Abstractions.Downloads;

/// <summary>
/// A client that downloads a release the owner picked. First implemented for qBittorrent by MAM-11, as
/// a further interface of its <see cref="IIntegrationAdapter"/> class. Phase A's members, and MAM-24's whole listing
/// behind the replacement gates (see the remarks).
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
/// them, rather than as members every client would have to stub until then.
/// </para>
/// <para>
/// MAM-13 — <see cref="SubmitAsync"/> comes back, and with it <see cref="FindJobsAsync"/>: the job list
/// read by the info hashes the app worked out from the payload, which is how a submission is confirmed
/// when the client gave no id (specification §3.2, step 6). It stands in for MAM-7's <c>ListJobsAsync</c>,
/// whose "every job" form would read a whole client to find one torrent; following a job by the id
/// stored on it (MAM-14) asks the same question. The files of a job (<see cref="ClientJobFile"/>) are
/// still MAM-14's.
/// </para>
/// <para>
/// MAM-14 — following a job adds two reads: <see cref="ListJobFilesAsync"/>, MAM-7's member back with its
/// own signature (ADR 0005), and <see cref="ListDestinationJobsAsync"/>, the jobs of one destination, for the
/// job a client renamed after it was sent and that its stored hashes no longer find (ADR 0013's gap). Nothing
/// more: no member removes, deletes, stops or pauses a job or its files, so cancelling a job in the app only
/// stops following it and can't reach the client (ADR 0014; <c>DownloadClientNeverRemovesTests</c> keeps it
/// so). The one write is still <see cref="SubmitAsync"/>.
/// </para>
/// <para>
/// MAM-24 — one more read, <see cref="ListJobsWithFilesAsync"/>: every job of the client with its files, the one
/// member that answers with a whole client. ADR 0031 decides it: an old copy a job lists is held by that job, so the
/// Download dialog marks it before the owner schedules its replacement. It is used only behind the replacement gates
/// (the owner's switch and ticked folders, ADR 0033), never to follow a job, which keeps its own reads (ADR 0014).
/// Still no member that removes, stops or moves anything; the one write is still <see cref="SubmitAsync"/>.
/// </para>
/// <para>
/// MAM-26 — no new member: the read right before a replacement moves an old copy into the quarantine, the one that
/// decides, is <see cref="ListJobsWithFilesAsync"/> again (ADR 0031). A held copy waits for the owner to remove its
/// torrent in the client; the app never moves, relocates or renames a job or its files, nor sets where they lie —
/// <c>DownloadClientNeverRemovesTests</c> now forbids those names too.
/// </para>
/// </remarks>
public interface IDownloadClient
{
    /// <summary>What this type of client can take and report.</summary>
    DownloadClientCapabilities Capabilities { get; }

    /// <summary>The client's own destinations (its categories).</summary>
    Task<IntegrationCallResult<IReadOnlyList<ClientDestination>>> ListDestinationsAsync(IntegrationConnectionSettings settings, CancellationToken cancellationToken = default);

    /// <summary>
    /// Hands the client a release for one of its destinations, exactly once: a submission is never sent
    /// again, by this call or a retry of it, because a write sent twice may act twice (house rule 8). A
    /// failure may still have reached the client — an answer that never came — so the caller confirms by
    /// <see cref="FindJobsAsync"/> rather than by sending again.
    /// </summary>
    Task<IntegrationCallResult<SubmissionReceipt>> SubmitAsync(IntegrationConnectionSettings settings, DownloadSubmission submission, CancellationToken cancellationToken = default);

    /// <summary>
    /// The client's jobs that hold any of <paramref name="infoHashes"/> — BitTorrent's names for a torrent,
    /// 40 hexadecimal characters for v1 and 64 for v2 — each with every info hash the client lists for it,
    /// for the caller to match (<c>ClientJobCorrelation</c>). Empty when the client has none of them.
    /// </summary>
    Task<IntegrationCallResult<IReadOnlyList<ClientJob>>> FindJobsAsync(IntegrationConnectionSettings settings, IReadOnlyCollection<string> infoHashes, CancellationToken cancellationToken = default);

    /// <summary>
    /// MAM-14 — the files of the job the client calls <paramref name="clientJobId"/>, in the client's order.
    /// Empty for a job whose files the client doesn't know yet (a magnet still fetching its metadata), and
    /// <see cref="IntegrationCallStatus.NotFound"/> when the client has no such job — gone since it was last
    /// listed, say.
    /// </summary>
    Task<IntegrationCallResult<IReadOnlyList<ClientJobFile>>> ListJobFilesAsync(IntegrationConnectionSettings settings, string clientJobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// MAM-14 — every job the client has in the destination <paramref name="destinationKey"/> (a
    /// <see cref="ClientDestination.Key"/>), each with every info hash the client lists for it, as
    /// <see cref="FindJobsAsync"/> gives them. For a job its stored hashes no longer find — one destination's
    /// jobs, not the whole client, which only MAM-24's <see cref="ListJobsWithFilesAsync"/> answers with, and only
    /// behind the replacement gates. MAM-38 — and, with <see cref="ListJobFilesAsync"/>, for the check before a send
    /// that no torrent already in the destination will write where the release's files go (ADR 0034).
    /// </summary>
    Task<IntegrationCallResult<IReadOnlyList<ClientJob>>> ListDestinationJobsAsync(IntegrationConnectionSettings settings, string destinationKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// MAM-24 — every job the client has, in any state and any destination, each with its files
    /// (<see cref="ClientJobWithFiles"/>): the jobs as <see cref="FindJobsAsync"/> reads them, and the files as
    /// <see cref="ListJobFilesAsync"/> reads them, or <c>null</c> for a job whose files the client doesn't know yet.
    /// The one member that answers with a whole client (ADR 0031): only for telling which job holds a library copy, and
    /// only behind the replacement gates (ADR 0033) — never to follow a job.
    /// </summary>
    /// <remarks>
    /// Read whole or not at all: a job the answer can't name, or a file list it can't read, fails the call rather
    /// than being left out, since a job missing from the list could hide a held copy. A client too old to list
    /// the files with the jobs is <see cref="IntegrationCallStatus.UnsupportedVersion"/> — never an answer of
    /// jobs without files, which would read as nothing held.
    /// </remarks>
    Task<IntegrationCallResult<IReadOnlyList<ClientJobWithFiles>>> ListJobsWithFilesAsync(IntegrationConnectionSettings settings, CancellationToken cancellationToken = default);
}
