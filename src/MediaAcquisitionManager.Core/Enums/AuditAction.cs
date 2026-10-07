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
    /// before the library is asked to identify the item; its identifiers record the ids the library reported for the item
    /// (Before) and the link's (After).
    /// </summary>
    LibraryLinkSet = 17,

    /// <summary>
    /// MAM-34 — the owner replaced a library item's TMDb link with another film or show, or changed its season and
    /// episode. Written with the change, before the library is asked to identify the item again.
    /// </summary>
    LibraryLinkReplaced = 18,

    /// <summary>
    /// MAM-34 — the owner removed a library item's TMDb link. Written with the delete. Since 0.2.1 the library is never
    /// called: removing a link forgets it in the app only, and what the library's Identify set stays (ADR 0026).
    /// </summary>
    LibraryLinkRemoved = 19,

    /// <summary>
    /// History only: written by the app before the 0.2.1 Identify rework, never written now; the value stays so old rows
    /// still read. It recorded that the app sent a link's ids into the library for one item, confirmed or not, with the
    /// ids the item held before and after.
    /// </summary>
    LibraryIdsWritten = 20,

    /// <summary>
    /// History only: written by the app before the 0.2.1 Identify rework, never written now; the value stays so old rows
    /// still read. It recorded that the app changed an item's ids back to what it held before the app wrote them, for a
    /// link removed or replaced.
    /// </summary>
    LibraryIdsRestored = 21,

    /// <summary>
    /// History only: written by the app before the 0.2.1 Identify rework, never written now; the value stays so old rows
    /// still read. It recorded that the app asked the library to look up a linked item's missing metadata.
    /// </summary>
    LibraryMetadataLookupRequested = 22,

    /// <summary>
    /// MAM-34 (0.2.1) — the app sent a link's match to the library's Identify for one item: written whenever Apply left
    /// the app, confirmed or not, and never for an item found already identified, a missing match, a refusal or a failure
    /// before Apply. Before and After are the item's registry ids as read before and after Apply (or the ids Identify
    /// meant to leave, when the read after it never came). It records no titles, overviews or images: there is no undo
    /// (ADR 0026). It is also the app's only record that an Apply was sent, which the repair and a series' retry read.
    /// </summary>
    LibraryItemIdentified = 23,

    /// <summary>
    /// MAM-21 — the owner confirmed a possible match of the Duplicates list, or changed a dismissal to a confirmation.
    /// Written with the decision; never for the same decision given again. It makes no works one and allows no removal
    /// (ADR 0029).
    /// </summary>
    DuplicateMatchConfirmed = 24,

    /// <summary>
    /// MAM-21 — the owner dismissed a possible match of the Duplicates list, or changed a confirmation to a dismissal.
    /// Written with the decision; never for the same decision given again.
    /// </summary>
    DuplicateMatchDismissed = 25,

    /// <summary>
    /// MAM-21 — the owner undid the decision on a possible match: the decision is forgotten and the group is asked about
    /// again. Written with the delete; never when there was no decision to undo.
    /// </summary>
    DuplicateMatchDecisionUndone = 26,
}
