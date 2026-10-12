# 30. A replaced copy is renamed into a quarantine on the same mount and stays there until the owner restores it or deletes it for good

Status: accepted (Phase C, MAM-23)

## Context

Specification §3.4 asks Phase C to "move the selected media-library copy into quarantine instead of
irreversibly deleting it, but only if the quarantine directory is on the same filesystem and the
move is atomic. Otherwise, offer manual confirmation of deletion after the checks", and to implement
"automatic quarantine cleanup separately, after defining retention and disk-space policies". §7,
question 4, asks which quarantine mode and retention, and whether deleting a quarantined file needs
a second approval. §6 wants "a way to restore the quarantined copy". ADR 0003 put the quarantine
under `/media`, outside every library location, because a rename is atomic only within one mount.

What MAM-23 found on 2026-10-08, from the records of MAM-22 and from the vendors' source:

- **One mount.** In production's MAM container `/media` is a single read-write bind of one ext4 file
  system (device 8:33) holding all four libraries, every download and nothing mounted inside it;
  `/data` is another file system (252:13). A rename from a library folder to
  `/media/.mam-quarantine` is a same-mount rename; anything under `/data` would be a copy.
- **.NET moves are not atomic-or-nothing.** On Linux, `File.Move` and `FileInfo.MoveTo` fall back to
  copying the whole file and deleting the source when `rename()` fails with `EXDEV` (another mount,
  even of the same file system) — and, without overwrite, after any failure of `rename()`, where
  `link()` is refused too —; `Directory.Move` never copies but can replace a folder created between
  its check and its `rename()`. .NET has no "rename, never replace, never copy". Linux's
  `renameat2(…, RENAME_NOREPLACE)` is that primitive: it fails with `EEXIST` rather than replace,
  with `EXDEV` across mounts and with `EINVAL` where the file system lacks the flag, and ext4 has it
  since Linux 3.15. `st_dev` can't tell two bind mounts of one file system apart; `statx`'s
  `STATX_MNT_ID` can.
- **Jellyfin 12.2 never sees the quarantine.** It scans and watches only its libraries' locations
  and skips every name that starts with a dot, so `/media/.mam-quarantine` is out of its sight twice
  over. Its real-time monitor reads a rename out of a library as a deletion, but removes the item
  only after a 60-second quiet period, and a .NET watcher on Linux can stop silently after a folder
  is moved out of a library or its event queue overflows, until the next full scan; production's
  Jellyfin carries .NET 10.0.12, which has that fault. `POST /Library/Media/Updated` with the paths
  makes Jellyfin look at them whatever its monitor does; any API key may send it.
- **The app's own scan would see it.** The folder scan (ADR 0021) walks hidden folders, so a
  quarantined video under the allowed folder `/media` would come back as a file no library reported,
  counted on the Library page and, when its file or folder name claims a film's title with one year,
  offered as a possible match on the Duplicates page (ADR 0028).
- **Companions.** Jellyfin saves subtitles beside the media on all four libraries, and a release's
  folder may hold `.srt` and `.nfo` files named after its video. qBittorrent saves a single-file
  torrent straight into the library folder, so one folder may hold many films' videos and subtitles.
  Four copies are Blu-ray folders, where the app knows only `BDMV/STREAM/00000.m2ts`.
- **Restore keeps the item.** A Jellyfin item id is a hash of its type and path, so a file put back
  at its path is the same item again, and its watched state, kept aside while it was away, comes
  back. Its metadata, images and provider ids were deleted with the item, so Jellyfin identifies it
  from scratch.
- **Room.** No free-space figure of the media disk is recorded; 7.34 TiB of media sit on a 14 TB
  disk.

Vasyl's decision of 2026-10-08: a quarantined copy is kept until the owner deletes it for good in
the app, after a second confirmation, with Restore available until then and no automatic cleanup.
Stated without objection the same day: only the picked video and its own companion files move, never
a folder, another video or a disc; the folder scan skips the quarantine; and where a move can't be
atomic nothing is deleted.

