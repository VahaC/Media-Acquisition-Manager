namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// MAM-34 — where a library item's TMDb link stands against the library's Identify. Stored by name; the names are the
/// ones the links were stored with before the 0.2.1 rework, so "written" now means "identified" and nothing is renumbered.
/// </summary>
/// <remarks>
/// The link counts in the app from the moment it is saved, whatever this says (the ticket: "a failed or unconfirmed
/// identification leaves the link working in the app"); this only says whether the library shows the item as the link
/// says, and what the owner can do about it.
/// </remarks>
public enum LibraryLinkWriteState
{
    /// <summary>
    /// Never identified, by a rule: an episode-to-show link that names no film or show to identify the item as (without
    /// its season and episode, or with an episode TMDb gives no IMDb id), a link on a kind of item the library doesn't
    /// identify (Jellyfin: an episode item), or one of a library type that can't identify. A sync leaves it so, whatever
    /// the library reports.
    /// </summary>
    KeptInApp = 1,

    /// <summary>Waiting for the background worker (a save from the dialog, "Accept all confident", the repair). A sync
    /// leaves it so.</summary>
    Queued = 2,

    /// <summary>
    /// An identification is under way: set by a conditional claim right before the library is called. A link left so by
    /// a restart is turned into <see cref="NotWritten"/> at startup — into <see cref="KeptInApp"/> when it is never
    /// identified.
    /// </summary>
    Writing = 3,

    /// <summary>
    /// Not identified: every refusal (a library that saves beside the media, a locked item, a conflicting id), a missing
    /// match, a failure and an identification the library took but the item read again doesn't show; the link's error says
    /// which. Retry, or the repair for one where nothing was sent. A sync leaves it to them.
    /// </summary>
    NotWritten = 4,

    /// <summary>The library identified the item as the link says: the item read again showed the match, or already did.
    /// No sync has read it since.</summary>
    Written = 5,

    /// <summary>A sync read the linked id as the library's own.</summary>
    InLibrary = 6,

    /// <summary>
    /// It was <see cref="Written"/> or <see cref="InLibrary"/>, and a later sync read the item without the link's id: the
    /// library lost it (a refresh replaced its metadata). Identify it again.
    /// </summary>
    Lost = 7,
}
