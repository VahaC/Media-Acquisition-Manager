# 17. An episode is a work known by registry ids, and the library alone says what an item is

Status: accepted (0.2.0, MAM-17)

## Context

Specification §7, question 3, asked whether duplicate detection covers movies only, or TV shows,
seasons, anime and documentaries too. Phase A imports only what Jellyfin lists as movies (MAM-8 asks
`/Items` for `includeItemTypes=Movie`), and `MediaWorkKind` has `Movie` alone. MAM-17 read the
reference installation's Jellyfin 12.1.0 on 2026-10-04 (`VendorSamples/Jellyfin/series-page.json`,
`seasons-page.json` and `episodes-page.json`):

| Library | Collection type | Movies | Series | Seasons | Episodes |
| --- | --- | ---: | ---: | ---: | ---: |
| Фільми | movies | 649 | 0 | 0 | 0 |
| Мульфільми | mixed | 194 | 2 | 8 | 315 |
| Документалки | mixed | 35 | 1 | 2 | 16 |
| Серіали | tvshows | 0 | 7 | 15 | 140 |

What Jellyfin reports for TV makes the obvious shapes go wrong:

- **A Jellyfin series is not a series.** The Grand Tour is four series items, one per season folder,
  all with the same IMDb, TMDb and TVDB ids, and its three season-5 files lie directly in the TV
  library, in no series item at all. Keyed by Jellyfin's `SeriesId`, one show is five things.
- **One item can hold many episodes.** Jellyfin made the 65 files of the Tom & Jerry Kids folder, 65
  different episodes, one episode item (season 1, episode 4) with 65 media sources, no series and
  no ids.
- **One episode can be two files, and ids can be wrong.** Eight Mickey Mouse Clubhouse episodes are
  split into halves (`S03E09a`, `S03E09b`); Jellyfin gives both halves the episode's numbers and its
  IMDb and TVDB ids. "The Golden Boo Boo" (S03E31) carries the ids of "Donald Hatches an Egg"
  (S03E33).
- **An episode is not always a Jellyfin episode.** 38 movie items are episodes by their file names.
  The Continental's S01E01–03 sit in the movie library, in the `John Wick. Collection` folder, as
  three movies that share one IMDb id, `tt29669983`, and nothing else. In the documentary library,
  Greatest Events of World War II in Colour's S01E01–10 and the 25 episodes of Planet Earth,
  Planet Earth II and Planet Earth III (11, 6 and 8) are movies without ids.
- **An episode can be virtual.** 31 episodes, 22 of them specials, are `LocationType` `Virtual`:
  each lists one media source with protocol `File` and no path (the sampled one is of type
  `Placeholder`).
- **Registry ids depend on the kind.** Episodes carry IMDb (287 of 471) and TVDB (301) ids of their
  own and no TMDb id; 9 of the 10 series items carry IMDb, TMDb and TVDB ids. A TMDb id of a show
  is a number in another space than a TMDb id of a movie: the same number can name both.

## Decision

