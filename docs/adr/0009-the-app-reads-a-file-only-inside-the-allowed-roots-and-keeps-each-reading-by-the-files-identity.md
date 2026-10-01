# 9. The app reads a file only inside the allowed roots, every link resolved, and keeps each reading by the file's identity

Status: accepted (0.1.0, MAM-9)

## Context

MAM-9 shows verified facts about the files the library already has: duration, resolution, codecs,
bit rate, audio and subtitle tracks with their languages, and HDR. Specification §3.1 asks for them
"only when those fields are actually available", unknown rather than guessed; §4 says to "scan only
explicitly authorized roots; do not follow arbitrary symlinks outside them"; §3.3 keys the hash
cache "by file identity, size, and modification time" and invalidates it when any of them changes.

The paths come from Jellyfin, through the integration's path mappings (ADR 0003): a service's path
is where to look, never a reason to open whatever it names. The container mounts media read-only,
but it also holds the app's own data at `/data`, the database and the encryption key among it, and a
link inside a media folder can point there as easily as anywhere else. The media inspector, ffprobe,
parses files downloaded from the internet, and some formats name other files and URLs to open.

Reading is slow where it matters: the reference library is 870 films on one spinning 14 TB disk, and
ffprobe reads the start of each. Reading them all again at every sync and every start would keep the
disk busy for minutes each time. And a stored file row is one name, not one set of bytes (ADR 0006):
two hardlinks are two rows, and a sync replaces a row when its path moves (ADR 0008).

Vasyl decided four things for MAM-9: the allowed roots are a setting on a page, not an environment
variable; an inspection runs after every successful sync, when the app starts, and when the owner
asks; the Library page shows its progress; and track titles ("MVO | DniproFilm") are not shown.

## Decision

**The app opens a library file only when its path, and every link on the way to it, lies inside a
folder the installation owner allowed — none by default — and keeps what the media inspector read
under the file's device, inode, size and modification time, so the same bytes are read once whatever
names lead to them, and read again as soon as any of the four changes.**

**The allowed roots are the owner's setting** (Vasyl's decision). `GET`/`PUT
/api/settings/allowed-roots` behind the Media folders page keeps them in the `AllowedRoots` table,
owner-only for reading too, as they describe the host. `AllowedRootRules` refuses, each with a
sentence: more than 20, a path that isn't full, a `.` or `..` folder, the whole file system, a path
of the other operating system's style than the one the app runs on, the same folder twice and a
folder inside another. With none, which is the default, nothing is read and a request to inspect
answers 409 with the way to the page. Saving starts no inspection.

**Every link is resolved, and where it ends must still be inside.** `LocalFileInventory` walks the
path folder by folder from the file system's root and follows each link it meets — symbolic links
and Windows junctions, relative or absolute, at most 40 — keeping the resolved part free of links,
so a `..` in a link's target means the folder above where the link really is. The allowed roots are
resolved the same way, so a root may itself be a link the owner chose. The path as reported must be
inside a root, and the path it resolves to must be inside a resolved root; anything else is
`OutsideAllowedRoots`. Only a regular file the app can open for reading counts, and it is opened
without following a link: on Linux from `/`, name by name with `O_NOFOLLOW`, the last name with
`O_PATH` so a pipe or a device is looked at and never opened for reading. A link met there was swapped
in after the walk — by anyone who may write inside a root — and the file is refused, not read through
it.

**The inspector opens the checked file and nothing else.** ffprobe reads the file the app opened:
on Linux, where the app runs, the very descriptor, which it inherits, as `/proc/self/fd/<n>`, so a
link swapped in at the path after the checks leads it nowhere; on Windows, where the app is
developed, the resolved path while the app holds the file open without sharing its deletion, which
keeps the file and every folder above it from being renamed. It gets it as `file:<path>` (a name
with a colon is still a file) with `-protocol_whitelist file`, so a crafted file can't make the
backend open a URL, and `-format_whitelist` of the containers films and episodes come in —
Matroska, MP4 and MOV, AVI, MPEG-TS, MPEG-PS, ASF, FLV, Ogg and RealMedia, and WAV for the tests —
so a downloaded file named `.mkv` whose content is an `ffconcat` list or an HLS playlist can't make
it open the files it names, outside the roots included; such a file is unreadable, its sentence
naming what it is. Through an argument list and never a shell, for at most 2 minutes and 8 MiB of
answer. The open file is asked again after the read; if its size or modification time moved, the
reading belongs to neither version and is not kept (`ChangedWhileReading`). When ffprobe fails, the
sentence quotes its verdict — the last line it writes, not a demuxer's noise before it.

