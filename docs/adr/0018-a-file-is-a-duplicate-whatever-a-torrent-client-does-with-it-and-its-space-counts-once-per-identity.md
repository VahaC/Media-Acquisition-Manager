# 18. A file is a duplicate whatever a torrent client does with it, and its space counts once per identity

Status: accepted (0.2.0, MAM-17)

## Context

Specification §7, question 6, asked whether the library's files are original seeding files,
hardlinks of them or independent copies. §3.3 counts a hardlink's space once and estimates the
recoverable space "with hardlinks/snapshots in mind"; §3.4, check 6, leaves the old file of a
replacement untouched while a torrent seeds it. ADR 0003 found the libraries and the downloads in
the same folders and left unmeasured how often a library file is a torrent's data.

MAM-17 measured it on 2026-10-04: every regular file under `/mnt/media14tb/jelly_qbit_data/Torrent`
(the folder Jellyfin and the app see as `/media` and qBittorrent as `/downloads`), every
multiply-linked file on the whole 14 TB filesystem, and qBittorrent 5.2.4's 355 torrents with each
of their 845 files. The video files in the library folders, where "seeding" means a complete
torrent seeding now (768 files) or queued to seed (54):

| Library folder | Video files | A seeding torrent's data | A torrent still downloading | No torrent's |
| --- | ---: | ---: | ---: | ---: |
| `/media/movies` | 655 · 6.32 TiB | 379 · 3.52 TiB | 1 · 19.15 GiB | 275 · 2.78 TiB |
| `/media/cartoons` | 548 · 316.62 GiB | 366 · 249.62 GiB | – | 182 · 67.00 GiB |
| `/media/tv-shows` | 140 · 492.55 GiB | 77 · 315.35 GiB | – | 63 · 177.20 GiB |
| `/media/documentary` | 51 · 208.80 GiB | – | – | 51 · 208.80 GiB |
| All | 1394 · 7.31 TiB | 822 · 4.07 TiB | 1 · 19.15 GiB | 571 · 3.22 TiB |

- **No file has a second name.** No regular file on the filesystem has a link count above 1. A
  library file is a torrent's own data at the very path qBittorrent wrote it, or a file no torrent
  holds.
- **A torrent's data is seeding data.** 354 of the 355 torrents are complete and seeding (stalledUP
  297, uploading 3) or queued to seed (queuedUP 54); the other is a movie still downloading. Every
  torrent file lies inside a library folder, and none is missing.
- **Nothing imports.** Radarr 6.4.4 runs beside qBittorrent, with "use hardlinks instead of copy"
  on, but has never imported a file: its history is empty, its root folder is qBittorrent's own
  `/downloads/movies`, and its 6 files are ones it found there. Vasyl doesn't use it.
- **No two video files have one size.** No two library video files have the same size, so no two
  of them are byte-identical today. Only small files share a size: the `BDMV/BACKUP` copies of the
  four Blu-ray folders' metadata, and two audio tracks.

Asked whether Phase B should mark a seeding copy on the Duplicates page and leave it out of the
space that can be freed, Vasyl answered that a file being seeded doesn't stop it being a duplicate.

## Decision

**Phase B's duplicate classes and its space figures read only the files — their identity, size,
hash and the library's ids — never whether a download client holds or seeds them (Vasyl's
decision); a file's space counts once per device and inode.**

**Seeding is not evidence.** Two byte-identical files are byte-identical whether a torrent seeds
one, both or neither: the classes of §3.3 say what the files are, and seeding says what a client
does with one. The scan and the grouping read no client, and a seeded copy counts in the
recoverable-space estimate like any other.

**Space by identity, as ADR 0006 stores it.** Every name of one device and inode counts its size
once. On the reference installation that changes nothing today, since every file has one name, so
the rule rests on tests (MAM-19), not on the live library.

**Seeding matters to a replacement, not to a duplicate.** §3.4, check 6, stays Phase C's: what
replacing an old copy means when it is a seeding torrent's data, as 822 of the 1394 video files are
here, is MAM-23's to decide.

| Rejected option | Why not |
| --- | --- |
| Mark seeding copies in Phase B and leave them out of the estimate | Vasyl: being seeded doesn't make a file less of a duplicate |
| Expect hardlinks from an importer | None exist, and the one importer installed has never imported |

`PhysicalFileEntity`'s identity columns already hold the rule (ADR 0006). MAM-19's tests pin the
count by identity ("two hardlink names count their space once"), and MAM-20's that a seeding file is
classed like any other.

## Consequences

**Phase B needs no download client.** The scan, the hashing and the grouping work with qBittorrent
disconnected.

**The live check has little to watch.** With no hardlinks and no two video files of one size,
MAM-22 can't see the hardlink rule or a byte-identical pair of videos at work on the real library;
its record says so, and those criteria rest on the tests.

