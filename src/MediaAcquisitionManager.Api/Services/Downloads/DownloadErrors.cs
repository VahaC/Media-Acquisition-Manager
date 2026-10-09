namespace MediaAcquisitionManager.Api.Services.Downloads;

/// <summary>
/// MAM-24 — the sentences a download send and the replacement routes answer alike, kept in one place so the Download
/// dialog reads the same words whichever route refused it.
/// </summary>
public static class DownloadErrors
{
    /// <summary>Another user's release and a forgotten one answer alike: nothing confirms a release exists in someone
    /// else's search (ADR 0011).</summary>
    public const string ReleaseMissing = "This release isn't available any more: a search keeps what it found for a day. Search again.";

    /// <summary>The destination picked in the dialog is gone.</summary>
    public const string DestinationGone = "The destination you picked no longer exists. Reload the page and pick one again.";
}
