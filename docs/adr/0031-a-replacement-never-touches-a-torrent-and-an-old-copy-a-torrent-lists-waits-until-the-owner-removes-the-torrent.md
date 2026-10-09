# 31. A replacement never touches a torrent, and an old copy a torrent lists waits until the owner removes the torrent

Status: accepted (Phase C, MAM-23)

## Context

Specification §3.4, check 6: "If the old file belongs to an active torrent, is still seeding, or
shares its data through a hardlink, do not modify the torrent or its seeding data. Keep 'remove a
media-library copy' separate from 'delete a client's files.' When uncertain, use `NeedsReview`." §1
names "torrents that must continue seeding" among the owner's setup, and ROADMAP's Phase C kept
"deleting a torrent or its data from the client" out of scope (MAM-23 widens that to any write to a
download client). §7, question 6, asked what the old files are; ADR 0018 measured it on 2026-10-04:
of the 1394 video files in the library folders, **822 (4.07 TiB) are a torrent's own data at the
very path qBittorrent wrote** — 768 seeding, 54 queued to seed —, one (19.15 GiB) belongs to a
torrent still downloading, **571 (3.22 TiB) belong to no torrent**, and no file has a second name.
qBittorrent seeds with no ratio or time limit there, and its categories save straight into the
library folders, so every new download is a seeding torrent's data too.

On the reference installation the library copy and the seeding data are one inode with one name.
§3.4's escape — "moving a hardlinked media copy leaves seeding data in place" — needs a second name
that doesn't exist here: moving any of those files takes the data away from its torrent. What
qBittorrent 5.2.4 then does, read by MAM-23 from its source and from libtorrent 2.0.15's (the build
recorded for the 5.2.3 sample; production's build isn't recorded, and the code read is the same
across libtorrent 2.0):

- **Nothing visible at first.** A peer's read of the missing file fails, the peer gets a refusal and
  the log a "File error alert"; the torrent's state stays `stalledUP` or `uploading` at 100 %, and a
  file still open in libtorrent's file pool may go on being seeded from its new name. Production's
  `qBittorrent.conf` names POSIX disk I/O (`DiskIOType=POSIX`), a spelling qBittorrent may not read
  — its key is `Posix` —, in which case it runs the memory-mapped default this reading assumed; the
  decision below doesn't depend on which.
- **`missingFiles` after a restart.** Resume data is checked only when a torrent loads, by file
  sizes; the torrent with a missing file then stops in `missingFiles` and stays there.
- **A recheck downloads it back.** **Force recheck** forgets the torrent's pieces; started again, it
  downloads the old copy back to its old path, into the library, where Jellyfin finds it.
- **Removing with files deletes whatever is at the path.** qBittorrent deletes a torrent's content
  by its stored paths, whatever file sits there now.
- **The client's credential is all-or-nothing.** The Web API has no roles: the key the app sends
  downloads with can also delete content.

The app can't yet tell which torrent holds a library file. `IDownloadClient` reads a client only to
follow the app's own jobs — by their hashes, by their category, and every torrent only for a
category whose name holds a `+` (ADR 0014) —, "never to read a whole client" for a job its hashes no
longer find, as its own comment says; Phase B reads no client at all (ADR 0018).

Vasyl's decision of 2026-10-08, of three options — the app waits for the owner; the app removes the
torrent itself, keeping the files and saving its `.torrent` for Restore; or qBittorrent moves the
torrent's data into the quarantine and keeps seeding it there: **the app waits for the owner.**

## Decision

**A replacement never writes to a download client and never moves a file a torrent lists: right
before the move, the app reads every torrent's files from each download client whose paths reach the
allowed folders, and an old copy any torrent lists, in any state, stops the job at `NeedsReview`
naming the torrent — "Still held by the torrent <torrent> in <client>: remove that torrent there
without its files, then press Retry." — until no torrent lists it (Vasyl's decision of
2026-10-08).**

**Listed means held, whatever the state.** Seeding, queued, stopped, checking, errored or still
downloading: a queued torrent starts again on its own, a stopped one as soon as anyone presses
Start, and a file's progress stays 100 % after the file is gone, so no state says a torrent no
longer needs its files. A path that no pair of a client's path mappings covers, or that two cover
differently (ADR 0003), a client that doesn't answer, and a listing the app can't read — larger than
its 4 MiB cap on an answer, say — all count as uncertain, and stop the job the same way.

