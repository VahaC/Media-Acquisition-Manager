namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// MAM-34 — where a library item's TMDb link stands against the library it was written into. Stored by name.
/// </summary>
/// <remarks>
/// The link counts in the app from the moment it is saved, whatever this says (the ticket: "a failed or unconfirmed
/// write leaves the link working in the app"); this only says whether the library holds the id too, and what the
/// owner can do about it.
/// </remarks>
public enum LibraryLinkWriteState
{
    /// <summary>
    /// Never written: an episode-to-show link that writes no id of its own (without its season and episode, or with an
    /// episode TMDb gives no IMDb id), a link on a kind of item the library doesn't write (Jellyfin: an episode item), or
    /// one of a library type that can't set ids. A sync leaves it so, whatever the library reports. A series' link and a
    /// numbered episode's that MAM-34 first kept here are written by the catch-up (Vasyl, 2026-10-06).
    /// </summary>
    KeptInApp = 1,

    /// <summary>Waiting for the background writer ("Accept all confident", the catch-up). A sync leaves it so.</summary>
    Queued = 2,

    /// <summary>
    /// A write is under way: set by a conditional claim right before the library is called. A link left so by a
    /// restart is turned into <see cref="NotWritten"/> at startup — into <see cref="KeptInApp"/> when it is never
    /// written.
    /// </summary>
    Writing = 3,

    /// <summary>Not in the library yet: the write failed or wasn't confirmed; the link's error says why. Retry.</summary>
    NotWritten = 4,

    /// <summary>The read after the write showed the id; no sync has read it since.</summary>
    Written = 5,

    /// <summary>A sync read the linked id as the library's own.</summary>
    InLibrary = 6,

    /// <summary>
    /// It was <see cref="Written"/> or <see cref="InLibrary"/>, and a later sync read the item without it: the library
    /// lost it (a refresh replaced its metadata). Write it again.
    /// </summary>
    Lost = 7,
}