## Decision

**The old copy the owner picked moves, with its own companion files, into `.mam-quarantine` at the
top of the allowed folder that holds it, by one `renameat2(RENAME_NOREPLACE)` per name on the
folders the checks opened, never a copy; it stays there — out of Jellyfin's sight and out of the
app's own folder scan — until the owner restores it to where it was or deletes it for good after a
second confirmation, and Phase C never deletes a file that isn't in the quarantine (Vasyl's decision
of 2026-10-08).**

**Where.** `<allowed folder>/.mam-quarantine/<replacement job id>/<path relative to that folder>`:
on the reference installation `/media/.mam-quarantine/<id>/movies/Alien (1979)/Alien.mkv`, outside
every library location. The relative path keeps where each name came from, for Restore and for the
owner reading the folder by hand; the job's own folder keeps two replacements of one name apart. It
sits in the allowed folder because the checks reach nothing outside one (ADR 0009) and the rename
must stay on one mount. Where an allowed folder is itself a library folder, the quarantine is inside
that library's location — a departure from ADR 0003's "outside every library location" for that case
only —, and Jellyfin still never lists it, as it skips dot names. The app creates `.mam-quarantine`
and the job's folders and nothing else, and before each move checks that the old copy's folder and
the quarantine share one mount (`STATX_MNT_ID`), not merely one device. Each job records its
quarantine folder, and the Media folders page refuses a Save that would leave a quarantine holding
files outside every allowed folder, naming it, so Restore, Delete for good and the room it takes
stay within reach.

**What.** The picked video file, then each file in its folder whose name is the video's name without
its extension followed by a dot (`Alien.srt`, `Alien.uk.srt`, `Alien.nfo`), that is no media file
itself, and that doesn't begin with the longer name of another video in that folder followed by a
dot: beside `Alien (1979).mkv` and `Alien (1979).2160p.mkv`, `Alien (1979).2160p.uk.srt` belongs to
the second and stays. Never the folder, which stays, empty or not; never another video; never a
Blu-ray or DVD folder's clip, which stops the job at `NeedsReview` ("a disc folder can't be replaced
one file at a time"). A companion that can't move, or that a torrent lists ([ADR
0031](0031-a-replacement-never-touches-a-torrent-and-an-old-copy-a-torrent-lists-waits-until-the-owner-removes-the-torrent.md)),
stays and is named on the job.

**How.** On Linux, `renameat2(RENAME_NOREPLACE)` through the `O_PATH | O_NOFOLLOW` folder
descriptors of ADR 0009's walk, so a link swapped in after the checks leads nowhere. A failed rename
— `EXDEV`, `EEXIST`, `EINVAL`, `EACCES`, `EPERM`, `EROFS`, `EOVERFLOW` (an owner the container can't
map) — leaves that name where it was. On a video it stops the job at `NeedsReview` with the error's
sentence, and copies that already moved (earlier episodes of a season) stay in the quarantine,
listed on the job, for the owner to restore or keep; on a companion the video stays moved and the
companion is named on the job. After each rename the new name must show the same device and inode,
both folders are flushed to disk, and audit rows are written before (the intent) and after (the
outcome). A job a restart cut between an intent and its outcome looks each recorded name up by
device and inode, at its old path and in the quarantine, records what it finds, and stops at
`NeedsReview` ("The app restarted while moving this copy: 3 of 5 names are in the quarantine."); it
renames and deletes nothing by itself. `File.Move`, `FileInfo.MoveTo` and `Directory.Move` are never
used for it.

