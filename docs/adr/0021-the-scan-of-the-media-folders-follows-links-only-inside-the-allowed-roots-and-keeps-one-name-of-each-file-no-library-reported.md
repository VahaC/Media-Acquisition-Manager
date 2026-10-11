# 21. The scan of the media folders follows links only inside the allowed roots, and keeps one name, with no version, of each file no library reported

Status: accepted (0.2.0, MAM-18)

## Context

MAM-18 scans the allowed folders for the files no library reported, movies' and episodes' alike
(ADR 0017). Phase B's scope set the rule beforehand: inside the allowed roots only, and without
following links out of them — the rules MAM-9's inspection applies to the library's own files
([ADR 0009](0009-the-app-reads-a-file-only-inside-the-allowed-roots-and-keeps-each-reading-by-the-files-identity.md));
specification §4 says to scan "only explicitly authorized roots" and not to "follow arbitrary
symlinks outside them".

A library's word is not enough to find duplicates. Jellyfin reports what it makes items of: a DVD
or Blu-ray folder is a version with no file (ADR 0006, MAM-8 section), a file it hasn't scanned yet
or that lies in a folder no library covers is no item at all, and such a copy takes space like any
other. MAM-17 counted 1394 video files in the reference installation's library folders
([ADR 0018](0018-a-file-is-a-duplicate-whatever-a-torrent-client-does-with-it-and-its-space-counts-once-per-identity.md)),
on one spinning disk that also holds four Blu-ray folders and no file with a second name; seeding
plays no part in a duplicate, so Phase B reads no download client.

The obvious shapes each go wrong somewhere. Every file opened by ffprobe to tell media from the rest
starts it thousands of times on posters, subtitles and `.nfo` files. A link inside a root can lead
anywhere, `/data` included, or back to a folder above it, and a diamond of links doubles what lies
below it. A walk that forgets whatever it didn't find forgets every name on a disk that was away for
one scan, and the readings with them. A file reached through a folder link is one file under two
names. And "no library reported it", stored on a row, goes stale at the next sync or the next edit of
a path mapping (ADR 0003).

## Decision

