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