**Kept until the owner says.** No timer, no retention and no automatic cleanup in Phase C. The job's
page offers **Restore** and **Delete for good**. Delete for good asks again, naming every path and
the space it frees; it re-reads each quarantined name's device, inode, size and modification time
and refuses if any changed; space comes back only once no process holds the file open; a deletion a
restart cut records the names already gone and keeps the rest. The app shows how much room the
quarantine takes. Only the owner does either ([ADR
0033](0033-replacement-is-off-until-the-owner-switches-it-on-and-ticks-the-folders-it-may-touch-and-the-app-may-only-rename-there.md)).

**Restore.** The same rename back to each recorded path, never replacing what sits there now: a new
copy at that path, or a folder that is gone, stops the restore with the reason, and no folder is
created for it. Jellyfin then finds the same item again, with its watched state.

**Out of the app's own lists.** The folder scan leaves out every folder named `.mam-quarantine`, at
any depth, so a quarantined file is never a file no library reported, a possible match or part of
the Media folders row's count, whatever the allowed folders become; the replacement job is where the
app knows it.

**Jellyfin is told, and nothing more.** After every move and every restore the app sends `POST
/Library/Media/Updated` with the paths involved. It writes nothing else into Jellyfin: no watched
state, collection or playlist is copied to the new copy, and a restore sends no Identify (Vasyl's
decision of 2026-10-08).

**No atomic move, no deletion.** This departs from §3.4's "Otherwise, offer manual confirmation of
deletion after the checks", so §3.4's `Removed` step never occurs: where the rename can't be done,
the job stops at `NeedsReview` with the reason, and the owner keeps both copies or frees the room by
hand. A deletion with no quarantine is the one outcome §6 rules out — a replacement without a way
back.

| Rejected option | Why not |
| --- | --- |
| A quarantine under `/data`, or on a separate mount | Another file system, or another mount of the same one: every move becomes a copy (`EXDEV`) |
| `File.Move` / `FileInfo.MoveTo` / `Directory.Move` | Copy and delete on `EXDEV` (and after other failures), or replace a folder created in between |
| Delete automatically after a number of days | Vasyl: nothing goes without the owner's click; §3.4 waits for a disk-space policy first |
| The app never deletes; the owner empties the folder by hand | Shell work, and every replacement's record goes stale |
| Delete after a confirmation where the move can't be atomic | No way to restore, which §6 requires of a replacement |
| Hide a quarantined file by renaming it with a suffix (`.mkv.mam-quarantined`) | Two names for Restore to keep straight, and the file reads wrong to every other tool; one rule in the scan does it |
| Move the release's whole folder | Takes other videos, a collection's other films or a season's other episodes with it |

## Consequences

**What the owner sees.** From the download until the move, Jellyfin shows two cards of the film; the
old one goes about a minute after the move. A film's watched state follows to the new card only when
both sit side by side in one folder — each in its own film folder straight in the library folder,
say, where a new download lands — and the new card already has the same TMDb or IMDb id: that is
Jellyfin's own doing. An old film inside a collection or trilogy folder doesn't pass it on, nor does
an episode in another show folder; Jellyfin keeps that watched state aside, and it comes back if the
owner restores the old copy. The old copy drops out of collections and playlists it was added to by
hand, and the new one isn't added. A restored copy is identified by Jellyfin from scratch; where the
owner had linked it to TMDb by hand ([ADR
0026](0026-a-tmdb-id-set-by-hand-is-the-owners-word-kept-per-library-item-and-written-into-the-library-as-ids-only.md))
and Jellyfin's own match doesn't give it the link's id, the Library shows the link as "Jellyfin lost
it", and identifying it again is the owner's click, as for any link Jellyfin lost.

**Room.** A replacement needs room for both copies until the old one is deleted for good.
Quarantined files stay on disk, outside the database backup, as ROADMAP's Phase C says.

**Code to come.** No code moves a file yet. MAM-26 adds a write contract beside `IFileInventory`,
which stays read-only by construction, and keeps the parent's descriptor in the Linux walk (today it
closes each folder's descriptor and keeps only the last name). Its tests over real folders must show
a changed old file, a name already taken in the quarantine and another mount moving nothing; a
restart after a rename and before its outcome, and one between two names of a copy, each found again
by device and inode and stopped for review with nothing renamed twice or deleted; another video's
companion whose name begins with the picked video's name staying; a move and a restore that keep the
device and inode; a change of the allowed folders keeping the quarantine reachable; and the scan
skipping the quarantine.

