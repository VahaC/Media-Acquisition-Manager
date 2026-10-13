# 10. A destination is a download client's own category, never a folder of the app's

Status: accepted (0.1.0, MAM-11)

## Context

Specification §3.2 gives the owner logical destinations (`Movies`, `Movies 4K`) mapped to a client's
categories, and says: "For qBittorrent, check that the selected category exists and has the expected
path. The UI must not override the category with a conflicting `savePath`. If the destination path
cannot be determined, disable automatic replacement." On the reference installation each of
qBittorrent's four categories saves straight into a Jellyfin library folder, with automatic torrent
management on (ADR 0003).

The obvious shapes each leave something behind. A destination that stores a folder goes stale the
day the owner changes the category in qBittorrent, and invites sending that folder as `savepath`,
which overrides the category. A destination saved without asking the client turns a typo into a
category the client would create on the first download. "The expected path" typed by the owner is a
second setting that can disagree with both the client and the libraries. And a default destination
replaced automatically when it is removed is one the owner never chose. Vasyl settled these on
2026-10-01; the code cites them as MAM-11's decisions 1 (the expected place), 2 (the default) and 3
(the save), labelled so below.

## Decision

**A destination stores a name and the download client's own category name, exactly as the client
spells it, and never a folder; it is saved only once that client confirms it has the category, and
where the category saves is asked of the client and checked against the media library folders each
time it is shown, never stored.**

**No folder anywhere.** `DestinationMappingEntity` has no save-path column, no request has a field
for one, and every request the destinations make to a client is the sign-in or a GET. A submission
(MAM-13) will send the category alone, so qBittorrent's own setting for it decides the folder.

**The key is the client's spelling.** Never trimmed and never matched in another letter case:
`Movies`, `movies` and ` Movies` are three categories. A category whose name the app couldn't store or
send back intact (blank, over 256 characters, a control character) is left out of what the adapter
lists.

**Saving asks the client (decision 3).** Adding a destination, and a change of its client or
category, list the client's categories live: no category spelled exactly so is 409 ("“qBittorrent”
has no category “Movies 4K”. Create it in the client first, or pick one it has."), a client that was
asked and couldn't answer is 502 with its sentence, and one that can't be asked — switched off, a type
this build can't send downloads to, a stored secret it can't read — is 409. A rename or a change of
the default alone doesn't depend on the client and doesn't ask it.

**The expected place is inside a media library folder (decision 1).** The category's folder, mapped
through the client's path mappings, must lie inside the app path of a Jellyfin library's root folder,
which the library sync stores; how that is worked out, and the statuses for every way it can't be, are
the MAM-11 section of
[ADR 0003](0003-service-paths-reach-the-app-through-an-explicit-prefix-mapping.md). Every status but
`InLibrary` will keep automatic replacement off for the destination in Phase C; a download can still
go there.

**One default for the whole installation, never chosen for the owner (decision 2).** The first
destination becomes the default without being asked, so there is one as soon as there is anywhere to
send; marking another moves it, the old one cleared first in the same transaction, as SQLite checks
the partial unique index `IX_DestinationMappings_OneDefault` row by row. Un-marking or removing the
default leaves none until the owner picks one.

**One qBittorrent session per saved integration, renewed once.** The adapter keeps the session it
signed in for, in memory, keyed by the integration's id and a SHA-256 fingerprint of its address, user
name and password, so an edited one never reuses it; an unsaved draft keeps none, and the connection
test signs in afresh because proving the credentials is its job. qBittorrent answers a session it has
forgotten (unused past its timeout, an hour by default, or lost in a restart) with 403, recorded in
`VendorSamples/QBittorrent/expired-session-response.txt`: a 403 to a kept session drops it, signs in
once more and sends that one read again, once. A 403 to a session the call has just opened fails and
drops it. Only reads are sent again; MAM-11 sends nothing else (the MAM-11 section of
[ADR 0007](0007-an-integration-credential-is-sealed-to-its-row-and-never-leaves-through-the-api.md)).

