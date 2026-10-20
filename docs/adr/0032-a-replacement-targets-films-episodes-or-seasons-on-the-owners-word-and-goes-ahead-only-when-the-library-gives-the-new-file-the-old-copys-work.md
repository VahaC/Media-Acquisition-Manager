# 32. A replacement targets films, episodes or seasons on the owner's word, and goes ahead only when the library gives the new file the old copy's work

Status: accepted (Phase C, MAM-23)

## Context

Specification §7, question 3, was answered for the app on 2026-10-04: movies and TV in full, an
episode a work of its own (ADR 0017), and since 0.2.0 TV releases are searched, matched to the
library's series and sent (ADR 0019, ADR 0020). Whether a *replacement* may target an episode or a
season was left to MAM-23.

What a replacement has to work with:

- **A release's claim is its title.** A TV release's seasons and episodes are read from its title
  (`ClaimedEpisodes`: whole seasons, or a range within one), a TV match counts the series' held
  episodes the claim covers, and nothing is stored. A `.torrent` lists its files, a magnet doesn't;
  either way the app learns them from the client only once the client has the job
  (`ListJobFilesAsync`), and a file name's `SxxEyy` is a hint, never the library's word (ADR 0017).
- **Episodes are places.** An episode work is placed in its series by season and episode number per
  library item; an episode's `a` and `b` files are one work with two versions (ADR 0028's MAM-37
  section); Jellyfin 12.2 makes two files of one `SxxEyy` in one folder versions of one episode. In
  the mixed libraries an episode file without season folders is a movie item, and so is every file
  of the movie library: 38 episodes on the reference installation, The Continental's three in the
  movie library and the rest in the documentary library, which stay movies (ADR 0017). The Jellyfin
  adapter doesn't read a multi-episode file's end number.
- **Toloka has no ids.** Toloka, the reference installation's release source, lists no TMDb or IMDb
  id, so its releases match the library by title and year, or by title alone, never by identifier
  (ADR 0012). Specification §3.1 says "a title or year match alone never authorizes replacement", §5
  "if the match is not reliable, scheduling removal is disabled", and MAM-24's ticket made "an
  identifier match without a conflict" a condition. Read together, no Toloka release could ever be
  scheduled to replace anything. §3.4, check 3, allows "reliable metadata or manual confirmation".
- **The library knows, once it has looked.** Check 4 waits until Jellyfin lists the new file.
  Jellyfin saves a new file's item first and gives it registry ids only in the metadata refresh that
  follows (Jellyfin 12.2, `Folder.cs`: `CreateItems` before `RefreshMetadataRecursive`), if its
  catalog finds the work; that can take minutes for a new series.
- **qBittorrent writes into a file that is already there.** A torrent added where its files' paths
  already hold files is checked against them, and every piece that doesn't match is downloaded and
  written into that file.

Vasyl's decisions of 2026-10-08: replacement covers films, single episodes and whole seasons, a
season pack replacing every episode the library holds, paired after Jellyfin has read the new files,
with every unclear pair — `a`/`b` halves, one file for two episodes, a missing episode — stopped for
review; a release that matched by title and year may be scheduled on the owner's explicit
confirmation of the work in the dialog, provided Jellyfin then gives the new file the same ids as
the old copy, else the job stops for review. Asked again the same day: a new episode a pack brings
that the owner holds no ticked copy of is simply added, and a release that claims a range of
episodes within one season is treated as those single episodes.

## Decision

