# 23. A recommendation only proposes, explains every reason and never sends

Status: accepted (0.2.1, MAM-33)

## Context

MAM-33 adds a Recommendations page: films, series, cartoons and documentaries the library doesn't
have yet, picked from what it has, with what the release sources carry of them. Specification §1
makes the app one "for a user who **chooses releases manually**", and Vasyl's ticket says it again:
"Nothing is ever sent automatically." He decided on 2026-10-05 that TMDb is the catalog, under his
own key (decision 1); that the page has four tabs, Movies, Series, Cartoons and Documentaries
(decision 2); that "Not interested" is kept per user (decision 3); and that every user's Profile
holds a daily limit of release-source searches and a switch per tab, every tab on for a user who
never saved (decision 5).

The reference library makes the obvious shapes go wrong. TMDb with Vasyl's key, read on 2026-10-05
for the 640 TMDb ids of the library's movies, named 4,891 released films the library doesn't have —
3,518 over a rating floor, 2,274 with a collection, a director or two library films behind them — so
an unordered list buries what matters, and a list ordered by a model nobody can read can't say why a
card is there. 279 of the library's movies sit in 118 collections, 42 of them incomplete with 63
released parts missing; 138 directors have two or more films in it. 210 movies have no id at all,
168 of them in the cartoon library, titled the way files are ("120. Landing Stripling", "02. Merry
Madagascar"): a work matched by title alone would recommend a cartoon the library already holds
under another spelling, or hide one it doesn't. A catalog's average from four votes outranks every
classic. A work announced for next year can't be downloaded. And a recommendation that downloads,
queues or "monitors" by itself is exactly what the specification refuses.

## Decision

**A recommendation is a catalog work the library lacks, listed with every reason it is there — each
a chip on its card — in the order of a fixed score of those reasons; the app never sends, queues or
picks a release for it: "Releases" opens the Search page, and the person downloads through the
Download dialog as from any search (Vasyl's decisions 1, 2, 3 and 5).**

**The seeds are the library's works the catalog knows.** A computation reads the library's films and
series (a series while it holds an episode) and finds each one's catalog work: its own TMDb or TMDb
TV id; else, best effort, the work TMDb files under its IMDb id; else its titles, cleaned of what a
file name adds (`SeedTitleCleaning`: "120. " in front, underscores, "_E08", a bracketed tag, a
season marker or a quality word ending the title), looked up with their year and used only when
exactly one result matches by title and year. Such a possible match seeds recommendations and keeps
works out; it is never shown or written on the library
([ADR 0025](0025-the-catalog-is-kept-apart-from-the-library.md)). Only the kinds some tab wants are
asked: films for Movies, Cartoons or Documentaries, shows for Series, Cartoons or Documentaries.

**Every signal is a reason, kept as a row and shown as a chip.**

- **Collection:** each released part of a seed film's collection the library lacks, with how many of
  the released parts it holds: "Guardians of the Galaxy · 2 of 3".
- **Director** and **Creator:** a person two or more seeds of a kind are by — a film's director, a
  show's creator — and their released works of that kind, with how many seeds are theirs.
- **Recommended:** the works TMDb recommends beside a seed, counted by how many seeds recommend each —
  a film the library holds twice is one seed and counts once —, with the display titles of at most
  five of them ("Because you have X, Y and Z").
- **MissingSeasons:** a held series' regular seasons that have aired (by today) and of which the
  library holds no episode: "Missing seasons 4–5". Season 0 is never missing, and a season the
  library holds one episode of is held. The candidate is the held series itself.
- **NewAtSource:** a work a release source published lately — first seen in its newest releases
  within 14 days, `FeedFreshness` — with the source's name and its latest publish date
  ([ADR 0024](0024-release-sources-are-asked-for-recommendations-within-a-per-user-budget.md)).
- **TopRated**, the fallback: for a tab with fewer than 10 seeds (`FewSeeds`), two pages
  (`DiscoverPages`) of the catalog's best-rated works of the tab's genre among works at least 300
  people rated. A work with no other reason is listed only once a release source carried it.

**The tab comes from the genres** (`RecommendationTabs.Classify`): Documentary first, so an animated
documentary is a documentary; then Animation, anime included; else Movies for a film and Series for
a show. Cartoons and Documentaries hold films and shows, with a films/series filter inside them. A
work of a tab nobody has on is dropped, the catalog's best-rated works are asked for only for a tab
someone has on, and with every tab off for every user the run asks the catalog nothing. No call is
made per candidate: a candidate comes with what the list that named it said.

**The score is a sum anyone can redo from the chips** (`RecommendationScoring`):
`8·MissingSeasons + 6·Collection + 3·(Director or Creator) + 2·log2(1 + recommenders) +
1·NewAtSource + 0.5·TopRated + genre affinity + decade affinity + 1.5·(weighted rating − 6)`, the
weighted rating being `(votes × rating + 50 × 5.0) / (votes + 50)`: the rating floor's average,
pulled toward 5.0 as if 50 more people had rated the work so. What the owner holds part of comes
first — the rest of a show, then of a collection — then a person whose work they keep. The
recommending seeds count by their logarithm, each doubling adding 2 — one is worth 2, three 4,
seven 6 —, so a work many library works point at climbs, but slowly. The rating counts 1.5 a point
of the weighted rating above 6.0: a 7.9 rated by thousands gains about 2.8 and a 6.2 gains 0.3, so
of two works with like reasons the one rated better by more people goes first. A reason of taste needs the
weighted rating to reach 6.0 (the floor below), so it never loses by it; a collection's part or a
missing season under the floor loses a little and still stays. The genre affinity is the largest
share of the seeds of the work's kind having one of its genres, and the decade affinity the share of
those seeds from its decade, each 0 to 1. The list is ordered by score, then votes, then title
(ordinal), then catalog id: the same library and catalog always give the same order. A card shows
its reasons in that same order, strongest first.

**The weights come from the reference library's first computation** (2026-10-05). The first formula,
`4·MissingSeasons + 3·Collection + 2·(Director or Creator) + min(recommenders, 10) + …` with a
hundredth of the rating as a tie-breaker, put Knight and Day, Paycheck and Surrogates — films rated
around 6.2 that many library films recommend — at the top of Movies, and buried "2 of 3" collection
parts at ranks 17, 51 and 98. Scored as above, the same candidates open with Guardians of the
Galaxy Vol. 3 (7.9, "2 of 3" and its director), Back to the Future Part II and Blade Runner 2049,
and those collection parts stand at ranks 1, 6 and 4.

**A reader may sort a tab otherwise** (`sort`, Vasyl's request): by the weighted rating, so a 10.0
of three votes doesn't top it; by the votes; by the release date, newest or oldest first, a work of
no known date last; by the title in Ukrainian alphabetical order, as the Library page sorts; or by
the release count the availability check found
([ADR 0024](0024-release-sources-are-asked-for-recommendations-within-a-per-user-budget.md)) — the
works found first, then those not found, then those never checked. Ties keep the score's order, and
the counts the filters show don't depend on the sort. The score's order is the default.

**The rating floor applies to taste alone** (`RecommendationRatingFloor`). A reason of taste —
Recommended, Director, Creator, TopRated — is kept only for a work whose average, pulled toward 5.0
as if 50 more people had rated it so, still reaches 6.0: `votes × (rating − 6.0) ≥ 50`, so a 7.0
needs 50 votes, an 8.0 25, and a 9.5 of four votes is out, as is anything under 6.0 or unrated. A
reason the library itself gives — a collection's next part, a held series' missing seasons, a release
a source just published — is kept whatever the rating. A work left with no reason is dropped.

**Unreleased works are left out everywhere**: a work with no date, or a date after today, is never
named by any signal, and a season counts only once it has aired.

**Exclusions.** A work the library holds keeps only a missing-seasons reason. It is held when the
library has its catalog id — a work's own, or a seed's possible match —, its IMDb id where its
details gave one, or, kinds kept apart, a work it matches by title and year (`WorkMatching.Match`
with evidence `Identifier` or `TitleAndYear`,
[ADR 0012](0012-a-search-or-a-release-matches-a-library-work-by-identifier-then-title-and-year-then-title-alone-and-every-match-is-listed.md));
or when the name of a file only the folder scan found (MAM-18), or its folder's name, claims it by
title and year. A title alone never excludes: a remake of another year stays a recommendation.
When a user reads a tab, the works they marked "Not interested" are left out for them, and so is a
fallback work until a check found it at a release source. Every user's download jobs count, as a
download serves the household as the library does: a job's release is matched to a work as the
Search page matches a release to the library — of the work's kind, by an id its listing gives or by
the title and year its title or description claims, never by a title alone, and for a held series by
a missing season or none named. A work a followed job (`Created`, `Submitted`, `Downloading`,
`Paused`) is of shows **Downloading**; one a job of which ended without failing (`Downloaded`,
`NeedsLinking`, `Cancelled`) is left out, the household having dealt with it, even while another job
of it is under way; a failed job changes nothing. All of it is read on every request, never stored.

**Never sent.** No endpoint, run or loop of MAM-33 writes a download job, adds to a client or
follows a work. A card's **Releases** opens `/search?q=<title> (<year>)&tmdbId=<id>` — the film's
TMDb id only, since the Search page takes a film's — and the person picks a release there, sends it
through the Download dialog (MAM-13, the destination its tracker section is mapped to, ADR 0020) and
follows it on Jobs. A work leaves the list once a library sync holds it.

**"Not interested" is the user's own (Vasyl's decision 3).** It is kept per user by the work's kind
and catalog id (`PUT /api/recommendations/dismissals/{kind}/{catalogId}`), with no key to the catalog
row, so it outlives the row and holds for a work no list holds yet; **Undo** (`DELETE`) removes the
caller's mark alone. Both answer 204 whether or not anything changed. It writes no audit row: it is a
preference, as a Profile switch is.

**Computed by a run, replaced whole.** A run (202 + run row + reconciler, the file inspection's
shape) starts after every successful library sync, when a TMDb integration is added switched on or
switched back on, on the owner's **Recompute now**, when a user switches on a tab no other user had
on, and when the newest releases of a source resolved to new works. It reads the library, the
catalog and every enabled user's tabs as they are when it starts, and replaces every candidate and
reason in one transaction at its end; a failure of the catalog fails the run with the catalog's
masked sentence and keeps the previous candidates. A first run is minutes of calls to the catalog
(3 min 15 s for the reference library's 815 seeds), so a running one says how far it is (Vasyl's
request): its row keeps the stage — `ReadingLibrary`, `FindingLibraryWorks`, `ReadingWorks`,
`ReadingCollections`, `ReadingPeople`, `Discovering`, `Saving` — and how many of the stage's items
are done of how many, no counts for a stage that has none, written at each change of stage and at
most every 2 seconds within one, and the page shows "240 of 815" with a progress bar and how long
ago the run started. A run that isn't running answers no stage. The three are nullable columns of
`RecommendationRuns`, added by `AddRecommendationRunProgress` without rebuilding the table.
`RecommendationRunProgressHttpTests.Recompute_writes_each_stage_in_order_with_its_counts_and_ends_with_none`,
`RecommendationRunProgressHttpTests.Recompute_writes_a_stages_count_at_most_every_two_seconds_and_always_at_its_end`
and `RecommendationRunProgressHttpTests.LatestRun_while_a_run_reads_the_catalog_shows_its_stage_and_counts_to_the_owner_and_a_member`
pin it.

| Rejected option | Why not |
| --- | --- |
| Send, queue or follow the best recommendations by itself | Specification §1 and Vasyl's ticket: the person chooses every release |
| A learned or weighted-by-feel score | A card can't say why it is there, and its place moves without a reason the page shows |
| Weight by what was watched or marked favourite | Jellyfin's user data needs a user; the integration holds an API key only |
| The rating floor on every reason | It would hide the third part of a collection the owner holds two of |
| A bare average as the floor | A 9.5 from four votes outranks every classic |
| Exclude by title alone | A remake of another year, or a look-alike title, would hide a work the library lacks |
| Recommend works not released yet | Nothing to download; announced titles would crowd the list |
| One "Not interested" for the whole installation | One person's dismissal would hide a work from everyone (Vasyl's decision 3) |
| List the catalog's best-rated works unchecked | Thousands of works with nothing behind them but a rating; listed only once a source carries one |
| Recommenders counted one point each | Uncapped, one work every seed recommends would outrank every collection and missing season; capped at ten, as first built, 6.2-rated films topped the reference library's Movies and buried "2 of 3" collection parts |
| The rating only as a tie-breaker | A middling film many seeds recommend outranks a far better one with the same chips |

Over HTTP, `RecommendationSignalsHttpTests.Recompute_recommends_a_collections_released_parts_the_library_lacks_with_held_of_total`,
`RecommendationSignalsHttpTests.Recompute_recommends_a_directors_released_films_once_two_seeds_are_theirs`,
`RecommendationSignalsHttpTests.Recompute_recommends_a_creators_released_shows_once_two_show_seeds_are_theirs`,
`RecommendationSignalsHttpTests.Recompute_weighs_a_recommended_work_by_its_recommenders_and_names_their_titles`,
`RecommendationSignalsHttpTests.Recompute_names_at_most_five_recommending_titles_and_counts_them_all`,
`RecommendationSignalsHttpTests.Recompute_recommends_what_a_release_source_published_lately_with_its_name_and_date`
and `RecommendationSignalsHttpTests.Recompute_fills_a_tab_with_few_seeds_with_its_best_rated_works_listed_only_once_available`
pin the signals; `RecommendationMissingSeasonsHttpTests.Recompute_recommends_a_held_series_for_its_aired_seasons_it_holds_no_episode_of`
the missing seasons; `RecommendationTabsHttpTests.Recompute_puts_a_documentary_in_documentaries_an_animation_in_cartoons_and_the_rest_in_movies_or_series`
and `RecommendationTabsHttpTests.Recompute_with_every_tab_off_for_everyone_asks_the_catalog_nothing_and_leaves_no_candidate`
the tabs; `RecommendationRatingFloorHttpTests.Recompute_keeps_a_collections_part_under_the_floor_but_only_for_the_collection`
and `RecommendationRatingFloorHttpTests.Recompute_drops_a_directors_and_a_top_rated_work_under_the_floor`
the floor; `RecommendationExclusionsHttpTests.Recompute_leaves_out_a_work_the_library_holds_by_title_and_year`,
`RecommendationExclusionsHttpTests.Recompute_matches_titles_kind_against_kind_so_a_held_show_of_the_title_leaves_a_film_in`,
`RecommendationExclusionsHttpTests.Recompute_leaves_out_a_work_a_scan_found_files_name_claims` and
`RecommendationExclusionsHttpTests.Recompute_leaves_out_a_work_not_released_yet_or_of_no_date` the
exclusions; and `RecommendationCatalogCacheHttpTests.Recompute_failing_mid_run_keeps_the_previous_candidates_and_fails_with_the_catalogs_masked_sentence`
the replacement. In the core, `RecommendationScoringTests` (each reason's weight once, the
recommenders' logarithm, the weighted rating and the affinities; `RecommendationScoringTests.Score_ranks_a_well_rated_collection_part_above_a_middling_film_fourteen_works_recommend`
and `RecommendationScoringTests.Score_puts_missing_seasons_above_every_other_single_reason_at_the_same_rating`
the weights' order),
`RecommendationRatingFloorTests.Passes_only_a_work_whose_adjusted_average_reaches_six`,
`RecommendationTabsTests.Classify_puts_a_documentary_first_then_an_animation_then_the_works_kind`
and `SeedTitleCleaningTests.Clean_drops_what_a_file_name_adds_around_a_title` pin the rules. When a
tab is read, `RecommendationListHttpTests.List_orders_by_score_then_votes_then_title_then_catalog_id`
pins the order, `RecommendationListSortHttpTests.List_without_a_sort_is_in_the_score_order_the_same_as_sorted_by_score`,
`RecommendationListSortHttpTests.List_sorted_by_rating_puts_the_best_weighted_average_first`,
`RecommendationListSortHttpTests.List_sorted_by_votes_puts_the_most_rated_first`,
`RecommendationListSortHttpTests.List_sorted_newest_or_oldest_puts_a_work_of_no_date_last_and_pages_after_ordering`,
`RecommendationListSortHttpTests.List_sorted_by_title_follows_the_ukrainian_alphabet_then_the_original_title`,
`RecommendationListSortHttpTests.List_sorted_by_releases_puts_the_found_most_first_then_not_found_then_never_checked`
and `RecommendationListSortHttpTests.List_sorted_breaks_a_tie_by_the_score_then_votes_then_title_then_catalog_id`
the sorts (`RecommendationListSortHttpTests.List_with_a_sort_it_doesnt_take_is_400_with_why` their 400),
`RecommendationListFiltersHttpTests.List_leaves_a_fallback_out_until_a_check_found_it`
the fallback, `RecommendationListDownloadsHttpTests.List_shows_a_work_a_followed_job_is_of_as_downloading`,
`RecommendationListDownloadsHttpTests.List_leaves_out_a_work_a_job_of_which_ended_and_counts_it_nowhere`,
`RecommendationListDownloadsHttpTests.List_shows_a_work_a_failed_job_is_of_as_if_there_were_none`,
`RecommendationListDownloadsHttpTests.List_leaves_out_a_work_one_job_of_which_ended_though_another_is_under_way`
and `RecommendationListDownloadsHttpTests.List_doesnt_take_a_release_for_a_film_on_a_title_alone`
the download jobs, and `RecommendationDismissalsHttpTests.Dismiss_is_204_and_hides_the_work_from_the_callers_list_only`
and `RecommendationDismissalsHttpTests.Undismiss_leaves_another_users_mark_on_the_same_work` "Not
interested".

## Consequences

**A card explains itself.** Each chip is a reason the score counted, so the order of two cards
follows from what they show, and a filter by reason lists the works with that chip. The owner can
reread the constants here and in `RecommendationScoring` and `RecommendationRatingFloor`; they are
constants, not settings.

**Known limits, left on purpose.** A library work with no id whose cleaned title TMDb doesn't find
seeds nothing and excludes nothing: the reference library's Planet Earth episodes, movie items with
no id, found nothing, so its first computation recommends Planet Earth (MAM-34 links them), while 23
of 25 Tom & Jerry shorts were found. One TMDb finds under another spelling than the library's isn't
excluded by title, so it can be recommended although the library holds it. A possible match that is
the wrong work seeds that work's neighbours. A series counts as held with one episode, and a partly
held season isn't missing. The weights are a judgement, tuned on one library to put what the owner
holds part of first and well-rated works before middling ones; a person who disagrees reads the
chips, or sorts the tab by rating, votes, date, title or releases. A fallback work shows only once the
release sources were asked about it, which the daily limit paces
([ADR 0024](0024-release-sources-are-asked-for-recommendations-within-a-per-user-budget.md)).

## Anti-consequence

A recommendation is no match (ADR 0012): it says the catalog and the library suggest a work, not
that the library lacks it for sure or that a release is it. It authorises nothing — no download,
no replacement, no write to the library ([ADR 0025](0025-the-catalog-is-kept-apart-from-the-library.md))
— and asks the release sources only within the budget of ADR 0024.
