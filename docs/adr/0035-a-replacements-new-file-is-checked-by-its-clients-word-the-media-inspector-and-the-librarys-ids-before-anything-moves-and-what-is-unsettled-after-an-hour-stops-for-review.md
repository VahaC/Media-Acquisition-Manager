# 35. A replacement's new file is checked by its client's word, the media inspector and the library's ids before anything moves, and what is unsettled after an hour stops for review

Status: accepted (0.3.0, MAM-25)

## Context

Specification §3.4 lists six checks before an old copy is touched; MAM-25 builds the first four,
and MAM-26 the last two, right before the move. [ADR
0032](0032-a-replacement-targets-films-episodes-or-seasons-on-the-owners-word-and-goes-ahead-only-when-the-library-gives-the-new-file-the-old-copys-work.md)
settled what check 3 must find — the old copy's own work by the library's ids, each ticked copy
paired with exactly one new file — but left three things open: how long the app waits for Jellyfin
("a bounded time"), how it knows Jellyfin has finished reading a file, and how it finds the item
Jellyfin made of a path. What the obvious answers would have done, read in Jellyfin 12.2's source
and, on 2026-10-10, on the reference installation's Jellyfin 12.2.0 by a read-only check (GETs only,
with the app's stored settings, from the development PC):

- **The request to look proves nothing.** `POST /Library/Media/Updated` answers 204 at once, before
  Jellyfin looks at anything. Jellyfin waits its `LibraryMonitorDelay` (60 s there) after the last
  report, saves the new item without metadata, then refreshes it; that final save carries the
  provider ids, the running time and the episode numbers together, and for a new series it can take
  minutes. A check that reads right after the request finds no item, or one without ids.
- **`/Items` can't find every new file.** It has no path filter, and it leaves alternate versions
  out even when asked for their ids, so a new file Jellyfin makes a second version of a film the
  owner already has — the usual replacement — is never found there. A listing by
  `minDateLastSaved` misses an item Jellyfin refreshed before the request: the real-time monitor is
  on in all four media libraries there, and an item read already is not saved again.
- **An item's id is its path.** Jellyfin gives an item
  `new Guid(MD5(UTF-16 of its type's full name + its path))`, the rule ADR 0030 relies on to say a
  restored file is the same item. The check found it right for 880 of 880 movies and 440 of 440
  episode files (the 29 episodes it missed are virtual, without a path); `GET /Items/{id}?userId=`
  of an administrator answers a hidden alternate version whole, with its own path and numbers, and
  `GET /Items?ids=` of the same id answers nothing.
- **The download's rows are stale.** The following stops reading a download once it is done
  ([ADR 0014](0014-a-download-is-followed-by-reading-its-client-and-cancelling-it-only-stops-the-following.md)),
  so its stored files can be a pass older than the finish and miss an error reported later, and a
  pack's 100 % says nothing of which file is complete.
- **A gap was left here.** [ADR
  0034](0034-a-download-is-refused-before-it-is-sent-when-its-client-would-write-over-a-different-file-already-at-its-path.md)'s
  MAM-24 section left a torrent of another category writing at a new file's path "MAM-25's to catch
  when it reads the new files, before anything moves".
- **The following writes no audit row** for a mirrored step, "as nobody started them" (ADR 0014's
  MAM-24 section); the ticket asks for an audit row per transition of the check.

Vasyl's decisions of 2026-10-10: the library's wait is one hour from the request to look, a missing
id or an unread file waiting that long while a wrong or conflicting id stops at once; a paired new
file stops only when it runs under three quarters of its old copy's measured running time — a
longer cut passes, an old copy never measured is compared with nothing —; and MAM-25 ships in
0.3.0. Stated to him the same day without objection: the check runs in the background whatever the
switch says and moves nothing; Jellyfin hears only the request to look and qBittorrent is only read;
`Failed` is for a bad file and every other stop is `NeedsReview`; an unavailable Jellyfin or
qBittorrent is asked again every pass until its hour runs out; a file of several episodes stops only
when it covers a ticked copy's episode; a new video file Jellyfin still doesn't list at the hour
stops the job; another torrent writing a new file stops it; and each transition writes an audit row
with no actor. He decided the same day that a film the library identifies as another work is simply
added once every ticked copy found its new file — a collection pack's other film, which the job,
holding only the works a copy is ticked of, never matched —, and stops the job while one hasn't.