**A replacement may target a film, one or more episodes, or the held episodes of whole seasons; the
owner may schedule it on a release that matched by identifier, or by title and year once the owner
confirms the work in the dialog — never by title alone or across conflicting ids —, and an old copy
moves only after the media server has given its new file the old copy's own work: the same TMDb or
IMDb id for a film, the same series, season and episode for an episode; anything less certain stops
the job at `NeedsReview`, and only the library's ids, never the owner's word after the download, let
it go on (Vasyl's decisions of 2026-10-08).**

**What the dialog offers.** For a film release, the files of the matched film work, every version
apart. For a TV release, the held episodes it claims — its one episode, those inside its range of
one season, or every held episode of each whole season it claims —, by season, each episode with its
versions. The owner ticks the copies to replace; nothing is ticked in advance. Each ticked file's
identity at that moment (device, inode, size, modification time) is stored with the job (MAM-24).

**Confirming a title-and-year match.** The dialog names the work — "This release is Alien (1979)"
or "… The Penguins of Madagascar, season 2" — and Replace stays disabled until the owner ticks it;
the tick is stored with the job and its audit row, as the consent §3.2 asks for. A title match
without a year and a match with conflicting ids offer **Keep both** only, with the reason. An
identifier match without a conflict needs no tick.

**A download never lands on a file that is there.** *(Found by MAM-23's review, not one of the
2026-10-08 answers.)* Before a replacement is sent, the app works out where each of the `.torrent`'s
files will be written — the category's save path read from the client, mapped through its path
pairs, joined with the torrent's own paths as the client's content layout lays them out — and a path
that already holds a file in the allowed folders, a ticked old copy or any other, refuses the
replacement before anything is sent: "qBittorrent would write over <path>." A magnet, whose files
are unknown until the client has the job, offers **Keep both** only, with the reason. Every send is
checked the same way since MAM-38
([ADR 0034](0034-a-download-is-refused-before-it-is-sent-when-its-client-would-write-over-a-different-file-already-at-its-path.md)),
which lets the same bytes through, as a Toloka update re-adds them; a replacement refuses any file
there, the same bytes included, as a file replaced by itself is no replacement.

**The check that decides comes after the download.** Check 3 reads the item Jellyfin made of each
new file once Jellyfin has finished refreshing it, waiting a bounded time for that and asking
Jellyfin for nothing but the look of ADR 0030. A film's new item must carry the old copy's work's
TMDb or IMDb id — the owner's own link (ADR 0026) counts as the old work's —, and no id of the same
scheme that differs; an episode's must be the same series by a registry id, ADR 0017's way, at the
same season and episode number. A new file still without an id when that time runs out, one with
another work's, or a conflict stops the job at `NeedsReview`. There the owner can correct the new
item in the library — for a film, MAM-34's TMDb link, which Jellyfin's Identify then applies — and
press **Retry**, which reads Jellyfin's ids again; or drop that copy from the job (MAM-27); or
cancel, and both copies stay. An episode at the wrong place can only be dropped or cancelled.
Neither the tick in the dialog nor any click at review stands in for this check: §3.4's "manual
confirmation" is the tick before the send, and the library's ids decide after it.

**Pairs by work, not by names.** Each ticked old copy is paired with the new file whose item is the
same work, after Jellyfin has read them all — a film in a collection's pack the same way as an
episode of a season pack. Exactly one new file of that work makes a pair. Two new files of one work
(two versions, an `a` and a `b`), a file that covers several episodes — by Jellyfin's end number,
which MAM-25 reads for every new file and ticked old copy, or by a range in its name where Jellyfin
gives none —, an old copy that is one half of `a`/`b`, a work that is a movie item on one side and
an episode on the other, and a ticked old copy with no new file all stop the job at `NeedsReview`;
the owner settles each pair as above or drops it from the job, and nothing moves before every ticked
copy is paired or dropped. A file's name may stop a replacement, never authorise one. A new episode
no ticked old copy pairs with is simply added to the library and holds nothing up. Each old copy
moves as one file with its companions ([ADR
0030](0030-a-replaced-copy-is-renamed-into-a-quarantine-on-the-same-mount-and-stays-there-until-the-owner-restores-it-or-deletes-it-for-good.md));
a season's folder never moves as a whole.

**Editions are shown, not guessed.** The edition words MAM-37 reads (theatrical, extended,
director's cut…) and the measured dynamic range are shown beside each old copy and the release's
claim in the dialog, as the owner's basis for ticking; after the download a new file whose name
claims another edition than the release did stops at `NeedsReview` (check 3's "intended edition").

| Rejected option | Why not |
| --- | --- |
| Identifier matches only, as §3.1 and §5 read | No Toloka release could ever replace anything |
| The owner's tick alone, without the ids after the download | A title and a year are the evidence §3.1 refuses; the downloaded file's ids are the reliable kind |
| The owner confirms the work by hand at review | A click is the evidence §3.1 refuses; the library's ids are what lets a copy move |
| A title match without a year, on the owner's tick | §3.1: a title alone never authorises replacement |
| Films only, TV later | Vasyl: the whole flow for TV too; episodes are a third of the works and season packs how seasons come |
| Pair new and old files by their names at scheduling | A name's `SxxEyy` is a hint, never the library's word (ADR 0017), and a magnet's files are unknown before the send |
| Stop for review on every new episode a pack brings | Vasyl: it is simply added; most packs bring some |
| Move a season's folder whole | Takes episodes no one ticked and the companions of others with it |

## Consequences

**The specification changes in two sentences.** §3.1's "a title or year match alone never authorizes
replacement" and §5's "if the match is not reliable, scheduling removal is disabled" now read with
this ADR: a title and year match may be scheduled on the owner's confirmation, and the replacement
still needs the library's ids before anything moves. Dated notes say so in both places.

**MAM-24's condition changes.** "An identifier match without a conflict" becomes an identifier match
without a conflict, or a title-and-year match the owner confirms, for a `.torrent` whose files land
on no existing file. MAM-25's check 3 waits for Jellyfin's refresh and reads the ids it gave the new
file, and reads an episode's end number; MAM-27 settles pairs and drops copies.

**More review on TV.** A season pack with an `a`/`b` episode, a double episode or a missing episode
waits for the owner. That is the price of never guessing a pair.

**Sends without replacement are checked too.** Found here and built as MAM-38 (ADR 0034): a **Keep
both** send whose torrent would land on a different file already there is refused before qBittorrent
is handed it, where it let qBittorrent write into the file from 0.1.0 to 0.2.1's MAM-37.

## Anti-consequence

A confirmed title match is consent to schedule, not evidence of the work: it never links a work,
never writes an id and never lets a copy move before the library has identified the new file. And a
possible match the owner confirmed on the Duplicates page is still no reason to replace anything
(ADR 0029).

## What the dialog offers and keeps, as built (added 0.3.0, MAM-24)

**What it lists.** For a film release every file of every version of each matched film, in the
Library's order; for a TV release each held episode the claim holds, at its first place in the
Library's order among the places the claim holds — an episode two items place in two seasons is
listed in the one claimed —, by season, number and title. Each version is tied to its own library
item's place: a version whose own item places the episode outside the claim, or under another
series, is `OutsideTheClaim`, and each file row keeps that version's own season and number. A TV
title naming no season is `NoClaimedSeason`, one claiming nothing held `NoClaimedEpisodes`.

**One file, one copy.** A file the mappings bring to one path is one copy however many works list it
— one folder under two libraries is two works —, with one id, and the strictest status of every work
listing it: a work that can't be replaced makes the copy unavailable under the others too
(`SharedWithUnavailableWork`, a ninth copy reason added while building, as none of the eight said
why), and a work needing confirmation makes its tick required whichever listing the owner ticked.

**A copy's reasons, in order:** `NoAppPath`, `FolderNotTicked` (ADR 0033), `OutsideTheClaim`,
`AlreadyScheduled` (a replacement not yet ended holds its path, or its device and inode),
`NotInspected` (not inspected, or without an identity), `SharedWithUnavailableWork`, then, for a
copy that passed those, a fresh look at the disk: `Missing`, `Unreadable`, `ChangedSinceInspection`
(device, inode, size or modification time unlike its inspection's). A reading that failed only drops
the dynamic-range chip.

**The confirmation, in the server's words.** `ConfirmationText` is built by the server — "This
release is Alien (1979)", "This release is Alien" without a year, a series' with its year and what
the release claims of it (", season 2", ", seasons 1–5", ", season 2, episodes 1–5", ", season 2,
episode 3") — and is the one source of the tick's label, of the 400's sentence and of the stored and
audited words. Two works that would read the same are told apart by their libraries (" in
“Фільми”"); two that still do — two works of one title and year in one library, one without an id,
the duplicates MAM-20 finds — by the file name of each one's first copy (" (the copy
“Halloween.2018.1080p.mkv”)") when those differ and fit the audit row's name, else by their place in
the dialog (" (1 of 2)"). As built, Replace can be picked before the tick: **Send** waits for every
confirmation a ticked copy's work needs, and the plan refuses a schedule without it (400).

**The strict refusal, the same bytes included.** A replacement is sent only when MAM-38's check
could look at every place and found no file there: a file at a place refuses it whatever its bytes,
told by being there, its pieces never read ("… a file is already there, and a replacement never
lands on a file already there, so nothing was sent."), and each `NotChecked…` outcome refuses it
too, where a Keep both send goes unchecked — a magnet, a list of files that can't be read, a folder
with no path or outside the Media folders, a place behind a link leading out, a name the app can't
look up, a client that couldn't list its torrents or their files. The job ends `Failed`, nothing is
handed to the client, the dialog keeps the form, and the owner picks Keep both himself (Vasyl's
decision of 2026-10-09): never a quiet downgrade. The scope is [ADR
0034](0034-a-download-is-refused-before-it-is-sent-when-its-client-would-write-over-a-different-file-already-at-its-path.md)'s
MAM-24 section's.

**Magnets.** A release stored as a magnet offers Keep both only (`Magnet`). A gateway's link, whose
kind the listing doesn't state, may turn out a magnet once fetched, after the job is written: the
strict send then fails it ("This release turned out to be a magnet link, whose files the app can't
check before sending, so nothing was sent and no copy will be replaced. Pick Keep both to download
it."), and every send now records the kind it turned out to be (`DownloadJobs.PayloadKind`).

**What is kept.** Each ticked file's identity read from the disk when the owner scheduled — device,
inode, size, modification time to the tick, link count —, read again by the plan, so a copy changed
since the dialog's read is refused by its name; the matched works with their title, year, evidence,
the confirmation's words and whether the owner ticked them, and their registry ids then
(`ReplacementJobWorkIdentifiers`; the owner's TMDb links count as the old work's, ADR 0026, marked
`SetByOwner`; a series' are its show's own), kept whatever a later sync does; and the edition words
of the release's title (`ReleaseEditions`), check 3's "intended edition". The rows are a snapshot
keyed to nothing a sync rebuilds.

`ReplacementOptionsHttpTests.Options_gives_each_copy_the_first_reason_that_keeps_it_from_being_ticked`,
`ReplacementOptionsHttpTests.Options_lists_one_file_two_libraries_report_as_one_copy_of_both_works_told_apart_by_their_libraries`,
`ReplacementOptionsHttpTests.Options_tells_two_works_of_one_title_and_year_in_one_library_apart_by_their_copies`,
`ReplacementOptionsHttpTests.Options_tells_two_works_their_copies_names_can_not_tell_apart_by_their_place_in_the_dialog`,
`ReplacementOptionsEpisodesHttpTests.Options_lists_an_episode_two_items_place_once_at_the_place_the_claim_holds_and_refuses_the_other_items_copy`,
`ReplacementScheduleHttpTests.Schedule_of_a_file_two_works_list_with_only_one_of_their_confirmations_is_400_naming_the_other`,
`ReplacementScheduleHttpTests.Schedule_keeps_the_works_ids_the_owners_link_included_after_a_library_sync_replaces_its_rows`,
`ReplacementScheduleHttpTests.Schedule_of_a_copy_that_changed_after_the_dialog_read_it_is_409_naming_the_file_and_writes_nothing`,
`ReplacementScheduleSendHttpTests.Schedule_whose_file_would_land_on_the_same_bytes_already_there_fails_and_hands_qbittorrent_nothing`
and
`ReplacementScheduleSendHttpTests.Schedule_whose_gateway_link_turns_out_a_magnet_fails_and_asks_qbittorrent_nothing`
pin it.

## Check 3 and the pairs, as built (added 0.3.0, MAM-25)

**The new file's work is read from the item Jellyfin made of it, compared with the matched works'
ids kept with the job, and each ticked copy is paired by its place, never by a name; the check waits
for Jellyfin as [ADR
0035](0035-a-replacements-new-file-is-checked-by-its-clients-word-the-media-inspector-and-the-librarys-ids-before-anything-moves-and-what-is-unsettled-after-an-hour-stops-for-review.md)
says (Vasyl's decisions of 2026-10-10).** The Decision's "bounded time" is an hour from the moment
the library took the request to look, and "finished refreshing" an item that carries a running time
— and, for an episode, its number.

**A film.** The new item must carry a TMDb or IMDb id of exactly one matched film — among the ids
kept at scheduling, the owner's TMDb link counted — and, in every scheme both carry, no other value;
ids are compared trimmed and in any letter case. Sharing one id and differing in another is a
conflict ("“Living room” gave “…Alien.1979.Directors.Cut.2160p.mkv” conflicting ids: IMDb tt0078748
is Alien (1979)'s, TMDb 679 is not."), and ids fitting two matched films an unclear work; both stop
at once. Ids differing from every matched film's in a scheme they share make the item another work
("“Living room” identified “…” as another work (IMDb tt0090605), not Alien (1979)."). The matched
films are only those a copy is ticked of, so a collection pack's other film is such another work;
Vasyl's decision of 2026-10-10 settles it by the pairs. Once every ticked copy found its new file,
another work's film is simply added, as a season pack's extra episode is — even when a pair then
stops the job for its own reason, its edition or its length —; while a ticked copy has found none,
the film may be that copy's new file misidentified, so it stops the job at once, its sentence the
reason. An item without a TMDb or IMDb id waits, then stops ("… gave “…” no TMDb or IMDb id within
an hour, so it can't be told to be Alien (1979)."). Ids only of schemes the film has none of are
compared with nothing — no work is shown, and none disproved —, so the item waits as one without an
id does, the owner free to identify it, then stops: "“Living room” gave “…” TMDb 348 within an hour,
but Alien (1979) is known here only by IMDb tt0078748." A film confirmed by its title and year with
no TMDb or IMDb id at all can't be compared with any id, so an item with ids stops at once: "“Living
room” gave “…” IMDb tt0078748 and TMDb 348, but Alien (1979) has no TMDb or IMDb id here to compare
them with."

**An episode.** Its series item's ids must be one matched show's by ADR 0017's rule — an IMDb id
among the show's ids, TMDb TV and TVDB within their kind; a film scheme on a series is no id —, with
the same conflict and other-work rules ("… gave the series of “…” conflicting ids: IMDb tt4236770 is
Yellowstone (2018)'s, TMDb TV 99999 is not.") and the same comparison with nothing — but an episode
of another series ("“Living room” placed “…” in another series (…), not …") stops the job even once
every ticked episode is paired, as a season pack is one show's where a collection holds several
films —; no series item, a series without ids, or an item without a season and episode number waits,
then stops. The item's own end number, else a range its name gives (Jellyfin 12.2's ten
multi-episode expressions, ported), widens the episodes it covers, never narrows them.

**The library says what each item is.** A film item in a job of episodes ("… made “…” a film, while
the old copies are episodes of Yellowstone (2018).") and an episode item in a job of films stop it;
so does a ticked episode Jellyfin now lists as a film ("… lists the old copy “…” as a film, while
its new file would be an episode, so the two can't be paired.") or as neither. The ticked episode's
item is read on every pass, as the owner may re-identify or renumber it while the download runs, and
it must still be the show's by a new episode's rules, at the season and number it had when
scheduled: another series ("… placed the old copy “…” in another series (…), not …"), conflicting
series ids, and other numbers ("“Living room” now numbers the old copy “…” season 2, episode 3, not
season 2, episode 1 as when it was scheduled.", or "now gives the old copy “…” no season and episode
number, not …") stop at once; a series without an id waits, then stops ("“Living room” gave the old
copy “…” no series with a TMDb, TVDB or IMDb id within an hour, so it can't be told to be an episode
of …"). A plain video or a trailer, an extra, is no work and holds nothing up. A film copy's old
item isn't read: its work is the matched film's.

**The pairs.** A ticked film pairs with the one new file of its work; a ticked episode with the one
new episode of its show whose season is its own and whose episodes cover its number. A ticked
episode the library numbers past itself (`IndexNumberEnd`) or whose name holds a range "covers
episodes 2–3 of season 2, so one new episode can't replace it."; one whose name glues `a` or `b` to
its first `SxxEyy` "is one half of an episode (its name gives the episode the letter “a”), so it
can't be replaced on its own."; one Jellyfin gives no number can't be paired. A new file covering a
ticked episode and the next "covers episodes 2–3 of season 2, so it can't replace “…” alone.", a
half of one can't replace it either, two new files of one ticked work are "Two new files are … the
app can't tell which one replaces the old copy.", and none is "No new file is Yellowstone (2018),
season 2, episode 2, so “…” has nothing to replace it." — held back while another new file is still
unread or has its own problem, a collection's other film identified as another work among them,
which is then the reason. A new file of a ticked work no ticked copy pairs with — the extra episode
of a pack, a file of two episodes beside no ticked one — is simply added, and so is a collection's
other film once every ticked copy is paired.

**The pair's edition and length.** The edition words of the new file's name (MAM-37's
`EditionLabels`, with every title the work goes by left out — its titles as the library sync keeps
them, else the one the Download dialog showed, should a sync have dropped them — and an episode's
own titles too) must all be among the release's: "“…” claims Extended cut, while the release claimed
Director's cut." (or "claimed none"). A paired file running under three quarters of its old copy's
measured running time — MAM-37's `RunningTimeSpread`, one way only — stops: "“…” runs 1:06:40, less
than three quarters of the old copy's 1:33:24."; exactly three quarters, a longer cut and an old
copy never measured pass. Such a copy keeps its pair beside its reason.

**What review allows is unchanged**, but for its buttons: Retry, dropping a copy and settling a pair
are MAM-27's. Until then the owner reads the reason on the Jobs page, and a check that waits can be
helped within its hour by identifying the new item in Jellyfin. Nothing checks a release's claim of
a season or a range against the files; the ticked copies alone decide what must pair.

`ReplacementVerificationHttpTests.Verify_of_a_film_whose_new_item_carries_only_the_tmdb_id_the_owner_linked_verifies_it`,
`ReplacementVerificationHttpTests.Verify_of_a_new_file_jellyfin_identifies_as_another_work_stops_for_review_at_once`,
`ReplacementVerificationHttpTests.Verify_of_a_collection_pack_whose_ticked_film_paired_simply_adds_the_film_jellyfin_identifies_as_another_work`,
`ReplacementVerificationHttpTests.Verify_of_a_collection_pack_whose_alien_file_jellyfin_identifies_as_another_film_stops_for_review_at_once`,
`ReplacementVerificationHttpTests.Verify_of_a_new_file_whose_ids_conflict_with_the_films_stops_for_review_at_once`,
`ReplacementVerificationHttpTests.Verify_of_a_new_file_whose_ids_share_no_scheme_with_the_films_keeps_checking_until_the_hour_runs_out_then_stops_for_review`,
`ReplacementVerificationHttpTests.Verify_of_a_download_holding_two_new_files_of_the_film_stops_for_review_as_an_unclear_pair`,
`ReplacementVerificationHttpTests.Verify_of_a_new_file_whose_name_claims_another_edition_than_the_release_stops_for_review`,
`ReplacementVerificationHttpTests.Verify_of_a_new_file_running_under_three_quarters_of_the_old_copy_stops_for_review`,
`ReplacementVerificationHttpTests.Verify_of_a_new_file_running_at_least_three_quarters_of_the_old_copy_verifies_it`,
`ReplacementVerificationHttpTests.Verify_reads_no_edition_from_any_title_the_work_goes_by_in_the_new_files_name`,
`ReplacementVerificationHttpTests.Verify_of_a_work_whose_titles_a_sync_dropped_reads_no_edition_from_the_title_the_dialog_showed`,
`ReplacementVerificationEpisodesHttpTests.Verify_of_a_season_pack_pairs_each_ticked_episode_and_simply_adds_the_extra_one_keeping_the_evidence`,
`ReplacementVerificationEpisodesHttpTests.Verify_of_a_new_file_covering_a_ticked_episode_and_the_next_stops_for_review`,
`ReplacementVerificationEpisodesHttpTests.Verify_of_a_ticked_copy_jellyfin_numbers_as_two_episodes_stops_for_review`,
`ReplacementVerificationEpisodesHttpTests.Verify_of_a_ticked_copy_that_is_one_half_of_an_episode_stops_for_review`,
`ReplacementVerificationEpisodesHttpTests.Verify_of_a_ticked_episode_the_pack_holds_no_new_file_of_stops_for_review`,
`ReplacementVerificationEpisodesHttpTests.Verify_of_two_new_files_of_one_ticked_episode_stops_for_review_as_an_unclear_pair`,
`ReplacementVerificationEpisodesHttpTests.Verify_of_a_new_file_jellyfin_makes_a_film_for_an_episode_copy_stops_for_review`,
`ReplacementVerificationEpisodesHttpTests.Verify_of_a_ticked_episode_jellyfin_now_numbers_otherwise_stops_for_review`,
`ReplacementPairingFilmsTests.Pair_of_a_new_item_with_ids_for_a_work_without_any_stops_for_review_at_once`,
`ReplacementPairingFilmsTests.Pair_of_a_collection_pack_where_one_ticked_film_of_two_found_no_new_file_stops_with_the_other_films_sentence`,
`ReplacementPairingFilmsTests.Pair_of_a_collection_pack_whose_pair_stops_for_its_edition_still_simply_adds_the_film_of_another_work`,
`ReplacementPairingEpisodesTests.Pair_of_a_season_pack_whose_extra_episode_is_of_another_series_stops_for_review_though_every_ticked_episode_paired`,
`ReplacementPairingEpisodesTests.Pair_of_an_old_copy_the_library_now_places_in_another_series_stops_for_review`,
`ReplacementPairingEpisodesTests.Pair_of_an_old_copy_whose_series_has_no_id_waits_keeping_its_pair_and_stops_once_the_hour_ran_out`,
`ReplacementPairingFilmsTests`, `ReplacementPairingEpisodesTests`,
`ReplacementPairingVerdictTests` and `EpisodeRangeNamesTests` pin it.

## Settling a pair, dropping a copy and what only a drop settles, as built (added 0.3.0, MAM-27)

**A stopped replacement is settled only through the library — the owner corrects the new item in
Jellyfin and presses Retry, which checks the new files again so that Jellyfin's ids decide —, by
dropping the old copy that can't be paired, which stays where it is, or by keeping both copies; no
click confirms a work, an edition or a pair by hand, and nothing moves before every ticked copy is
paired or dropped (Vasyl's decisions of 2026-10-08 and 2026-10-11).** The Decision's "drop that copy
from the job (MAM-27)" and "the owner settles each pair as above or drops it from the job", as
built.

**Drop.** `POST /api/replacements/{id}/copies/{copyId}/drop`, the owner's, only from a `NeedsReview`
the check or the move stopped — Vasyl's answer: only once the replacement stopped and waits for him
—, never the last copy still in the replacement (keep both instead) nor one a name of which has left
its folder. It sets the copy's `DroppedUtc`, forgets its names still `Planned` or `Stayed`, writes a
`ReplacementCopyDropped` row aimed at the copy (`AuditTargetKind.ReplacementJobFile`), and changes
nothing else: the job keeps its state and reason until the owner presses Retry — Vasyl's answer
again —, so several copies can be dropped for one check, and nothing moves between two drops. The
copy's row stays, keeping its last problem: the schedule's replay compares the ticks as they were
sent, and the row is the drop's history.

**A dropped copy is out of the replacement, not out of the library.** The check's library stage no
longer reads its episode item, pairs it or waits on it, so a new file it would have paired with is
simply added, as any new file no ticked copy pairs with, once Jellyfin has listed, read and
identified it — until then it still holds the job, as MAM-25's rule has it, since it may be a second
file of a copy left, and a fix in Jellyfin, then Retry, settles it; checks 5 and 6 and the move pass
it by, so a torrent that holds it stops nothing; and another download may tick it. It still counts
for check 2's "not one of the old copies", as it is still in the library. A drop can't be undone:
the owner keeps both and schedules again.

**What only a drop settles.** The Decision's "An episode at the wrong place can only be dropped or
cancelled" is widened, by Vasyl's answer, to every stop no fix in Jellyfin or qBittorrent can
change: the ticked episode Jellyfin numbers otherwise now, no new episode at its number, an old copy
or a new file that is half an episode or covers several, a new file whose name claims another
edition than the release, and one under three quarters of the old copy's running time. The pairing
marks such a copy (`ReplacementOldCopyOutcome.OnlyDropSettles`, kept as
`ReplacementJobFiles.OnlyDropSettles` beside its problem), the job's page says "Only dropping it or
keeping both settles this." on it — on the last copy left, "It is the replacement's last copy: keep
both instead." —, and while a copy still in the replacement carries the mark, Retry is withheld —
the Jobs row hides it, the job's page disables it with "Drop the copies a new check can't pair
first, or keep both." (or, when every copy left carries it, "A new check can't pair the copies left,
and the last copy can't be dropped: keep both instead."), and `POST …/retry` answers 409
"“Yellowstone S02E05.mkv” can only be dropped from this replacement, or both copies kept: checking
again wouldn't change it.". Two new files of one work, a film with no new file, an old copy Jellyfin
gives no number or lists as a film, and a series it hasn't identified stay open to a Retry, as the
owner can fix those in Jellyfin. The mark is the pairing's verdict only: a stop of the library stage
before the pairing clears what the passes that waited stored, so a mark of theirs never withholds
Retry.

**What the owner sees.** Each ticked copy with the new file it pairs with, its status, its problem
and **Drop**, and each new file with how far Jellyfin got and the ids its work is expected to carry,
on the job's page and under the Jobs row's **Copies** — "Nothing moves until every copy is paired or
dropped: 1 of 2 paired, 1 dropped." The Consequences' "MAM-27 settles pairs and drops copies" and
the MAM-25 section's "Retry, dropping a copy and settling a pair are MAM-27's" hold as built here.

`ReplacementDropCopyHttpTests`,
`ReplacementDroppedCopyHttpTests`,
`ReplacementReviewFlowHttpTests`,
`ReplacementRetryHttpTests`
and `ReplacementPairingDropOnlyTests` pin it, and `JobDetailsPage.replacement.test.tsx` and
`ReplacementCopies.test.tsx` the job's page.

## A copy a removal holds can't be ticked (added 0.3.0, MAM-40)

**A copy the owner removed on the Duplicates page, while its removal is open — on its way into the
quarantine, in it, waiting for review, or on its way back or out —, can't be ticked in the Download
dialog nor sent for replacement: it gets a tenth copy reason, `HeldByRemoval` (10, by name on the
wire), after `AlreadyScheduled` and read the same way, by path or by device and inode ([ADR
0037](0037-a-copy-is-removed-from-the-duplicates-page-into-the-quarantine-on-the-owners-word-through-replacements-gates-and-checks.md)).**
No download replaces it, and the owner settles it on the Duplicates page, so it is no
`AlreadyScheduled`, whose words name another download. The dialog says "A removal under Removed
copies on the Duplicates page holds this copy."; a send that ticks it anyway is 409 "A removal under
Removed copies on the Duplicates page holds “Alien (1979).mkv”, so it can't be replaced. Pick Keep
both to download the release." with nothing written or sent. A removal that ended — `Restored`,
`Deleted` or `NotMoved` — holds nothing. **A copy's reasons, in order** above now reads
`NoAppPath`, `FolderNotTicked`, `OutsideTheClaim`, `AlreadyScheduled`, `HeldByRemoval`,
`NotInspected`, `SharedWithUnavailableWork`, then the disk's three.

`ReplacementOptionsHttpTests.Options_marks_a_copy_an_open_removal_holds_and_not_one_an_ended_removal_held`
and
`ReplacementScheduleHttpTests.Schedule_of_a_copy_an_open_removal_holds_is_409_naming_the_removal_and_writes_nothing`
pin it.

## The copies open under the Jobs row's chevron (added 0.3.0, MAM-41)

**The MAM-27 section's "under the Jobs row's **Copies**" now reads "in the details the chevron
before the job's title opens under its row": the same ticked copies, each with the new file it pairs
with, its status, its problem and **Drop**, asked for only while open and again every 2 s while the
check runs, under the replacement's whole line; the **Copies** button left the row's actions, which
hold verbs only (Vasyl's decision of 2026-10-12: the copies are details, not an action; [ADR
0014](0014-a-download-is-followed-by-reading-its-client-and-cancelling-it-only-stops-the-following.md)'s
MAM-41 section).** The row's **Replacement** cell says where the replacement stands in a few words —
"Check stopped", "Move stopped", "Can't follow the download" for a review — and the details the
sentence with what to do. Nothing about pairing, dropping or what only a drop settles changes: the
server still works out each copy's status, a Drop still waits for Retry, and the row still leaves
**Retry** out while a copy only a drop settles is in the replacement.

## The confirmation kept as its claim (added 0.5.0, MAM-47)

`ConfirmationText` is no longer the stored words: it holds the claim — the work, its seasons and episodes, and the
tail that tells twins apart — as a message (ADR 0042), so the tick's label and the 400's sentence read in the
reader's language. The `ReplacementWorkConfirmed` audit row names the claim in no language
(`ReplacementConfirmationLabel`: `Alien (1979)`, `Doctor Who (2005) S02E01–E05 · Фільми`), and the startup converter
rewrites older rows the same way (Vasyl's decision of 2026-10-19). Which tail tells twins apart is still chosen by the
length of the English sentence against the audit name's 200 characters, so the choice doesn't change with the
language. A work with no title loses its year in the words ("a work with no title, season 2").
