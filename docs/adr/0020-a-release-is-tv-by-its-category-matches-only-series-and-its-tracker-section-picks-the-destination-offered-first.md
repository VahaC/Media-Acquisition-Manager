# 20. A release is TV by its category, matches only series, and its tracker section picks the destination offered first

Status: accepted (0.2.0, MAM-32)

## Context

Vasyl chose movies and TV in full (ADR 0017), and for MAM-32 everything with them: the search finds
TV releases, a TV release is matched to the series in the library, and it can be downloaded
(decision 4); the Download dialog picks the destination by the release's tracker section, the
installation's default when none of its sections is mapped (decision 5); and he added a Season
field to the search form and kind filters to the Library page and the releases list (decision 6).
Phase A searched for movies only: the movie search in the movie categories, by title and year (ADR
0004, ADR 0012).

Toloka through Prowlarr answered three TV searches on 2026-10-04
(`VendorSamples/Torznab/prowlarr-toloka-tv-*.xml`), and they break the movie shapes:

- **The movie search never lists a show.** Its releases are kept in the movie categories (2000 to
  2999); Toloka files series under TV (5000), TV/HD (5040) and TV/Documentary (5080), beside its
  own sections («Мультсеріали», 100044, and «Мультсеріали в HD», 100174, for Mickey Mouse
  Clubhouse). Toloka's caps offer a TV search that takes a title, a season and an episode.
- **A TV title names the years of its seasons.** Prowlarr rewrites "Єллоустоун (Сезони 1-5) /
  Yellowstone (Seasons 1-5) (2018-2024) …" as "Yellowstone (S1-5) (2018-2024) WEB-DL-AVC …", and a
  pack of season 5 as "Yellowstone (S5) (2022-2024) WEB-DL 1080p (S5)". The movie reading takes
  "(2018-2024)" for a collection's range and reads no title at all; a search for the show's year,
  2018, finds only the packs that hold its first season.
- **A title can be a film's and a show's.** A BBC documentary "Yellowstone (S1) (2009)" has the title
  of Vasyl's Yellowstone (2018); TMDb numbers shows apart from movies, so one number can name both.
  None of the 19 TV listings gives an IMDb, TMDb or TVDB id.
- **A standard category doesn't say where a download belongs.** A cartoon series and a drama are
  both TV (5000); only the tracker's own section — «Мультсеріали» or «Телесеріали» — tells them
  apart, and Vasyl keeps them in different qBittorrent categories (Cartoons, TV Shows).
- **`season=1` barely narrows.** Toloka answered 6 of the 9 Yellowstone releases.

## Decision

