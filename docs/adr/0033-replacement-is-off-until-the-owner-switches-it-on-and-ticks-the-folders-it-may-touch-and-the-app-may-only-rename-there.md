# 33. Replacement is off until the owner switches it on and ticks the folders it may touch, and the app may only rename there

Status: accepted (Phase C, MAM-23)

## Context

Specification §4: "Read-only search needs read access, whereas managed replacement requires
separately configured write access limited to authorized roots." ADR 0009 made the allowed folders
"not a licence to write". The house rule for a dangerous feature is off by default and two gates —
a master switch kept in the database and an opt-in per target — with a confirmation and an audit
row for every destructive action.

What the gates have to fit:

- **The mount.** The published `docker-compose.yml` mounts the media folders read-only, and the
  kernel then refuses every rename (`EROFS`). Production mounts `/media` read-write today (MAM-22),
  though nothing writes there. A rename stays atomic only within one mount ([ADR
  0030](0030-a-replaced-copy-is-renamed-into-a-quarantine-on-the-same-mount-and-stays-there-until-the-owner-restores-it-or-deletes-it-for-good.md)):
  `/media` read-only with read-write mounts of chosen library folders would make every move into the
  quarantine at the top of `/media` fail with `EXDEV`, so the mount can't be the per-folder limit.
- **The allowed folders.** Production allows one folder, `/media`. Allowed folders can't nest and
  hold only a path, and the Media folders page saves the whole list at once. An opt-in per allowed
  folder would switch all four libraries on together.
- **Who acts.** Every signed-in user may send a download (ADR 0013), a job stays its sender's and
  another user's is a bare 404, for the owner too (ADR 0014), and only the owner decides on
  duplicates (ADR 0029). The library is shared.
- **Other writers the app already holds.** The Jellyfin API key gets Jellyfin's administrator role,
  and Jellyfin's `DELETE /Items/{id}` deletes a film's whole folder — on production Jellyfin runs as
  root with `/media` read-write —; qBittorrent's Web API has no roles. A read-only mount of the app
  stops neither.
