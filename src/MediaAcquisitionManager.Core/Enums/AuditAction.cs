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

    /// <summary>
    /// MAM-24 — the owner switched replacement on from the Media folders page (ADR 0033). Written with the switch, by the
    /// page's one Save, and never for a Save that leaves it on. No target: the switch belongs to the installation.
    /// </summary>
    ReplacementSwitchedOn = 27,

    /// <summary>
    /// MAM-24 — the owner switched replacement off from the Media folders page, or saved the page without the switch,
    /// which means off. Written with the switch; never for a Save that leaves it off. No target.
    /// </summary>
    ReplacementSwitchedOff = 28,

    /// <summary>
    /// MAM-24 — the owner changed which library folders are ticked for replacement on the Media folders page (ADR 0033):
    /// one row for the whole new set, written with it, and never for a Save that leaves the set as it was. No target.
    /// </summary>
    ReplacementFoldersChanged = 29,

    /// <summary>
    /// MAM-24 — the owner scheduled the replacement of the old copies he ticked by a download (ADR 0032): written with
    /// the download job, its <see cref="DownloadRequested"/> row and the replacement's rows, in their one save, before
    /// anything is fetched or sent. Its target is the replacement job, named by the release.
    /// </summary>
    ReplacementScheduled = 30,

    /// <summary>
    /// MAM-24 — the owner confirmed, for a replacement he scheduled, that the release is a work it matched only by title
    /// and year (ADR 0032): one row per work confirmed, written with <see cref="ReplacementScheduled"/>, its target the
    /// replacement job, named by the confirmation's own words ("This release is Alien (1979)").
    /// </summary>
    ReplacementWorkConfirmed = 31,

    /// <summary>
    /// MAM-24 — a replacement ended because its sender cancelled its download from the Jobs page: written with the
    /// download's <see cref="DownloadCancelled"/> row, its target the replacement job.
    /// </summary>
    ReplacementCancelled = 32,

    /// <summary>
    /// MAM-25 — the app started checking a replacement's new files after its download finished (Downloaded to
    /// Inspecting, ADR 0035). No actor: nobody clicked, the app's own check did it — unlike ADR 0014's mirrored steps,
    /// which write no row, these are the app's own decisions. Its target is the replacement job, named by the release.
    /// </summary>
    ReplacementCheckStarted = 33,

    /// <summary>
    /// MAM-25 — every ticked copy of a replacement was paired with a checked new file of its work (Inspecting to
    /// Verified). No actor; its target the replacement job, named by the release. Nothing was moved.
    /// </summary>
    ReplacementVerified = 34,

    /// <summary>
    /// MAM-25 — the check of a replacement's new files stopped for the owner's review, the job's reason saying why
    /// (Inspecting to NeedsReview); its target the replacement job, named by the release. Both copies stay. MAM-26 — and
    /// any later stop: the mover's checks before the move, a name that couldn't move, a move, restore or deletion a
    /// restart cut (Verified, Quarantining, Restoring or Deleting to NeedsReview). No actor when the app stopped it; the
    /// owner when his own Restore or Delete for good ended in review.
    /// </summary>
    ReplacementNeedsReview = 35,

    /// <summary>
    /// MAM-25 — a replacement's new file is not a usable media file (empty, not a regular file, no video stream or no
    /// running time, or unreadable as media), so nothing will be replaced (Inspecting to Failed). No actor; its target
    /// the replacement job, named by the release.
    /// </summary>
    ReplacementFailed = 36,

    /// <summary>
    /// MAM-26 — the mover claimed a verified replacement and wrote the plan of the names it moves into the quarantine
    /// (Verified to Quarantining, ADR 0030). No actor; its target the replacement job, named by the release.
    /// </summary>
    ReplacementQuarantineStarted = 37,

    /// <summary>
    /// MAM-26 — the intent of one rename into the quarantine, committed before the call (ADR 0030): a restart between it
    /// and its outcome finds the name by device and inode. No actor; its target the name, named by the file's own name.
    /// </summary>
    ReplacementNameMoving = 38,

    /// <summary>MAM-26 — one name was renamed into the quarantine. No actor; its target the name.</summary>
    ReplacementNameMoved = 39,

    /// <summary>MAM-26 — one name was not renamed into the quarantine and stays where it was, the name's problem saying
    /// why — or, after a restart, was found neither there nor in the quarantine. No actor; its target the name.</summary>
    ReplacementNameNotMoved = 40,

    /// <summary>
    /// MAM-26 — every ticked copy's video of a replacement is in the quarantine (Quarantining to Quarantined), kept there
    /// until the owner restores it or deletes it for good. No actor; its target the replacement job, named by the
    /// release.
    /// </summary>
    ReplacementQuarantined = 41,

    /// <summary>
    /// MAM-26 — the owner pressed Retry on a replacement the mover stopped (NeedsReview to Verified): the mover's next
    /// pass checks it again. Nothing is written to the media. Its actor the owner, its target the replacement job.
    /// </summary>
    ReplacementRetried = 42,

    /// <summary>
    /// MAM-26 — the owner's Restore started putting a replacement's quarantined names back (to Restoring, ADR 0030). Its
    /// actor the owner, its target the replacement job.
    /// </summary>
    ReplacementRestoreStarted = 43,

    /// <summary>MAM-26 — the intent of one rename back out of the quarantine, committed before the call. Its actor the
    /// owner, its target the name.</summary>
    ReplacementNameRestoring = 44,

    /// <summary>MAM-26 — one name was renamed back to where it was. Its actor the owner — none when a restart's check
    /// found it there —, its target the name.</summary>
    ReplacementNameRestored = 45,

    /// <summary>MAM-26 — one name was not put back and stays in the quarantine, the name's problem saying why — or, after
    /// a restart, was found in neither place. Its actor the owner — none after a restart —, its target the name.</summary>
    ReplacementNameNotRestored = 46,

    /// <summary>
    /// MAM-26 — the owner's Restore put every quarantined name of a replacement back (Restoring to Restored): both copies
    /// are in the library again. Its actor the owner, its target the replacement job.
    /// </summary>
    ReplacementRestored = 47,

    /// <summary>
    /// MAM-26 — the owner's Delete for good, confirmed a second time, started deleting a replacement's quarantined names
    /// (Quarantined to Deleting, ADR 0030). Its actor the owner, its target the replacement job.
    /// </summary>
    ReplacementDeleteStarted = 48,

    /// <summary>MAM-26 — the intent of one deletion from the quarantine, committed before the call. Its actor the owner,
    /// its target the name.</summary>
    ReplacementNameDeleting = 49,

    /// <summary>MAM-26 — one name was deleted from the quarantine for good. Its actor the owner — none when a restart's
    /// check found it gone —, its target the name.</summary>
    ReplacementNameDeleted = 50,

    /// <summary>MAM-26 — one name was not deleted and stays in the quarantine, the name's problem saying why — or, after a
    /// restart, something else was found at its place. Its actor the owner — none after a restart —, its target the
    /// name.</summary>
    ReplacementNameNotDeleted = 51,

    /// <summary>
    /// MAM-26 — the owner's Delete for good removed every quarantined name of a replacement (Deleting to Completed): the
    /// replacement is done. Its actor the owner, its target the replacement job.
    /// </summary>
    ReplacementCompleted = 52,

    /// <summary>
    /// MAM-27 — the owner pressed Retry on a replacement whose check of the new files stopped (NeedsReview to Downloaded):
    /// the verifier's next pass checks them again from the files, and the library is asked to look at them again. Its own
    /// action, not <see cref="ReplacementRetried"/>, whose rows mean the mover's queue. Nothing is written to the media or
    /// a client. Its actor the owner, its target the replacement job, named by the release.
    /// </summary>
    ReplacementCheckRetried = 53,

    /// <summary>
    /// MAM-27 — the owner cancelled only the replacement (Keep both, to Cancelled): both copies stay, and its download is
    /// left as it is — nothing is sent to the client and no <see cref="DownloadCancelled"/> row is written, which tells it
    /// from <see cref="ReplacementCancelled"/>. Its actor the owner, its target the replacement job, named by the release.
    /// </summary>
    ReplacementCancelledAlone = 54,

    /// <summary>
    /// MAM-27 — the owner dropped one ticked old copy from its replacement: it stays where it is and never moves with this
    /// job, and the job's state is left as it was. Its actor the owner, its target the copy (its <c>ReplacementJobFiles</c>
    /// row), named by the file's own name.
    /// </summary>
    ReplacementCopyDropped = 55,
}
