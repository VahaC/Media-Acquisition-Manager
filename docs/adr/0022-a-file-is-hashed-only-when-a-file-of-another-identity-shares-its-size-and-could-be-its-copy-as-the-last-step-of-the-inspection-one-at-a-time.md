# 22. A file is hashed only when a file of another identity shares its size and could be its copy, as the last step of the inspection, one at a time

Status: accepted (0.2.0, MAM-19)

## Context

Specification §3.3 tells a byte-identical duplicate by a "full cryptographic hash of both files" and
asks for it sparingly: "Compute hashes lazily: shortlist candidates by size and metadata, then run a
full hash only where useful. Key the hash cache by file identity, size, and modification time;
invalidate it if any changes." The same table says of a hardlink "**do not** count the occupied
space twice", and Phase B's acceptance bar that "two hardlink names do not inflate occupied space".
MAM-9 already keeps each reading under the file's device, inode, size and modification time
([ADR 0009](0009-the-app-reads-a-file-only-inside-the-allowed-roots-and-keeps-each-reading-by-the-files-identity.md)),
and MAM-18 stores a name, with the identity and the size the app read, for every media file in the
allowed folders, a library's or not
([ADR 0021](0021-the-scan-of-the-media-folders-follows-links-only-inside-the-allowed-roots-and-keeps-one-name-of-each-file-no-library-reported.md)).

The reference installation is the case to plan for (MAM-17,
[ADR 0018](0018-a-file-is-a-duplicate-whatever-a-torrent-client-does-with-it-and-its-space-counts-once-per-identity.md)):
1394 video files, 7.31 TiB, on one spinning 14 TB disk; no file has a second name, and no two of
those video files have the same size. Two files of different sizes are never the same bytes, so
there, the sizes alone already say that no two films are copies.

The obvious shapes each go wrong somewhere. Hashing every file reads 7.31 TiB end to end from that
disk — the better part of a day at the first run, and again for each file that changes — to learn
what the sizes already say. A hash run of its own is a second pipeline — a queue, a run row, a
reconciler, a button, a progress line — with nothing to add: which files need a hash depends on the
identity, size and modification time the inspection's look has just read and on the media facts it
keeps, so such a run would look at every file again, or trust a look another run made. Space summed
per name counts a hardlinked file once per name, and summed per row counts a file two libraries
list, or a library and the scan, twice in the total; and the size a library reports is its word,
not the disk's. A cache keyed by path reads two names of one file twice and loses its hash when a
sync replaces the name (ADR 0008). A hash of a file that changed while it was read belongs to
neither version: kept under either key, it calls two different files identical, or a file
different from itself. And two full reads at once on one spinning disk make its head jump between
them, so both take longer than one after the other.

Vasyl decided three things for MAM-19 on 2026-10-05: hashing is the last step of every file
inspection run, with no run, queue, button or endpoint of its own (1); the Library page shows the
hashing in the file inspection's row, and the room the files really take in every Jellyfin source's
row, in the Media folders row and in total, each file counted once (2); and one file is hashed at a
time (3).

## Decision

