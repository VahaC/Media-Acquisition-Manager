# 14. A download is followed by reading its client, and cancelling it only stops the following

Status: accepted (0.1.0, MAM-14)

## Context

Specification §3.4: "Persist transitions in SQLite so jobs survive restarts. Each step must be
idempotent. Polling the client is sufficient for the MVP; webhooks, where available, can be added
later." Its Jobs screen asks for the "reason for any block", and "Cancelling a job does not
automatically delete its torrent from the client." MAM-13 leaves a job `Submitted`, sometimes without
the client's id, and two gaps for this ticket (ADR 0013): a job left `Created` by a stop in the middle
of its sending, and a hybrid torrent sent as a magnet naming only its v1 hash, which qBittorrent built
on libtorrent 2 renames to its v2 hash cut to 40 characters, so `torrents/info?hashes=<v1>` no longer
finds it.

The obvious shapes each go wrong on the reference installation:

- **End the job on what the client reports.** qBittorrent's `error` and `missingFiles` clear once the
  owner fixes the disk or rechecks the torrent, and a torrent removed by hand can be added back with
  the same hash. A `Failed` job would stop following a download that then finishes.
- **Cancel by removing, stopping or pausing the torrent.** It may be seeding, its files may already be
  in a Jellyfin library, and Phase C hasn't settled which copies may be touched at all (§3.4, check 6).
  A click in the app would undo what the owner keeps.
- **Keep what is followed in memory, or reconcile once at start.** A restart loses it, or a job left
  `Created` by a send still under way is judged before the send has ended.
- **Call a job downloaded by its state or its progress alone.** A finished torrent being re-checked
  reports the check's progress under its finished state, and a magnet still fetching its metadata
  reports nothing left to download.

## Decision

**A background pass reads every followed job's state from its client every 10 seconds and writes
what changed, conditionally, to the job's row; what the client reports never ends a job but a finished
download; and cancelling a job only stops following it, sending nothing to the client.**

**Followed and ended.** `DownloadJobStatus` gains `Paused` and `Cancelled` (stored by name, so no
migration). Followed: `Submitted`, `Downloading`, `Paused`, and `Created` once it is 5 minutes old — a
send interrupted by a stop, as no send takes that long. Ended: `Downloaded`, `Failed`, `NeedsLinking`
and `Cancelled`. Cancellable: the followed ones and `NeedsLinking`, which has no linking screen yet, so
cancelling is the only way to dismiss it (`DownloadJobFollowing`). A `Created` job younger than 5
minutes is neither: its send may still be under way, and the sender writes its outcome only on a job
still `Created`, so a cancel then would throw that outcome away while the client keeps the torrent. The
cancel's conditional update checks the age itself, and such a job is 409 "This download is still being
sent, so it can't be cancelled yet. Try again in a few minutes."

**The client's word, mapped in the core.** Downloading, queued and paused take the client's state and
progress, a queued job staying `Submitted` until it has started; `Downloaded` takes a finished state and
a progress of 1 together. A finished state short of 1 and a state this build doesn't know move
nothing; a client error keeps the status but takes the client's progress; the client's listing a
`Created` job makes it `Submitted`; a progress the client doesn't give keeps the stored one.

