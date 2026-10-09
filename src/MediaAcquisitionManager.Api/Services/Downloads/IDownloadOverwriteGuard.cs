using MediaAcquisitionManager.Api.Services.Destinations;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Search;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Services.Downloads;

/// <summary>
/// MAM-38, ADR 0034 — the check, before a release is sent, that none of its files lands on a different file
/// already in the destination's folder: a download client takes over any file it finds at a torrent's path and
/// writes the torrent's pieces into it, and nothing the app sends afterwards can stop it.
/// </summary>
public interface IDownloadOverwriteGuard
{
    /// <summary>
    /// Why the folder of <paramref name="destination"/> can't be checked — it has no path in the app, or isn't
    /// inside the Media folders —; <c>null</c> when it can. What the Download dialog warns of before Send, and
    /// where the send's own check starts.
    /// </summary>
    /// <param name="destination">The destination as its client listed it; <c>null</c> when the client couldn't
    /// list it.</param>
    /// <param name="cancellationToken">Cancels the read of the Media folders.</param>
    Task<DownloadOverwriteCheck?> FolderReasonAsync(CheckedClientDestination? destination, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether <paramref name="payload"/> may be sent to <paramref name="destination"/>: refused when one of its
    /// files would land on a different file already there, or on one a torrent the client already has will
    /// write; sent, and the job told how it was checked, otherwise.
    /// </summary>
    /// <param name="anyFileRefuses">
    /// MAM-24, ADR 0032 — a replacement's strict check: any file already at one of its places refuses it, the same bytes
    /// included — a file replaced by itself is no replacement —, told by being there, its pieces never read; and every
    /// way the check couldn't look at each place refuses it too, where a Keep both send goes unchecked: a replacement is
    /// sent only when the check looked everywhere (Vasyl's decision of 2026-10-09, never downgraded).
    /// </param>
    Task<DownloadOverwriteVerdict> CheckAsync(
        ReleasePayload payload,
        CheckedClientDestination destination,
        IntegrationConnectionEntity client,
        IDownloadClient downloadClient,
        IntegrationConnectionSettings settings,
        bool anyFileRefuses = false,
        CancellationToken cancellationToken = default);
}