## Anti-consequence

This lets the app rename the copy the owner picked and its own companions, and delete what it put in
the quarantine. It licenses no other write: no folder of the library is created, renamed or removed
other than `.mam-quarantine` and its job folders, no permission is changed, nothing is deleted
outside the quarantine, and Jellyfin's `DELETE /Items/{id}` — which deletes a film's whole folder —
is never called.

## The look is also asked after a download (added 0.3.0, MAM-25)

**Jellyfin is now also asked to look at a replacement's new files once their download is done and
they passed the checks of the files, before any move: `POST /Library/Media/Updated` to each library
with every new file's path in that library's own terms, at most 500 to a request, and still nothing
else written into it ([ADR
0035](0035-a-replacements-new-file-is-checked-by-its-clients-word-the-media-inspector-and-the-librarys-ids-before-anything-moves-and-what-is-unsettled-after-an-hour-stops-for-review.md)).**
The request is the optional capability `IMediaLibraryFileLookup.RequestScanAsync`, beside
`IMediaLibraryProvider` and never a member of it (ADR 0005), so MAM-26's look after a move and a
restore will send the same member. Each path goes as an update of type `Created`, the way Jellyfin's
own folder watcher reports a file that appeared, so it works with the real-time monitor on or off;
204 is the one answer taken, and it proves only that Jellyfin took the request. The adapter sends it
once; the check asks again on its next pass while the request isn't taken, within its hour.

**The item id this Context relies on is read, not listed.** "A Jellyfin item id is a hash of its
type and path" is now how the check finds the item Jellyfin made of a new file —
`IMediaLibraryFileLookup.ReadFilesAsync`, `GET /Items/{id}?userId=` of an administrator for the id
the path computes as a film's, an episode's, a plain video's and a trailer's —, as `/Items` hides
alternate versions. A read-only check of the reference installation's Jellyfin 12.2.0 on 2026-10-10
found the formula right for 880 of 880 movies and 440 of 440 episode files, so Restore's "the same
item again" stands on it as well. Every such call is a read.

`JellyfinAdapterFilesScanTests.RequestScan_posts_every_path_in_one_body_with_the_key_and_is_taken_on_204`,
`JellyfinAdapterFilesScanTests.RequestScan_answered_with_anything_but_204_fails_with_its_status_and_sentence_and_is_never_sent_again`,
`JellyfinAdapterFilesReadTests.ReadFiles_finds_a_film_by_the_id_its_path_computes_as_a_movie_with_its_ids`,
`JellyfinAdapterFilesReadTests.ReadFiles_never_takes_an_item_of_another_path_for_the_file`,
`ReplacementVerificationLibraryHttpTests.Verify_where_jellyfin_wont_take_the_request_to_look_asks_again_each_pass_until_the_hour_runs_out_then_stops_for_review`
and
`ReplacementVerificationHttpTests.Verify_of_a_film_whose_new_item_carries_the_old_works_imdb_id_verifies_it_and_keeps_the_evidence`
(one POST, every other call a read) pin it.

## The move, Restore and Delete for good, as built (added 0.3.0, MAM-26)