## Decision

**Once its download is done, a replacement checks every new video file before anything is touched —
complete by the download client's word, read fresh; readable by the media inspector inside the
allowed folders and written by no other download; listed and read by the library once it took the
request to look, found by the item id its path computes; identified by the library's own ids, every
ticked copy paired with exactly one new file of its work, and a collection's other film simply added
only once they all are —; what can't be settled yet waits at most an hour, a definitive problem
stops the job at once, only a bad file fails it, and nothing moves in this version (Vasyl's
decisions of 2026-10-10).**

**A loop of its own.** `ReplacementVerificationTracker` runs a pass when the app starts and 15 s
after each pass ends, apart from the downloads' 2 s following, which a check that reads files and
asks the library would hold up. The rows are the queue: a `Downloaded` replacement is claimed —
`Inspecting`, `InspectionStartedUtc`, a `ReplacementCheckStarted` row, one transaction —, and each
later pass takes it a step on from its rows, so a restart loses nothing and starts no wait again.
`Inspecting` and `Verified` are not followed states, so the following never mirrors them back.

**Checks 1 and 2, the files.** The client is asked again: the download seeding or complete at a
progress of 1, its content in its save folder, and each file it downloads with a video file's
extension complete by its own word — a skipped file or a subtitle is no new file, and a pack with
none stops. Each file is placed in the app through the client's path mappings and in the one library
folder holding it, which names its library and its path there; the media inspector then reads it
inside the allowed folders: a regular file, not empty, with a video stream and a running time, of
the size the client downloaded, and neither an old copy's device and inode nor its path. Last, the
client's whole listing (`ListJobsWithFilesAsync`, qBittorrent 5.2 or later) must show no other
download writing one of the files, by path or by device and inode — ADR 0034's gap, closed before
anything moves. A download there the app can't place — no metadata yet, a save path or a file no
mapping places, a file inside the allowed folders whose size or identity can't be read — leaves that
open, so the check waits for it as for a client out of reach, and stops once the hour ran out, as
the ticket's "anything unclear stops" says; a client too old to give the listing stops the job at
once.

**A request to look, then reads.** Once every file passed, each library holding new files is sent
`POST /Library/Media/Updated` with their paths in its own terms — the folder's path in the library,
never the app's, and at most `IMediaLibraryFileLookup.MaximumPaths` (500) to a request, the reads
batched alike, so a pack of any size is asked and read whole —; a library that doesn't take it is
asked again next pass. `LibraryAskedUtc` is the moment the request was taken, read after the files'
checks, which may take minutes for a pack. A call the adapter refuses as a caller's mistake is the
app's own fault, which every pass would repeat, so it stops the job at once ("The app refused its
own request to “Living room” about the new files as one it should never make, so they can't be
confirmed."). From then on a pass looks only at each file's identity, size and modification time — a
file that changed or went stops the job — and reads each new file's item by its computed id, as a
film, an episode, a plain video, then a trailer — Jellyfin 12.2 resolves a trailer as a type of its
own; both are extras, read as no work, which hold nothing up —, the answer's `Path` equal to the
path asked: its kind, ids, an episode's season, number and end number and its series' ids; and the
ticked episode copies' own items, read by their path in the library, for their kind, series, numbers
and end numbers. Each call to the library is preceded by the version check every sync makes (MAM-8).
**A file counts as read once its own item carries a running time, and an episode its number:**
Jellyfin's refresh saves them with the ids, so an item without them is one Jellyfin has made and not
read yet.

**Check 3 and the pairs, ADR 0032's way.** Its MAM-25 section says how, with the edition rule and
MAM-37's three quarters: ids are another work only where they differ in a scheme the work has, a
film of another work is simply added once every ticked copy is paired and stops the job while one
isn't, an episode of another series stops it whatever paired, a ticked episode must still be the
show's at the number it had when scheduled, by its own item as Jellyfin reads it now (ADR 0017's
MAM-25 section), and every title the work goes by is taken out of a name before its edition is read.
The reason the owner reads is the first problem: the ticked copies' by their place, then the new
files'.

