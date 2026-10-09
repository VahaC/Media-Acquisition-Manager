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