**The old copy moves as the Decision says, through `IFileQuarantine` — a write contract beside
`IFileInventory`, which stays read-only by construction — whose Linux implementation,
`LocalFileQuarantine`, makes one `renameat2(RENAME_NOREPLACE)` per name on folders it holds as
`O_PATH | O_NOFOLLOW` descriptors; the job rests `Quarantined` until the owner's Restore, which ends
it `Restored`, or Delete for good, which ends it `Completed`, and a cut is found again by device and
inode and stops for review ([ADR
0033](0033-replacement-is-off-until-the-owner-switches-it-on-and-ticks-the-folders-it-may-touch-and-the-app-may-only-rename-there.md),
[ADR
0031](0031-a-replacement-never-touches-a-torrent-and-an-old-copy-a-torrent-lists-waits-until-the-owner-removes-the-torrent.md)).**
`Restored` is a state of its own, both copies back in the library, by Vasyl's decision of
2026-10-10; Restore and Delete for good act on the whole job, every name it holds in the quarantine.

**Where and what.** `QuarantineFolder.PathOf` gives `<allowed folder>/.mam-quarantine/<job id>/<path
inside the folder>` and refuses a path already in a quarantine. `QuarantineCompanions.Pick` reads a
name in the video's own letter case on a POSIX folder and in any on a Windows one; `DiscFolders`
tells a disc's clip, which stops the job before the claim. The claim writes a `ReplacementJobNames`
row per planned name — the video, then its companions — with the allowed folder, the place in the
quarantine, and the device, inode, size, modification time and link count read right before: the
facts every later rename, Restore and Delete for good compares.

