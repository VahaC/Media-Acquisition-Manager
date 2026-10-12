namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>
/// MAM-39 — for a library folder whose hint says <c>NotWritable</c>, the folder the app's user may not write to, whose it
/// is and whom the app runs as: what the Media folders page needs to say which PUID and PGID to set.
/// </summary>
/// <param name="Path">The folder refused, in canonical form: the library folder itself when it is that one; otherwise its
/// media folder's <c>.mam-quarantine</c> when there is one, the media folder itself while there isn't.</param>
/// <param name="OwnerUserId">The folder's owner.</param>
/// <param name="OwnerGroupId">The folder's group.</param>
/// <param name="AppUserId">The user the app runs as (its effective id).</param>
/// <param name="AppGroupId">The group the app runs as (its effective id).</param>
public sealed record ReplacementFolderWriteAccessDto(
    string Path,
    uint OwnerUserId,
    uint OwnerGroupId,
    uint AppUserId,
    uint AppGroupId);