**A reading belongs to bytes, not to a name.** `MediaInspections` holds one row per key `(DeviceId,
Inode, SizeBytes, ModifiedUtc)` — unique — with its tracks; a file name points at its reading and
carries what the app's last look at it found (`InspectionStatus`, the sentence, when, and the
identity and link count it read). Two names of one inode are read once and share the reading; a name
a sync replaced, because the file moved, finds its reading again without a read; a changed file has
a new key, so it is read again, and the reading of the old key, which no name points at any more, is
deleted at the end of the run. A run deletes only the readings that were its to forget — those no name
pointed at when it started, and those it moved a name away from — so one a sync orphaned while the run
went, a file moved under a new name, waits for the run that sync queued, which finds it there. A name
the run found nothing at, or couldn't read, keeps pointing at
its last reading — its status says the facts don't apply now — so a mount that was away for one
run, or a root narrowed by mistake, costs no reading when it is back. Nothing else deletes a reading, so the name needs no foreign key to it
— which SQLite could only add by rebuilding `PhysicalFiles`, a three-transaction migration on a table
that now holds the library.

**A failure is cached too, except the host's.** A file ffprobe can't read is stored as a reading
with its sentence, so a damaged file isn't read on every run, only once it changes. An inspector that
can't start says something about the host, not the file: it is never cached, the files still waiting
in that run get the same sentence without starting it again, and the next run tries again. A
platform without file identity caches nothing (`IdentityUnknown`): an unknown identity beats a
guessed one. Linux reads it with `statx` (one layout on amd64 and arm64, `st_dev` rebuilt as glibc's
`makedev` so it matches `stat -c %d`); Windows, where the app is developed, with the NTFS file index.

**Unknown stays unknown.** The parser reads ffprobe's JSON field by field. A video track's dynamic
range comes from a Dolby Vision configuration record, else from its transfer characteristic (PQ is
`Hdr10`, HLG is `Hlg`, any other declared one is `Sdr`); a stream that declares none has none, even
when it is 10-bit — the reference Continental episode is one. Cover art is not a track. A language
tag `und` is no language. Track titles are not stored.

