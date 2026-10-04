# 19. A series is a work its episodes point at, its seasons come from their numbers, and an episode without a file is left out

Status: accepted (0.2.0, MAM-32)

## Context

[ADR 0017](0017-an-episode-is-a-work-known-by-registry-ids-and-the-library-alone-says-what-an-item-is.md)
decided what an episode is — a work of its own, known by its own IMDb and TVDB ids and by its
series' ids with its season and episode numbers, never by a library's series item — and left to
MAM-32 how series, seasons and episodes are stored and shown. The movie shapes of Phase A go wrong
on the reference installation's TV (`VendorSamples/Jellyfin/series-page.json`, `seasons-page.json`
and `episodes-page.json`):

- **A series item is not a show.** The Grand Tour is four series items with the same ids. A series
  keyed by its item is four cards of one show, and an episode keyed by its item's `SeriesId` lands
  on one of the four.
- **A season item adds nothing but a list to read.** Jellyfin lists seasons as items of their own
  (25 on the installation), and an episode already says its season (`ParentIndexNumber`).
  Mickey Mouse Clubhouse's "Specials" season is itself virtual, without a path.
- **An episode can have no file.** 31 episodes, 22 of them specials, are virtual: Jellyfin lists
  them from the show's metadata, each with a placeholder media source and no path.
- **An episode can have no series, or two items.** The Tom & Jerry Kids item is one episode with
  65 media sources and no series; the halves of eight Mickey Mouse Clubhouse episodes are two items
  with the same numbers and ids.
- **One list per kind.** Jellyfin's `/Items` lists one item type at a time, each with its own total,
  and a scan can change any of them while the sync reads the others.

## Decision