- **The permission was unknown.** Renaming a file needs write and search permission on its folder
  and on the quarantine, qBittorrent creates the library's folders, and a file whose owner an
  unprivileged LXC can't map can't be renamed by anyone in it. None of that was recorded, so
  `tools/mam23/facts.py` read it on the media host on 2026-10-08, writing nothing: the LXC is
  privileged (its id map is the identity, so no uid is shifted); every file under `/media` and all
  but four of its folders (root's) belong to uid 1000 and group `users` (100) — 1,454 files, 1,350
  of them 644, and 154 folders, 140 of them 755 or 2755 and 14, `movies` and `tv-shows` among them,
  2775 —, none to an id the LXC can't map, no folder sticky, no file immutable; qBittorrent runs as
  1000 and 100 with umask 022, so the folders it creates are writable by their owner alone; and the
  app runs as the image's uid 10001, with write permission on none of `/media`, the six folders in
  it and their 92 subfolders. The ext4 file system has 5.3 TiB of its 13 TiB free, and no ACL tool
  is installed.

Vasyl's decisions of 2026-10-08: only the owner schedules, restores and deletes for good; and, the
permission measured first, the app runs as the media folders' owner. Stated without objection: a
master switch kept in the app, off by default, plus a tick per library folder, and the compose file
keeps `:ro` until the owner switches replacement on.

## Decision

**A replacement moves nothing until four things hold: the media folders are mounted read-write, the
app runs as their owner (`APP_USER_ID` and `APP_GROUP_ID`, 1000 and 100 here), the owner has
switched replacement on — a setting kept in the database, off by default —, and the owner has ticked
the library folder that holds the old copy; only the owner schedules, retries, resolves, restores
and deletes for good, and the app's only writes to the media folders are the quarantine's folders,
the renames into and out of it, and the deletions in it (Vasyl's decisions of 2026-10-08).**

**The mount comes first.** With `:ro` no setting can move a file. Switching replacement on starts
with mounting the media folders read-write, each allowed folder as one mount with nothing else
mounted inside it, so that its library folders and its quarantine (ADR 0030) share that mount — on
the reference installation the one `/media` mount. The compose file keeps `:ro` and says what
changes for replacement.

**The switch is the owner's, in the app.** On the Media folders page, saved by its one Save with an
audit row. Off — the default, and the state after an upgrade — the Download dialog offers **Keep
both** only, saying why; jobs already scheduled keep their downloads and their checks but move,
restore and delete nothing, and say so. Every move, restore and deletion reads the switch again
right before it writes.

**A tick per library folder.** Beside the switch, the page lists the library folders the app knows —
each Jellyfin library's location that lies inside an allowed folder (`/media/movies`,
`/media/cartoons`, `/media/documentary`, `/media/tv-shows`) — each with a tick, none ticked by
default. An old copy outside every ticked folder can't be picked in the dialog, and is checked again
before the move (check 5). The quarantine belongs to the allowed folder (ADR 0030) and needs no
tick. A folder that leaves the libraries or the allowed folders loses its tick's effect.

**The owner's alone.** The dialog's **Replace after verification** shows for the owner; a member
sees **Keep both** with "Only the installation owner can replace a copy." Retry, resolving review,
cancelling a pending removal, Restore and Delete for good are the owner's too, enforced as the other
owner-only writes are, by a test that discovers the routes. The owner's replacement jobs are the
owner's own, so ADR 0014's rule that a job is its sender's still holds.

**Nothing else is written.** No folder of the library is created, renamed or removed — but
`.mam-quarantine` and its job folders, inside a library folder only where that folder is itself an
allowed folder —, no permission or owner is changed, no file is created or edited; nothing is
written to a download client ([ADR
0031](0031-a-replacement-never-touches-a-torrent-and-an-old-copy-a-torrent-lists-waits-until-the-owner-removes-the-torrent.md))
and Jellyfin only hears `POST /Library/Media/Updated` (ADR 0030). The write contract is a separate
one beside `IFileInventory`, which stays read-only by construction, with a name guard of its own.

**The app checks before it offers.** For each ticked folder the page says whether a rename can work
there: the same mount as the quarantine (`STATX_MNT_ID`), read-write in the mount table, and write
and search permission for the app's user. This is a hint for the owner; the rename itself decides,
and its error stops the job (ADR 0030).

**The app runs as the media's owner** (Vasyl's decision of 2026-10-08, on the measurement). The
compose file runs it as `${APP_USER_ID:-10001}:${APP_GROUP_ID:-10001}`; the reference installation
sets 1000 and 100, qBittorrent's PUID and PGID. Not one of the owner's files or folders changes
owner, group or mode for it: a renamed file keeps its owner and mode, and the quarantine folders the
app creates belong to 1000 and `users` like everything around them. The data volume, created under
10001, is handed to the new ids once with the container stopped — `docker compose run --rm --no-deps
--user 0:0 --entrypoint chown app -R 1000:100 /data`, which `.env.example` gives and which MAM-23
tried on a throwaway volume (modes 0700 and 0600 kept, the secrets readable by 1000) —, and back the
same way. The price: under those ids the kernel lets the app write wherever qBittorrent may, every
folder it owns — all of the mount but root's four —, so the switch, the ticks, ADR 0009's walk
before every write and the write contract are what keep a replacement to the ticked folders. The
default stays 10001, which can write nowhere in the media, for every installation that never turns
replacement on.