**How.** The name is read by `statx` without following it and must be a regular file with the
expected facts; the source folder must share the allowed folder's mount by `STATX_MNT_ID` (a kernel
without it, before Linux 5.8, is `NotSupported`), and so must the quarantine's last folder; the
quarantine's folders are made one level at a time (`mkdirat`, 0755 before the umask), a link
planted there refused. Right before the rename both folder paths are walked again and must still
lead to the folders held, and the name is compared again; after it both folders are flushed — a
failed flush is ignored, as the rename happened and a restart's look covers a crash before the
flush — and the new name must show the same device and inode. `EEXIST` is `NameTaken`, `EXDEV`
`OtherMount`, `EINVAL` `NotSupported`, `EACCES`, `EPERM` and `EOVERFLOW` `NotAllowed`, `EROFS`
`ReadOnly`, each with the contract's sentence, the name left where it was; a rename whose new name
then shows another device or inode moved a file put at the name after the last look, and is
`Swapped`. The last name is never followed, and a library video that is itself a link stops the job
before the claim ("“Alien.mkv” is a link to another file, and the app moves only a file itself, so it
stays where it is."). A move's source folder is located within the allowed folder whose quarantine
takes the name alone, its links followed, and no folder of a move, a restore or a listing may lead
into a quarantine ("… already lies in a quarantine."): a folder link into another allowed folder or
into a quarantine moves nothing. Anywhere but Linux, `Capabilities.CanRename` is false and every
write is `NotSupported`: a move there stops for review, and Restore and Delete for good refuse
before the claim.

**Rows before and after.** Each name's intent (`Moving`, `Restoring` or `Deleting`, with its audit
row) is committed before the system call and its outcome after, no transaction held across the
disk; a job being worked on is held in the process's `ReplacementJobsInHand`, so the mover and an
owner's click never write for one job at once. A video that can't move stops the job at
`NeedsReview` ("“Yellowstone S02E02.mkv” couldn't be moved into the quarantine: …"), what moved
before it staying in the quarantine and its companions left `Planned` — a Retry's run takes them up
again, each checked as its video was —; a name whose rename was `Swapped` is `Lost` ("A different
file now sits in the quarantine under “…”, …") and stops the job too; a companion that can't stays
`Stayed` with the sentence. A cut — a job found `Quarantining`, `Restoring` or `Deleting` that no one
in the process holds — looks each name in the middle up through the file inventory at both places
and records `Quarantined`, `Planned`, `Restored`, `Deleted` or `Lost`, then stops: "The app stopped
while moving the old copies: 1 of 3 names is in the quarantine.", where the Decision's example said
"The app restarted while moving this copy"; it renames and deletes nothing. A cut restore that put
every name back ends `Restored`, and a cut deletion that deleted every name `Completed`, as nothing
is left to review; a look that failed, or that finds a name nowhere while neither of its folders is
there, decides nothing, and the next pass looks again.

**Restore.** From `Quarantined`, or from `NeedsReview` with names in the quarantine; every name is
checked before anything is written — the switch, the old path's ticked folder, the file in the
quarantine unchanged, no name at the old path (a link leading nowhere included), the old folder
still there, which a listing of its names never following one tells — and refused with nothing
moved otherwise; so is a job one of whose videos a Delete for good that stopped part-way deleted, as
its companions would go back alone (Retry settles it, then Delete for good takes the rest). Each
name goes back by the same rename, videos first; one that can't stops the job for review with how
many are back. Each name keeps its own
allowed folder as the contract's root while a current allowed folder holds it, so a quarantine stays
reachable when its folder is replaced by one above it.

**Delete for good.** Only from `Quarantined`, and only for exactly the names the owner's second
confirmation listed; each is compared again right before one `unlinkat`, which takes only a path
inside a job's folder of a quarantine, and no folder is removed. The room it frees counts only names
with one link, as the disk says it of the file now at the quarantine path while it is the one moved
there.

**Jellyfin.** After a move and a restore, `POST /Library/Media/Updated` with each name's original
place in the library's terms, as `Deleted` after a move and `Created` after a restore
(`LibraryFileChange`), never a quarantine path; owed (`LibraryNoticeDueUtc`) and sent again on each
pass for an hour while it isn't taken. A deletion sends nothing more. Nothing else is written to
Jellyfin, and Restore sends no Identify.

**Out of the lists, within reach.** The scan leaves out every `.mam-quarantine` ([ADR
0021](0021-the-scan-of-the-media-folders-follows-links-only-inside-the-allowed-roots-and-keeps-one-name-of-each-file-no-library-reported.md)).
The Media folders page lists each quarantine's room from the rows, never a walk of the disk, and a
Save that would leave one holding names (`Quarantined`, `Moving`, `Restoring`, `Deleting`) outside
every allowed folder is 400, naming it — so is one that puts the quarantine itself in its folder's
place. The mover reads the allowed folders again inside each name's intent, under the same write
lock as the Save, so a Save and a rename can't pass each other.

**Not met by the tests.** The tests the Consequences' "Code to come" asked for are among those
below, over real folders where it said so. Not met by them: `EXDEV`, `EINVAL`, `EROFS` and
`EOVERFLOW` from the system calls
themselves — the mount check stops a cross-mount rename before it, and the test container's file
systems all have the flag —, a real read-only bind mount, a mount at `.mam-quarantine`, a failed
flush and arm64's open flags. Nothing was moved on the reference installation; MAM-28 checks it.

`LocalFileQuarantineLinuxTests.Move_renames_the_file_into_its_roots_quarantine_keeping_its_device_and_inode_and_leaves_nothing_behind`,
`LocalFileQuarantineLinuxTests.Move_of_a_file_that_changed_since_it_was_checked_moves_nothing_and_makes_no_folder`,
`LocalFileQuarantineLinuxTests.Move_onto_a_name_already_taken_in_the_quarantine_is_name_taken_and_both_files_stay`,
`LocalFileQuarantineLinuxTests.Move_from_a_mount_inside_its_root_is_other_mount_and_makes_nothing`,
`LocalFileQuarantineLinuxTests.Restore_renames_the_file_back_keeping_its_device_and_inode_and_leaves_nothing_in_the_quarantine`,
`LocalFileQuarantineLinuxTests.Restore_into_a_folder_that_is_gone_is_folder_gone_and_makes_no_folder`,
`LocalFileQuarantineLinuxTests.Delete_of_a_quarantined_file_that_changed_deletes_nothing`,
`ReplacementQuarantineHttpTests.Quarantine_of_a_verified_film_moves_its_video_and_own_companions_into_the_quarantine_keeping_their_identity`,
`ReplacementQuarantineHttpTests.Quarantine_leaves_another_videos_companion_whose_name_begins_with_the_ticked_videos_name_in_place`,
`ReplacementQuarantineHttpTests.Quarantine_of_a_season_whose_second_episode_cant_be_renamed_keeps_the_first_in_the_quarantine_and_leaves_the_third_where_it_is`,
`ReplacementQuarantineChecksHttpTests.Quarantine_of_a_clip_of_a_disc_folder_stops_for_review`,
`ReplacementQuarantineChecksHttpTests.Quarantine_across_two_mounts_renames_nothing_and_stops_for_review`,
`ReplacementQuarantineRecoveryHttpTests.Quarantine_cut_after_a_rename_before_its_outcome_finds_the_name_in_the_quarantine_on_the_next_pass_and_stops_for_review`,
`ReplacementQuarantineRecoveryHttpTests.Quarantine_cut_between_two_names_counts_the_moved_ones_on_the_next_pass_and_renames_nothing_twice`,
`ReplacementRestoreHttpTests.Restore_of_a_quarantined_replacement_puts_every_name_back_with_its_identity_and_is_200_restored`,
`ReplacementRestoreHttpTests.Restore_refused_by_a_check_is_409_with_why_and_writes_and_renames_nothing`,
`ReplacementDeleteForGoodHttpTests.DeleteForGood_of_the_confirmed_names_deletes_them_from_the_quarantine_and_is_200_completed`,
`ReplacementDeleteForGoodHttpTests.DeleteForGood_confirming_another_set_than_the_quarantine_holds_is_409_and_deletes_nothing`,
`ReplacementLibraryNoticeTests.SendDue_never_names_a_quarantine_path_even_where_the_quarantine_lies_in_a_library_folder`,
`ReplacementQuarantineFlowHttpTests.Flow_moves_the_old_copy_tells_jellyfin_it_went_then_restore_puts_it_back_with_its_identity_and_tells_jellyfin_it_came_back`,
`ReplacementSettingsHttpTests.Update_replacing_the_media_folder_by_its_parent_keeps_the_quarantine_reachable_and_is_204`,
`QuarantineFolderTests` and `QuarantineCompanionsTests` pin it.

## The quarantine also holds the copies the owner removes on the Duplicates page (added 0.3.0, MAM-40)

**A copy the owner removes from a group of exact copies or of one work's copies on the Duplicates
page goes into the same quarantine, by the same rename, with the same companions and the same
rows before and after each name, as a replaced copy: under `<allowed folder>/.mam-quarantine/<removal
id>/<path inside the folder>`, beside the replacements' job folders, kept until the owner restores it
or deletes it for good, now from the page's **Removed copies** tab ([ADR
0037](0037-a-copy-is-removed-from-the-duplicates-page-into-the-quarantine-on-the-owners-word-through-replacements-gates-and-checks.md),
Vasyl's decisions of 2026-10-12).** The Decision's "the old copy the owner picked" now reads "the
copy the owner picked", and "Each job records its quarantine folder" holds for each removal too.

