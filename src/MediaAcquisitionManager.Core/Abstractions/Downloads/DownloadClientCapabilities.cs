using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Abstractions.Downloads;

/// <summary>What a type of download client can take and report.</summary>
/// <remarks>
/// MAM-30 — five more, each a behaviour the app asks of the type rather than of its product name (ADR 0005): what its
/// destinations are, where and under which names it writes a torrent's files, the suffix an unfinished file carries,
/// whether it takes a torrent that is BitTorrent v2 only, and whether the id it gives a job can change. qBittorrent:
/// categories, any of three layouts, no suffix, v2-only taken, ids that change. Transmission: folders, its own layout,
/// <c>.part</c>, no v2-only torrent, ids that never change.
/// </remarks>
/// <param name="PayloadKinds">The releases it takes (specification §3.2, step 2).</param>
/// <param name="DestinationsHavePaths">Whether a destination resolves to a folder the app can check;
/// false turns automatic replacement off for it (§3.2).</param>
/// <param name="SubmissionReturnsJobId">Whether a submission answers with the job's id; false means
/// correlating by info hash or by the job list (§3.2, step 6).</param>
/// <param name="DestinationKind">MAM-30 — what its destinations are: its own categories, or folders the app proposes
/// and it confirms (ADR 0039).</param>
/// <param name="FilePlacement">MAM-30 — where it writes a torrent's files below a destination's folder and how it
/// names them, for the overwrite check (ADR 0034).</param>
/// <param name="PartialFileSuffix">MAM-30 — the suffix an unfinished file may carry on disk until it is complete
/// (Transmission's <c>.part</c>), which the overwrite check and the holdings look at too; <c>null</c> when it writes
/// every file under its own name from the start.</param>
/// <param name="TakesV2OnlyTorrents">MAM-30 — whether it takes a torrent that is BitTorrent v2 only, with no v1 info
/// hash; false makes the app refuse such a release before anything is listed or sent.</param>
/// <param name="JobIdsMayChange">MAM-30 — whether the id it gives a job can change after the job was sent (qBittorrent
/// on libtorrent 2 renames a v1 job to its v2 hash), so a job its stored ids no longer find is looked for among its
/// destination's jobs; false spares the client that whole listing on every pass.</param>
public sealed record DownloadClientCapabilities(
    IReadOnlyList<ReleasePayloadKind> PayloadKinds,
    bool DestinationsHavePaths,
    bool SubmissionReturnsJobId,
    DownloadDestinationKind DestinationKind,
    TorrentFilePlacement FilePlacement,
    string? PartialFileSuffix,
    bool TakesV2OnlyTorrents,
    bool JobIdsMayChange);