| Rejected option | Why not |
| --- | --- |
| A master switch in an environment variable | The house rule keeps it in the database beside the per-folder ticks; the read-write mount is already the step only the host's administrator can take |
| A tick per allowed folder | Production has one, `/media`: one tick would open all four libraries, and allowed folders can't nest |
| `/media` read-only plus read-write mounts of the ticked folders | The quarantine sits at the top of the allowed folder `/media`, on the read-only mount: every move into it fails with `EXDEV` |
| Every user replaces the copies of their own downloads | Anyone could take a file out of the shared library |
| Group `users` for the app, group write on the library's folders and qBittorrent's umask 002 | Changes the mode of about a hundred of the owner's folders and qBittorrent's settings, and every folder qBittorrent creates afterwards, to buy a kernel limit the app's gates already hold |
| ACLs for uid 10001 on the ticked folders | Writes ACLs onto the owner's folders, needs the `acl` package on the host, and is the hardest of the three ways to grant the permission to see |

## Consequences

**Turning it on takes four steps,** and the release note's "Turning it on" section lists them:
mount the media folders read-write; set `APP_USER_ID` and `APP_GROUP_ID` to their owner's ids and
hand the data volume over once; switch replacement on; tick the folders. On production the mount is
already read-write, so after the ids the switch and the ticks are what stand between the app and the
files.

**Members lose nothing they had**: downloads work as before, and Keep both stays the default for
everyone.

**Code to come.** MAM-24 stores the switch and the ticks, shows them on the Media folders page and
reads them in the dialog; MAM-26 rechecks them before each write and adds the per-folder rename hint
and the write contract; their HTTP tests must show a member refused on
every replacement route, the switch off and an unticked folder each moving nothing, and the audit
rows.

## Anti-consequence

The switch and the ticks open renames of the copies the owner picks, nothing else: no bulk action,
no replacement the owner didn't schedule, and no write outside the ticked folders and the
quarantine.

## The switch, the ticks and the owner's routes, as built (added 0.3.0, MAM-24)

**Storage.** The switch is the one row of `ReplacementSettings`, keyed like the installation's row,
and none until the owner first switches it on: no row is off, the state of a fresh installation and
of an upgraded one, so an upgrade opens no gate. The ticks are `ReplacementFolders`, keyed by the
folder's canonical path in the app, never by a library-folder row, which every sync deletes and adds
again. Whether a tick counts is worked out when asked — the path must still be a library folder the
mappings bring into the app, inside an allowed folder —, so "a folder that leaves the libraries or
the allowed folders loses its tick's effect" holds without the row being touched, and the effect
comes back if the folder does. Both are read fresh for every page, dialog read, schedule and list of
jobs, never cached (`IReplacementGates`).

**Every library folder holding a copy must be ticked.** A copy lies in a ticked folder only when
some library folder holds it and every library folder that holds it is ticked and inside the Media
folders: `/media/movies` ticked and `/media/movies/kids`, a library of its own, unticked keeps the
kids' copies out of reach. A library folder outside the allowed folders — an ancestor of the one
allowed — can't be ticked on the page, and a tick it has counts for nothing (`OutsideMediaFolders`),
so a copy under it stays out of reach too: fail closed.

**Fail closed on a save.** The Media folders page's one Save sends the folders, the switch and the
ticks in one request, saved in one transaction. A missing switch is off and a missing list of ticks
none, as a missing list of folders allows nothing, so a page of 0.2.1 that sends `{ paths }` alone
switches replacement off. A tick must be a library folder the mappings bring into the app or one
already stored — a stale tick may stay until unticked, but no new arbitrary path can be added —, at
most 50, each once; the membership rule and the change check run inside the transaction, on a read
made there.

