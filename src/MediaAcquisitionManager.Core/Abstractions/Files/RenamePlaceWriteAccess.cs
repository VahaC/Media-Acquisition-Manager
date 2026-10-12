namespace MediaAcquisitionManager.Core.Abstractions.Files;

/// <summary>
/// MAM-39 — for a <see cref="RenamePlaceProblem.NotWritable"/> place, which folder the app's user may not write to, whose
/// it is, and whom the app runs as: what the Media folders page needs to say which PUID and PGID to set, rather than only
/// that a move would fail.
/// </summary>
/// <param name="FolderAppPath">The folder the app's user may not write to: the folder as asked when it is that one;
/// otherwise its allowed root's <c>.mam-quarantine</c> when there is one, the allowed root itself while there isn't.</param>
/// <param name="OwnerUserId">The folder's owner (<c>stx_uid</c>).</param>
/// <param name="OwnerGroupId">The folder's group (<c>stx_gid</c>).</param>
/// <param name="AppUserId">The app's effective user id, the one the kernel judged the write by.</param>
/// <param name="AppGroupId">The app's effective group id, likewise.</param>
public sealed record RenamePlaceWriteAccess(
    string FolderAppPath,
    uint OwnerUserId,
    uint OwnerGroupId,
    uint AppUserId,
    uint AppGroupId);
