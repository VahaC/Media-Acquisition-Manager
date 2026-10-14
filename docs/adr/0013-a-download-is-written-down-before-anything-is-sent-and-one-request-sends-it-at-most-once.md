# 13. A download is written down before anything is sent, and one request sends it at most once

Status: accepted (0.1.0, MAM-13)

## Context

Specification §3.2, steps 5 and 6: "Create a job, send the release to the client once, and save a
stable client-side job identifier after confirmation. Repeated clicks and retries must not create
duplicate downloads." and "If the client or source does not immediately return a reliable ID,
correlate cautiously using the info hash/NZB ID and the client's job list; require manual linking if
ambiguous." Phase A's acceptance bar repeats it: "retrying the same command does not create duplicate
jobs".

The obvious shapes each leave something behind on the reference installation:

- **Send, then write the job.** A crash, a restart or a lost connection between qBittorrent's answer
  and the save leaves a download nobody recorded, and the person, seeing no job, sends it again.
- **Disable the button and trust it.** A double click lands before React has redrawn the button; a
  retry of a request whose answer was lost (the phone left the Wi-Fi) is a new request to the server.
- **Retry a submission that timed out.** qBittorrent may have taken it: a write sent twice can act
  twice (house rule 8).
- **Trust the listing's info hash.** No Toloka listing states one (VendorSamples), and a stated one can
  be wrong; qBittorrent names the torrent by what it was given.
- **Trust `torrents/add`'s answer for the id.** Before 5.2 it answers `Ok.` and nothing else (qBittorrent's
  source); 5.2 lists ids, and adds a torrent a moment after it answers.

## Decision

**A send is written as a `Created` job, with the id the browser made for that submission and an audit
row, in one save, before the payload is fetched or the client is called; the same id from the same user
answers that job and sends nothing; and the job is confirmed by the client's own id when it gives one,
otherwise by the info hashes the app works out from the payload, found in the client's job list.**