**A sign-in qBittorrent refused is not sent again, by that call or a later one.** Each refused
sign-in counts towards qBittorrent's ban of the app's address (five in a row by default, for as long
as its Web UI options say), and every page load, dialog and save lists destinations. So no call
repeats a failed sign-in, and the adapter remembers, beside the session and by the same id and
fingerprint, the last sign-in qBittorrent refused: the user name or password (401, or 200 `Fails.`)
or the address banned (403). While the address and credentials stay the same, a later call sends
nothing and answers that status with a sentence saying how to clear it — correct the credentials
under Integrations, or test the connection there once qBittorrent takes them. A connection test that
signs in with those very address and credentials forgets the refusal — it never asks whether they
were refused, as trying them again is what the owner asked for — but a test of other credentials
typed into the Edit dialog doesn't, since they may never be saved; a listing that signs in with
other ones does. A connection test qBittorrent refuses is remembered like a call's refusal, since it
counted towards the ban too, but never in place of a refusal of other credentials: one typed into the
Edit dialog and refused leaves the stored ones' refusal as it was
(`QBittorrentAdapterSessionsTests.ListDestinations_after_a_connection_test_qbittorrent_refused_sends_no_sign_in`,
`QBittorrentAdapterSessionsTests.ListDestinations_after_a_refused_connection_test_of_other_credentials_still_answers_the_kept_refusal`). Only qBittorrent's own no is remembered: unreachable, timed out, a server error or
an answer that isn't qBittorrent's never reached its count, and the next call tries again. An unsaved
draft remembers nothing, and a restart forgets it all.

| Rejected option | Why not |
| --- | --- |
| Store the category's folder with the destination | Stale once the category changes in qBittorrent, and one step from sending it |
| Send `savepath` beside the category | Overrides the owner's category, which specification §3.2 forbids |
| Let the owner type the expected folder | A second setting that can disagree with the client; the libraries already say where media belongs |
| Save without asking the client | A typo becomes a category the client creates on the first download |
| Ask the client for a rename too | Refuses a change that doesn't depend on the client while it is away |
| A default per client | A download would still have to choose among several defaults |
| Pick a new default when the default goes | Picks a destination the owner didn't choose |
| Sign in for every call | Every page load costs a sign-in, and each failed one counts towards the ban |
| Sign in again, in the same call or the next, after a refused sign-in | Every page load, dialog and save repeats the failure until qBittorrent bans the address, and then even corrected credentials are refused until the ban ends |
| Try a refused sign-in again after a cool-down | The app can't see how long qBittorrent's ban lasts, and wrong credentials stay wrong until someone changes them |
| Forget the refusal on any connection test of the integration that works | The Edit dialog tests credentials that may never be saved; cancelling it would leave the stored, refused ones to be sent again |