**A series is a work of the kind `Series` that holds the show's titles and ids and no version; each
episode is a work of the kind `Episode` that points at its series work through a position — the
series work, its season number and its episode number — one per library item; a series' seasons
are the season numbers of its stored episodes; and an episode the library lists without a file is
read but stored as nothing (Vasyl's decisions 1 and 3).**

**Three lists, one snapshot.** A sync reads the library's movies, then its series, then its episodes
(`includeItemTypes=Movie`, `Series`, `Episode`), each in pages that overlap by one with its own total
and its own checks, and stores all of it in one transaction or nothing (ADR 0008, MAM-32 section).

**Grouped per kind, series first.** `LibraryWorkGrouping` runs on one kind at a time, so a movie and
a series that share a TMDb number are never one work: a show's TMDb id is stored as `TmdbTv`, which
never meets a movie's `Tmdb` (ADR 0017). Series are grouped first; an episode's keys are its own
IMDb and TVDB ids and, for each registry id of its series item, that id with its season and episode
numbers, under a key of its own that never meets a registry id. So The Grand Tour's four series
items are one series work, the two halves of an episode are one episode work with two versions,
and two episodes that carry one episode's ids under different numbers stay two. The library's own
series id is never a key. The work assignment keeps a work across syncs by the same keys, an
episode's position included, and never moves an item onto a work of another kind.

**A position is a fact with its source.** `MediaEpisodePositions` holds one row per library item,
named after its source like a title (ADR 0006) and found again by its item, so it keeps its id
across syncs; two items of one episode each place it, and two sources that disagree both stay
visible. The series is the work its series item landed on; when that work goes, the position keeps
its numbers and points at no series (`SetNull`), and the episode is listed on its own.

**Seasons from the episodes.** A series' seasons are the season numbers of its episodes' positions,
each with how many different episode works it holds: regular seasons in order, then the specials
(season 0), then the episodes the library gives no season. Jellyfin's season list is not read.

**A virtual episode is read, not stored.** It counts in its list's total, so the pages still add
up, and is marked missing (`LocationType` `Virtual`); it becomes no work, no position and no file,
so no season counts it.

**One card per series.** The Library page lists a series as one card among the movies, in title
order, its seasons collapsed; an opened season loads its episodes from the server a page at a time
(`GET /api/library/works/{id}/episodes`), each with its versions and files (Vasyl's decision 2). An
episode of no series is a card of its own with its place ("Season 1 · Episode 4"). A series is
listed when at least one of its stored episodes passes the list's search and filters: a search or a
missing-id filter the series itself answers keeps all its episodes, otherwise only those that pass;
a library filter goes by the episodes' files alone. The ids a work lacks are those of its kind
(`LibraryReliableSchemes`): IMDb and TMDb for a movie, IMDb and TVDB for an episode, IMDb, TMDb (a
show's) and TVDB for a series.

| Rejected option | Why not |
| --- | --- |
| Key a series by its library item | The Grand Tour would be four series, each with the episodes of one season folder |
| Read Jellyfin's seasons | A fourth list to page and check, which says nothing an episode's season number doesn't (Vasyl's decision 3) |
| Store a virtual episode without versions | A row of nothing on the page, counted in a season the person doesn't hold (Vasyl's decision 1) |
| The series id and numbers as columns of the episode work | The halves place one episode twice, and two sources' disagreement would be lost (ADR 0006) |
| Group every kind in one pass | A movie and a series that share a TMDb number would be one work |
| Every episode a card, or every episode loaded with its series | Hundreds of cards among the movies, or seasons of 65-version items read in full for a closed card |

`LibrarySnapshotTvHttpTests.Sync_makes_one_series_of_a_show_the_library_knows_as_two_series_items`,
`LibrarySnapshotTvHttpTests.Sync_keeps_a_movie_and_a_series_sharing_a_tmdb_number_apart`,
`LibrarySnapshotTvHttpTests.Sync_makes_one_episode_of_items_at_one_position_of_a_series_with_ids_and_two_of_one_episodes_ids_at_two_positions`,
`LibrarySnapshotTvHttpTests.Sync_of_the_tv_samples_stores_series_and_episodes_and_leaves_the_virtual_special_out`
and `LibrarySnapshotTvHttpTests.Sync_after_a_series_item_is_gone_keeps_its_episodes_without_a_series`
pin the storing; `LibraryWorksTvHttpTests.Works_lists_the_seasons_regular_first_then_the_specials_then_the_episodes_without_one`,
`LibraryWorksTvHttpTests.Works_lists_an_episode_of_no_series_as_a_work_of_its_own_with_its_place`,
`LibraryWorksTvSearchHttpTests.Works_search_lists_a_series_with_the_episodes_it_finds_or_all_when_it_finds_the_series`
and `LibraryEpisodesHttpTests.Episodes_as_a_signed_in_non_owner_is_200_with_every_episode_in_season_then_number_then_title_order`
the listing; `JellyfinAdapterTvTests.ListItems_reads_a_virtual_special_as_missing_with_no_file` the
reading.

## Consequences

**A show is one card**, however many series items the library makes of it, and its season counts
follow the list's search and filters. A series none of whose episodes is stored — one whose
episodes are all virtual — is not listed at all. An episode without ids of its own and without a
series that has ids is a work of its own, never joined to another by its title or its numbers.

**Seasons follow the library's numbers.** An episode Jellyfin gives no season sits under "No
season", and an episode two items put in two seasons counts in both. What the show has but the
library doesn't — the virtual episodes — is not shown: the page says what you hold.

**A sync reads three lists.** It reads two more lists than before, each from its first page, and a
library scan that changes any of them fails the run as ADR 0008 says.

## Anti-consequence

This decides how the library's TV is stored and listed, not what a release matches: that is
[ADR 0020](0020-a-release-is-tv-by-its-category-matches-only-series-and-its-tracker-section-picks-the-destination-offered-first.md)'s.
No file or folder name makes an item an episode or places it in a season (ADR 0017), and nothing
here decides whether a replacement can target an episode or a season (MAM-23).