**The same, written once.** A removal's names (`CopyRemovalNames`) and a replacement's
(`ReplacementJobNames`) share their columns through `QuarantinedNameEntity`, an abstract base EF
doesn't map, so the MAM-26 section's rows before and after, its conditional writes, the look-up of a
cut name by device and inode (`QuarantineNameFinder`), the owner's write loop of Restore and Delete
for good (`QuarantineWriteLoop`) with its checks (`QuarantineNameChecks`), the companions'
listing (`QuarantineCompanionListing`) and Jellyfin's notice (`QuarantineNotices`) serve both, moved
out of the replacement's classes with their behaviour unchanged for replacements, and
`AddCopyRemovalsMigrationTests.Upgrade_keeps_every_replacement_name_whole` finds
`ReplacementJobNames` untouched.

**Where a removal differs.** The move runs inside the owner's request, not a background pass, the
video first: a video that can't move ends the removal `NotMoved` with nothing in the quarantine, where
a replacement stops for review. Restore and Delete for good act on the whole removal, also from
`NeedsReview`, and Delete for good takes no list of names: the page's second confirmation names every
path with its size and the room it frees. A cut is found again by the same pass and the same rules,
and a removal with nothing left in the quarantine ends rather than waiting for review, as it has no
Retry (ADR 0037).