`DestinationsClientDestinationsHttpTests.Every_call_the_destinations_make_to_a_client_is_the_sign_in_or_a_get_and_none_names_a_save_path`
and `QBittorrentAdapterDestinationsTests.ListDestinations_sends_nothing_but_the_sign_in_and_gets_and_never_a_save_path_or_the_preferences`
pin the reads, `DestinationsCreateHttpTests.Create_of_a_category_the_client_has_not_got_is_409_and_writes_nothing`
(`Movies 4K`, `movies`, ` Movies`), `DestinationsCreateHttpTests.Create_when_the_client_could_not_answer_is_502_with_its_sentence_and_writes_nothing`
and `DestinationsUpdateHttpTests.Update_of_the_name_only_saves_without_asking_the_client` the save,
`DestinationsDefaultHttpTests.Create_of_the_first_destination_makes_it_the_default_whatever_it_asks`,
`DestinationsDefaultHttpTests.Delete_of_the_default_leaves_the_installation_without_one`,
`DestinationsDefaultHttpTests.Two_saves_that_each_take_the_default_at_once_leave_exactly_one` and
`DestinationsUpdateHttpTests.Update_taking_the_default_that_loses_a_race_leaves_the_old_default_where_it_was`
the default, `QBittorrentAdapterSessionsTests.ListDestinations_whose_kept_session_qbittorrent_forgot_signs_in_once_more_and_sends_that_read_again`,
`QBittorrentAdapterSessionsTests.ListDestinations_whose_renewed_session_is_refused_too_fails_after_two_sign_ins_in_all`
and `QBittorrentAdapterSessionsTests.ListDestinations_whose_fresh_session_is_refused_fails_without_signing_in_again`
the session, and `QBittorrentAdapterSessionsTests.ListDestinations_twice_with_refused_credentials_signs_in_once_and_answers_the_second_from_memory`,
`QBittorrentAdapterSessionsTests.ListDestinations_from_a_banned_address_remembers_the_ban_and_sends_nothing_more`,
`QBittorrentAdapterSessionsTests.ListDestinations_whose_renewing_sign_in_fails_does_not_sign_in_again`,
`QBittorrentAdapterSessionsTests.ListDestinations_after_a_refused_sign_in_signs_in_again_once_the_address_or_credentials_have_changed`,
`QBittorrentAdapterSessionsTests.ListDestinations_after_a_connection_test_signs_in_with_the_refused_credentials_signs_in_again`,
`QBittorrentAdapterSessionsTests.ListDestinations_after_a_connection_test_of_other_credentials_still_answers_the_refusal`
and `QBittorrentAdapterSessionsTests.ListDestinations_after_a_sign_in_qbittorrent_did_not_refuse_signs_in_again`
the refusal.

## Consequences

MAM-13 has what it needs to send: a client, the category to name and a default to offer first.
Every read of the destinations asks each client on the page live, so a slow client slows the page,
each request bounded at 15 seconds. A destination whose client is switched off or away can still be
renamed or made the default, and shows why it can't be checked. Until a library is synced after the
upgrade, every destination's check says the libraries aren't known yet. Removing a download client
removes its destinations (ADR 0006), the default among them, and then none is the default; the
Integrations page's confirmation of the removal says so.

A password qBittorrent refused stays refused in the app once it is set right in qBittorrent alone,
and a ban once it has ended: the destinations show the remembered sentence until the owner tests the
connection or edits the credentials, or the app restarts. Two calls at the very same moment can each send one sign-in before either remembers the
refusal, as they can each open a session, and a call refused at the moment a connection test
succeeds can remember the refusal again just after the test forgot it; after that, no call sends
those credentials until the refusal goes.

Gaps left on purpose: on qBittorrent 5.0 and 5.1 a subcategory without a save path of its own has an
unknown folder, because it follows `use_subcategories`, which only `app/preferences` reports, and
that answer carries the Web UI's API key and the proxy and mail passwords. On 4.4 a save path that
starts with `//` has an unknown folder too: 4.4 lists it as it was saved, and it may be a Windows
host's network folder or a POSIX one (the MAM-11 section of ADR 0003). Only qBittorrent 5.2 was verified
live; 4.4.1 to 5.1 are accepted from qBittorrent's source, and how a Windows host lists a network
folder from qBittorrent's and Qt's. On 2026-10-01 the reference installation's qBittorrent v5.2.4
passed the connection test through the app, listed its four categories, and the destination Movies
was checked to land in the Jellyfin library "Фільми" at `/media/movies`.

## Anti-consequence

`InLibrary` says where a category saves, not where a given torrent's files are: a torrent moved by
hand, or saved before the category changed, can be elsewhere. Phase C still checks the file itself,
by its identity (specification §3.4, ADR 0003). A kept session licenses sending a read again once,
never a write. And a remembered refusal never stands in the way of the connection test, which is how
the owner tries the credentials again.

## Implementation (added 0.1.0, MAM-13)