**A reason, never an end (the owner's decisions 2 and 3).** A client error, and a torrent the client
no longer lists, keep the job's status with a block reason, and the job stays followed: the reason goes
once the error clears or the torrent comes back with the same hash. To send that release again, the
person cancels the job first. A job the client took without an id gets a reason only after a minute
unlisted, as a qBittorrent before 5.2 lists a torrent a moment after it took it. A client that can't
be asked — removed, switched off, of a type this build can't follow, with a secret the app can't read,
or whose call failed — gives every job of it that sentence, and never makes a job missing.

**Found by its ids, then by its destination.** One lookup per client and pass, with every job's stored
id and info hashes, 50 to a request. A job those don't find, sent with a v1 hash only, is looked for
among its destination's jobs (`ListDestinationJobsAsync`, qBittorrent `torrents/info?category=`) by
the hashes each lists. A category whose name holds a `+` can't be asked for: qBittorrent's request
parser percent-decodes a query value and only then reads `+` as a space, so `%2B` arrives as a space
too and `category=Movies%2BTV` would list "Movies TV". For such a key the adapter asks `torrents/info`
for every torrent and keeps those of exactly that category — a bigger answer, which on a client of a
couple of thousand torrents outgrows the 4 MiB cap and fails, deciding nothing. A match stores the
client's current id and the v2 hash it lists; two matches need linking by hand. An interrupted
`Created` job found is followed from then on; one not found, or with no hash to look for, fails with
the app's sentence. A failed listing, like a failed lookup, decides nothing: the job keeps its status
with the client's sentence.

**Files read when the job moved.** `ListJobFilesAsync` comes back (ADR 0005) and is asked for a job
with no files stored or whose status or progress changed, so an idle job isn't read every pass. They
are read before anything is written, then written with the job's row in one transaction — the row's
conditional write, or with nothing else to write, an `UPDATE` that changes nothing guarded the same
way — and only when that hit the job: a reader never sees a job `Downloaded` with the files of the pass
before, which nothing would read again. The rows are replaced by position; a failed read keeps them,
and the job's own write goes ahead. Once a files read or a destination listing times out, can't reach
the client, or its adapter throws, the pass asks that client nothing more: the rest of its files reads
are skipped, and a job that needs a listing shows that call's sentence, so one client that stops
answering mid-pass costs one timeout, not one per job.

**Cancel writes the app's row only.** A conditional update of the caller's own job while it is still
cancellable, with an audit row `DownloadCancelled`, in one transaction. `IDownloadClient` has no member
that removes, deletes, stops or pauses anything, so nothing could reach the client. Every write a pass
makes to a job — its row, and its files with it — is conditional on the status the pass read, so a
cancel racing a pass wins: one that lands first leaves the row and the files as they were, whatever the
pass had read meanwhile, and one that lands during the pass's transaction waits for it, then cancels.

**A job stays its sender's (the owner's decision 1).** The list, the files and Cancel answer the
caller's own jobs; another user's is a bare 404, for the installation owner too.

| Rejected option | Why not |
| --- | --- |
| The client's webhooks or run-on-completion script | qBittorrent has no webhook; a script is set up by hand on the host and misses what happened while the app was down |
| `Failed` on a client error or a missing torrent | Both can clear by themselves; the owner's decisions 2 and 3 |
| Deleting, stopping or pausing the torrent on cancel | Removes or holds what the owner keeps; specification §3.4 |
| A reconciler at boot for `Created` jobs | Races a send still under way; the age rule and the pass cover it every 10 s |
| A configurable poll interval, or one pass per job | Nothing about an installation makes another number better; one lookup per client is enough |
| Reading the files every pass | Every followed job each pass, for files that rarely change once known |

`DownloadJobTrackingHttpTests.Poll_maps_what_qBittorrent_reports_onto_the_job` and
`DownloadJobFollowingTests.Observe_maps_the_clients_state_and_progress_to_the_jobs` pin the mapping;
`DownloadJobRecoveryHttpTests.Poll_after_a_restart_follows_the_jobs_a_previous_run_left_and_asks_about_no_ended_one`,
`DownloadJobRecoveryHttpTests.Poll_of_an_interrupted_send_qBittorrent_took_follows_it_from_then_on` and
`DownloadJobRecoveryHttpTests.Poll_finds_a_hybrid_torrent_qBittorrent_renamed_after_its_magnet_through_its_destination`
the restart and ADR 0013's gaps;
`DownloadJobMissingHttpTests.Poll_of_a_torrent_removed_from_qBittorrent_keeps_the_job_with_why_and_follows_it_until_it_comes_back`,
`DownloadJobTrackingHttpTests.Poll_of_a_torrent_qBittorrent_reports_an_error_for_keeps_its_status_with_why_until_the_error_clears`
and `DownloadJobMissingHttpTests.Poll_of_a_client_that_can_not_be_asked_never_makes_a_job_missing` the
reasons; `DownloadsCancelHttpTests.Cancel_sends_nothing_to_qBittorrent_and_no_later_pass_asks_about_the_job`,
`DownloadsCancelHttpTests.Poll_racing_a_cancel_never_undoes_it`,
`DownloadsCancelHttpTests.Poll_racing_a_cancel_while_it_reads_the_files_writes_neither_the_job_nor_its_files`,
`DownloadsCancelHttpTests.Cancel_of_a_created_job_still_being_sent_is_409_with_why_and_changes_nothing` and
`DownloadClientNeverRemovesTests.No_download_client_member_is_named_for_removing_deleting_stopping_or_pausing`
the cancel; `DownloadJobTrackingHttpTests.Poll_reads_the_files_before_the_job_turns_downloaded_and_writes_them_with_it`
and `DownloadJobTrackingHttpTests.Poll_of_a_client_that_stops_answering_after_the_lookup_asks_it_nothing_more_that_pass`
the files; `QBittorrentAdapterDestinationJobsTests.ListDestinationJobs_of_a_key_with_a_plus_asks_for_every_torrent_and_keeps_exactly_that_category`
the `+`.