**An hour, from a time the job keeps.** Two constants (`ReplacementVerificationRules`), not
settings, as ADR 0014 keeps its intervals: `FilesWait`, an hour from `InspectionStartedUtc`, for
what the files can't settle yet — the client unreachable or switched off, a download still moving
its files, a file changing while it is read, the media inspector away, another download the app
can't place, a library that won't take the request —; and `LibraryWait`, an hour from
`LibraryAskedUtc`, for what the library hasn't done yet — a file not listed or not read, an episode
copy's item not read, a new item, or an episode copy's series, without an id or with ids only of
schemes the work has none of, a library that can't be read. At the hour exactly a wait has run out,
and the first thing still missing is the reason. A definitive problem stops at once, but only once
every file is listed and read, so the owner reads what Jellyfin settled on, not what a half-read
item said.

**`Failed` is for a bad file only.** Not a regular file, unreadable as media, empty, without a video
stream or a running time: no wait or review makes it a copy. Every other stop — the client's error,
a file it hasn't finished, a path no mapping places, the old copy itself, another download, another
work, an unclear pair — is `NeedsReview`, which MAM-27's Retry may take up once the owner fixed the
cause; a check claimed again starts from the files and reads Jellyfin's ids afresh.

**The evidence stays with the job.** `ReplacementJobNewFiles` keeps each new file, in the client's
order: its name, size and progress by the client; its path in the app, its library and its path
there; its device, inode, size, modification time, running time, video codec and frame size as read;
its item, kind, whether read, series item, season, episode and end number, and when last read; the
work it was identified as, and why it stopped the job, if it did. `ReplacementJobNewFileIdentifiers`
keeps its item's and its series' registry ids. Each ticked copy keeps the new file it paired with,
its end number by the library, its measured running time and why it stopped the job. The rows are
replaced whole each time the files are checked; MAM-27 will show them.

**Audit rows with no actor.** `ReplacementCheckStarted`, then `ReplacementVerified`,
`ReplacementNeedsReview` or `ReplacementFailed`, each with the replacement job named by the
release's title, in the transaction of its move. This departs from ADR 0014's "the mirrored steps
write no audit row": those mirror the client, while these are the app's own decisions about the
owner's files.

**Whatever the switch says.** As [ADR
0033](0033-replacement-is-off-until-the-owner-switches-it-on-and-ticks-the-folders-it-may-touch-and-the-app-may-only-rename-there.md)
says, jobs already scheduled keep their checks: the check runs with replacement switched off or a
folder unticked, as it moves nothing, and MAM-26 reads the gates again before each write.

| Rejected option | Why not |
| --- | --- |
| Find the new item through `/Items`, by a listing or `minDateLastSaved` | No path filter, alternate versions left out, and an item read before the request is never saved again |
| Read the library right after the request, or after a fixed pause | The 204 comes before Jellyfin looks, and a new series' refresh takes minutes |
| Wait for the library without a limit | A job that never ends, both copies held, and the owner never told |
| The hour as a setting | ADR 0014 keeps tuned intervals as constants, and the hour is Vasyl's |
| "Read" as an item that exists, or one with provider ids | An item exists before its refresh, and one Jellyfin can't identify never gets ids; the running time comes with the refresh either way |
| Ask Jellyfin to refresh or identify the new item | A write into Jellyfin beyond the look (ADR 0030, ADR 0033); the owner's Identify is his |
| The stored download rows for check 1 | A pass older than the finish, and blind to an error reported later |
| A third step of the download tracker's pass | Media inspection and library reads would hold the 2 s following up, and a bug in one would stop the other |
| `Failed` for every stop | Most stops are put right in Jellyfin or qBittorrent; `Failed` says the new file can never be a copy |
| Compare running times both ways | A longer cut, Redux or Extended, is the usual upgrade |
| Pass over another download the app can't place | A magnet that gets its metadata later, or a save folder no mapping reaches, may write the new file; `ClientJobWithFiles` counts such a job as one it couldn't place |
| Every path in one request | The adapter takes at most 500, and a whole series' pack, or new files read with as many ticked copies, can name more |
| The library's hour from the check's start | The files' checks may take minutes before the request is taken, and a library that took it late would get less than its hour |