**A file inspection ends by reading whole, one at a time, the files whose size a file of another
identity shares and whose media facts don't tell them apart, and keeps each one's SHA-256 under its
device, inode, size and modification time; the room the files take is counted from the app's own
look, once per device and inode (Vasyl's decisions 1 to 3).**

**A candidate is a file a copy could hide among.** The step starts once every name carries what
this run's look read. A file is one key, `(DeviceId, Inode, ReadSizeBytes, ReadModifiedUtc)`, of the
names the run loaded with an app path and found with an identity, and is named by its first name in
path order. Files are grouped by size; a size counts when files of two identities or more have it,
and a file of it is a candidate when one of another identity could be its copy: their readings
agree on the duration, the number of tracks and, track by track, the kind, codec, width, height,
channel count and language. A value either reading lacks is not compared, and a file without a
current reading — one ffprobe couldn't read, or an inspector that couldn't start — rules nothing
out: unknown tells nothing apart. The bit rate, profile, dynamic range, channel layout and default
flag are not compared, as another ffprobe may word them differently for the same bytes. So a file
whose size no other file shares is never read, nor are two names of one file alone, as the same
file twice is no copy; a third file of their size makes each identity a candidate once, read
through its first name.

**The cache is the readings' key.** A hash is a `FileHashes` row, unique on `(DeviceId, Inode,
SizeBytes, ModifiedUtc)`, with the digest in lowercase hex (what `sha256sum` prints) and when it was
read. No name points at it: a name's hash is the row its own read key names, so a change of any of
the four is a new key, and so a new read, while a renamed file, or another name of it, finds its
hash without one. A candidate whose key is stored is answered from the cache, and the inventory is
not asked.

**Only a whole read of an unchanged file is kept.** `IFileInventory.HashAsync` makes the checks of a
look (ADR 0009: inside the allowed roots, every link resolved, a regular file), then reads every
byte, 1 MiB at a time, of the file the reader opened — on Linux and Windows through that very
handle, never by its path again — and asks the open file for its facts again. They must agree with
the facts before the read on the identity, size and modification time, and the read must end at
that size; otherwise the answer is `ChangedWhileReading`: "“…” changed while the app was reading it
for its hash, so the hash can't be said to belong to either version." A read that fails is judged
after the facts are asked again, so a change wins. Such a file, and one gone, outside the roots or
unreadable, keeps nothing: its sentence is a problem of the run, never a failure, and the next run
tries again.

**A file that changed between the look and the hash keeps the hash of what was read.** When the
read comes with another key than the look's, the file changed in between and stayed put while it
was read: the hash is stored under the read's key, which its names carry from the next look on, and
the run counts "A file changed after this inspection looked at it, so its hash counts from the next
inspection." A read that comes without an identity keeps nothing. A hash already stored under a key
stands; the unique index refuses a second one.

**A hash lives as long as the app knows its bytes.** After hashing, the run deletes the hashes,
among those stored before it started, whose key neither a name carries nor a kept reading has. The
readings decide (ADR 0009): a name the last look found nothing at keeps its reading, and with it the
hash of those bytes, so a mount away for any number of runs costs no hash of a file the app has a
reading of; the reading of a file a sync moved under a new name while the
run went is kept for the run that sync queued, and so is its hash; and a changed file's old hash
goes with its old reading, in the run that found the change. A hash the run itself stored is never
its to forget: one kept under the key of bytes that changed after the look has neither a name nor a
reading yet.

**One file at a time (Vasyl's decision 3).** `FileInspectionRunner.MaximumConcurrentHashes` is 1: a
hash reads a file from its first byte to its last, and one disk reads one such stream fastest. The
looks and the media inspector's reads keep their two at a time.

**The last step of every inspection (Vasyl's decision 1).** It runs after the unused readings are
forgotten and before the run is marked succeeded, so every trigger hashes: a sync, a scan, the start
and **Inspect files**. A run that fails before it — no allowed root left, an error in the app —
hashes nothing and deletes no hash. A stop during it leaves the run `Running` with the hashes stored
so far; the reconciler fails it as before ("The app restarted before this inspection finished. What
it had read is kept.") and queues the start's run, which answers those from the cache and reads the
rest. That is how hashing survives a restart.

**Counted on the run.** `FileInspectionRuns` gains `HashCandidates` (`null` until the step listed
them), `HashCandidatesChecked`, `HashCandidatesRead`, `HashProblemCount` and `HashProblem`, the
first problem's sentence in path order cut to 1000 characters, written as each file is dealt with
so the page follows it. A run answers them as `hashing`, the sentence for the owner only, as it
quotes a path; the log line counts and never names one.

**Space by identity, from the app's own look (Vasyl's decision 2, ADR 0018).** `FileSpace.Of`
counts the names that carry an identity and a read size: one file per `(DeviceId, Inode)`, its size
once — the larger, when two names looked at a moment apart disagree —, how many names lead to the
files, and apart from the sum the names it has no measure of: not looked at yet, nothing found
there, or no identity. The size a library reported is never counted. A Jellyfin source's row counts
its integration's names, the Media folders row the scan's names no library reports now, and the
inspection's row every stored name, so a file a library and the scan, or two libraries, name counts
in each row and once in the total. Counted when read, and the same for every signed-in user: it
names no path.

**No download client.** A file a torrent seeds is a candidate and takes its space like any other,
and the step asks no client (ADR 0018).

| Rejected option | Why not |
| --- | --- |
| Hash every file | The whole disk read end to end at the first run, for files their sizes already tell apart |
| A hash run of its own, with a button and an endpoint | A second pipeline that needs the inspection's look and readings anyway (Vasyl's decision 1) |
| Size alone as the shortlist | Two different films of one size would be read whole; §3.3 says size and metadata |
| Compare every fact a reading has | A newer ffprobe may word the bit rate, profile or layout of the same bytes differently and hide a copy |
| Leave out a file ffprobe couldn't read | Unknown facts tell nothing apart; a damaged copy is still a copy |
| Key the cache by path, or by the name's row | Two names of one file read twice; a name a sync replaced loses its hash |
| Keep a hash read while the file changed | It belongs to neither version, and would call two different files identical |
| Two hashes or more at once | The disk's head jumps between the streams (Vasyl's decision 3) |
| Delete every hash no name has when a run ends | A file a sync moves during the run, or on a mount that is away, would be read whole again |
| Space as the sum of the sizes the library reported, or per row | Hardlinks counted per name, a file in two sources twice in the total, and the library's word for the disk's |

Who is a candidate:
`FileHashCandidatesHttpTests.Inspect_never_hashes_a_file_whose_size_no_other_file_shares`,
`FileHashCandidatesHttpTests.Inspect_of_files_all_of_different_sizes_hashes_nothing`,
`FileHashCandidatesHttpTests.Inspect_never_hashes_two_hardlink_names_of_one_file`,
`FileHashCandidatesHttpTests.Inspect_of_two_hardlink_names_and_a_file_of_another_identity_of_their_size_hashes_each_file_once_through_its_first_name`,
`FileHashCandidatesHttpTests.Inspect_never_hashes_two_different_films_of_one_size`,
`FileHashCandidatesHttpTests.Inspect_never_hashes_two_files_of_one_size_whose_compared_fact_differs`,
`FileHashCandidatesHttpTests.Inspect_hashes_two_files_of_one_size_whose_fact_only_one_of_them_knows`,
`FileHashCandidatesHttpTests.Inspect_hashes_two_files_of_one_size_whose_facts_differ_only_where_another_ffprobe_may_word_them_differently`
and `FileHashCandidatesHttpTests.Inspect_hashes_a_file_of_a_shared_size_the_media_inspector_couldnt_read`.
The read: `LocalFileInventoryHashTests.Hash_of_a_file_inside_the_root_is_the_sha256_of_its_bytes_with_its_facts`,
`LocalFileInventoryHashTests.Hash_through_a_folder_link_leading_outside_the_root_reads_nothing`,
`LocalFileInventoryHashTests.Hash_of_a_file_that_grows_while_it_is_read_is_changed_while_reading`,
`LocalFileInventoryHashTests.Hash_of_a_file_that_grows_and_shrinks_back_to_its_facts_while_it_is_read_is_changed_while_reading`,
`LocalFileInventoryHashTests.Hash_of_a_file_that_changed_and_then_failed_to_read_is_changed_while_reading`
and `LocalFileInventoryHashLinuxTests.Hash_reads_the_file_it_opened_after_its_path_was_swapped_for_a_link_leading_out`
(Linux, in CI). The cache: `FileHashCacheHttpTests.Inspect_again_with_nothing_changed_reads_no_file_whole_and_keeps_each_hash`,
`FileHashCacheHttpTests.Inspect_after_a_change_of_one_key_part_hashes_the_file_again_and_forgets_the_old_hash`
(size, modification time, inode and device), `FileHashCacheHttpTests.Inspect_of_a_renamed_file_finds_its_hash_without_reading_it_again`,
`FileHashCacheHttpTests.Inspect_of_a_file_back_unchanged_after_runs_without_it_reads_nothing_whole`
(one run away and three),
`FileHashCacheHttpTests.Inspect_while_a_sync_replaces_a_name_keeps_its_hash_and_the_next_run_finds_it_without_reading`
and `FileHashPersistenceTests.A_second_hash_of_the_same_identity_size_and_modification_time_is_refused`.
The step: `FileInspectionHashHttpTests.Inspect_of_a_file_that_changed_while_it_was_hashed_keeps_nothing_and_the_next_run_hashes_it_again`,
`FileInspectionHashHttpTests.Inspect_of_a_file_that_changed_between_the_look_and_the_hash_keeps_the_hash_under_the_reads_key_for_the_next_run`,
`FileInspectionHashHttpTests.Inspect_of_two_names_of_one_file_looked_at_either_side_of_a_write_keeps_the_first_hash_of_the_new_bytes_and_succeeds`,
`FileInspectionHashHttpTests.Inspect_of_a_file_that_cant_be_hashed_beside_a_good_one_succeeds_and_gives_its_sentence_to_the_owner_only`,
`FileInspectionHashHttpTests.Inspect_hashes_exactly_one_file_at_a_time`,
`FileHashCacheHttpTests.Inspect_after_every_media_folder_was_removed_hashes_nothing_and_deletes_no_hash`
and `FileHashWorkerHttpTests.RunQueued_stopped_by_a_shutdown_while_hashing_keeps_what_it_hashed_and_the_start_run_hashes_only_the_rest`.
The space: `LibrarySpaceHttpTests.Sources_count_two_hardlink_names_of_one_file_in_a_library_once`,
`LibrarySpaceHttpTests.Sources_count_a_file_a_library_and_the_scan_both_name_in_each_row_and_once_in_total`
and `LibrarySpaceHttpTests.Sources_count_names_without_a_measured_file_apart_and_never_the_size_a_library_reported`.
No client: `FileHashCandidatesHttpTests.Inspect_hashes_a_file_a_torrent_seeds_like_any_other_and_asks_no_download_client`.

## Consequences

**MAM-20 has what it groups.** A byte-identical pair is two hashes with one SHA-256, each under its
own identity (`FileInspectionHashHttpTests.Inspect_of_a_byte_identical_copy_keeps_two_hashes_with_the_same_sha256`),
and two hardlinks are one identity, so the grouping reads both without reading a file. After the
first run an inspection reads whole only a candidate with no stored hash — a file that is new or
changed, or one a new file of its size made a candidate — and a library whose files all differ in
size, as the reference installation's do, lists no candidate at all.

**Known limits, left on purpose.** A file rewritten in place at the same size whose modification
time doesn't move — two writes within one tick of its clock, or a writer that sets the time back —
keeps its key, so its old hash stands, as its reading does (ADR 0009). A pair whose compared facts
differ is never hashed, so two copies read by different ffprobe versions that disagree on a
duration, a codec name or a language would hide from each other; that is a copy added after an
upgrade of ffprobe, beside one read before it, and the facts compared are the ones least likely to
move between versions. Only the first problem's sentence is kept; the others are counted. A stop in
the middle of a file reads it again from its first byte. A file whose names all go — a sync drops
them, or a scan forgets a name it looked for and didn't find — loses its reading and its hash, so it
is read whole again if it comes back; and a file hashed without a reading of its own — the media
inspector couldn't be started, or the file changed while ffprobe read it — keeps its hash only through
its names, so a mount away for one run loses it, and the file is read whole again once it is back. Files on several disks are hashed one at a time all the same. On
Windows, where the app is developed, the file is held open without sharing its deletion for the
whole read — minutes for a film — so renaming or removing it meanwhile is refused until the read
ends; on Linux nothing is locked. On a platform without file identity nothing is hashed. The
reference installation has no hardlink and nothing to hash, so the live check (MAM-22) can show the
step listing no file and the space adding up, but not a pair found or a hardlink counted once:
those rest on the tests.

## Anti-consequence

Hashing only reads, inside the allowed roots; the roots are still no licence to write (Phase C). Two
files with one SHA-256 are the same bytes and nothing more: not which copy to keep, which work they
are, or that either may go. Grouping them and the Duplicates page are MAM-20's and MAM-21's;
nothing is removed, in bulk or one by one.

## Duplicates (added 0.2.1, MAM-20)

**MAM-20 groups what this keeps, as the Consequences said.** A name's hash counts only when it was
kept under the device, inode, size and modification time of the app's last look at that name, so a
hash of bytes changed since never makes a group; files of two identities or more with one SHA-256 are
a byte-identical group, each with all its names, and one file with two names is a same-file group,
never a byte-identical one, whatever its hash ([ADR 0028](0028-duplicates-are-worked-out-on-every-read-in-four-classes-by-one-file-one-hash-one-registry-key-or-one-title-and-year-and-a-group-only-shows.md)). No file is read to group them. The
Anti-consequence holds: two files with one SHA-256 are listed with the room removing all but one
would free where that is sure, never told which to keep, and nothing is removed; the page that shows
them is MAM-21's.

`LibraryDuplicatesByteIdenticalHttpTests.List_groups_two_files_with_one_sha256_and_estimates_the_room_a_copy_takes_on_ext4`,
`LibraryDuplicatesByteIdenticalHttpTests.List_makes_no_group_of_a_hash_kept_for_a_file_that_changed_since`
and `LibraryDuplicatesByteIdenticalHttpTests.List_makes_no_byte_identical_group_of_one_file_with_two_names`
pin it.