**Three audit actions.** `ReplacementSwitchedOn` or `ReplacementSwitchedOff` when the switch
changed, one `ReplacementFoldersChanged` for a changed set of ticks, beside `AllowedRootsChanged`,
each with no target — the switch and the ticks are the installation's —, and none for a Save that
changes nothing (MAM-4's rule).

**The page.** A second card, **Replacing copies**, inside the same form: the switch **Replace copies
after verification**, with a hint that this version only schedules, and **Library folders
replacement may touch**, each folder with its libraries. An unticked folder outside the Media
folders is disabled, a ticked one says its tick has no effect until it is inside them, a stored tick
that is no longer a library folder is listed ticked so it can be unticked, and the folders the
mappings leave without a path are counted. The ticks stay editable while the switch is off. The
per-folder hint of whether a rename can work there is MAM-26's.

**The owner's alone, by where the routes live.** Every action under `Controllers/Replacements`,
reads included, gets the owner filter from `ReplacementsAreOwnerOnlyConvention`, the
`Controllers/Library/Links` way, not from an attribute each action must remember; the dialog's read
shows the owner's ticks, paths and torrents, so it is the owner's too. A member's dialog never asks,
and its Replace is disabled with "Only the installation owner can replace a copy.".
`ReplacementsAreOwnerOnlyHttpTests` is the test that discovers the routes this Decision asked for:
it finds every endpoint in that namespace in the routing table, holds today's two among them, and
sends each as a member (403, nothing written, nothing asked of qBittorrent) and without a token
(401).

**Off is said on the Jobs page.** A job's `replacement` carries whether replacement is switched off
now and how many of its copies lie in no ticked folder now, worked out from the gates once per page,
and while its download goes on the Jobs line says "Replacement scheduled, but replacement is
switched off: nothing will move while it is off." or how many copies won't move — the "say so" of
jobs already scheduled.

**The mount, for this version.** Scheduling writes nothing to the media folders, so it needs only
the switch and the ticks: the compose file keeps `:ro`, and the read-write mount and `APP_USER_ID`
and `APP_GROUP_ID` are needed once MAM-26 moves files.

`ReplacementSettingsHttpTests.Get_on_a_fresh_installation_is_off_with_no_library_folders`,
`ReplacementSettingsHttpTests.Update_switching_on_and_ticking_folders_is_204_and_writes_the_rows_and_one_audit_row_per_change`,
`ReplacementSettingsHttpTests.Update_that_changes_nothing_is_204_and_records_nothing`,
`ReplacementSettingsHttpTests.Update_without_the_replacement_fields_switches_off_and_unticks_every_folder`,
`ReplacementSettingsHttpTests.Update_ticking_a_path_the_page_never_lists_is_400_and_changes_nothing`,
`ReplacementSettingsHttpTests.Get_lists_a_stored_tick_that_is_no_longer_a_library_folder_ticked_so_it_can_be_unticked`,
`ReplacementSettingsHttpTests.Update_racing_another_first_switch_on_writes_one_switch_row_and_both_are_204`,
`ReplacementOptionsHttpTests.Options_keeps_a_copy_in_an_unticked_library_nested_in_a_ticked_one_out_of_reach`,
`ReplacementOptionsHttpTests.Options_with_replacement_switched_off_is_SwitchedOff_and_reads_no_client_no_work_and_no_disk`,
`ReplacementsAreOwnerOnlyHttpTests.Discovery_finds_every_replacement_endpoint_of_today`,
`ReplacementsAreOwnerOnlyHttpTests.Every_replacement_endpoint_as_a_signed_in_non_owner_is_403_with_the_owner_sentence`
and
`ReplacementFollowingHttpTests.Get_of_a_job_says_when_replacement_is_off_and_its_copies_are_no_longer_in_a_ticked_folder`
pin it, and `MediaFoldersPage.test.tsx` the page.

## The checks run while switched off (added 0.3.0, MAM-25)

**The check of a replacement's new files runs whatever the switch and the ticks say: "jobs already
scheduled keep their downloads and their checks but move … nothing, and say so" ([ADR
0035](0035-a-replacements-new-file-is-checked-by-its-clients-word-the-media-inspector-and-the-librarys-ids-before-anything-moves-and-what-is-unsettled-after-an-hour-stops-for-review.md)).**
A job checked with replacement off ends `Verified` as it would with it on, and its line says so
while it is scheduled, checked or verified — "Replacement checks the new file, but replacement is
switched off: nothing will move while it is off." and "New file verified, but replacement is
switched off: nothing will move while it is off." —, with the count of copies no longer in a ticked
folder on these states too; a stopped replacement (needs review, failed or cancelled) goes no
further until the owner acts, so it says none of it, though one in review still holds its copies.
The check only reads the media folders, so the compose file's `:ro` stays right and neither the
read-write mount nor `APP_USER_ID` and `APP_GROUP_ID` are needed yet; Jellyfin hears only
`POST /Library/Media/Updated`, as the Decision says. MAM-26 reads the switch and the ticks again
before each write.

