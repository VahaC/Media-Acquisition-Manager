namespace MediaAcquisitionManager.Core.Enums;

/// <summary>What an audit event records. Stored by name, so reordering members never changes the
/// meaning of a stored row.</summary>
public enum AuditAction
{
    /// <summary>The first-run setup created the installation owner.</summary>
    SetupCompleted = 1,

    /// <summary>The owner created a user.</summary>
    UserCreated = 2,

    /// <summary>The owner disabled a user.</summary>
    UserDisabled = 3,

    /// <summary>The owner enabled a disabled user again.</summary>
    UserEnabled = 4,

    /// <summary>The owner set a new password for a user.</summary>
    UserPasswordReset = 5,

    /// <summary>The owner deleted a user.</summary>
    UserDeleted = 6,

    /// <summary>The owner added an integration.</summary>
    IntegrationCreated = 7,

    /// <summary>The owner changed an integration's settings (never its secrets' values, which no
    /// audit row holds).</summary>
    IntegrationUpdated = 8,

    /// <summary>The owner removed an integration.</summary>
    IntegrationDeleted = 9,

    /// <summary>The owner changed the allowed roots, the folders the app may read files in (MAM-9).
    /// The row has no target: the roots belong to the installation.</summary>
    AllowedRootsChanged = 10,

    /// <summary>The owner added a destination (MAM-11). A default it took over from another destination
    /// in the same save is part of this change, not a row of its own.</summary>
    DestinationCreated = 11,

    /// <summary>The owner changed a destination: its name, client, category or whether it is the
    /// default. A default taken off another destination by the same save is part of this change.</summary>
    DestinationUpdated = 12,

    /// <summary>The owner removed a destination.</summary>
    DestinationDeleted = 13,

    /// <summary>
    /// A user asked for a release to be sent to a download client (MAM-13). Written with the download job,
    /// before anything is fetched or sent, and never again for a retry of the same request; the job names
    /// the release, the client and the destination, and says how the sending went.
    /// </summary>
    DownloadRequested = 14,

    /// <summary>
    /// MAM-14 — a user stopped following a download job they sent. The row records the app's side only:
    /// nothing was sent to the download client, whose torrent and files stay as they were.
    /// </summary>
    DownloadCancelled = 15,

    /// <summary>
    /// MAM-16 — a user changed their own password, giving the current one; the actor and the target
    /// are that user. The owner setting someone's password, their own included, is
    /// <see cref="UserPasswordReset"/>.
    /// </summary>
    UserPasswordChanged = 16,

    /// <summary>
    /// MAM-34 — the owner linked a library item to a TMDb film or show where it had no link. Written with the link,
    /// before anything is sent to the library; its identifiers record the ids the library reported for the item
    /// (Before) and the link's (After).
    /// </summary>
    LibraryLinkSet = 17,

    /// <summary>
    /// MAM-34 — the owner replaced a library item's TMDb link with another film or show, or changed its season and
    /// episode. Written with the change, before anything is sent to the library.
    /// </summary>
    LibraryLinkReplaced = 18,

    /// <summary>
    /// MAM-34 — the owner removed a library item's TMDb link. Written with the delete, after the library was changed
    /// back where the app had written into it.
    /// </summary>
    LibraryLinkRemoved = 19,

    /// <summary>
    /// MAM-34 — the app sent a link's ids into the library for one item: written whenever the change left the app,
    /// confirmed or not. Before is what the item held then; After what it held again afterwards (or what was sent,
    /// when that read never came). Restoring the item later starts from these rows.
    /// </summary>
    LibraryIdsWritten = 20,

    /// <summary>
    /// MAM-34 — the app changed an item's ids in the library back to what it held before the app wrote them, for a
    /// link removed or replaced: only the keys that still held what the app wrote.
    /// </summary>
    LibraryIdsRestored = 21,

    /// <summary>
    /// MAM-34 — the app asked the library to look up a linked item's missing metadata with its own providers, right
    /// after writing the link's ids into it (or finding them there): one row per request the app made, whatever came of
    /// it — the link's <c>LookupError</c> says why the library didn't take it. The library fills what the item lacks and
    /// replaces nothing; the row carries no ids, as the request changes none.
    /// </summary>
    LibraryMetadataLookupRequested = 22,
}
