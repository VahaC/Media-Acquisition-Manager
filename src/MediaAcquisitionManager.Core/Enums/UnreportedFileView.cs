namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// MAM-42 — the tabs of the Files in no library dialog besides its first, which lists every file in no library the app
/// still counts. Never stored: a query string names one by name.
/// </summary>
public enum UnreportedFileView
{
    /// <summary>The counted files the app's last look couldn't read — the "couldn't be read" the Media folders row
    /// counts.</summary>
    Problems = 1,

    /// <summary>The files the owner told the app to stop counting.</summary>
    Ignored = 2,
}
