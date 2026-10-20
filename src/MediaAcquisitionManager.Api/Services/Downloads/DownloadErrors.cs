using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.Downloads;

/// <summary>
/// MAM-24 — the sentences a download send and the replacement routes answer alike, kept in one place so the Download
/// dialog reads the same words whichever route refused it. MAM-47 (ADR 0042) — messages under the names the constants
/// had, one key each in <c>downloads.json</c>.
/// </summary>
public static class DownloadErrors
{
    /// <summary>Another user's release and a forgotten one answer alike: nothing confirms a release exists in someone
    /// else's search (ADR 0011).</summary>
    public static readonly Message ReleaseMissing = Message.Of("downloads.release-missing");

    /// <summary>The destination picked in the dialog is gone.</summary>
    public static readonly Message DestinationGone = Message.Of("downloads.destination-gone");

    /// <summary>MAM-47 — the download client picked is gone: a send's and a destination's save say it alike, one key for
    /// the one wording (design §10.1).</summary>
    public static readonly Message ClientGone = Message.Of("downloads.client-gone");
}
