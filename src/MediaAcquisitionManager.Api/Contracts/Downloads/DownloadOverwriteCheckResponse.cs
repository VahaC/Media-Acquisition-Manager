using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-38, ADR 0034 — whether a release sent to a destination is checked for files it would write
/// over.</summary>
/// <param name="NotCheckedReason">Why its folder can't be checked — <c>NotCheckedFolderUnknown</c> or
/// <c>NotCheckedFolderOutsideMediaFolders</c>, or (MAM-30) <c>NotCheckedFolderNotSeen</c>, a folder its client can't
/// see, where nothing is sent at all —, which the dialog warns of before Send; <c>null</c> when it can be. A reason,
/// never a path: the Media folders describe the host and are the owner's to read.</param>
public sealed record DownloadOverwriteCheckResponse(DownloadOverwriteCheck? NotCheckedReason);