**Every release source is searched for movies and for TV, each in its own categories; a release is
TV when its source lists it in a TV category; a TV release's title is read for its seasons,
episodes and span of years, and it is matched to the library's series only, as a movie release is
to movies only; and the Download dialog picks first the destination the owner mapped the release's
tracker section to (Vasyl's decisions 4 and 5).**

**Two families, one source at a time.** `ReleaseSources` sends each enabled source the movie
queries (`ReleaseSearchPlanning.For`), then the TV queries (`ForTv`), one after the other; within
each, the tiers run in order until one lists a release. The TV tiers go to the TV categories
(5000 to 5999): an id asked for that the source's TV search takes — in practice the IMDb id, as
the form's TMDb id is a movie's and never goes to it —, then the title without the year, through
the TV search (`t=tvsearch`) or, when it takes no title, the general search. A season asked for
goes as `season=` where the TV search takes it, and runs the TV family alone. A source with no TV
category gets no TV query. The movie releases come first, then the TV ones; a listing both
families find (one `SourceItemId`) is kept once, as the movie search found it. Any failing query
fails the source, as before, and the sources are still asked at once. Each family lists at most 100
releases per source.

**The source says what a release is.** The Torznab adapter calls a release TV when it lists a
category from 5000 to 5999, a movie when it lists any other, and what its query asked for when it
lists none: TV for a TV search, and for a general search sent in TV categories alone — the title
tier of a source whose TV search takes no title —, a movie otherwise. The store keeps its `Kind`,
its `CategoryIds` in the listing's order (ids past the 512-character column dropped whole) and its
`TvdbId`; `TmdbId` is a show's on a TV release, told by `Kind`, and is read back as `TmdbTv`.

**A TV title is read for its seasons** (`ReleaseWorkClaims.ReadTv`). The first season marker gives
the seasons and episodes: `S01E01`, `S01E01-E10`, `S01E01E02`, `S1E1-9 of 9`, `S1-3`, `S01-S03`,
`Season(s) N[-M]`, `Сезон`/`Сезони N[-M]` with an optional ", серії a-b з n", bare or in brackets.
A bracketed year or span ends the title, and each " / " alternative is cut at its own marker;
without one, the title ends at a bare year or the first marker, whichever comes first. A backwards
span is no year, "(2018-2018)" one year, a backwards season range claims its first season, episodes
are claimed only within one season, and a run across seasons (`S01E05-S02E03`) claims those seasons
whole. A movie's title is read as before.

**Matching keeps the kinds apart.** A TV release is matched to series only: by a show's id (IMDb,
TMDb TV, TVDB), then by a title with a year its seasons allow, then by the title alone; a movie
release to movies only, as before. A film and a show of one title, or of one TMDb number, never
match each other's releases, and an episode of no series is matched by none. Each TV match carries
`heldEpisodeCount`: how many different episode works of the series sit in the claimed seasons (and
episodes, when the title names them); `null` when the title claims no season. It is worked out on
every read with the matches, never stored (ADR 0011, ADR 0012).

**A season pack's year counts from the show's first year on (Vasyl's choice).** A release names the
years of the seasons it holds, never the show's, so `ReleaseWorkClaims.YearsToMatch` reads the
show's year from the seasons the title claims (`ClaimedYears`, which `WorkMatching` tests a title's
year against). With season 1 among them, the first year is the year the show began and must be it
exactly: "Yellowstone (S1-5) (2018-2024)" is Yellowstone (2018) by title and year, while the BBC
"Yellowstone (S1) (2009)" — and "(S1-5) (2017-2024)", whose span merely holds 2018 — stay title
matches. With later seasons only, or the specials alone, the show began in the first year or before
it, so a series of that year or earlier matches: "(S5) (2022-2024)" and "(S4) (2021)" are
Yellowstone (2018) by title and year, and a show begun after 2021 is not. A title that claims no
season keeps any year of its span, its ends included, and a movie its one year, as before.