## Consequences

A restart resumes where the rows are: nothing is kept in memory, and every pass reads the followed jobs
from the database. A pass asks each client `app/version` and `torrents/info?hashes=…` on a session it
keeps, plus a files read per job that moved — little for a qBittorrent on the LAN, whose own Web UI
asks every 1.5 s. After qBittorrent refuses the credentials, the passes don't sign in again until the
owner changes them or a connection test with them works under Integrations (ADR 0007), so they never
feed its ban; until then every followed job of it shows the refusal
(`DownloadJobMissingHttpTests.Poll_after_qBittorrent_refused_the_credentials_never_signs_in_again_until_the_owner_tests_them`).

Gaps left on purpose: a send interrupted between recording its hashes and asking whether the client
already had the torrent (MAM-13's decision 4) is adopted if the client has it, as the pass can't tell
a torrent it took from one it already had. A `Created` or followed job whose client was removed stays
as it is, with the reason, until someone cancels it. `NeedsLinking` still has no screen. A client that
can't be asked logs it on every pass (an unreadable secret as a warning, an adapter that throws as an
error), unthrottled.

## Anti-consequence

Following reads; it licenses no write to the client. A later ticket that wants to remove, stop or pause
a torrent needs its own decision and its own contract member, and the convention test will say so.
`Cancelled` is the app's word only: it says nothing about the torrent, which may still be downloading.

## A pass every 2 seconds (added 0.1.0)

**The pass now runs 2 seconds after the previous one ends, not 10, and the Jobs pages ask again
every 2 seconds while they show a followed job.** The owner wanted a download's progress on the page
closer to what qBittorrent shows, and the page refreshing faster than the pass would
only bring the same rows back, so both numbers move together (`DownloadJobTracker.PollInterval`,
`JOBS_POLL_MS`). The cost stays small: a pass is still one `app/version` and one
`torrents/info?hashes=…` per client on a kept session, plus a files read per job that moved, and
qBittorrent's own Web UI asks every 1.5 s. Refused credentials are still never sent again, so a
faster pass doesn't feed qBittorrent's ban. The rest of the decision stands, the interval included
being a constant rather than a setting.

## The overwrite check (added 0.2.1, MAM-38)

**On 2026-10-08 `ListDestinationJobsAsync` and `ListJobFilesAsync` gained a second reader: the
check, right before a send, for files the download would write over
([ADR 0034](0034-a-download-is-refused-before-it-is-sent-when-its-client-would-write-over-a-different-file-already-at-its-path.md)).**
The sender lists the torrents the client already has in the destination's category, and the files of
those whose folder one of the new torrent's files would land in, for at most 20 torrents, so that a
place such a torrent will write refuses the send. Both stay reads, as here: the check writes nothing
to the client, whose one write is still the submission (ADR 0013), and a client that can't list its
torrents lets the release go unchecked rather than refused. The following, its passes and Cancel are
unchanged, and the anti-consequence stands: nothing here or there licenses removing, stopping or
pausing a torrent.

## A replacement follows its download (added 0.3.0, MAM-24)

**A scheduled replacement is followed by reading its download job, never the client: after every
pass, in the same scope and its own try, `IReplacementJobFollower` brings each replacement still
followed in line with its download, and it waits at `Downloaded`.** It runs from
`DownloadJobTracker.PollOnceAsync` after `IDownloadJobPoller.PollAsync` and outside it, so a pass
that wrote nothing, or ended early, still settles the replacements, and a bug in either never stops
the other. Each replacement is followed in its own try, the type and stack of a failure logged and
never its message; each write is a conditional update on the state it read, as `DownloadJobUpdates`'
are, so a cancel that got there first wins; and the mirrored steps write no audit row, as nobody
started them. Everything is rows, so a restart goes on where they stand;
`IX_ReplacementJobs_Followed` holds the followed states, `Scheduled`, `Submitted`, `Downloading`,
`Paused` and `NeedsReview`.