**Out of the lists, within reach.** A removal's names count in each quarantine's room on the Media
folders page, which now says whose they are ("/media/.mam-quarantine: 4 GiB in 4 files, from 1
replacement and 2 removals."), and in the Save that would leave a quarantine outside every allowed
folder, now "“/media/.mam-quarantine” still holds replaced or removed copies, so its media folder
can't be taken out of the list. Restore them or delete them for good first." And a name in the
quarantine, a replacement's or a removal's, no longer counts in any duplicate group until it is put
back ([ADR
0028](0028-duplicates-are-worked-out-on-every-read-in-four-classes-by-one-file-one-hash-one-registry-key-or-one-title-and-year-and-a-group-only-shows.md)'s
MAM-40 section), where the scan alone kept it out before.

`CopyRemovalHttpTests.Remove_of_a_copy_moves_its_video_and_own_companions_into_the_quarantine_and_answers_the_removal`,
`CopyRemovalHttpTests.Remove_keeps_a_companion_that_cant_be_renamed_where_it_is_and_still_removes_the_video`,
`CopyRemovalActionsHttpTests`, `CopyRemovalRecoveryHttpTests`,
`ReplacementSettingsHttpTests.Get_counts_the_names_a_removal_holds_in_a_quarantine_with_the_replacements_ones`,
`ReplacementSettingsHttpTests.Update_taking_out_the_media_folder_whose_quarantine_holds_a_removals_names_is_400_naming_it_and_changes_nothing`
and `AddCopyRemovalsMigrationTests` pin it.

## Restore and Delete for good on the Jobs row too (added 0.3.0, MAM-41)

**A replacement's Restore and Delete for good, on the job's page alone until now, are also offered
on its row on the Jobs page, by the same rules — `ReplacementJobOffers.CanRestore` and
`CanDeleteForGood`, which the job's `replacement` now carries as `canRestore` and
`canDeleteForGood` — and with the job page's confirmations (Vasyl's decisions of 2026-10-12).** The
owner had to open each job to settle what it holds in the quarantine; the row's actions now hold
every action on a whole replacement, while **Drop**, which acts on one copy, stays with the copies
in the row's details ([ADR
0014](0014-a-download-is-followed-by-reading-its-client-and-cancelling-it-only-stops-the-following.md)'s
MAM-41 section).

**The confirmed names, still.** Pressed on the row, Delete for good reads the replacement's details
afresh (`GET /api/replacements/{id}`), lists what the quarantine holds then — every file, its size,
the room it frees and how many files have another name — and sends exactly those names, so the MAM-26
section's "only for exactly the names the owner's second confirmation listed" holds on the row: a
quarantine that changed between the read and the click is refused with nothing deleted. Restore asks
as on the job's page and acts on the whole job, as before. Both are disabled with the reason while
replacement is switched off, as the server refuses them then ([ADR
0033](0033-replacement-is-off-until-the-owner-switches-it-on-and-ticks-the-folders-it-may-touch-and-the-app-may-only-rename-there.md)).

**What waits is listed.** As nothing empties the quarantine by itself — the Decision's "no timer" —,
a job whose old copies wait there is listed under the Jobs page's **Needs you** until the owner
restores them or deletes them for good. Every Delete for good confirmation, a removal's on the
Duplicates page included, is half as wide again as a plain one, as it lists every path; each file's size sits on its
path's line at the right edge, so the list fills that width rather than leaving its right half empty under a size on
a line of its own (Vasyl, 2026-10-12).