**Duplicate detection covers movies and episodes in every library (Vasyl's decision). An episode
is a work of its own, known by its own IMDb and TVDB ids and by its series' ids together with its
season and episode numbers, never by a library's series item; and whether an item is a movie or an
episode is the library's word alone: no file or folder name changes it, and the library stays as it
is (Vasyl's decision).**

**Movies and episodes, everywhere.** Of movies only, every file scanned but only movies grouped as
works, and both in full, Vasyl chose the last. Series, seasons and episodes are imported from
Jellyfin (MAM-32), the scan covers every allowed folder whatever it holds (MAM-18), and the
duplicate classes of specification §3.3 apply to episodes as to movies.

**An episode's identity is in the registries, not in the library.** Like a movie (ADR 0006, ADR
0008), an episode is a work with versions and files, and the library's item ids are provenance.
Its reliable identifiers (§3.1) are its own IMDb and TVDB ids, and its series' IMDb, TMDb or TVDB id
with its season and episode numbers. So The Grand Tour's four series items are one show, and its
three season-5 files, which no series item holds, are works without ids: only possible matches. A
series' TMDb id is a TV id and never matches a movie's TMDb id.

**The library says what an item is.** A movie item is a movie and an episode item an episode,
whatever its file is called. The Continental's three files stay three movie items with one IMDb id,
so ADR 0008's grouping keeps them one work with three versions, as it does today; the 35
documentary episodes stay 35 movies without ids, each a work of its own. Asked whether to re-file
them or to have the app read `S01E01` from their names, Vasyl answered that the library lived this
way before the app and will go on doing so; turning a name into a library item's kind would also
be the silent guess specification §4 forbids.

**What the library groups stays grouped, and proves nothing.** One item with 65 media sources is
one work with 65 versions, as Jellyfin says; the two halves of an episode are two versions of it.
No duplicate class rests on that: "another copy of the same work" needs a registry id the files
share (MAM-20), so the 65 id-less Tom & Jerry Kids files are no such copies, and no version is ever
called redundant (§3.3).

**A virtual episode is not a file.** One whose media source has no path gives no version and no
file.

| Rejected option | Why not |
| --- | --- |
| Movies only | Vasyl chose both: 440 episodes with 504 files, in three of the four media libraries |
| Scan every file, but group only movies as works | Two rips of one season would never show as versions of it |
| Key an episode by Jellyfin's `SeriesId` | The Grand Tour is four series items, and three of its files are in none |
| Read `SxxEyy` from a file name to make an item an episode | A guess from a name (§4), and the library is Vasyl's to keep as it is |
| Ask Vasyl to re-file the episodes Jellyfin lists as movies | His answer: the library stays as it is; §1 already makes the app fit the folders |
| One TMDb scheme for movies and shows | The same number names a movie and a show |

No code implements this yet. MAM-32 imports series, seasons and episodes against the samples above,
and its adapter tests pin the series items, the id-less series, the 65-source item, the halves and
the virtual episode; MAM-20's tests pin that a shared registry id, not a shared item, makes "another
copy of the same work".

## Consequences

**The Continental stays one work with three versions** on the Library page, as in 0.1.0, and Phase
B lists its three files as other versions of one work, never as redundant. Nothing is lost by it:
Phase B deletes nothing, and Phase C replaces only a file the person picks (§3.2).

**The halves show as versions.** The halves of eight Mickey Mouse Clubhouse episodes appear as two
versions of one episode; their paths, shown beside them, tell them apart. Two episodes that carry
one episode's ids under different numbers (S03E31 and S03E33) disagree, and how a disagreement is
shown for review is MAM-20's to decide.

**Phase B gains a ticket** (MAM-32), before the scan (MAM-18), which matches a scanned file to an
episode's file row as to a movie's.

## Anti-consequence

This decides only that no name changes what a library item is or which work it belongs to. How a
file no library reported becomes a possible match — a candidate always confirmed by hand (§3.3) —
is MAM-20's to decide, and the reading of a release's title stays as it is (ADR 0012). Nothing here
decides whether a replacement can target an episode or a season: that is MAM-23's question (§7,
question 3, for Phase C).

## Implementation (added 0.2.0, MAM-32)

