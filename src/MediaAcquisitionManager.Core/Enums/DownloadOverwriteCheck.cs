namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// MAM-38, ADR 0034 — how a sent download was checked for files it would write over before it went to the
/// client. Stored by name on the job; <c>null</c> for a job sent before the check existed, or one the check
/// never let through (a refused one fails with the reason). A job whose submission failed after the check keeps
/// its value; the Jobs page shows no note for it.
/// </summary>
public enum DownloadOverwriteCheck
{
    /// <summary>Checked: none of its files lands on a different file already there.</summary>
    Checked = 1,

    /// <summary>Sent unchecked: a magnet link names no files until the client has fetched them.</summary>
    NotCheckedMagnet = 2,

    /// <summary>Sent unchecked: the app couldn't read the .torrent's list of files.</summary>
    NotCheckedFileList = 3,

    /// <summary>Sent unchecked: the destination's folder has no path in the app, or the client couldn't say
    /// where it is.</summary>
    NotCheckedFolderUnknown = 4,

    /// <summary>Sent unchecked: the destination's folder isn't inside the Media folders the app may read, or a link
    /// on the way to one of the release's places leads out of them.</summary>
    NotCheckedFolderOutsideMediaFolders = 5,

    /// <summary>Sent unchecked: the client couldn't list the torrents it already has in the destination, or one of
    /// them hasn't fetched its list of files yet.</summary>
    NotCheckedClientJobs = 6,

    /// <summary>
    /// Sent unchecked: a file's name holds characters the app can't look up — a control character such as DEL or
    /// one of C1, which libtorrent keeps in a name and the app's paths refuse.
    /// </summary>
    NotCheckedFileName = 7,
}