`ReplacementVerificationHttpTests.Verify_while_replacement_is_switched_off_still_verifies_and_moves_nothing`
pins it, and `JobsPage.test.tsx` the lines.

## The gates before each write, the per-folder hint and the owner's own actions, as built (added 0.3.0, MAM-26)

**The switch and the ticks are read again right before every rename and every deletion — the
mover's, Restore's and Delete for good's —, the Media folders page says for each ticked folder
whether a rename can work there, a Save that would leave a quarantine holding names outside every
allowed folder is refused, and Retry, Restore and Delete for good are the owner's by where their
routes live ([ADR
0030](0030-a-replaced-copy-is-renamed-into-a-quarantine-on-the-same-mount-and-stays-there-until-the-owner-restores-it-or-deletes-it-for-good.md)).**

**Before each write.** Switched off, a `Verified` job is left as it is — its copies, the clients and
Jellyfin not even read —, and its line says nothing will move while it is off. A copy still to move
whose folder is no longer ticked stops the whole replacement at the check right before the move, so
its line says so ("1 copy is no longer in a ticked folder: unless it is ticked again, the replacement
stops for review and nothing moves."). Switched off, the copy's folder unticked or its allowed folder
taken out of the list, between two names, the move stops for review with how many names are in the
quarantine; the gates are read inside each name's intent, under the write lock a Save takes too.
Restore and Delete for good are refused with nothing written while it is off ("Replacement
is switched off on the Media folders page, so nothing can be put back. Switch it on, then press
Restore again."), the job's page disables both with "Replacement is switched off on the Media
folders page: nothing is put back or deleted while it is off.", and a switch turned off between two
names stops them for review. Retry writes nothing to the media and is not refused while it is off:
the job then waits, verified.

**The hint.** Each library folder whose tick counts (`Available`) gets `RenameProblems` from one
`IFileQuarantine.DescribeRenamePlacesAsync` for all of them: `OtherMount` when the folder's
`STATX_MNT_ID` isn't its allowed folder's, `ReadOnly` from the folder's own options in
`/proc/self/mountinfo` (field 6, so a read-only bind of a writable disk counts) or `EROFS`,
`NotWritable` when `faccessat(W_OK | X_OK, AT_EACCESS)` fails on the folder or where a move makes its
names on the quarantine's side — the allowed folder's `.mam-quarantine` once it is there, as
`mkdirat` answers `EEXIST` before it asks for write permission, the allowed folder while it isn't —,
and `CouldNotCheck` for whatever can't be read, a file or a link at `.mam-quarantine` included, and
for every folder anywhere but Linux. The page
shows it under each folder ticked in the form, one all-clear line or one per problem. It looks at
folders only, and stays a hint: the rename decides.

**The Save refused.** Inside the Save's transaction, when the folders change, every quarantine
holding names (`Quarantined`, `Moving`, `Restoring`, `Deleting`) must lie under one of the new
folders, or the answer is 400 "“/media/.mam-quarantine” still holds replaced copies, so its media
folder can't be taken out of the list. Restore them or delete them for good first." and nothing is
saved; a folder above the old one keeps it in reach, by the rule Restore and Delete for good reach
it by, and the quarantine itself put in its folder's place doesn't. The page also lists each
quarantine's room.

**The owner's alone.** `GET /api/replacements/{id}`, `POST …/retry`, `POST …/restore` and
`POST …/delete-for-good` live in `Controllers/Replacements`, so `ReplacementsAreOwnerOnlyConvention`
covers them as it does MAM-24's two, and `ReplacementsAreOwnerOnlyHttpTests` now holds all six; a
replacement is its owner's own, another's a bare 404. Resolving a review and cancelling a pending
removal are MAM-27's.

**The mount and the ids are now needed.** "The mount, for this version" (MAM-24's section) and
MAM-25's "neither the read-write mount nor `APP_USER_ID` and `APP_GROUP_ID` are needed yet" held
until this version: a move needs all four steps of the Consequences, which the release note's
"Turning it on" lists. The compose file keeps `:ro` in its example and says to drop it for
replacement. With the switch on and the mount still `:ro`, or the image's own ids, the hint says a
move would fail, and a move stops for review with nothing moved.

**Nothing else is written.** The write contract's members that change a file all name the
quarantine, and none copies, writes over, links or changes an owner or a mode
(`FileQuarantineNamesTests`); `IFileInventory` still has no member named for a write
(`FileInventoryNeverWritesTests`).

`ReplacementQuarantineHttpTests.Quarantine_while_replacement_is_switched_off_leaves_the_job_verified_and_reads_nothing_but_the_gates`,
`ReplacementQuarantineHttpTests.Quarantine_switched_off_between_two_names_stops_for_review_with_the_names_already_moved_and_moves_no_more`,
`ReplacementQuarantineHttpTests.Quarantine_whose_folder_is_unticked_between_two_names_stops_for_review_with_the_names_already_moved`,
`ReplacementQuarantineChecksHttpTests.Quarantine_of_a_copy_no_longer_in_a_ticked_folder_stops_for_review`,
`ReplacementRestoreHttpTests.Restore_with_replacement_switched_off_between_two_names_stops_for_review_and_renames_no_more`,
`ReplacementDeleteForGoodHttpTests.DeleteForGood_with_replacement_switched_off_is_409_and_deletes_nothing`,
`ReplacementSettingsHttpTests.Get_says_for_each_available_folder_what_keeps_a_rename_into_the_quarantine_from_working_there_and_nothing_for_the_others`,
`ReplacementSettingsHttpTests.Get_lists_how_much_room_each_quarantine_takes_from_the_names_it_holds`,
`ReplacementSettingsHttpTests.Update_taking_out_the_media_folder_whose_quarantine_holds_names_is_400_naming_it_and_changes_nothing`,
`LocalFileQuarantinePlacesLinuxTests.DescribeRenamePlaces_of_a_folder_on_another_mount_than_its_root_is_other_mount`,
`LocalFileQuarantinePlacesLinuxTests.DescribeRenamePlaces_of_a_folder_whose_mount_the_table_says_is_read_only_is_read_only`,
`LocalFileQuarantinePlacesLinuxTests.DescribeRenamePlaces_of_a_folder_the_app_may_not_write_to_is_not_writable`,
`MountTableTests.MountOptionsOf_a_read_only_bind_of_a_writable_disk_says_ro_whatever_the_file_system_says`,
`ReplacementsAreOwnerOnlyHttpTests.Discovery_finds_every_replacement_endpoint_of_today`,
`ReplacementsAreOwnerOnlyHttpTests.Every_replacement_endpoint_as_a_signed_in_non_owner_is_403_with_the_owner_sentence`,
`ReplacementRestoreHttpTests.Restore_and_delete_for_good_of_the_owners_quarantined_job_as_a_member_are_403_and_move_nothing`,
`FileQuarantineNamesTests.No_file_quarantine_member_is_named_for_a_change_it_never_makes`
and `FileInventoryNeverWritesTests.No_file_inventory_member_is_named_for_a_write` pin it,
`MediaFoldersPage.test.tsx` the page's hint and room, and `JobDetailsPage.replacement.test.tsx` the
job's page.

## Keep both and Drop are the owner's too, and usable while replacement is off (added 0.3.0, MAM-27)

**`POST /api/replacements/{id}/cancel` (Keep both) and
`POST /api/replacements/{id}/copies/{copyId}/drop` join `Controllers/Replacements`, so
`ReplacementsAreOwnerOnlyConvention` covers them as it does the rest, and
`ReplacementsAreOwnerOnlyHttpTests` now holds all eight; the Decision's "resolving review,
cancelling a pending removal", as built.** A member gets 403 "Only the installation owner can do
this." before anything is read; a replacement is its owner's own, another's a bare 404, and a copy
that isn't one of the replacement's a bare 404 too. Members see nothing new: no **Copies** button,
no Keep both on a Jobs row, and their Keep both in the Download dialog as it was.

**Sweeps over a real replacement.** Besides the 403 for each discovered route,
`ReplacementsAreOwnerOnlyHttpTests` plants a `NeedsReview` replacement the move stopped, with two
copies — one with names of its move, none of which left its folder, one paired with its new file —,
fills each discovered route's parameters by name — `id` the replacement, `copyId` one of its copies,
a parameter it doesn't know failing the test —, sends each as a member with no body, and finds every
row as it was, nothing asked of qBittorrent or Jellyfin or read on the disk, and nothing renamed,
deleted or made. It does so twice: over the owner's job, and over the same job planted as the
member's own, as a hand-edited row would leave it. The actions find a job by its sender, so on the
owner's job a gate that came after the write would still write nothing; only the member's own, where
Retry, Keep both and Drop would each write for him, shows that the gate comes before them.
`ReplacementRestoreHttpTests` refuses a member Restore and Delete for good of his own quarantined
job the same way. `ControllerAccessDeclarationTests` lists the two new actions.

**Usable while switched off.** Keep both, Drop and the check's Retry write nothing to the media and
send nothing to a client, so, like MAM-26's Retry, none is refused while replacement is switched
off; Restore and Delete for good still are. A replacement kept both while the switch is off ends
`Cancelled`; one retried waits for its check, which runs whatever the switch says, and once verified
waits for the switch.

**Nothing else is written.** Each of the three is one database transaction: the replacement's or the
copy's row — Keep both and Drop also forgetting the names still `Planned` or `Stayed`, and the
check's Retry the last check's evidence — and an audit row with the owner as its actor,
`ReplacementCancelledAlone`, `ReplacementCopyDropped` or `ReplacementCheckRetried`; nothing to the
media folders, nothing to a client, and Jellyfin hears only the check's own request to look once a
retried check reaches it.

`ReplacementsAreOwnerOnlyHttpTests.Discovery_finds_every_replacement_endpoint_of_today`,
`ReplacementsAreOwnerOnlyHttpTests.Every_replacement_endpoint_as_a_signed_in_non_owner_is_403_with_the_owner_sentence`,
`ReplacementsAreOwnerOnlyHttpTests.Every_replacement_endpoint_as_a_member_on_the_owners_real_stopped_replacement_is_403_and_changes_nothing`,
`ReplacementsAreOwnerOnlyHttpTests.Every_replacement_endpoint_as_a_member_on_his_own_real_stopped_replacement_is_403_and_changes_nothing`,
`ReplacementRestoreHttpTests.Restore_and_delete_for_good_of_a_members_own_quarantined_job_are_403_and_move_nothing`,
`DownloadsGetHttpTests.Get_of_another_users_job_with_a_stopped_replacement_is_a_bare_404_even_for_the_installation_owner`,
`ControllerAccessDeclarationTests`,
`ReplacementCancelHttpTests`
and `ReplacementDropCopyHttpTests` pin it, and `JobsPage.test.tsx` and
`JobDetailsPage.replacement.test.tsx` a member's view.