**Every name the move would rename.** The check covers the video and each companion ADR 0030 would
move with it. A companion a torrent lists stays where it is and is named on the job, as a companion
that can't move does; the video, if no torrent lists it, still moves.

**Read by path, confirmed by identity.** Each torrent's save path joined with each of its files,
mapped onto the app's paths through the client's pairs, is compared with each of those names, and
the device and inode of what the app finds there decide (a mapped path is where to look, not proof,
ADR 0003). One request per client, qBittorrent's `torrents/info?includeFiles=true`. This is a new
read member of `IDownloadClient`, behind the replacement gates and used only for this check — for
the Download dialog's marks and for the read before the move; following a job keeps its own reads
(ADR 0014).

**A second name is not the torrent's path.** Where an old copy is another name of a torrent's data —
a hardlink, the case §3.4 allows — it may move, because the torrent's own path stays; the dialog and
the job say that moving it frees no room. No such file exists on the reference installation.

**Said before, decided at the end.** The Download dialog reads the same listing when the owner picks
old copies and marks each held one: "held by the torrent <torrent>: before this copy can move you'll
remove the torrent in qBittorrent without its files — which also stops seeding its other files".
Scheduling is still allowed: the download takes its time, and the owner may remove the torrent
meanwhile. The read right before the move, after check 5, is the one that decides.

**No client write.** The app never stops, pauses, removes, relocates or renames a torrent or its
files. The owner removes it in qBittorrent, where "also delete files" is unticked by default on the
reference installation, and presses **Retry** on the job.

| Rejected option | Why not |
| --- | --- |
| Move a seeding copy anyway | qBittorrent says nothing until a restart turns the torrent into `missingFiles`, and a recheck then downloads the old copy back into the library |
| The app removes the torrent itself, keeping its files, and saves its `.torrent` so Restore can add it back | Vasyl chose to leave it to the owner; it is a client write §3.4 check 6 and ROADMAP's scope rule out |
| qBittorrent moves the torrent's data into the quarantine and keeps seeding it there | Changes the torrent, frees no room while it seeds, moves the whole torrent (a season pack's every episode), turns its automatic management off and removes folders it empties |
| Stop the torrent, then move | The torrent breaks as soon as anyone starts it again |
| Offer no replacement of a seeding copy in the dialog | The owner couldn't schedule ahead and remove the torrent while the new copy downloads |

## Consequences

**Most replacements wait for one step in qBittorrent.** On 2026-10-04 that was 823 of 1394 files
(822 seeding or queued to seed, one still downloading), and the new copy will be seeding data too,
so replacing it later waits again. Removing a torrent ends the seeding of every file it lists, not
only the old copy's: replacing one episode or one film of a pack stops the seeding of the pack's
other files, which stay in the library. A season's old episodes held by one pack are freed by
removing it once; held by several torrents, each is named. The 571 files no torrent holds move
without it. Restore then brings back the file, not the seeding: qBittorrent forgets a removed
torrent's `.torrent`, and seeding again is the owner's, by adding the torrent anew.

**qBittorrent never sees a file go missing because of the app.** No torrent ends up in
`missingFiles` because of a replacement, and no recheck can download a quarantined copy back.

**The client contract grows by one read.** MAM-24 (the dialog's marks) and MAM-26 (the decisive
read) add it. `DownloadClientNeverRemovesTests` forbids member names for removing, stopping and
pausing; MAM-26 adds moving, relocating, renaming and setting a location to its words, since a
`SetLocationAsync` would pass today.

**Tests to come.** A held old copy — seeding, queued, stopped —, a held companion, an uncovered path
and a client that doesn't answer end at `NeedsReview` or leave the companion, with the old file
untouched and no request but the listing sent to the client; after the torrent is gone, Retry moves
it.

## Anti-consequence

Seeding still decides nothing about duplicates (ADR 0018). And this licenses no client write of any
kind, not even removing a torrent that keeps its files: that stays the owner's, in the client.

## The dialog's read of every torrent (added 0.3.0, MAM-24)

**The Download dialog marks the copies a torrent holds from a read of every download client's
torrents with their files — `IDownloadClient.ListJobsWithFilesAsync`, qBittorrent's one
`torrents/info?includeFiles=true` — made only once the owner has picked Replace after verification
and while a copy can be ticked; a hold never keeps a copy from being ticked, and nothing uncertain
reads as "not held".** "Said before, decided at the end" is built for its first half: the dialog
asks `GET /api/replacements/options` with `holdings=false` when a destination is picked and with
`holdings=true` once Replace is picked, and a Keep both send, and the schedule itself, never read a
whole client. The read that decides, right before the move, is MAM-26's.