**One run at a time, two files at a time, every file a result.** Runs follow the sync's pattern
(202 + run row + reconciler, MAM-8) with three triggers; at most one is queued and one running, a
trigger that finds one queued joins it, and the reconciler fails a run a stop interrupted, queues
the queued one again and queues the start's. Only the one worker runs a run, so a running run the
next claim finds is dead — its worker couldn't mark it failed, the database refusing that too — and
the claim fails it rather than break the one-running index until a restart. The Library page says
when a run waits behind the running one, and Inspect files is disabled while one waits. A run reads two files at a time
(`FileInspectionRunner.MaximumConcurrentReads`, the spinning disk's sweet spot) and writes each
file's outcome as it comes, so the page's counts move and a stopped run keeps what it read. A file it
can't read is an outcome with its sentence, shown as unknown — never a failure of the run.

| Rejected option | Why not |
| --- | --- |
| The roots in an environment variable beside the mount | Vasyl chose the settings page: edited where the rest is, effective at the next run without a restart |
| Roots taken from Jellyfin's library folders | A guess at what the owner allows, and a service's word; specification §4 asks for explicitly authorized roots |
| Refuse every link | Owners link folders inside their media tree; the danger is a link leading out, which is what is refused |
| Check the path's text only | A link inside a root can lead anywhere, `/data` included |
| Key the cache by the file name (the row) | A sync replaces the row when the path moves (ADR 0008), and two hardlinks would be read twice |
| Key the cache by path, size and time | A file replaced at the same path with the same size and time keeps a stale reading; the identity changes when the bytes are new |
| Read every file on every run | Minutes of a spinning disk at each sync and start for facts that didn't change |
| A foreign key from the name to its reading | A table rebuild in three transactions on upgrade, to guard a deletion only the run does, and only of readings no name points at |
| Show the track titles | Vasyl's decision: a title is the release's claim, not a verified fact, and some are in a broken encoding |
| `-protocol_whitelist file` alone | It stops URLs, not the demuxers that open local files a crafted list names (concat, HLS) |
| Open the checked path again by its text | Anyone who may write inside a root can swap a folder for a link between the check and the open |
| Delete every reading no name points at when a run ends | A sync landing during the run orphans the readings of the files it moved, and the next run would read them all again |
| A periodic scan | Wakes the disk on a timer; Vasyl chose after a sync, at the start and on request |

The roots and the links are pinned by
`LocalFileInventoryLinksTests.Describe_through_a_folder_link_leading_outside_the_root_is_refused`,
`LocalFileInventoryLinksTests.Describe_through_a_file_link_leading_outside_the_root_is_refused`,
`LocalFileInventoryLinksTests.Describe_through_a_relative_link_climbing_out_with_dot_dot_is_refused`,
`LocalFileInventoryTests.Describe_a_file_in_a_sibling_folder_whose_name_starts_with_the_roots_is_refused`
and `LocalFileInventoryInspectionTests.InspectMedia_through_a_link_leading_out_never_starts_ffprobe`
(the symbolic-link cases run on Linux, in CI; Windows without Developer Mode covers the folder links
with junctions), and the rules of a root by `AllowedRootRulesTests`. The open that follows no link by
`LinuxFileStatusReaderTests.Open_through_a_folder_swapped_for_a_link_since_the_check_is_refused`,
`LinuxFileStatusReaderTests.Open_gives_ffprobe_the_file_it_opened_even_after_its_path_leads_elsewhere`
and `LinuxFileStatusReaderTests.Describe_a_named_pipe_is_not_a_regular_file_and_is_never_opened`. What
ffprobe is given by `FfprobeProcessTests.Run_hands_ffprobe_its_options_and_the_file_as_separate_arguments`,
`FfprobeProcessTests.Run_of_the_real_ffprobe_refuses_a_file_whose_content_is_a_list_of_other_files`
and `FfprobeProcessTests.Run_that_never_finishes_is_killed_at_the_time_limit`; a file that changes under
the read by `LocalFileInventoryInspectionTests.InspectMedia_of_a_file_that_grows_while_ffprobe_reads_it_is_changed_while_reading`.
The cache by `FileInspectionCacheHttpTests.Inspect_again_with_nothing_changed_reads_nothing_and_keeps_each_reading`,
`FileInspectionCacheHttpTests.Inspect_after_a_change_of_one_key_part_reads_the_file_again_and_forgets_the_old_reading`
(size, modification time, inode and device), `FileInspectionCacheHttpTests.Inspect_reads_two_names_of_one_file_once_and_links_both_to_one_reading`,
`FileInspectionCacheHttpTests.Inspect_of_a_file_back_unchanged_after_a_run_without_it_reads_nothing`,
`FileInspectionCacheHttpTests.Inspect_while_a_sync_replaces_a_name_keeps_its_reading_for_the_next_run`
and `FileInspectionCacheHttpTests.Inspect_again_does_not_read_a_file_the_inspector_could_not_read_before`.
The outcomes by `FileInspectionRunHttpTests.Inspect_of_an_unreadable_file_beside_a_good_one_succeeds_and_shows_it_unknown`,
`FileInspectionRunHttpTests.Inspect_without_the_inspector_gives_every_waiting_file_its_sentence_and_still_succeeds`,
`FileInspectionRunHttpTests.Inspect_of_a_file_without_identity_is_identity_unknown_and_reads_nothing`
and `FileInspectionRunHttpTests.Inspect_reads_exactly_two_files_at_a_time`. Unknown values by
`FfprobeOutputParserTests.TryParse_reads_every_continental_track_in_file_order_and_leaves_the_cover_art_out`
and `FfprobeOutputParserTests.TryParse_reads_the_dynamic_range_from_the_transfer_characteristic`.
The triggers by `FileInspectionTriggerHttpTests.Sync_with_a_root_allowed_queues_a_library_synced_inspection_and_looks_at_no_file_yet`,
`FileInspectionWorkerHttpTests.Reconcile_fails_the_running_run_with_the_restart_sentence_leaves_finished_ones_and_queues_the_start_run`,
`FileInspectionWorkerHttpTests.RunQueued_with_a_dead_run_left_running_fails_it_with_the_app_error_and_runs_the_queued_one`
and `FileInspectionStartHttpTests.Start_without_an_allowed_root_is_409_with_the_sentence_and_queues_nothing`.

## Consequences

**Phase B and C build on it.** The hardlink count and the identity on every name are there for Phase
B's space accounting, and a hash can use the same key. Phase C's checks reuse both members:
`InspectMediaAsync` is check 2 (the new file opens and has the expected streams), `DescribeAsync`
inside the roots is the recheck of check 5.

**What it leaves out on purpose.** A file changed on disk keeps its old facts until the next run looks
— after a sync, at the start or on request; there is no watcher. HDR10+ shows as HDR10, as the stream
summary doesn't carry its metadata, and a Dolby Vision profile isn't shown. A file whose every name a
sync dropped loses its reading at the inspection that sync queues, so it is read again if a later sync
brings it back; a file moved within one sync, and the files of a root removed and added back, keep
theirs. A file in a container left off the format list (an ISO image, a raw stream) is unreadable,
its sentence naming what it is; adding a format is a change of one constant. Every start looks at
every file, which wakes the disk once.

## Anti-consequence

This allows reading only. The roots are not a licence to write: Phase C's quarantine needs write
access of its own, limited to the roots (specification §4). A shared reading says two names are one
set of bytes, nothing about which work they are (ADR 0006, ADR 0008). And a mapped path is still
only where to look (ADR 0003): the identity the app reads itself decides what a file is.