## Consequences

**What the owner sees.** The Jobs line says the new file is being checked until the check ends,
then that it is verified, or why it needs review; Jellyfin shows both copies meanwhile, and still
after `Verified`, until MAM-26 moves the old one. A job whose new item lacks an id can be put right
by identifying the item in Jellyfin within the hour: the next pass reads it.

**Harder.** A Jellyfin with case-insensitive item ids, or one that rewrites the paths it reports,
never lists a new file, so such a check ends at `NeedsReview` after the hour; so does the check of a
download that brings a sample file, a new video file Jellyfin ignores and never lists, as the
Context's rule for a file still not listed at the hour says. A torrent of the client the app can't
place — a magnet fetching its metadata for longer than the hour, one saving where no path mapping
reaches — stops every check that runs meanwhile. Nothing takes a job up again after `NeedsReview`
before MAM-27's Retry, and **Cancel job** refuses a done download, so a replacement can't be
cancelled once its download is done until MAM-27's cancel of a pending removal. The Jobs pages ask
every 2 s while a check runs, which may last two hours.

**Gaps left on purpose.** A torrent added after the check isn't seen by it: the check reads one
moment, as the send's does. The app's name readings only stop a pair: names Jellyfin 12.2 reads as
no range (`S02E05-S02E06`) give none, and only `a` and `b` are halves. No replacement download was
tried live: the read of 2026-10-10 found the facts this rests on, and MAM-28 checks the whole flow.

## Anti-consequence

This licenses one write, the request to look, and nothing else: no refresh, identify or delete in
Jellyfin, nothing to a download client, nothing to the media folders. `Verified` is no leave to move
a file: MAM-26 checks the old copy, its torrents and the gates again right before it moves anything.

`ReplacementVerificationHttpTests.Verify_of_a_film_whose_new_item_carries_the_old_works_imdb_id_verifies_it_and_keeps_the_evidence`,
`ReplacementVerificationHttpTests.Verify_while_replacement_is_switched_off_still_verifies_and_moves_nothing`,
`ReplacementVerificationHttpTests.Verify_of_a_collection_pack_whose_ticked_film_paired_simply_adds_the_film_jellyfin_identifies_as_another_work`,
`ReplacementVerificationFilesHttpTests.Verify_of_a_download_qbittorrent_reports_an_error_for_stops_for_review_without_reading_a_file`,
`ReplacementVerificationFilesHttpTests.Verify_of_a_new_file_the_media_inspector_cant_read_fails_it_without_asking_jellyfin`,
`ReplacementVerificationFilesHttpTests.Verify_of_a_new_file_that_is_no_usable_video_fails_it`,
`ReplacementVerificationFilesHttpTests.Verify_while_qbittorrent_is_unreachable_keeps_checking_until_the_hour_runs_out_then_stops_for_review`,
`ReplacementVerificationLibraryHttpTests.Verify_while_jellyfin_is_unavailable_keeps_checking_until_the_hour_runs_out_then_stops_for_review_never_verified`,
`ReplacementVerificationLibraryHttpTests.Verify_of_a_new_file_jellyfin_gives_no_id_keeps_checking_until_the_hour_runs_out_then_stops_for_review`,
`ReplacementVerificationLibraryHttpTests.Verify_of_a_new_file_the_owner_identifies_in_jellyfin_within_the_hour_verifies_it`,
`ReplacementVerificationLibraryHttpTests.Verify_of_a_new_file_jellyfin_hasnt_read_keeps_checking_until_the_hour_runs_out_then_stops_for_review`,
`ReplacementVerificationLibraryHttpTests.Verify_of_a_jellyfin_that_took_the_request_to_look_late_gives_it_its_hour_from_then`,
`ReplacementVerificationLibraryHttpTests.Verify_of_a_new_file_that_changed_or_went_after_it_was_checked_stops_for_review_without_reading_jellyfin`,
`ReplacementVerificationFilesHttpTests.Verify_of_a_new_file_another_download_reaches_by_a_link_at_another_path_stops_for_review_without_asking_jellyfin`,
`ReplacementVerificationFilesHttpTests.Verify_where_another_download_cant_be_placed_keeps_checking_until_the_hour_runs_out_then_stops_for_review`,
`ReplacementVerificationEpisodesHttpTests.Verify_of_a_pack_of_more_new_files_than_one_call_to_jellyfin_takes_asks_and_reads_it_in_batches_and_verifies_it`,
`ReplacementVerificationLibraryHttpTests.Verify_on_later_passes_goes_on_from_the_rows_never_asking_the_library_or_reading_the_file_again`,
`JellyfinAdapterFilesReadTests.ReadFiles_reads_a_hidden_alternate_version_as_jellyfin_answered_it`,
`JellyfinAdapterFilesReadTests.ReadFiles_finds_a_trailer_by_its_own_type_and_reads_it_as_another_kind_with_no_ids`,
`JellyfinAdapterFilesScanTests.RequestScan_posts_every_path_in_one_body_with_the_key_and_is_taken_on_204`,
`FakeJellyfinServerFilesTests.Of_gives_every_recorded_movie_and_episode_file_the_id_jellyfin_gave_it`,
`ReplacementLibraryPlacesTests`, `ReplacementVerificationRulesTests` and `NewFileReadabilityTests`
pin it.

