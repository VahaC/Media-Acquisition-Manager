# 37. A copy is removed from the Duplicates page into the quarantine on the owner's word, through replacement's gates and checks

Status: accepted (0.3.0, MAM-40)

## Context

Until MAM-40 the Duplicates page only showed ([ADR
0028](0028-duplicates-are-worked-out-on-every-read-in-four-classes-by-one-file-one-hash-one-registry-key-or-one-title-and-year-and-a-group-only-shows.md)'s
Anti-consequence, [ADR
0029](0029-a-possible-match-is-confirmed-or-dismissed-by-the-owner-alone-kept-by-its-groups-key-and-never-authorises-removal.md)):
the owner could confirm or dismiss a possible match and nothing else. The one way a copy left the
library was Phase C's replacement, which moves an old copy into the quarantine ([ADR
0030](0030-a-replaced-copy-is-renamed-into-a-quarantine-on-the-same-mount-and-stays-there-until-the-owner-restores-it-or-deletes-it-for-good.md))
only once a release downloaded to replace it is verified as its work.

On 2026-10-12, looking at the four groups production lists — The Gods Must Be Crazy (1980), «Ніж у
воді» (1962), Сексмісія (1984) and Alien: Romulus (2024), all copies of one work (ADR 0028's
**Production's copy**) — Vasyl asked how he is supposed to settle them on that page:

- **No way to remove.** In each group the better copy is already in the library; Phase C could
  take the other out only by downloading a release to replace it, which makes no sense.
- **No way to keep.** A group of one work's copies couldn't even be dismissed: ADR 0029 refused a
  decision on any class but a possible match, so a group he had looked at stayed on the list.
- **No plain words.** He couldn't tell what "Same work" means, nor "Byte-identical" or "Same file".

Vasyl's decisions of 2026-10-12: a copy is removed into the quarantine; its Restore and Delete for
good live on a tab of the Duplicates page, not on Jobs; MAM never suggests which copy is worse; Keep
both exists, and the group comes back when a new file of the work appears; the classes get plain
names; and all of it goes into 0.3.0, published again with MAM-28's and MAM-39's fixes.

What the obvious ways get wrong:

- **A removal of its own, beside replacement's gates**, would reach files the owner never ticked for
  any write — and with the media mounted read-write and the app running as their owner, the kernel
  lets the app write wherever that owner may ([ADR
  0033](0033-replacement-is-off-until-the-owner-switches-it-on-and-ticks-the-folders-it-may-touch-and-the-app-may-only-rename-there.md)).
- **Trusting the list** — the last inspection's identity, a page drawn minutes ago — would move a
  file changed or put in its place since, or the only copy of a work that plays.
- **Two removals at once** over a group's last two copies each find the other still there, and both
  go.
- **A copy a torrent seeds** would leave the torrent pointing at nothing ([ADR
  0031](0031-a-replacement-never-touches-a-torrent-and-an-old-copy-a-torrent-lists-waits-until-the-owner-removes-the-torrent.md)).
- **A quarantined name still counted** — Jellyfin and the scan list a moved file until their next
  read — would keep the group on the list after its copy is gone.
- **A removal stored as a replacement**, a `ReplacementJobs` row with no download, would make
  `DownloadJobId` nullable on a table SQLite would rebuild, which every replacement read, the Jobs
  page and their tests take to be one download's.

## Decision

**The owner's Remove this copy, on a file of an "Exact copies" (`ByteIdentical`) or "Same film,
different files" (`SameWork`) group, renames that file and its own companions into the quarantine of
the allowed folder that holds it, as a replaced copy goes (ADR 0030): behind the same switch and the
same per-folder ticks (ADR 0033), after the same look at the copy right before the move and the same
read of every download client (ADR 0031), one removal at a time. It rests there, a `CopyRemovals`
row of its own, until the owner restores it or deletes it for good under **Removed copies** on the
Duplicates page, and a name in the quarantine counts in no duplicate group. MAM never says which
copy to remove (Vasyl's decisions of 2026-10-12).**

**Only copies of those two classes.** "One file, two names" (`SameFile`) holds one file: removing a
name frees nothing until the last one goes. "Maybe the same film" (`Possible`) rests on a title and
a year, which make no copies (§3.3, ADR 0029's Anti-consequence). Their files carry no verdict and
no button; a Remove sent for one is 409 "Only a copy in “Exact copies” or “Same film, different
files” can be removed."

**The stored rules, one place for the list and the request.** `CopyRemovalRules.ReasonOf` judges a
file by what the app has stored, in this order, the first that fails giving its sentence, which
names no path, as members read it too:

1. the class, as above;
2. the switch on — "Removing copies is switched off. Turn on “Replace copies after verification”
   under Media folders.";
3. inside an allowed folder and in a ticked library folder — "This file isn't inside the Media
   folders, so the app can't move it.", "This file's library folder isn't ticked for replacement
   under Media folders.";
4. inspected and read as media, as the move expects the identity that look read — "The app hasn't
   inspected this file yet. Press Inspect files on the Library page first.", "The app's last look
   couldn't read this file as media, so it can't tell what would go.";
5. no clip of a disc folder — "This file is part of a disc folder, which can't be removed one file
   at a time.";
6. every work holding the file one of the group's — "This file also holds a film or an episode
   outside this group, which would go with it." (a two-episode file in one episode's group);
7. held by no open replacement (a copy it ticked and didn't drop) and no open removal, by path or by
   device and inode — "A replacement on the Jobs page holds this file.", "A removal under Removed
   copies holds this file.";
8. another file of the group read as media — another device and inode, never a second name of
   this very file — "No other copy in this group is one the app has read as media, so this one
   stays: it may be the only one that plays."

`GET /api/library/duplicates` gives each file of those two classes `removal: { allowed, reason }`
from these rules, the gates and the holds read once per page, for every signed-in user, and `null`
on the other classes; the page shows **Remove this copy** disabled with the sentence, a member's
own "Only the owner keeps or removes copies." first. The list still asks no download client (ADR
0018): the torrents are the request's.

**At the request: the rules again, then the disk, then the clients.** `POST
/api/replacements/removals` with `{ groupKey, fileId, deviceId, inode, sizeBytes }` works the group
out as the list does — a bare 404 for a key no group has, of any shape, or a file not in it — and
runs the eight rules again. Then, before anything is written: a platform that can't rename ("The app
can't rename files on the platform it runs on, so nothing was removed."); the identity the page
showed against the stored one ("This copy changed since the page showed it. Reload the page to see
it as it is now."); the disk, by check 5's half a replaced copy shares (`QuarantineCopyCheck`) —
path, device, inode, size and modification time, inside the root whose quarantine takes it ("“…”
is gone, so there is nothing to remove.", "“…” changed since the app last read it, so it stays where
it is. Inspect the library's files again, then press Remove again.") —; another readable copy of the
group, another file than this one, still on the disk as read ("No other copy of this group is on the
disk as the app last read it, so this one stays. Inspect the library's files again to see what is
there."); the companions,
listed as the mover lists them (`QuarantineCompanionListing`), a video that is a link refused; and
check 6 (`QuarantineHoldingCheck`), one read of every client a replacement's move reads — those
whose mappings reach the allowed folders, and every client a replacement went through (ADR 0031's
MAM-28 section) — for every name. A client not read whole refuses ("The app couldn't tell whether a
torrent in “qBittorrent” holds this copy: …"), a torrent listing the video refuses ("Still held by
the torrent “…” in “qBittorrent”: remove that torrent there without its files, then press Remove
again."), and a torrent listing a companion keeps that companion where it is, named on its row,
while the video moves. Each refusal is 409 with the sentence and keeps no row. A second name of a
torrent's file may move, as in Phase C; its link count is kept, and Delete for good counts no room
for it.

**One at a time.** `CopyRemovalTurns`, one semaphore in the process, holds a Remove from its first
check to its last rename, so the second of two requests over a group's last two copies checks what
the first left: the group is gone, and it answers 404. Restore and Delete for good don't wait there,
as neither takes a copy out of the library.

**The move.** One transaction claims it: the row `Moving`, every name of its plan as a
`CopyRemovalNames` row — the video, then its companions, a held one `Stayed` with the hold's
sentence — and `CopyRemovalStarted`, the removal held in `CopyRemovalsInHand` from before the
commit. Then name by name, the video first, as the mover does: the gates read again under the
intent's write lock (`CopyRemovalNameMoving`), one `renameat2(RENAME_NOREPLACE)` into `<allowed
folder>/.mam-quarantine/<removal id>/<path inside the folder>`, the outcome (`CopyRemovalNameMoved`
or `CopyRemovalNameNotMoved`). It ends `Quarantined` (`CopyRemoved`) once the video is there, and
the request answers 200 with the removal. A video that can't move ends it `NotMoved`
(`CopyRemovalNotMoved`) with nothing in the quarantine, and the request is 409 with "“…” couldn't be
moved into the quarantine: …": `NotMoved` means only that. A rename that moved another file — one
put at the video's place after the last look — records the video `Lost` and ends the removal
`NeedsReview` (`CopyRemovalNeedsReview`), never `NotMoved`, as the copy's own file is nowhere the
app knows: it stays listed under Removed copies with its reason, and the request is 409 with the
same sentence, which names where to look — "“…” couldn't be moved into the quarantine: A different
file now sits in the quarantine at “…”, as another file took the place of “…” just before the
rename, and this copy's own file wasn't found. Look there to see which file it is." A companion that
can't move stays, named on its row. The switch off or the folder unticked between two names stops it
`NeedsReview` while a name is in the quarantine. Jellyfin is then told the old paths went, as for a
replacement (`QuarantineNotices`).

**Restore and Delete for good, of the whole removal.** `POST …/removals/{id}/restore` and `POST
…/removals/{id}/delete-for-good` run the quarantine's one write loop (`QuarantineWriteLoop`) with a
replacement's checks of every name (`QuarantineNameChecks`) before anything is written. While
replacement is switched off each is 409 with nothing written, and the list's `switchedOff` lets the
tab disable both with the reason and a line linking to Media folders, as a replacement's page does.
A removal has no Retry, so three things differ from a replacement's: both are offered from
`NeedsReview` as well as `Quarantined` while a name is in the quarantine, so a Delete for good
stopped after its video can be finished; a write stopped part-way waits for review only while a name
is still in the quarantine, else it ends; and Delete for good takes no body — the page's second
confirmation lists every path with its size and the room it frees. Restore is refused once the video
was deleted for good ("This copy's video was deleted for good, so nothing is put back without it.
Delete for good what the quarantine still holds."). `Restored`, `Deleted` and `NotMoved` end a
removal; ended ones stay as history with their audit rows and are listed nowhere. The removals are
the installation's: the owner acts on every one, also one another owner made.

**A cut found again.** The quarantine's pass (`ReplacementQuarantineTracker`) runs
`CopyRemovalRecovery` after the replacements', in a scope of its own: a removal `Moving`,
`Restoring` or `Deleting` that no one in the process holds was cut; each name in the middle is
looked up by device and inode at both places (`QuarantineNameFinder`, shared), recorded with the
outcome row the write would have written, and the removal settled in one transaction with no actor —
from a move `NeedsReview` ("The app stopped while moving this copy: 1 of 2 names is in the
quarantine.") while a name is in the quarantine, or, when the video was found neither at its old
path nor in the quarantine (`Lost`), with "The app stopped while moving this copy, and found its
video neither at “…” nor in the quarantine at “…”. Look in both places to see where it went.", else
`NotMoved`; a restore or deletion that got through every name `Restored` or `Deleted`, else
`NeedsReview` while a name is still there, else its end with how far it got. A look that fails
decides nothing. Owed notices go on the same pass.

**A quarantined name counts in no group.** The list leaves out every name a replacement or a removal
took and hasn't given back — `Moving`, `Quarantined`, `Restoring`, `Deleting` or `Deleted`
(`ReplacementNameStates.Taken`) — matched by its old app path and the device, inode, size and
modification time it moved with — all five, against the app's last read of the library's file —, so
a new file put at that path counts, and so does one a later download got the inode of a name deleted
for good. A two-file group leaves the list at once; a larger one stays without the file; a restored
copy counts again.

**Apart from the replacements, through their code.** `AddCopyRemovals` adds `CopyRemovals` — the
requester (`SetNull`) and his name copied, the state by name, its reason and time, the group's
heading as the page showed it ("The Gods Must Be Crazy (1980)", "Clarkson's Farm · S02E03 · Title"),
its class and key, the file's id with no foreign key, the video's app and library paths,
`QuarantinedUtc`, `LibraryNoticeDueUtc`, a filtered index of the open ones — and `CopyRemovalNames`,
removed with it. The name columns moved into `QuarantinedNameEntity`, an abstract base EF doesn't map,
which `ReplacementJobNameEntity` and `CopyRemovalNameEntity` both extend, so the conditional writes,
the finder, the write loop, the checks and the notices are written once; `ReplacementJobNames` is
unchanged. `AuditAction` gains 58 to 74, `CopyRemovalStarted` to `CopyRemovalDeletedForGood`, and
`AuditTargetKind` `CopyRemoval` (10) and `CopyRemovalName` (11).

| Rejected option | Why not |
| --- | --- |
| A `ReplacementJobs` row with a nullable `DownloadJobId` | SQLite rebuilds the table; every replacement read, the Jobs page and their tests take a replacement to be one download's; Restore would live on Jobs, where Vasyl doesn't want it |
| The mover's checks and writes copied for removals | Two copies of the rules that keep a file safe drift apart; shared, a fix lands in both |
| A switch or ticks of removal's own | Two sets of gates over one write access; a tick is the owner's word on a folder, whatever moves the file |
| Remove on "One file, two names" or "Maybe the same film" | The first frees nothing; the second is no evidence of a copy (§3.3) |
| MAM suggests the copy to remove — the larger, the newer, the sharper | Vasyl's decision: never; a cut, a language or a dub is the owner's to weigh |
| Trust the list's verdict at the click | The disk and the torrents change between the page and the click |
| Several removals at once | Two requests can each leave the other the last copy |
| Delete without the quarantine | ADR 0030: no way back |

`CopyRemovalHttpTests.Remove_of_a_copy_moves_its_video_and_own_companions_into_the_quarantine_and_answers_the_removal`,
`CopyRemovalHttpTests.Remove_takes_a_group_of_two_copies_off_the_duplicates_list`,
`CopyRemovalHttpTests.Remove_of_the_last_two_copies_at_once_removes_one_and_finds_no_group_for_the_other`,
`CopyRemovalHttpTests.Remove_when_the_video_cant_be_renamed_ends_not_moved_with_nothing_in_the_quarantine_and_is_409`,
`CopyRemovalHttpTests.Remove_whose_video_rename_moves_another_file_waits_for_review_saying_where_to_look_and_is_409`,
`CopyRemovalRecoveryHttpTests.Pass_settles_a_removal_cut_while_its_video_moved_and_found_nowhere_for_review_saying_where_to_look`,
`CopyRemovalChecksHttpTests.Remove_whose_only_other_readable_member_is_a_second_name_of_the_same_file_is_409_as_the_list_says`,
`CopyRemovalListHttpTests.List_says_whether_replacing_and_removing_copies_is_switched_off_now`,
`CopyRemovalChecksHttpTests` (each rule "as the list says", the disk, the other copy, the torrents),
`CopyRemovalVerdictsHttpTests`, `CopyRemovalActionsHttpTests`, `CopyRemovalListHttpTests`,
`CopyRemovalRecoveryHttpTests`, `LibraryDuplicatesQuarantineHttpTests`,
`ReplacementsAreOwnerOnlyHttpTests`, `CopyRemovalPersistenceTests` and
`AddCopyRemovalsMigrationTests.Upgrade_keeps_every_replacement_name_whole` pin it; in the web app
`DuplicateGroupCard.remove.test.tsx`, `DuplicatesPage.removed.test.tsx` and
`copy-removals-api.test.ts`.

## Consequences

**The four groups can be settled on the page,** each by removing a copy or keeping both, with the
same gates as a replacement: turning it on is the release note's **Turning it on**, and while
replacement is off every file says so and nothing moves.

**Room comes back only at Delete for good,** as for a replaced copy; the quarantine stays outside the
database backup. The Media folders page counts a removal's names in a quarantine's room ("… from 1
replacement and 2 removals.") and refuses a Save that would strand them ("… still holds replaced or
removed copies …").

**A torrent stays the owner's.** A qBittorrent older than 5.2 can't say which torrents hold a
library file, so while one is read every Remove is refused as uncertain; a seeded copy waits until
the owner removes the torrent without its files. Jellyfin drops the card about a minute after the
move; a restored copy is identified afresh, so a TMDb link set by hand may need Identify again (ADR
0030).

**A copy an open removal holds** can't be ticked in the Download dialog, which marks it with a
reason of its own, `HeldByRemoval` — "A removal under Removed copies on the Duplicates page holds
this copy." —, and a send that ticks it is refused ("A removal under Removed copies on the
Duplicates page holds “…”, so it can't be replaced. Pick Keep both to download the release."; ADR
0032's MAM-40 section).

**Known limits.** A removal whose video got lost on its way into the quarantine stays listed as
`NeedsReview` with no button: nothing of the copy is in the quarantine for Restore or Delete for
good to act on, so it is settled outside the app, at the paths its reason names, and while it is
listed it keeps holding its copy's path — that file can't be removed or replaced. A Restore or
Delete for good whose video is lost on the way — another file at the old path during Restore, or a
restart that finds it nowhere — ends `Restored` or `Deleted` with that reason once nothing of it is
left in the quarantine, and leaves the list.

**Not met on the reference installation.** MAM-40 was built after MAM-28's check, and rests on its
tests. The page itself was checked in a browser on 2026-10-12, at desktop and phone width, on a
throwaway database with the API's answers stood in for: Remove this copy on a file's row, its
dialog, the short row it leaves, Keep both and its Undo, a copy refused with the server's reason,
Removed copies with Restore and Delete for good's dialogs, a member's disabled buttons and the
hidden tab. No file moved: the move itself rests on the HTTP tests and on the Linux tests of the
quarantine, run as root and as uid 1000.

## Anti-consequence

This licenses one owner's removal of one file and its own companions into the quarantine, nothing
more: no bulk removal, no copy picked by the app, no removal by a member or of a possible match or
one file's names, no deletion outside the quarantine, no write to a download client, and no move
while replacement is off or outside a ticked folder. Keeping both removes nothing.