**The owner's Scan folders walks every allowed root for media file names — a link followed only when
it ends inside a root, nothing opened for reading — and stores one name, with no version and the
app's own path, for each file no library reported, which the usual file inspection then reads;
whether a name is in a library is decided by its path, against the libraries' file names as they map
now (Vasyl's decisions 1 to 3).**

**A media file is a name with a video extension (Vasyl's decision 2).** `MediaFileNames` takes a
regular file whose name ends in `mkv mp4 m4v avi mov ts m2ts mts mpg mpeg vob wmv asf flv webm ogv rm
rmvb iso`, letter case ignored, only the part after the last dot (`film.mkv.part` is none). Nothing is
special-cased by folder: the clips of a Blu-ray or DVD folder count like any other file. An extension
decides only whether the app looks at a file, never what it is.

**The walk is ADR 0009's look, a folder at a time.** `FolderWalker` (behind
`IFileInventory.ListMediaFilesAsync`) walks the roots in the owner's order, depth first, each
folder's entries in ordinal name order, hidden ones included. Every folder has two paths: its app
path under the root as the owner typed it, which its names are given by, and its resolved path,
which is opened. A child's paths are its folder's paths and its name, never the full name a listing
reports. A symbolic link or junction is resolved by `LinkResolver` (40 links at most) and followed
when it ends inside a resolved root, its names given under the link's own path; one leading out is
refused and never listed through. A folder that is the one it is in, or one above it on the way
down — by its resolved path, and on Linux by its device and inode too — is not walked again: a link
loop, a bind mount of a folder inside itself. On Linux a folder is opened from `/` one name at a
time with `O_PATH|O_DIRECTORY|O_NOFOLLOW|O_CLOEXEC` (`IFileStatusReader.OpenFolder`) and listed
through `/proc/self/fd/<n>`, so a folder swapped for a link after the checks is refused, not listed.
A folder is read whole before any entry is judged, and nothing is opened for reading or written.

**What the walk couldn't list keeps its names; what it refuses doesn't.** A root that is missing,
a folder the app may not list or whose listing fails half-way, a folder changed while it was being
checked and a link that leads nowhere now are `Unlisted`: the names stored at or under them stay, so
a mount that is away costs nothing. A root that lists no entry at all is `Unlisted` too ("“/media” is
empty; if it is a mount, it may be away, so the files found in it before are kept."); an empty
folder below a root is just empty. A link leading out, a chain of more than 40 links, a loop, a name
that isn't valid UTF-8 or holds a control character, and a path longer than 4096 characters are
`Skipped`: nothing under them is a name of the media folders. Neither fails the scan; each is a
problem it counts, the owner reading the first one's sentence.

**The walk is bounded.** At most 100,000 folders listed and 200,000 entries given
(`FolderWalker.MaximumFolders`, `MaximumEntries`); past either it gives one `LimitReached` entry and
ends, and the scan fails with that sentence, storing nothing and queuing no inspection: an
incomplete walk would forget every name it didn't reach.

**A file is not a name, and a library's file is matched by path.** The names the walk reaches are
grouped by where they really lead: two names through a folder link are one file, two hardlinks two.
A file is reported when any of its names is the app path of a file name a library reported — any
`PhysicalFiles` row with a version, a movie's or an episode's, mapped through its integration's
current mappings (`LibraryAppPaths`), `NormalizedPath` equality, so letter case follows the path's
style — never by a file name's text, a title or an id.

**One name per unreported file, with no version (Vasyl's decision 1).** It is a `PhysicalFiles` row of
source kind `FileInspection`, with no connection, item, library, reported size or version, whose
`ServicePath` is the app's own canonical path: the app is the reporting source and no mapping applies
to it. The row one of the file's names already has is kept, its id, first-seen time and readings with
it, and `ObservedUtc` moves; else the first name in walk order is stored. Making `MediaVersionId`
nullable rebuilds `PhysicalFiles` once on upgrade (EF warning 20410); Vasyl accepted it for one table
of names that the inspection, MAM-19 and MAM-20 read, as ADR 0006 planned.

**A scan keeps and forgets in one transaction.** After the walk, with the run's outcome: a stored
name the walk reached under a library's path, or as another name of a file it found, goes; one
outside every root this scan walked (the owner may have narrowed them by mistake; the inspection
shows it outside) or at or under an `Unlisted` place is kept; any other goes, as the walk looked
where it was and found nothing. A failure or a stop stores nothing.

**Counted when read.** The Media folders row counts only the scan's names no library name maps to
now, through the same `LibraryAppPaths`, so a sync that starts reporting a path, or a fixed mapping,
takes it off the count at once; the next scan deletes its row. Nothing about the libraries is stored
on a scan's row.

**The owner starts it, and the inspection reads it (Vasyl's decision 3).** `POST /api/library/scans`
is owner-only, as it walks the owner's disks (202 + run row + reconciler, one queued and one running
at most). A scan runs only on that request: no timer, not after a sync, not at the start; the
reconciler fails one a stop interrupted, queues a queued one again and queues none of its own. Once
its names are stored it queues the usual file inspection (`FileInspectionTrigger.FolderScanned`),
which reads them with ffprobe through MAM-9's cache, as a sync does. The Library page shows it as a source row of its own, **Media
folders**, with **Scan folders**. No download client is read (ADR 0018).

| Rejected option | Why not |
| --- | --- |
| ffprobe every file to tell media from the rest | Thousands of starts on posters, subtitles and `.nfo` files at the first scan, for a question an extension answers |
| Skip Blu-ray and DVD folders | Their clips take space like any other copy, and they are where the library has no file to report |
| A table of its own for the scan's names | The inspection, MAM-19's hash and MAM-20's grouping would read two tables of names; ADR 0006 planned `FileInspection` rows here |
| Refuse every link | Owners link folders inside their media tree; the danger is a link leading out, which is refused (ADR 0009) |
| Forget every stored name the walk didn't find | A disk away for one scan would cost every name on it, and the inspection after it their readings |
| Store every name of a file | A folder link would count each file under it twice in no library, and every inspection would look at it twice |
| Scan on a timer, after each sync or at the start | Walks a spinning disk nobody asked about; Vasyl chose a scan on request |
| List a folder again by its path text on Linux | A folder swapped for a link between the check and the listing would lead the walk out of the roots |
| Match a found file by its name, title or id | A guess (ADR 0017); the same name in another folder is another file |
| Store "no library reported it" on the row | Stale after the next sync or mapping edit, until a rescan |
| Store what a walk past its limit found | Every name it didn't reach would be forgotten |

What a media file is: `MediaFileNamesTests.VideoExtensions_are_the_nineteen_decided_in_lower_case_without_a_dot`
and `MediaFileNamesTests.IsMediaFile_refuses_a_download_not_yet_finished`. The walk:
`LocalFileInventoryListingTests.List_walks_nested_and_hidden_folders_and_gives_hidden_files`,
`LocalFileInventoryListingTests.List_follows_a_folder_link_inside_the_root_under_the_links_own_name`,
`LocalFileInventoryListingTests.List_refuses_a_folder_link_leading_out_and_lists_nothing_behind_it`,
`LocalFileInventoryListingTests.List_refuses_links_that_lead_to_each_other`,
`LocalFileInventoryListingTests.List_skips_a_folder_with_the_identity_of_one_above_it`,
`LocalFileInventoryListingTests.List_answers_a_link_that_leads_nowhere_as_unlisted`,
`LocalFileInventoryListingTests.List_answers_an_empty_root_as_unlisted`,
`LocalFileInventoryListingTests.List_stops_with_one_limit_reached_entry_past_its_folders`,
`LocalFileInventoryListingLinuxTests.List_answers_a_folder_swapped_for_a_link_after_its_folder_was_listed_as_unlisted`,
`LocalFileInventoryListingLinuxTests.List_skips_names_that_are_not_utf8_and_never_walks_such_a_folder`
and `LocalFileInventoryListingLinuxTests.List_answers_a_folder_the_app_may_not_list_as_unlisted_and_walks_its_neighbours`
(the Linux cases run in CI). What a scan stores:
`FolderScanRunHttpTests.Scan_finds_every_media_file_stores_a_name_for_each_one_no_library_reported_and_reads_none`,
`FolderScanRunHttpTests.Scan_matches_by_the_librarys_current_mappings_and_by_path_alone`,
`FolderScanRunHttpTests.Scan_counts_a_library_file_reached_through_a_folder_link_too_once_and_stores_no_name_for_it`,
`FolderScanRunHttpTests.Scan_keeps_the_row_a_later_name_of_the_file_already_has_instead_of_storing_its_first_name`,
`FolderScanRunHttpTests.Scan_with_a_folder_it_cant_list_counts_the_problem_and_keeps_the_names_stored_under_it_as_they_were`,
`FolderScanRunHttpTests.Scan_counts_a_link_leading_out_as_a_problem_the_owner_reads_and_forgets_what_was_stored_under_it`,
`FolderScanRunHttpTests.Scan_keeps_a_name_stored_outside_every_current_root`,
`FolderScanRunHttpTests.Scan_that_reaches_the_walks_limit_fails_with_its_sentence_and_stores_and_queues_nothing`
and `FolderScanWorkerHttpTests.RunQueued_whose_outcome_cannot_be_written_rolls_back_every_name_it_had_saved`.
The count when read: `LibrarySourcesScanHttpTests.Sources_takes_a_scanned_name_off_the_count_once_a_sync_reports_its_path_without_a_rescan`
and `LibrarySourcesScanHttpTests.Sources_takes_a_scanned_name_off_the_count_once_the_owner_fixes_a_path_mapping`.
The trigger: `FolderScanStartHttpTests.Start_as_a_signed_in_non_owner_is_403_with_the_owner_sentence_and_queues_nothing`,
`FolderScanWorkerHttpTests.Reconcile_with_nothing_under_way_queues_no_scan_of_its_own` and
`FolderScanInspectionHttpTests.RunQueued_after_a_scan_reads_the_new_names_and_records_their_identity_size_and_modification_time`.

## Consequences

**MAM-19 and MAM-20 read one table of names.** The scan's names sit in `PhysicalFiles` beside the
libraries', each with the identity, size and modification time the inspection read
(`ReadSizeBytes`, `ReadModifiedUtc`), so space by identity (ADR 0018) and the hash need no second
source. A name no library reports is counted only through `LibraryAppPaths` at read time, never
from a stored flag.

**Known limits, left on purpose.** A link whose target the app may not look into can't be told from
one leading out, so it is `Unlisted` and the names stored under it are kept; the inspection still
reads nothing they lead to outside the roots. On Windows, where the app is developed, a folder is
listed again by its path, a junction swapped in since the checks is followed, and a loop is told by
path alone. A name that isn't valid UTF-8 is skipped, so a file named so is never found. An empty
mount point below a root is just an empty folder: the names under it go and the inspection after
the scan forgets their readings, so once it is mounted again the next scan finds them anew and its
inspection reads them again. The scan doesn't follow the disk: a file added later is found by the
next one. The page counts the names in no library and doesn't list them yet. The live check on the
reference installation (Vasyl's decision 4) is still to come.

## Anti-consequence

This allows reading names only: the walk opens no file for reading and writes nothing, and the
roots are still no licence to write (Phase C). A stored name says nothing about which work it is: it
belongs to no work and no version, shows in no works list, search or match, and no file or folder
name makes it a movie or an episode (ADR 0017); how such a file becomes a possible match is MAM-20's
to decide.

## Duplicates (added 0.2.1, MAM-20)

**MAM-20 reads the one table of names, as the Consequences said.** The duplicates' grouping reads
the scan's names from `PhysicalFiles` beside the libraries', each with the identity, size and
modification time the inspection read, and tells a name no library reports only at read time through
`LibraryAppPaths`: a scan row whose path a library reports now is left out, so it never looks like
a second name of its own file ([ADR 0028](0028-duplicates-are-worked-out-on-every-read-in-four-classes-by-one-file-one-hash-one-registry-key-or-one-title-and-year-and-a-group-only-shows.md)). A scanned name with the identity of a library's
file is a second name of that file, and a scanned file with a library file's SHA-256 a byte-identical
copy of it.

**How a scanned name becomes a possible match, as the Anti-consequence left to MAM-20.** It is a
subject of its own, offered beside a library work or another scanned name only by what its file name,
then its folder's name, claims — a title with one year —, and never when either names seasons or
episodes. It still belongs to no work and no version, is never a copy of the same work, and each
match is for the owner to confirm.

`LibraryDuplicatesSameFileHttpTests.List_leaves_out_a_scanned_name_a_library_reports_now`,
`LibraryDuplicatesSameFileHttpTests.List_groups_a_scanned_hardlink_name_with_the_librarys_name`,
`LibraryDuplicatesByteIdenticalHttpTests.List_groups_a_scanned_copy_with_a_library_file`,
`LibraryDuplicatesPossibleHttpTests.List_offers_a_scanned_file_named_with_a_works_title_and_year`,
`LibraryDuplicatesPossibleHttpTests.List_offers_a_scanned_file_by_its_folders_name`,
`LibraryDuplicatesPossibleHttpTests.List_offers_two_scanned_files_named_with_one_title_and_year` and
`LibraryDuplicatesPossibleHttpTests.List_never_offers_a_scanned_file_whose_name_has_no_year` pin it.

## The quarantine is never walked (added 0.3.0, MAM-26)

**The scan leaves out every entry named `.mam-quarantine`, at any depth, follows no link into one
and gives nothing for an allowed folder that lies in one, so a file a replacement moved there is
never a name no library reported ([ADR
0030](0030-a-replaced-copy-is-renamed-into-a-quarantine-on-the-same-mount-and-stays-there-until-the-owner-restores-it-or-deletes-it-for-good.md)).**

**Where in the walk.** The name is checked first, before the rules of a usable name and of a path's
length, and the entry gives nothing — not a place it didn't scan, so no problem is counted and the
Media folders row's count stays the files the libraries could report. The name is matched as
`QuarantineFolder.IsQuarantineName` reads it: in its own letter case on a POSIX path, in any on a
Windows one. A link is followed only after the rule that it ends inside the allowed folders, and is
then left out when what it leads to lies in a quarantine. `IFileInventory.ListMediaFilesAsync` says
so, and `FakeFileInventory` does the same, so the HTTP tests' scans match.

**What follows.** A quarantined file is never a scanned name, so it is never offered as a possible
match on the Duplicates page (ADR 0028) nor counted in the room the files take; the replacement job
is where the app knows it, and the Media folders page shows the room it takes from the job's rows.
The walk still writes nothing: the quarantine's renames go through a contract of their own (ADR
0030), never through `IFileInventory`.

`LocalFileInventoryListingTests.List_never_walks_a_folder_named_mam_quarantine_at_any_depth_and_counts_no_problem`,
`LocalFileInventoryListingTests.List_never_follows_a_link_into_a_quarantine`,
`LocalFileInventoryListingTests.List_of_a_root_that_lies_in_a_quarantine_gives_nothing_for_it_and_walks_the_others`
and `QuarantineFolderTests.Holds_a_quarantine_folder_and_anything_inside_one_at_any_depth` pin it.