**Mirrored only after the strict check passed.** A replacement moves past `Scheduled` only for a
download whose `OverwriteCheck` is `Checked`, strict mode's only passing verdict, written before the
submission. Then `Submitted`, `Downloading`, `Paused` and `Downloaded` are mirrored, a download that
fails fails it ("The download failed, so nothing will be replaced."), and `NeedsLinking` puts it at
`NeedsReview` ("qBittorrent's downloads couldn't be told apart for this release, so the app can't
follow it to verify the new file."). Without that verdict a `Created` download leaves it
`Scheduled`, a `Failed` one fails it as refused before it was sent, and any other — a send the app
stopped during, which this Decision's following adopted with nothing checked — fails it: "The app
stopped while sending this download, before it checked where the new files would land, so nothing
will be replaced.". A download `Cancelled` cancels it from any followed state, and one waiting for
review ends `Failed` when its download fails.

**Cancel still only stops the following.** **Cancel job** cancels the replacement in the same
transaction as the job's cancel, from any followed state, `NeedsReview` included, with a
`ReplacementCancelled` audit row of its own ("You cancelled the download, so nothing will be
replaced."); the torrent and its files stay in the client. The Anti-consequence stands: nothing here writes to a client, and a
replacement's states are the app's words only.

`ReplacementFollowingHttpTests.Pass_follows_the_download_to_downloaded_and_leaves_the_replacement_waiting_there_with_nothing_sent`,
`ReplacementFollowingHttpTests.Pass_after_a_restart_goes_on_from_the_rows_a_previous_run_left`,
`ReplacementFollowingHttpTests.Pass_leaves_a_replacement_scheduled_while_its_download_is_still_being_sent`,
`ReplacementFollowingHttpTests.Cancel_of_the_download_cancels_its_replacement_with_a_row_of_its_own`,
`ReplacementFollowingHttpTests.Pass_that_finds_two_downloads_for_the_hash_needs_review_and_a_cancel_then_ends_it`,
`ReplacementFollowingHttpTests.Pass_over_a_download_that_failed_after_the_check_fails_its_replacement_as_a_failed_download`,
`ReplacementFollowingHttpTests.Pass_that_adopts_an_interrupted_send_fails_its_replacement_as_never_checked`,
`ReplacementFollowingHttpTests.Pass_whose_following_of_one_replacement_throws_follows_the_others_and_logs_the_type_never_the_message`
and
`ReplacementFollowingHttpTests.Pass_whose_following_of_the_replacements_throws_still_followed_the_downloads`
pin it.

## A done download's replacement is checked in a loop of its own (added 0.3.0, MAM-25)

**A replacement no longer waits at `Downloaded`: `ReplacementVerificationTracker`, a background loop
of its own, takes it up within 15 s and checks its new files ([ADR
0035](0035-a-replacements-new-file-is-checked-by-its-clients-word-the-media-inspector-and-the-librarys-ids-before-anything-moves-and-what-is-unsettled-after-an-hour-stops-for-review.md)),
never a third step of this pass.** A check reads every new file with the media inspector and asks
the library, which would hold the downloads' 2 s following up for as long, and a bug in either must
not stop the other. The following is unchanged: `Inspecting` and `Verified` are not among its
states, so it never mirrors them back to `Downloaded`, and `IX_ReplacementJobs_Followed` keeps the
five it had; the check reads `IX_ReplacementJobs_Verifying`, `Downloaded` and `Inspecting`. The
check reads the client afresh — `FindJobsAsync`, `ListJobFilesAsync`, and `ListJobsWithFilesAsync`
for the other torrents —, as the following's rows stop at the finish; all are reads, and the
Anti-consequence stands. Unlike the mirrored steps, each of the check's moves writes an audit row,
with no actor: they are the app's own decisions about the owner's files. **Cancel job** still
refuses a done download, so a replacement being checked or verified can't be cancelled through it;
cancelling a pending removal is MAM-27's. The Jobs pages ask again every 2 s while a job's
replacement is `Downloaded` or `Inspecting`, as for a followed download (`JOBS_POLL_MS`), though the
check passes every 15 s.

`ReplacementFollowingHttpTests.Pass_never_touches_a_replacement_whose_new_files_are_being_checked_or_verified`,
`ReplacementVerificationHttpTests.Tracker_runs_a_pass_as_soon_as_it_starts_and_stops_without_an_error`,
`ReplacementVerificationHttpTests.Verify_whose_check_of_a_job_throws_logs_the_type_never_the_message_and_the_next_pass_goes_on`,
`ReplacementVerificationLibraryHttpTests.Verify_on_later_passes_goes_on_from_the_rows_never_asking_the_library_or_reading_the_file_again`,
`ReplacementJobStatesTests` and `JobsPage.polling.test.tsx` pin it.