## Verified is followed by the move (added 0.3.0, MAM-26)

**`Verified` is no longer where a replacement rests: while replacement is switched on, a loop of its
own takes it up, checks the old copy and the torrents again and moves it into the quarantine ([ADR
0030](0030-a-replaced-copy-is-renamed-into-a-quarantine-on-the-same-mount-and-stays-there-until-the-owner-restores-it-or-deletes-it-for-good.md));
this check still never moves anything, and a review it stops still waits for MAM-27's Retry.**
The Decision's "nothing moves in this version" held for 0.3.0 until MAM-26 joined it.

**Two loops.** `ReplacementQuarantineTracker` runs apart from `ReplacementVerificationTracker`, on
the same 15 s, so a bug in either can't stop the other, and the verifier's pass never touches a
`Verified` job again. The verifier's check that a paired new file is still the file it read is now
`ReplacementNewFileCheck`, shared: check 5 runs it again right before the move ("The new file paired
with “…” changed or went since it was verified, so nothing moves."), which is the Anti-consequence's
"MAM-26 checks the old copy, its torrents and the gates again right before it moves anything".

**Retry is split.** The Consequences' "Nothing takes a job up again after `NeedsReview` before
MAM-27's Retry" now holds for a stop of this check only. MAM-26's Retry takes a job the move stopped,
marked by `QuarantineCheckedUtc`, which this check never sets; asked for one this check stopped, it
answers 409 "This replacement isn't waiting for review after a move, so there's nothing to retry
here.", and the job's page offers nothing for it.

**What the owner sees.** "Jellyfin shows both copies meanwhile, and still after `Verified`, until
MAM-26 moves the old one" is so: the line reads "New file verified: 1 old copy moves into the
quarantine next.", and Jellyfin drops the old card about a minute after the move, told with the same
request, its paths sent as `Deleted` where this check sends the new files' as `Created`
(`LibraryFileChange`).

`ReplacementVerificationHttpTests.Verify_while_replacement_is_switched_off_still_verifies_and_moves_nothing`,
`ReplacementQuarantineChecksHttpTests.Quarantine_whose_new_file_changed_since_it_was_verified_stops_for_review`,
`ReplacementRetryHttpTests.Retry_of_a_replacement_the_check_of_its_new_files_stopped_is_409_and_writes_nothing`,
`ReplacementDetailsHttpTests.Get_of_a_replacement_the_check_of_its_new_files_stopped_offers_nothing`
and
`ReplacementQuarantineFlowHttpTests.Flow_moves_the_old_copy_tells_jellyfin_it_went_then_restore_puts_it_back_with_its_identity_and_tells_jellyfin_it_came_back`
pin it, and `JobsPage.test.tsx` the line.
