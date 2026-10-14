using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-38, ADR 0034 — whether a release sent to a destination is checked for files it would write
/// over.</summary>
/// <param name="NotCheckedReason">Why its folder can't be checked — <c>NotCheckedFolderUnknown</c> or
/// <c>NotCheckedFolderOutsideMediaFolders</c>, or (MAM-30) <c>NotCheckedFolderNotSeen</c>, a folder its client can't
/// see, where nothing is sent at all —, which the dialog warns of before Send; <c>null</c> when it can be. A reason,
/// never a path: the Media folders describe the host and are the owner's to read.</param>
/// <param name="FolderUnknownCause">MAM-31 — for <c>NotCheckedFolderUnknown</c>, why the folder is unknown, so the warning
/// names the client and the fix; <c>null</c> for every other reason, and when no cause has a fix to name (a client that
/// says no folder for the category, mappings that send the folder to different places).</param>
/// <param name="ClientName">MAM-31 — the name of the destination's download client whenever
/// <paramref name="FolderUnknownCause"/> is given; <c>null</c> otherwise.</param>
/// <param name="ClientError">MAM-31 — for <see cref="DownloadFolderUnknownCause.ClientNotAsked"/>, the client's own
/// sentence saying why it couldn't be asked and what to do, every secret masked. For the installation owner only, as it
/// can name the client's address (MAM-10's rule); <c>null</c> for everyone else and for every other cause.</param>
public sealed record DownloadOverwriteCheckResponse(
    DownloadOverwriteCheck? NotCheckedReason,
    DownloadFolderUnknownCause? FolderUnknownCause = null,
    string? ClientName = null,
    string? ClientError = null);