**The 5.2 gate.** qBittorrent before 5.2 ignores `includeFiles` without a word and lists every
torrent without its files, which would read as no copy held, so the adapter refuses it
(`UnsupportedVersion`, "This is qBittorrent 5.1.4; telling which torrents hold a library file needs
qBittorrent 5.2 or later."), and the dialog says the copies couldn't be checked; scheduling stays
allowed, as with any uncertain answer.

**Read by path, confirmed by identity, as decided.** Each torrent file's path — its save path joined
with its path in the torrent — is mapped through that client's pairs as a whole (`PathMapper`, which
refuses a `..`), never the save path's app path with the name appended, which a pair deeper than the
save path wouldn't map. Only a file inside the Media folders of a tickable copy's size is looked at
on the disk, once per path, and the copy's own device and inode make it a hold. At the copy's own
path, or reached through a link (one name), the torrent holds the copy: "Held by the torrent “X” in
“qBittorrent”: before this copy can move you'll remove that torrent there without its files — which
also stops seeding its other files.", once per torrent of a work, with how many of its copies. The
copy's data under another name the torrent lists is a second name: "This file has another name, so
moving it frees no space. It is the torrent “X”'s file.". Holds are told apart by client and torrent
id, qBittorrent's hash, so two torrents of one name — a cross-seed — are two, and every state
counts.

**Other names, with a torrent found or not.** Every tickable copy whose link count, read fresh, is
above one is marked as having another name: a torrent's own name usually lies outside the Media
folders, where the app may not look.

**Uncertain is said, client by client.** Every download client is listed, switched off or not. One
the app didn't ask — switched off, a type this version can't read, a secret it can't open — or whose
answer failed or broke the reader is `NotRead`, reported after the dialog's own words with the app's
short sentence ("“seedbox” is switched off.", "This version of the app can't read “transmission”.")
or the client's own, masked; one some of whose torrents couldn't be placed — no save path, no
metadata yet, a path no pair covers or two cover differently, an unknown size, a file of the copy's
size the disk doesn't show — is `Partial`, with how many. The 4 MiB cap fails in the adapter's own
words, since on a big installation that is the honest size of the list.

**What the live read showed.** On 2026-10-09 the reference installation's qBittorrent, read through
the app's own adapter and stored settings from a copy of the development data directory, answered in
355 ms: 358 torrents — 353 seeding, 4 errored, 1 downloading —, every one with its files, 848 files,
each named below its save path with the torrent's folder first, and every path mapped into the app
through the `/downloads` → `/media` pair, none left uncovered. No copy was matched to a torrent's
file there: that PC reaches no media file.

`ReplacementOptionsHoldingsHttpTests.Options_without_the_dialog_asking_for_holdings_reads_no_torrent`,
`ReplacementOptionsHoldingsHttpTests.Options_with_holdings_marks_the_copies_torrents_hold_whatever_their_state_and_only_reads`,
`ReplacementOptionsHoldingsHttpTests.Options_with_holdings_names_each_of_two_torrents_of_one_name_that_list_a_copy`,
`ReplacementOptionsHoldingsHttpTests.Options_with_holdings_does_not_mark_a_copy_held_by_a_torrent_file_of_its_size_that_is_another_file`,
`ReplacementOptionsHoldingsHttpTests.Options_with_holdings_marks_a_copy_that_is_a_second_name_of_a_torrents_file`,
`ReplacementOptionsHoldingsHttpTests.Options_with_holdings_says_a_copy_has_other_names_when_the_torrents_name_lies_outside_the_media_folders`,
`ReplacementOptionsHoldingsHttpTests.Options_with_holdings_counts_the_torrents_it_can_not_place_and_still_marks_the_rest`,
`ReplacementOptionsHoldingsHttpTests.Options_with_holdings_says_a_switched_off_client_was_not_read_and_asks_it_nothing`,
`ReplacementOptionsHoldingsHttpTests.Options_with_holdings_says_a_qbittorrent_too_old_to_list_the_files_was_not_read`,
`ReplacementOptionsHoldingsHttpTests.Options_with_holdings_reads_no_torrent_while_no_copy_can_be_ticked`
and
`DownloadsSendHttpTests.Create_records_what_the_release_turned_out_to_be_and_never_reads_the_whole_client`
pin it, and `QBittorrentAdapterJobsWithFilesTests` the read itself.
