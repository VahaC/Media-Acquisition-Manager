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
