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