**Removing a duplicate can end a seed.** The Duplicates page offers no removal (§3.3); when Phase C
replaces a copy, check 6 applies there.

## Anti-consequence

This doesn't make a download client's data irrelevant to the app: Phase A's download jobs read it
(ADR 0014), and Phase C's check 6 will. It only keeps it out of what a duplicate is.

## Implementation (added 0.2.0, MAM-19)

**The count by identity is built, and shown.** The Library page says how much room the files take
on disk in every Jellyfin source's row and in the Media folders row ("Takes 6.5 TiB on disk: 870
files."), and for every stored file name in the file inspection's row ("All files take 6.54 TiB on
disk: 882 files."), with "1 more name leads to a file counted once." when a file has several names.
`FileSpace.Of` counts one file per device and inode, its size the one the app's last look at a name
read (`ReadSizeBytes`), never the size a library reported: a hardlinked file counts once in its row,
and a file a library and the scan, or two libraries, name counts in each row and once in the total.
A name without an identity or a read size is counted apart, as not measured yet, and left out of the
sum. `GET /api/library/sources` gives the figures as `space`, the same to every signed-in user
(Vasyl's decision 2,
[ADR 0022](0022-a-file-is-hashed-only-when-a-file-of-another-identity-shares-its-size-and-could-be-its-copy-as-the-last-step-of-the-inspection-one-at-a-time.md)).
As the Decision expected, the rule rests on the tests: every file of the reference installation has
one name.

**The hashing reads no client either.** MAM-19 hashes the files that could be byte-identical copies
as the last step of each file inspection, and a file a torrent seeds is a candidate like any other;
no download client is asked, as the Consequences said of the scan, the hashing and the grouping.

`LibrarySpaceHttpTests.Sources_count_two_hardlink_names_of_one_file_in_a_library_once` pins "two
hardlink names count their space once";
`LibrarySpaceHttpTests.Sources_count_a_file_two_libraries_name_in_each_row_and_once_in_total`,
`LibrarySpaceHttpTests.Sources_count_two_files_of_one_size_twice` and
`LibrarySpaceHttpTests.Sources_count_names_without_a_measured_file_apart_and_never_the_size_a_library_reported`
the rest of the count, and
`FileHashCandidatesHttpTests.Inspect_hashes_a_file_a_torrent_seeds_like_any_other_and_asks_no_download_client`
the client left alone.

## Duplicates (added 0.2.1, MAM-20)

**A seeding file is classed like any other, as built.** The grouping reads the names, identities,
sizes, hashes and ids the database holds, and `LibraryDuplicateService` is given no download client
([ADR 0028](0028-duplicates-are-worked-out-on-every-read-in-four-classes-by-one-file-one-hash-one-registry-key-or-one-title-and-year-and-a-group-only-shows.md)): two byte-identical files are one group whether a torrent seeds one, both or
neither, the seeded copy counted in the group's space and in the estimate like the other, and no
client is asked. The Consequences' "Phase B needs no download client" holds for the grouping.

**The recoverable space, with hardlinks and snapshots in mind (Vasyl's decision of 2026-10-07).**
Every group's space counts each file once by its device and inode, with the size the app read
(`FileSpace.Of`, as the MAM-19 section built it). Removing a hardlink name frees nothing, so a
same-file group says `0`. A byte-identical group says every copy's size but the largest's only when
every copy's file system is known not to share blocks — ext2 to ext4, JFS, ReiserFS, FAT, exFAT,
NTFS, read from Linux's mount table by device — and every copy's link count equals the names the app
knows; otherwise it gives no figure and says why: a file system that can share blocks (btrfs, ZFS,
XFS and the like, where reflinks and snapshots may make two copies take the room of one), an unknown
one (a network share, an overlay, a device the table doesn't name, or Windows), or a name the app
never found. Copies of one work and possible matches give no estimate at all, as none of them is
redundant.

`LibraryDuplicatesByteIdenticalHttpTests.List_classes_a_file_a_torrent_seeds_like_any_other_and_asks_no_download_client`
pins "a seeding file is classed like any other", with a qBittorrent that lists the torrent and is
never asked; `LibraryDuplicatesSameFileHttpTests.List_says_removing_a_hardlink_name_frees_nothing`,
`LibraryDuplicatesByteIdenticalHttpTests.List_groups_two_files_with_one_sha256_and_estimates_the_room_a_copy_takes_on_ext4`,
`LibraryDuplicatesByteIdenticalHttpTests.List_gives_no_estimate_on_a_file_system_that_can_share_blocks`,
`LibraryDuplicatesByteIdenticalHttpTests.List_gives_no_estimate_where_the_file_system_is_unknown`,
`LibraryDuplicatesByteIdenticalHttpTests.List_gives_no_estimate_when_a_file_has_a_name_the_app_never_found`
and `LibraryDuplicatesByteIdenticalHttpTests.List_gives_no_estimate_when_a_files_link_count_is_unknown`
the estimate.
