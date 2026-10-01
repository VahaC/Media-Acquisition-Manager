namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// What a release's title says it was made from (MAM-10): a claim, never checked against the file.
/// Never stored; read again from the title whenever it is shown (<c>ReleaseTitleClaims</c>).
/// </summary>
public enum ReleaseMedium
{
    /// <summary>A Blu-ray's streams copied unchanged into a file: BDRemux, Remux.</summary>
    BluRayRemux = 1,

    /// <summary>A Blu-ray named without saying how it was copied: Blu-ray, BluRay, BD25 to BD100 — a whole
    /// disc on Toloka ("Blu-ray CEE"), an encode in many other trackers' names.</summary>
    BluRay = 2,

    /// <summary>A Blu-ray encoded again: BDRip, BRRip.</summary>
    BluRayRip = 3,

    /// <summary>A file downloaded from a streaming service unchanged: WEB-DL.</summary>
    WebDownload = 4,

    /// <summary>A streaming service's picture recorded or encoded again: WEBRip, WEB-DLRip.</summary>
    WebRip = 5,

    /// <summary>A high-definition television broadcast: HDTV, HDTVRip.</summary>
    Hdtv = 6,

    /// <summary>A DVD's content unchanged: DVD5, DVD9, DVD.</summary>
    Dvd = 7,

    /// <summary>A DVD encoded again: DVDRip.</summary>
    DvdRip = 8,

    /// <summary>A standard-definition broadcast: TVRip, SATRip, DVBRip.</summary>
    TvRip = 9,

    /// <summary>Filmed in a cinema: CAMRip, CAM, TS, TeleSync.</summary>
    Camera = 10,
}