**The tracker section picks the destination offered first.** In a destination's dialog the owner
ticks sections of each release source (`GET /api/settings/destinations/sections`): its categories,
read live from the source's caps every time, one section per cleaned name (Prowlarr's "|-" dropped),
rank (the source's own, id 100000 and up, or standard) and destination. A ticked section is a row
per (source, category), unique, saved with the destination in its one save, transaction and audit
row; a section mapped to another destination moves here, and the rows go with their destination or
their source. `GET /api/downloads/options` answers `suggestedDestinationId` from the release's stored
categories, counting only the destinations it offers: the source's own section wins over a standard
category; two different destinations at the rank that decides suggest none; nothing mapped suggests
the default, when it is offered. No source or client is asked.

| Rejected option | Why not |
| --- | --- |
| Search TV by the title and the show's year | A pack names its seasons' years: "Yellowstone 2018" finds only the packs that hold season 1, and the tiers stop there |
| One query for both, in every category | A general search with nothing to keep lists every movie a second time as TV |
| Both families at once within a source | Two requests at a time to one source, and no fixed order of them |
| Read the kind from an `S01` in the title | A guess from a name (ADR 0017); the category is the source's word |
| Match a TV release to movies too | A film and a show of one title, or one TMDb number, would claim each other's releases |
| Take any year of a season pack's span | A later seasons' pack never names the show's first year, so "(S5) (2022-2024)" stayed a title match to Yellowstone (2018); and a season-1 pack whose span merely holds the year rose |
| Count "every episode" when a title names no season | Nothing says what the release holds |
| A destination per release kind | A cartoon series and a drama are both TV (5000) |
| Store each source's sections | A section the tracker adds or renames would be stale; only the mapped rows are kept |
| On a tie, fall back to the lower rank or the default | It would hide that the release's sections disagree; the dialog leaves the pick to the person |

`ReleaseSearchTvHttpTests.Search_lists_the_movie_releases_then_the_tv_releases_and_stores_each_with_its_kind_and_categories`,
`ReleaseSearchTvHttpTests.Search_of_a_season_sends_the_tv_search_alone_with_the_season`,
`ReleaseSearchTvHttpTests.Search_whose_tv_query_fails_fails_the_source_and_keeps_none_of_the_movies_it_found`
and `ReleaseSearchPlanningTests.ForTv_toloka_searches_its_tv_search_by_the_title_without_the_year_in_the_tv_categories`
pin the search; `ReleaseWorkClaimsTvTests.ReadTv_claims_what_each_title_of_the_real_answers_names`
the reading; `SearchReleasesTvMatchingHttpTests.Releases_matches_a_tv_release_to_series_and_a_movie_release_to_movies_with_the_episodes_held`,
`SearchReleasesTvMatchingHttpTests.Releases_matches_a_tv_release_by_the_year_its_seasons_allow`,
`WorkMatchingTests.Match_of_a_tv_releases_title_takes_the_shows_year_its_seasons_allow`
and `SearchReleasesTvMatchingHttpTests.Releases_matches_a_tv_release_by_a_shows_ids_and_a_movie_release_by_a_films_never_the_other`
the matching; `DestinationsSectionsSaveHttpTests.Create_with_a_section_mapped_to_another_destination_moves_its_row_here`,
`DownloadOptionsSuggestionHttpTests.Options_prefers_the_sources_own_section_to_a_standard_category`
and `DownloadOptionsSuggestionHttpTests.Options_with_two_destinations_at_the_rank_that_decides_suggests_none`
the destination.

## Consequences

**A search asks each source more.** Toloka gets up to two movie queries and one TV query, one after
the other, so its answer takes longer; a source stores up to 200 releases a search. The Search page
filters them by kind (Movies, TV), and its narrow form takes a season.

**Toloka's TV releases match by title and year.** They list no ids, and a season pack's span starts
with its first season: "Yellowstone (S1-5) (2018-2024)", "(S5) (2022-2024)" and "(S4) (2021)" all
match Yellowstone (2018) by title and year, while the BBC documentary of 2009 matches it by title
alone. The held count says how much of a pack the library already has.

**An older show of the same title matches a newer show's later seasons too.** A pack of later
seasons says only that its show began by its first year, so if the library held an older show of
the title as well — a Yellowstone of 2009 beside the one of 2018 —, "(S5) (2022-2024)" would be
title and year evidence for both. Both are listed, as every match is (ADR 0012); the season-1
packs, and any id the listing gives, still tell them apart.

**Known limits, left on purpose:** episodes are never searched by number (`ep=`), only seasons; a
title with two disagreeing markers is read by the first; MAM-10's single-answer search
(`POST /api/releases/search`) answers a release's kind but no seasons; a release stored before
0.2.0 has no categories and is offered the default destination. The sections list is neither paged
nor searched, and lists switched-off sources with the sections mapped to them unasked: a save
replaces the destination's sections, so one it doesn't show would be lost.

## Anti-consequence

A suggested destination is a first pick the person can change, and a match or a held count is
evidence to read: nothing is sent, picked or replaced because of either (ADR 0012). This doesn't
make a destination anything but a client's category
([ADR 0010](0010-a-destination-is-a-download-clients-own-category-never-a-folder.md)), and it decides
nothing about replacing an episode or a season (MAM-23).