**A submission sends the category and `autoTMM=true`, never a folder (MAM-13's decision 2).** The
Decision's "a submission will send the category alone" became the category and automatic torrent
management: under manual management, qBittorrent's default for a new torrent, a category alone
decides no folder, and the torrent would land in the default save path. So `torrents/add` carries
the release, `category` — the destination's key exactly as stored, never trimmed — and
`autoTMM=true`, and never `savepath`, `downloadPath`, `useDownloadPath`, `rename`, `tags`, `paused`
or `stopped`: the category's folder is where the torrent lands whatever qBittorrent's default, and
the folder the destination's check shows is where it goes. The reference installation already runs
automatic management (VendorSamples' `QBittorrent/preferences.json`), so nothing changes there. Before anything is sent, the category is
confirmed live through the destinations' own listing, since qBittorrent creates a category it lacks
in its default folder: one that is gone fails the job and sends nothing ("“qBittorrent” has no
category “Movies” any more, so nothing was sent: it would have created one in its default folder.
Create the category again in “qBittorrent”, or the owner edits the destination."). The default
destination is what the Download dialog picks first, as the Consequences expected
([ADR 0013](0013-a-download-is-written-down-before-anything-is-sent-and-one-request-sends-it-at-most-once.md)).

**One write per submission, never sent again.** "Only reads are sent again; MAM-11 sends nothing
else" now reads: the one write is a submission's `torrents/add`, sent once. The Anti-consequence's
"A kept session licenses sending a read again once, never a write" holds: a submission reads
`app/version` first, through the same renewal, so a kept session qBittorrent has forgotten is renewed
by that read and the write goes out on a session qBittorrent has just accepted. A 403 to the write is
its answer and is never resent — without a session, the sign-in qBittorrent asks for; with one, the
session is dropped and the next call signs in. The version gate refuses a qBittorrent older than
4.4.1 before anything is posted. The destinations still send only the sign-in and GETs, and so does
the job lookup (`torrents/info?hashes=…`).

`DownloadsSendHttpTests.Create_sends_the_torrent_once_into_the_category_with_automatic_management_and_no_save_path`
pins the one add, its fields and no save path in any request,
`DownloadsSendFailuresHttpTests.Create_whose_category_is_gone_fails_and_sends_nothing` the live
check, and `QBittorrentAdapterSubmitTests` the renewal at the version read and the 403 never resent.

## The destination offered first (added 0.2.0, MAM-32)

The Consequences' "a default to offer first" now comes second. The owner can map sections of a
release source (Toloka's «Мультсеріали», «Телесеріали», …) to a destination in its dialog, and the
Download dialog opens on the destination the release's section is mapped to, the source's own
section winning over a standard category; with none mapped, on the default, as before, and with two
of its sections of that rank mapped to different destinations, on none
([ADR 0020](0020-a-release-is-tv-by-its-category-matches-only-series-and-its-tracker-section-picks-the-destination-offered-first.md)).
A destination is still a client's category and nothing else: a section only says which one is
offered first, and the person can pick another. `DownloadOptionsSuggestionHttpTests` pins it.

## A client without categories (added 0.4.0, MAM-29)

**This record is about a download client with categories. For one without them — Transmission, which
Phase D puts in place of qBittorrent —
[ADR 0039](0039-a-download-client-without-categories-gets-the-library-folders-it-can-reach-and-its-own-default-folder-as-destinations.md)
amends it: a destination is a Jellyfin library folder the client can reach, or the client's own
default folder, and a send names that folder and carries the destination's name as a label (Vasyl's
decisions of 2026-10-13).** This record's reasons against a folder assume a category: a stored folder
goes stale when the category changes, a `savepath` overrides the owner's category, and a typed folder
can disagree with the client's. Transmission has no category. A folder the owner types stays rejected
there too. qBittorrent keeps everything above: its category alone, sent with automatic management,
never a folder.
