using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>A release sent to a download client, and how it went (MAM-13) — as its client reports it while
/// it is followed (MAM-14).</summary>
/// <remarks>
/// MAM-13 — the client's and the destination's names as they were when the job was made, so a job reads
/// the same after either is removed. Nothing that locates the payload: the release's download link stays
/// on the server (ADR 0004).
/// </remarks>
/// <param name="Id">The job's id.</param>
/// <param name="ReleaseId">The release snapshot that was sent.</param>
/// <param name="ReleaseTitle">Its title as listed.</param>
/// <param name="SourceName">The source that listed it.</param>
/// <param name="DownloadClientName">The download client it went to.</param>
/// <param name="DestinationName">The destination it went to; <c>null</c> only for a job of an older
/// build.</param>
/// <param name="ClientDestinationKey">The category that was sent — MAM-30, or the folder, for a folder job
/// (<paramref name="DestinationKind"/>), only when the installation owner reads it: <c>null</c> for anyone else, who never
/// sees a folder (ADR 0039, D13).</param>
/// <param name="Status">Where the job stands: <c>Created</c> while it is being sent (or when how that went
/// isn't known), <c>Submitted</c> once the client took it, <c>NeedsLinking</c> when the client took it but
/// which of its downloads it became has to be picked by hand, <c>Failed</c> with <paramref name="Error"/>.
/// MAM-14 — <c>Downloading</c>, <c>Paused</c> and <c>Downloaded</c> as the client reports it, and
/// <c>Cancelled</c> once its sender stopped following it.</param>
/// <param name="Progress">MAM-14 — how far the client has downloaded it, from 0 to 1; <c>null</c> until the
/// client has said.</param>
/// <param name="ClientJobId">The client's id of the download (an info hash for qBittorrent); <c>null</c>
/// while the client hasn't listed it.</param>
/// <param name="Error">Why it failed, every secret masked; <c>null</c> unless it failed.</param>
/// <param name="BlockReason">MAM-14 — why a followed job isn't moving on, every secret masked, with
/// <paramref name="Error"/>'s rule for a client's own sentence; <c>null</c> when nothing blocks it.</param>
/// <param name="OverwriteCheck">MAM-38 — how it was checked, before it was sent, for files it would write over:
/// <c>Checked</c>, or one of the <c>NotChecked…</c> reasons it was sent without; <c>null</c> for a job sent before
/// the check existed or one the check didn't let through. A job whose submission failed after the check keeps its
/// value; the Jobs page shows it only for a job that didn't fail.</param>
/// <param name="CreatedUtc">When it was sent.</param>
/// <param name="StatusChangedUtc">When <paramref name="Status"/> last changed.</param>
/// <param name="Replacement">MAM-24 — the replacement of old copies the owner scheduled with it, and where it stands;
/// <c>null</c> for a download sent with Keep both.</param>
/// <param name="DestinationKind">MAM-30 — what <paramref name="ClientDestinationKey"/> was, as the client's type said
/// when the job was made: a category or a folder (ADR 0039); <c>null</c> for a job of an older build, which sent to a
/// category.</param>
public sealed record DownloadJobDto(
    Guid Id,
    Guid ReleaseId,
    string ReleaseTitle,
    string SourceName,
    string DownloadClientName,
    string? DestinationName,
    string? ClientDestinationKey,
    DownloadJobStatus Status,
    double? Progress,
    string? ClientJobId,
    string? Error,
    string? BlockReason,
    DownloadOverwriteCheck? OverwriteCheck,
    DateTime CreatedUtc,
    DateTime StatusChangedUtc,
    DownloadJobReplacementDto? Replacement = null,
    DownloadDestinationKind? DestinationKind = null);