**The request id.** The Download dialog makes one id when it opens (from `crypto.getRandomValues`: a
LAN address over plain http is no secure context, so `crypto.randomUUID()` isn't there) and sends it
with every retry of that submission; only a `Failed` job, which reached nothing, makes the next Send a
new request. The server looks the id up first, before anything else is read, so a retry is answered
with its job even after the release, the client or the destination went; the same id asking for
something else is 409. A unique index on `(RequestedByUserId, RequestId)` settles two copies racing:
the loser answers the winner's job.

**Sent once, confirmed by reading.** The job's sending runs to its end whether or not the browser is
still there. The payload is fetched, its kind checked against the client, its info hashes worked out
from the .torrent's info dictionary (SHA-1 for v1, SHA-256 for v2, BEP 52) or the magnet's topics and
stored on the job, the destination's category confirmed live (qBittorrent creates a category it lacks,
in its default folder — ADR 0010), the client's list read for the torrent, and only then one
`torrents/add`. An answer that leaves open whether the client took it — no answer in time, a lost
connection, a server error, an answer the app can't read — is followed by reading the client's list
(once more after half a second when the torrent isn't listed yet), never by sending again: listed, it
was taken; not listed after the second look, or with no hash to look for, the job fails with the
client's sentence.

**Matched by hash, never by name.** For an add the client answered, one job holding the hashes is the
submission's; none means the client hasn't listed it yet (looked for once more after half a second, then recorded as taken with its
hash for MAM-14 to find); more than one, or no hash at all, is `NeedsLinking` — the specification's
manual linking, which the app never guesses at.

**Why it failed is the owner's to read.** A failed job keeps the sentence of the step that stopped it,
and whose it is (`ErrorOrigin`). A source's or a client's own sentence can name its address or a
redirect target, so MAM-10's rule holds: only the owner reads it; anyone else who sent the release reads
the app's sentence naming that service. The app's own sentences — a category gone, a torrent the client
already has — name nothing of the owner's and are everyone's
(`DownloadsErrorVisibilityHttpTests`).

**Owner's decisions (2026-10-02).** (1) Every signed-in user may send, as everyone may search.
(2) qBittorrent gets the category and `autoTMM=true`, never a save path, so the category's folder is
where the torrent lands even when qBittorrent's default management mode is manual. (3) A release a job
that didn't fail already sent to the same client — recognised by its source and the source's id for it,
or the info hash its listing states — is refused with a sentence, not sent again: every search stores
its own copy of a listing (ADR 0006). (4) A torrent the client already has, added outside the app, fails
the job with a sentence: the app never adopts a download it didn't add.

| Rejected option | Why not |
| --- | --- |
| Write the job after the client answered | A lost answer leaves an unrecorded download, and the person sends it again |
| Key the request by release and client instead of an id | A deliberate second attempt after a failure couldn't be told from a double click |
| A request id made by the server when the dialog opens | One more round trip and a row for every dialog opened and closed unused |
| Send again after a timeout | qBittorrent may have taken the first; house rule 8 |
| Queue the sending (202 and a worker) | The dialog would poll for what one request answers in seconds; the job row already survives a restart |
| Correlate by name and category | Two releases share names; the client renames; the hash is the torrent's identity |
| Link a job to a torrent the client already had | A torrent the app didn't add becomes the app's, and Phase C could replace files by it |

`DownloadsIdempotencyHttpTests.Create_twice_with_one_request_id_answers_the_same_job_and_sends_once`,
`DownloadsIdempotencyHttpTests.Create_twice_at_once_with_one_request_id_makes_one_job_and_sends_once`,
`DownloadsIdempotencyHttpTests.Create_with_a_request_id_used_for_another_download_is_409_and_writes_nothing`
and `DownloadJobPersistenceTests.A_second_job_for_one_users_request_id_is_refused` pin the request id;
`DownloadsSendHttpTests.Create_sends_the_torrent_once_into_the_category_with_automatic_management_and_no_save_path`,
`DownloadsSendHttpTests.Create_against_qbittorrent_before_5_2_finds_the_job_by_its_info_hash`,
`DownloadsSendFailuresHttpTests.Create_whose_add_answered_500_but_listed_the_torrent_records_it_submitted` and
`DownloadsSendFailuresHttpTests.Create_with_two_jobs_listed_for_the_hash_needs_linking` the sending and its
confirmation; `DownloadsIdempotencyHttpTests.Create_of_a_release_already_sent_to_the_client_is_409_and_sends_nothing`,
`DownloadsSendFailuresHttpTests.Create_for_a_torrent_qbittorrent_already_has_fails_and_sends_nothing` and
`DownloadsSendFailuresHttpTests.Create_whose_category_is_gone_fails_and_sends_nothing` decisions 3, 4 and the
category check; `TorrentInfoHashReaderTests` and `ClientJobCorrelationTests` the hashes and the matching.

## Consequences

One click sends at most one download, a retry of it never a second, and every job says what reached the
client. The send answers 201 with the job whatever came of it — the job's status and sentence are the
outcome — so a client that couldn't be reached is a `Failed` job, not a 502. A send waits for the fetch and
up to five calls to qBittorrent, each of their requests bounded at 15 seconds; on the LAN it takes under a second.

Gaps left on purpose: a job left `Created` by a crash in the middle of its sending stays so until MAM-14
follows the client's jobs (its info hashes are stored as soon as they are known); so does a `Submitted`
job without the client's id. A hybrid torrent sent as a magnet naming only its v1 hash is renamed by
qBittorrent built on libtorrent 2, once its metadata arrives, to its v2 hash cut to 40 characters, which
the app doesn't know: `torrents/info?hashes=` then no longer finds it by its v1 hash, so a job confirmed
just after the add keeps the v1 id qBittorrent answered, and one whose add went unanswered can end
`Failed` though qBittorrent has it. Following such a job is MAM-14's (by category, matching
`infohash_v1`); Toloka's releases are .torrent files, whose v2 hash the app works out itself. `NeedsLinking` is recorded but there is no screen to link it yet. Two different
requests for the same release at the same instant can both pass the "already sent" check; the second then
finds the torrent in the client, or the client refuses it as one it already has, and fails — two jobs, but
still one download.

## Anti-consequence

The request id makes a retry safe; it is not a lock on the release. A deliberate new attempt after a
failure is a new request and may send. And confirming by reading licenses reading again, never sending
again.

## Implementation (added 0.1.0, MAM-14)

**The gaps left to MAM-14 are closed by the following of jobs**
([ADR 0014](0014-a-download-is-followed-by-reading-its-client-and-cancelling-it-only-stops-the-following.md)).
A `Created` job is taken for an interrupted send once it is 5 minutes old, far longer than any send,
so the following never races a sending still under way; the sending's own writes are conditional on
`Created` too, so whichever writes first wins. With no hash stored it fails with "The app stopped
while sending this download, before it could tell whether “qBittorrent” took it, so it isn't
followed. Check “qBittorrent” before sending the release again."; found once in the client it is
followed from then on; found twice it needs linking; not found it fails with "The app stopped while
sending this download, and “qBittorrent” doesn't list it, so it isn't followed. Send the release
again if you still want it."; and while the client can't be asked it stays `Created` with the reason.
A `Submitted` job without the client's id gets it once the client lists the torrent, and a reason
after a minute unlisted. The hybrid torrent renamed after its magnet is looked for among its
destination's jobs (`torrents/info?category=`) by the v1 hash qBittorrent lists for it as
`infohash_v1`, and the job then keeps qBittorrent's current id and the v2 hash. A job that already
ended `Failed` though qBittorrent had it stays so: an ended job is never read again.