**The import is built as decided.** The Decision's "No code implements this yet" no longer holds:
a sync reads Jellyfin's series and episodes beside its movies, and stores a series as a work its
episodes point at, with the show's TMDb id in a scheme of its own (`TmdbTv`)
([ADR 0019](0019-a-series-is-a-work-its-episodes-point-at-its-seasons-come-from-their-numbers-and-an-episode-without-a-file-is-left-out.md)).
The Grand Tour's four series items are one series; the halves of an episode are one episode with two
versions; two episodes that carry one episode's ids under different numbers stay two works; the Tom
& Jerry Kids item is one episode of no series with 65 versions, listed as a card of its own ("Season
1 · Episode 4"), whose versions show as rows only, as the Library page offers the table for 2 to 6
versions; a virtual episode is stored as nothing; and a movie item stays a movie whatever its file
is called. The Anti-consequence's "the reading of a release's title stays as it is" now has one
addition: a TV release's title is read for the seasons it claims, to count the episodes the library
holds of them
([ADR 0020](0020-a-release-is-tv-by-its-category-matches-only-series-and-its-tracker-section-picks-the-destination-offered-first.md));
it still never makes a library item an episode.

`JellyfinAdapterTvTests.ListItems_reads_a_series_imdb_tmdb_and_tvdb_ids_its_tmdb_id_as_a_shows`,
`JellyfinAdapterTvTests.ListItems_reads_the_tom_and_jerry_kids_item_as_one_episode_of_no_series_with_65_versions`,
`JellyfinAdapterTvTests.ListItems_reads_the_two_halves_of_an_episode_with_the_same_numbers_and_ids`,
`JellyfinAdapterTvTests.ListItems_reads_a_virtual_special_as_missing_with_no_file`,
`LibrarySnapshotTvHttpTests.Sync_makes_one_series_of_a_show_the_library_knows_as_two_series_items`
and `LibrarySnapshotTvHttpTests.Sync_keeps_a_movie_and_a_series_sharing_a_tmdb_number_apart` pin
it.

## Implementation (added 0.2.0, MAM-18)

**The scan matches an episode's file as it matches a movie's: by its path.** The Consequences' ticket
order holds: MAM-32 stored the episodes' files first, and MAM-18's scan of every allowed folder,
whatever it holds, counts a file as reported when one of the names it was reached by is the app path
of any file a library reported — a movie's or an episode's alike, through its integration's current
path mappings
([ADR 0021](0021-the-scan-of-the-media-folders-follows-links-only-inside-the-allowed-roots-and-keeps-one-name-of-each-file-no-library-reported.md)).
No file or folder name decides anything: an `S01E01` in a name makes no file an episode, a title in a
path joins it to no work, and the same file name in another folder is another file. A file no library
reported is stored as a name with no version, so it belongs to no work, neither a movie nor an
episode; how it becomes a possible match stays MAM-20's, as the Anti-consequence says.

`FolderScanRunHttpTests.Scan_counts_a_movies_and_an_episodes_file_as_reported_and_stores_no_name_for_them`,
`FolderScanRunHttpTests.Scan_matches_by_the_librarys_current_mappings_and_by_path_alone` and
`FolderScanIsolationHttpTests.Scan_of_a_name_whose_path_holds_a_works_title_changes_nothing_the_library_shows_or_finds`
pin it.

## A link to TMDb (added 0.2.1, MAM-34)

**The library still says what an item is.** The owner may link a library work to its TMDb film or
show ([ADR 0026](0026-a-tmdb-id-set-by-hand-is-the-owners-word-kept-per-library-item-and-written-into-the-library-as-ids-only.md)),
and a movie item linked to a show stays a movie: its link is an episode-to-show link, the show's TMDb
TV id with a season and an episode when the owner gives them — the key the Decision names for an
episode, its series' id with its numbers. Such a link is kept in the app and never written into
Jellyfin, where a movie item's `Tmdb` names a film. An episode of no series (the Tom & Jerry Kids
item) takes such a link only; an episode of a series is linked through its series ("Link its series
instead: an episode is known by its series' ids."); a series item takes its show's id, kept in the
app: Jellyfin keeps a show's id under the same `Tmdb` key on a series, but every save of a series
rewrites all its seasons and episodes, so the app never writes one (Vasyl, 2026-10-06). The 25 Planet Earth films stay 25 movie items,
each linkable to its show and episode, and two linked to different episodes stay two works.

**A name is a hint, never a decision.** A file name's `_E08` or `S01E08` and a file's folder name
are queries for the suggestions and prefill the numbers of a show suggested to a film item
(`EpisodeMarker`, `FolderHint`); they decide no link, no kind and no match. The rejected option
"Read `SxxEyy` from a file name to make an item an episode" holds.

**A linked id is a registry id.** "Another copy of the same work needs a registry id the files share
(MAM-20)": a linked id is one, inside the library whose items it links.

`LibraryLinkSetHttpTests.Set_of_a_show_on_a_film_item_is_an_episode_link_kept_in_the_app_that_replaces_the_wrong_film_id_and_calls_nothing`,
`LibraryLinkSetHttpTests.Set_of_a_show_on_an_episode_no_series_holds_is_an_episode_link_kept_in_the_app`,
`LibraryLinkSetHttpTests.Set_on_an_episode_of_a_series_is_400_saying_to_link_its_series`,
`LibraryLinkSetHttpTests.Set_of_a_show_on_a_series_keeps_the_link_in_the_app_says_why_and_calls_nothing`,
`LibraryLinkSyncHttpTests.Resync_keeps_items_linked_to_other_episodes_of_one_show_apart_and_drops_the_film_id_jellyfin_gave_them`,
`LibraryLinkSuggestionsHttpTests.Dialog_of_a_film_never_has_a_show_confident_and_prefills_its_episode`
and `EpisodeMarkerTests.Read_of_a_text_with_no_marker_of_its_own_is_nothing` pin it.

**Amended 2026-10-06 (ADR 0026's section added in 0.2.1).** A series item's link is written into
Jellyfin like a film's — the show's TMDb id under the series' `Tmdb` key, which Jellyfin keeps as a
show's —, Vasyl having accepted that a series' save gives its seasons and episodes the series' age
ratings; and a movie item's link to an episode of a show, naming its season and episode, writes the
episode's own IMDb id there, never a TMDb id. The library still says what every item is. The series
test named above is now
`LibraryLinkSetHttpTests.Set_of_a_show_on_a_series_writes_the_shows_ids_into_jellyfin_confirmed_asks_it_to_look_the_series_up_and_the_next_sync_finds_them`.

**Amended 2026-10-06 (ADR 0026's Identify section, added in 0.2.1).** This replaces the wording of the
amendment above and of "kept in the app and never written into Jellyfin", "the app never writes one" and
the ratings it accepted. A series item's link is identified: Jellyfin replaces the series' title,
overview and images with TMDb's and refreshes its seasons and episodes (age ratings do not matter to
Vasyl). A film item's link to an episode of a show, naming its season and episode, is identified by the
episode's own IMDb id through The Open Movie Database — no TMDb id is ever sent for it; the film id such
an item held is dropped by Apply, and a sync keeps reading it as giving way (R7), so the item stays a
movie. An episode item still keeps its link in the app, with the library's sentence "Jellyfin identifies
movie and series items only, so a link on an episode stays in the app."; a numbered episode's link
whose episode TMDb gives no IMDb id, or without its numbers, stays in the app too, saying why. The
library still says what every item is. The series test named above is replaced by
`LibraryLinkSetHttpTests.Set_of_a_show_on_a_series_is_identified_by_the_worker_through_the_series_search_with_the_shows_tmdb_id_and_the_next_sync_finds_it`.

## Duplicates (added 0.2.1, MAM-20)

**A shared registry id, not a shared item, makes another copy of the same work.** The grouping
keys every film and episode work by its registry ids — its own, and an episode's series' ids with its
season and episode — across every library and integration, never by a library's item or series item
([ADR 0028](0028-duplicates-are-worked-out-on-every-read-in-four-classes-by-one-file-one-hash-one-registry-key-or-one-title-and-year-and-a-group-only-shows.md)). The 65 id-less files of the Tom & Jerry Kids item make no group; The Continental
filed three times under one IMDb id is one group of three versions, never redundant; the two halves
of a Mickey Mouse Clubhouse episode are one group of two versions; and an episode of The Grand Tour,
known as two series items in one library and as one in another, is one work by the show's ids with
its numbers. A key of TMDb or TVDB carries the work's kind, so a movie and a series sharing a TMDb
number stay apart, while an IMDb id is one key for every kind, so a film item and the library's
episode with one IMDb id are one work. The Decision's last sentence — MAM-20's tests pin that a
shared registry id makes the copy — holds.

**A disagreement is shown for review, never resolved (Vasyl, 2026-10-07).** The Consequences left how
to show S03E31 and S03E33 to MAM-20: the two works are one same-work group, `needsReview`, with the
distinction `EpisodeNumbers` — one show, one episode's ids at two places —, each file listed under
its own work with its own place. Two works with one IMDb id and two TMDb ids are flagged
`IdentifierConflict` the same way. Neither is merged, and the library keeps both works.

**A file no library reported is a possible match only.** As the Anti-consequence left to MAM-20: by
what its file name, then its folder's name, claims — a title with one year, read as ADR 0012 reads a
release's —, against the title keys and years of the library's works and of other such files; never
by a name that names seasons or episodes, which every episode of a show shares; never a copy of the
same work, as it has no id; and always for the owner to confirm. The name still makes no file an
episode and joins it to no work.

**A linked id joins two libraries' copies.** The MAM-34 section's "inside the library whose items it
links" was the sync's grouping; the duplicates' grouping reads a linked id as a registry key across
every library ([ADR 0026](0026-a-tmdb-id-set-by-hand-is-the-owners-word-kept-per-library-item-and-written-into-the-library-as-ids-only.md)'s Duplicates section). An episode-to-show link with its
numbers is the key of the show's own episode, so a film item linked as S01E01 and the library's own
S01E01 are one work's copies; the film item stays a film and holds none of the show's ids.

`LibraryDuplicatesSameWorkHttpTests.List_makes_no_group_of_the_files_of_one_item_without_ids`,
`LibraryDuplicatesSameWorkHttpTests.List_lists_three_movie_items_with_one_imdb_id_as_one_work_never_redundant`,
`LibraryDuplicatesSameWorkHttpTests.List_groups_the_two_halves_of_an_episode_as_versions_of_one_work`,
`LibraryDuplicatesSameWorkHttpTests.List_groups_the_episodes_of_a_series_the_library_knows_as_two_series_items`,
`LibraryDuplicatesSameWorkHttpTests.List_keeps_a_movie_and_a_series_sharing_a_tmdb_number_apart`,
`LibraryDuplicatesSameWorkHttpTests.List_groups_a_film_item_and_the_librarys_episode_with_one_imdb_id`,
`LibraryDuplicatesSameWorkHttpTests.List_flags_two_episodes_with_one_episodes_ids_under_different_numbers_for_review`,
`LibraryDuplicatesSameWorkHttpTests.List_groups_a_film_item_linked_as_an_episode_with_the_librarys_own_episode`,
`LibraryDuplicatesPossibleHttpTests.List_offers_a_scanned_file_named_with_a_works_title_and_year`,
`LibraryDuplicatesPossibleHttpTests.List_offers_a_scanned_file_by_its_folders_name` and
`LibraryDuplicatesPossibleHttpTests.List_never_offers_the_episodes_of_a_show_no_library_reports_as_one_work`
pin it.

## Duplicates of episodes (added 0.2.1, MAM-37)

**Two episodes at different places are never one duplicate group, and an episode's `a` and `b` files
are no copies of each other (Vasyl, 2026-10-08).** The Duplicates section above made S03E31 and
S03E33 one same-work group flagged `EpisodeNumbers`, and an episode's `a` and `b` files one group of
two versions; MAM-37 reverses both
([ADR 0028](0028-duplicates-are-worked-out-on-every-read-in-four-classes-by-one-file-one-hash-one-registry-key-or-one-title-and-year-and-a-group-only-shows.md)'s
Episodes at different places and Segments sections). Two episodes of one show at different places —
another season or another number — are never one work of the Duplicates list, whatever ids they
share: the 20 Penguins of Madagascar pairs under one IMDb id each, S03E31 under every id of S03E33.
A show is its ids and its titles, so one the library knows by no id still keeps its places apart; a
work a sync made of two such episodes, for want of the show's ids, is a copy of nothing; and a film
item carrying the IMDb id of two episodes is a copy of neither, as the sync joins an item that fits
two works to neither. `EpisodeNumbers` is gone. One episode's copies at one place still group, and
copies of one episode at its place under two shows' ids are still flagged `IdentifierConflict`. Two
episodes the library places apart are no possible match either, whatever title and year they share.

**The library is unchanged.** The Decision holds: the sync still makes an episode's `a` and `b`
items one episode work with two versions, as Jellyfin gives both the episode's numbers and ids, and
the Library page still shows one episode with two versions, as Jellyfin does; S03E31 and S03E33 stay
two works, each at its place. Only the Duplicates list leaves them out.

**A segment letter may keep two files from being copies, and never joins anything.** The letter a
file name glues to its first `SxxEyy` — the `a` of `Mickey Mouse Clubhouse_S03E09a_-_Road Rally …`,
a lowercase `a` to `d` that `EpisodeSegments` reads narrowly — parts an episode's copies: an `a`
file is a copy of another `a` file only, never of the `b` file or of the whole episode. It makes no
item an episode, joins no work and moves no place: the rejected option "Read `SxxEyy` from a file
name to make an item an episode" and the MAM-34 section's "A name is a hint, never a decision" hold.
Nor does a year in an episode's name part its copies: it is its show's or its season's.

`LibraryDuplicatesSameWorkHttpTests.List_makes_no_group_of_an_episodes_a_and_b_files` replaces the
Duplicates section's `LibraryDuplicatesSameWorkHttpTests.List_groups_the_two_halves_of_an_episode_as_versions_of_one_work`,
and `LibraryDuplicatesSameWorkHttpTests.List_makes_no_group_of_two_episodes_with_one_episodes_ids_under_different_numbers`
its `LibraryDuplicatesSameWorkHttpTests.List_flags_two_episodes_with_one_episodes_ids_under_different_numbers_for_review`;
with `LibraryDuplicatesSameWorkHttpTests.List_makes_no_group_of_two_episodes_of_one_show_at_different_places_under_one_imdb_id`,
`LibraryDuplicatesSameWorkHttpTests.List_makes_no_group_of_one_work_the_library_holds_at_two_places_of_a_show`,
`LibraryDuplicatesSameWorkHttpTests.List_groups_each_segment_of_an_episode_with_its_copy_in_another_library`,
`LibraryDuplicatesSameWorkHttpTests.List_keeps_an_episodes_whole_file_apart_from_its_segment_file`,
`LibraryDuplicatesSameWorkHttpTests.List_keeps_an_episodes_copies_one_group_whatever_years_their_names_claim`,
`LibraryDuplicatesPossibleHttpTests.List_never_offers_two_episodes_with_one_imdb_id_at_different_places_with_one_title_and_year`,
`DuplicateWorkComponentsTests` and `EpisodeSegmentsTests`, they pin it.

## End numbers, read for a replacement only (added 0.3.0, MAM-25)

**Jellyfin's end number of an episode file that covers several (`IndexNumberEnd`) is read by the
check of a replacement's new files alone — for each new file and each ticked episode copy, through
the new `IMediaLibraryFileLookup` —, never by the sync, so the Library and the Duplicates page see
one episode per item as before ([ADR
0035](0035-a-replacements-new-file-is-checked-by-its-clients-word-the-media-inspector-and-the-librarys-ids-before-anything-moves-and-what-is-unsettled-after-an-hour-stops-for-review.md),
[ADR
0032](0032-a-replacement-targets-films-episodes-or-seasons-on-the-owners-word-and-goes-ahead-only-when-the-library-gives-the-new-file-the-old-copys-work.md)'s
MAM-25 section).** A file covering a ticked episode and the next stops that replacement for review,
and one covering only episodes nobody ticked is simply added.

**The Decision holds for a replacement.** The kind is the library's word: a new file Jellyfin makes
a film for an episode copy, and a ticked episode it now lists as a film, stop the replacement. So
are an episode's series and numbers, as the library gives them now, not when the copy was ticked: a
ticked episode's own item is read again on every pass, its series must still be the show by its ids
and its season and number those it had when scheduled, so one Jellyfin now places in another series
("“Living room” placed the old copy “…” in another series (…), not …") or numbers otherwise
("“Living room” now numbers the old copy “…” season 2, episode 3, not season 2, episode 1 as when it
was scheduled.") stops the replacement, and the episodes it covers are read from its numbers now. An
episode's work is its series' registry ids at its season and number — an IMDb id among the show's
ids, TMDb TV and TVDB within their kind, a film scheme on a series no id, as above —, never its
series item. Names are hints that may only stop a replacement: a range a file's name gives, read the
way Jellyfin 12.2 reads it (its ten multi-episode expressions, ported to `EpisodeRangeNames`), only
widens the episodes an item covers where Jellyfin gives no end number, and an `a` or `b` glued to
the first `SxxEyy` — MAM-37's segment letter, `c` and `d` left out — marks a half that can't be
replaced on its own. Neither makes an item an episode or a pair. On 2026-10-10 no file of the
reference installation had an end number or a half's letter, so these rest on the tests.

`JellyfinAdapterFilesReadTests.ReadFiles_reads_the_end_number_of_an_episode_whose_file_covers_several`,
`ReplacementVerificationEpisodesHttpTests.Verify_of_a_new_file_covering_a_ticked_episode_and_the_next_stops_for_review`,
`ReplacementVerificationEpisodesHttpTests.Verify_of_a_ticked_copy_jellyfin_numbers_as_two_episodes_stops_for_review`,
`ReplacementVerificationEpisodesHttpTests.Verify_of_a_pack_whose_file_of_two_episodes_touches_no_ticked_episode_simply_adds_it`,
`ReplacementVerificationEpisodesHttpTests.Verify_of_a_ticked_episode_jellyfin_now_lists_as_a_film_stops_for_review`,
`ReplacementVerificationEpisodesHttpTests.Verify_of_a_ticked_episode_jellyfin_now_numbers_otherwise_stops_for_review`,
`ReplacementPairingEpisodesTests.Pair_of_an_old_copy_the_library_now_places_in_another_series_stops_for_review`,
`ReplacementPairingEpisodesTests.Pair_of_an_old_copy_the_library_now_numbers_otherwise_stops_for_review_and_pairs_nothing`,
`ReplacementPairingEpisodesTests.Pair_widens_a_new_files_episodes_by_its_name_and_never_narrows_them`
and `EpisodeRangeNamesTests` pin it.
