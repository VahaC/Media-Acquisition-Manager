namespace MediaAcquisitionManager.Core.Abstractions.Files;

/// <summary>
/// What one entry of a walk through the allowed roots is (MAM-18, ADR 0021): a media file name, or a place
/// the walk didn't go into or through, and whether what it found there before still counts.
/// </summary>
public enum FileListingStatus
{
    /// <summary>A media file name: its path under the root as the owner typed it, and where it really is.</summary>
    MediaFile = 1,

    /// <summary>
    /// A root or folder the walk couldn't list now — missing, an empty root, not allowed, changed while it
    /// was being checked, a listing that failed half-way, a link that leads nowhere now. What was found
    /// under it before is kept: a mount that is away costs no names.
    /// </summary>
    Unlisted = 2,

    /// <summary>
    /// A folder, link or name the walk refuses — a link leading out of the roots, a loop, a name the app
    /// can't use, a path too long. Nothing under it is a name of the media folders.
    /// </summary>
    Skipped = 3,

    /// <summary>The walk stopped at its limit, so what it gave is incomplete and must not replace anything.</summary>
    LimitReached = 4,
}