**A cancelled job no longer counts as sent (MAM-13's decision 3).** Cancelling is how a person sends a
release again whose job is stuck or dismissed; while the torrent is still in the client, the check of
decision 4 still refuses a second copy. A paused job counts as sent.

**Still open:** a send interrupted between recording its hashes and asking the client whether it
already has the torrent is adopted when the client has it (ADR 0014's gap); `NeedsLinking` still has
no screen, and Cancel is the only way to dismiss one.

`DownloadJobRecoveryHttpTests.Poll_of_an_interrupted_send_qBittorrent_took_follows_it_from_then_on`,
`DownloadJobRecoveryHttpTests.Poll_of_an_interrupted_send_qBittorrent_does_not_list_fails_with_why`,
`DownloadJobRecoveryHttpTests.Poll_leaves_a_created_job_younger_than_five_minutes_to_its_send`,
`DownloadJobRecoveryHttpTests.Poll_finds_and_keeps_the_id_of_a_job_qBittorrent_took_without_one`,
`DownloadJobRecoveryHttpTests.Poll_finds_a_hybrid_torrent_qBittorrent_renamed_after_its_magnet_through_its_destination`
and `DownloadsCancelHttpTests.Cancel_lets_the_release_be_sent_again_once_qBittorrent_no_longer_has_it`
pin it.

## The overwrite check (added 0.2.1, MAM-38)

**On 2026-10-08 one step joined the sending, after the client's list is read for the torrent and
before the one `torrents/add`: the check for files the download would write over
([ADR 0034](0034-a-download-is-refused-before-it-is-sent-when-its-client-would-write-over-a-different-file-already-at-its-path.md)).**
The job is still written, with its request id and audit row, before anything is fetched or sent,
and its hashes are stored before the check, so a send interrupted there is found or failed by the
following as before. A send the check refuses ends `Failed` with the app's own sentence —
everyone's, as a torrent the client already has is — and reaches nothing, so the next Send in the
dialog is a new request, as after any failure. A send that goes ahead first records on the job how
it was checked (`OverwriteCheck`). One request still sends at most once, and the check licenses no
second call to the client's add.

## A replacement is written with its job (added 0.3.0, MAM-24)

**A schedule is a send with more to it, never a second copy of the send: `POST /api/replacements`
and `POST /api/downloads` run one private core, and the replacement is written in the job's one
save, before anything is fetched or sent.** The core runs this Decision's steps — the rules, the
request id looked up, the release, client and destination, the client's and the source's refusals,
"already sent" — and then, for a replacement, plans it from the database and the disk alone
(`IReplacementPlanner`): no client is asked before the job exists. What only the client can tell —
where the category saves, what a gateway's link turns out to be — is the strict send's, on a written
job ([ADR
0034](0034-a-download-is-refused-before-it-is-sent-when-its-client-would-write-over-a-different-file-already-at-its-path.md)'s
MAM-24 section). The save adds to the job and its `DownloadRequested` row the replacement job, its
file rows with each copy's identity read from the disk, its work rows with their ids and
confirmations, the `ReplacementScheduled` row and one `ReplacementWorkConfirmed` row per confirmed
work: a job and its replacement exist together or not at all. The sender also records on every job
what the release turned out to be (`PayloadKind`), conditional on `Created` as its other early
writes are.

**What a request was made for includes its replacement.** The replay compares the earlier job's
replacement — its file ids and its confirmed works, as sets — with what the retry asks: a Keep both
request is answered only with a job that carries none, a schedule only with one whose sets are the
same, and anything else is 409 "This request id was already used to send a different download.
Reload the page and try again.", so a request id one route used is something else to the other.
Every place a request id is answered goes through that one comparison: the first lookup, the racing
branch after "already sent", and the loser of the unique index's save. A refused plan also looks the
request id up again: a copy of the same request that wrote its job meanwhile makes its own copies
read as already scheduled, and the copy is answered with that job, not refused.

**Left open, as decision 4's race is.** "Already scheduled" — a copy a replacement not yet ended
holds — is checked by the plan before the save, under no lock or index: two different schedules, of
other request ids or releases, racing to tick one copy at the same instant can both pass. It is rare
and the owner's alone, and the second would find the copy gone when a later version comes to move
it.

`ReplacementScheduleHttpTests.Schedule_writes_the_job_and_the_replacement_with_fresh_identities_before_anything_is_fetched_and_answers_201`,
`ReplacementScheduleHttpTests.Schedule_of_a_title_and_year_match_keeps_the_owners_confirmation_with_its_audit_row`,
`ReplacementScheduleHttpTests.Schedule_again_with_its_request_id_and_the_same_ticks_answers_its_job_and_sends_once`,
`ReplacementScheduleHttpTests.Schedule_again_with_its_request_id_and_other_ticks_is_409_and_writes_nothing_more`,
`ReplacementScheduleHttpTests.Schedule_with_the_request_id_of_a_keep_both_send_is_409_and_writes_nothing_more`,
`ReplacementScheduleHttpTests.Send_with_the_request_id_of_a_schedule_is_409_and_writes_nothing_more`,
`ReplacementScheduleHttpTests.Schedule_twice_at_once_with_one_request_id_makes_one_job_and_one_replacement_and_sends_once`
and
`ReplacementScheduleHttpTests.Schedule_whose_copy_wrote_its_job_while_it_planned_answers_that_job_rather_than_already_scheduled`
pin it, and
`DownloadsSendHttpTests.Create_records_what_the_release_turned_out_to_be_and_never_reads_the_whole_client`
the payload kind of a Keep both send.

## A request Transmission never read is sent once more (added 0.4.0, MAM-30)

**A `torrent-add` Transmission answered with a 409 is sent once more, with the session id that 409
carried in its `X-Transmission-Session-Id` header: Transmission checks the session id before it reads
the request, so the first one added nothing, and "sent once" still holds.** Transmission renews its
session id every hour and at every restart, so a 409 may follow any call. Its source answers 401, 403,
421 and 409 before it reads the body, and MAM-29 recorded it: after a stale session's 409, Sintel's hash
was nowhere. A second 409, or one without a usable id, fails the job with "Transmission refused the
session id it had just sent." and is never a status the sender's "may have been taken" look accepts.
Every other unclear answer is read back, never resent, as the Decision says. `IDownloadClient`'s
`SubmitAsync` documents the exception.

**A duplicate is the client's no.** As for qBittorrent, the sender first asks the client for the
release's hashes, and a torrent Transmission already has — it keys each by its v1 `hashString` — is
refused before anything is sent: "“Transmission” already has this torrent, so nothing was sent." Only a
torrent Transmission takes between that look and the add reaches it as a duplicate. Transmission
answers that with `torrent-duplicate` and changes nothing of the torrent it has, whatever folder, label
or `paused` the add carried. The app never adopts a download it didn't add (owner's decision 4), so the
job fails with where the torrent is, read with one `torrent-get` of its `downloadDir` and `labels`:
"Transmission already has this torrent, in “/downloads/movies” with the label “Movies”, so it added
nothing." — or "Transmission already has this torrent, so it added nothing." when that read fails —,
whatever that read answers.

**The job keeps its client's kind of destination** (`DownloadJobs.DestinationKind`, by name, `null` for
a job sent before), written with the job, so its folder reads as a folder, and stays the owner's, even
once its client is gone.

`TransmissionAdapterSubmitTests.Submit_whose_session_rotated_right_before_the_add_sends_the_add_once_more_with_the_new_id`,
`Submit_whose_add_is_answered_409_twice_is_refused_and_the_add_is_sent_no_third_time`,
`Submit_whose_add_is_answered_409_without_a_usable_id_is_refused_and_not_sent_again`,
`Submit_of_a_torrent_transmission_already_has_is_refused_with_where_it_is_and_changes_nothing`,
`Submit_of_a_duplicate_whose_read_after_fails_is_still_refused_in_fewer_words`,
`Submit_of_a_duplicate_whose_read_after_never_answers_is_refused_and_the_add_is_not_sent_again` and
`Submit_whose_answer_never_comes_is_timed_out_and_not_sent_again`,
`DownloadClientContractTests.Submit_whose_session_the_client_forgot_right_before_the_add_adds_the_release_at_most_once`
for both clients,
`DownloadJobPersistenceTests.A_job_keeps_the_kind_of_its_destination_by_name_and_an_older_job_none`,
and end to end through `FakeTransmissionServer` `DownloadsSendTransmissionHttpTests` pin it.
