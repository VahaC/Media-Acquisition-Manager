namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>MAM-34 — where one write of a claimed link (<see cref="ILibraryLinkWriter.WriteAsync"/>) left it.</summary>
public enum LibraryLinkWriteStatus
{
    /// <summary>The library holds the link's ids: the read after the write showed them, or the item held them already;
    /// the library was then asked to look the item up.</summary>
    Written = 1,

    /// <summary>The library doesn't hold them (yet): the write failed, wasn't confirmed, or the library is switched off;
    /// the link's error says why.</summary>
    NotWritten = 2,

    /// <summary>The link isn't written into its library — an episode's link that writes no id of its own, a link on a
    /// kind of item the library doesn't write (Jellyfin: an episode item), or one of a library type that can't set ids;
    /// whatever the app had written for it before was put back first.</summary>
    KeptInApp = 3,

    /// <summary>The link was no longer this write's to settle — removed, or claimed again — so nothing was changed on it.</summary>
    ClaimLost = 4,
}
