# 24. Release sources are asked for recommendations within a per-user budget

Status: accepted (0.2.1, MAM-33)

## Context

A recommendation card says what the release sources carry of its work — "4 releases · up to 2160p ·
Ukr", "Not found (checked 3 days ago)" or "Not checked yet" — and the fallback of a tab with few
seeds is listed only once a source carries it
([ADR 0023](0023-a-recommendation-only-proposes-explains-every-reason-and-never-sends.md)). Knowing
that means searching a source for each work. The only source today is Toloka, through Prowlarr with
the owner's tracker account (ADR 0004), and every signed-in user's lists draw on it.

The numbers rule out the obvious shape. TMDb named 4,891 released films the reference library lacks,
2,274 of them with a strong reason behind them, besides the series, cartoons and documentaries; a
search of each on every run would be thousands of tracker searches a day, on someone else's tracker,
from one account, for nothing a person typed. Vasyl decided that every user's Profile holds a daily
limit of these searches, 60 by default and 0 for none, with a switch per tab (decision 5).

The searches the app already keeps don't fit either. A search on the Search page is one user's, lives
a day and stores every release it found with its download link sealed
([ADR 0011](0011-a-search-is-stored-once-and-every-filter-sort-and-page-reads-what-it-stored.md)):
kept there, what a check found would go with the day's prune, be one user's, and store releases
nobody will send.

Toloka's newest releases come cheaper. Asked on 2026-10-05 with `t=search` and no `q`, Prowlarr
answered Toloka's latest 90 releases, newest first, each with Toloka's own topic title in
`<description>`: in the movie categories 90 over 2026-09-26 to 10-05, in the TV categories 90 over
09-28 to 10-05. Asked with `offset=50` or `offset=100` it answered no release at all: there is
nothing to page through.

## Decision

**The app asks the release sources about recommended works only within each user's daily budget — a
rolling 24 hours, split evenly between the user's tabs that are on, a tab's unused share going to the
others — and keeps what a search found per work, apart from the searches, shared by every user and
checked again after a day when found and after 7 days when not; it searches at most 4 works every 15
minutes, and reads each source's newest releases with one query every 3 hours (Vasyl's decision 5).**

**A budget per user.** A user's limit comes from their Profile — a whole number from 0 to 500, 60
until they save (`RecommendationSettingsRules`), 0 asking nothing for them — and counts their
searches of the last 24 hours (`RecommendationAvailabilityPass.BudgetWindow`; a search exactly 24
hours old no longer counts), logged in `RecommendationSearches` with the user, the tab and the work
and forgotten after two days. `RecommendationSearchBudget` splits the limit evenly between the tabs
the user has on, the remainder one each to the first in the page's order: 62 over four tabs is 16,
16, 15 and 15. The user's next search serves the tab with a work due that is furthest under its
share; a tab at or past its share gets one only once no tab under its share has a work due, so a
share nobody can use goes to the others rather than being lost for the day. A search counts against
the one user whose list it serves, even when it fails, as the trackers were asked all the same. A
work the user marked "Not interested", and the works of a tab they have off, are never due for them;
a user whose limit is 0, or whose tabs are all off, is searched for not at all.

**The result is the work's, shared.** `ReleaseAvailabilities` holds one row per catalog work: when it
was checked, whether any release matched, how many, the best resolution a matching release claims,
whether one claims Ukrainian audio, and the most seeders. A work checked for one user is not due for
anyone until it is due again, so the installation runs at most the sum of its users' limits a day,
and usually less.

**What is due.** A work never checked; one found and checked a day or more ago (`FoundRecheck`), as a
release can go or a better one come; one not found and checked 7 days or more ago
(`NotFoundRecheck`), as most of the budget would otherwise go on works the tracker doesn't carry. A
work kept only as a fallback, to be listed once found, is checked like any other. Within a tab, the
works never checked come first, then by score, votes, title and catalog id: the list's default
order, whatever a reader sorts the page by.

**A slow, steady pace.** `RecommendationAvailabilityChecker` is a loop like the download tracker's: a
pass as the app starts, then one 15 minutes after each pass ends (`PassInterval`), at most 4
searches a pass for the whole installation (`SearchesPerPass`). The users take turns, one search
each, the one searched for least recently first, then by user name, so no user waits behind the
others for good. That is at most 384 searches a day whatever the limits say, a few at a time, never a
burst. With no release source switched on the pass asks nothing; it needs no catalog, as it reads
the candidates the last run stored.

**Searched as any search is, kept as no search is.** A work is asked through
`IReleaseSources.SearchKindAsync`, as a Search page search is asked, but in one family only: a film
through the movie queries with its year (`ReleaseSearchPlanning.For`), a show through the TV queries
with no year and no season (`ForTv`). The text is the original title when every letter of it is
Latin, else the catalog's Ukrainian title (`RecommendationQueryTitle`): Prowlarr keeps a Toloka
title's original part, and an original in another script is in neither part of a listing. No id is
sent — a tier by id that finds the few releases an indexer tagged would end the tiers before the
title's — but the work's ids still count when the releases are matched. A release counts when it is
of the work's kind and its title or its description claims the work by its TMDb or IMDb id, or by
title and year (`WorkMatching` with the years `ReleaseWorkClaims.YearsToMatch` allows, so a pack
"(S5) (2022-2024)" counts for the show begun in 2018;
[ADR 0012](0012-a-search-or-a-release-matches-a-library-work-by-identifier-then-title-and-year-then-title-alone-and-every-match-is-listed.md),
ADR 0020); for a held series' missing seasons it must also claim one of them, or name no season. What
the sources that answered list is stored, a source that failed beside them only making the answer
smaller; when no source answered, nothing is stored, the work stays due, and the pass stops, as the
next search would fail the same way and spend another user's budget. No `Searches` or `Releases` row
is written: ADR 0011's one-day searches are untouched, and a person who wants a release searches for
it on the Search page, which stores its own.

**The newest releases: one query a source, every 3 hours.** `RecommendationFeedReader` reads as the
app starts, then 3 hours after each read ends (`PassInterval`), and only while a metadata catalog is
connected and some user who can sign in has a tab on. Each enabled release source gets one query
(`IReleaseSources.LatestAsync`, `ReleaseSearchPlanning.ForLatest`): its general search with no text
(`t=search`, no `q`) in the categories of the kinds the tabs want — films for Movies, Cartoons or
Documentaries, in the movie categories with TV/Anime and TV/Documentary, shows for Series, Cartoons
or Documentaries, in the TV categories. No offset, as Toloka's gateway answers nothing past its 90.
Each release is kept as a `SourceFeedEntries` row of its source by the listing's id, a listing
without one left out, with no download link; a read again updates its title, description, kind,
categories, publish time, source name and when it was last listed, and keeps when it was first seen
and what it was resolved to. A source that fails keeps its entries as they are.

**Resolved once, by the titles Toloka gives.** At most 100 unresolved entries a pass
(`ResolvePerPass`), newest first and of the kinds some tab wants, are looked up in the catalog through
its cache ([ADR 0025](0025-the-catalog-is-kept-apart-from-the-library.md)): every title the
description names, Toloka's own, then every title the title names, each with its year, and each cut at
a bracket left open and trimmed of the punctuation at its edges; a claim with no year asks nothing.
A work is taken only when exactly one of that title and year exists. A pack of later seasons, without
season 1, names its seasons' years, not the show's, so it is looked up without a year and taken only
when exactly one show of that title began in or before the pack's first year
(`CatalogLookupQuery.ByTitleUpTo`, cached under a key of its own, `Series|title|YELLOWSTONE|<=2022`).
An entry is marked resolved whether or not a work was found; a failure of the catalog stops the
resolving for the pass, logged as a warning, and the entries left wait for the next one. When any
entry turned out to be a work, a run is queued (`FeedRead`), and a work first seen there within 14
days is **New on Toloka**
([ADR 0023](0023-a-recommendation-only-proposes-explains-every-reason-and-never-sends.md)). An entry
no source listed for 30 days is forgotten (`FeedEntryLifetime`, by when it was last listed). The feed
is at most eight queries a day per source and counts against no user's budget.

| Rejected option | Why not |
| --- | --- |
| Search every candidate on every run | Thousands of tracker searches a day from the owner's account |
| One limit for the installation | One user's lists would use up everyone's; Vasyl chose a limit per user |
| Keep the result as a stored search | It lives a day, is one user's, and seals download links nobody sends (ADR 0011) |
| Search each work once per user | The same tracker asked again for the same work, the same answer |
| Check a work not found every day | Most of the budget goes on works the tracker doesn't carry |
| Spend the budget as fast as it allows | Bursts of searches on a tracker; 4 every 15 minutes is the same day's work, spread |
| Send the catalog's ids to the source | A tier by id ends the tiers on the few releases an indexer tagged, and the title's never counts the rest |
| Keep searching once no source answers | Every search would fail alike and spend the next user's budget |
| Page through the newest releases | Toloka's gateway answers nothing past its latest 90 |
| Read the newest releases every few minutes | 90 releases span about a week; every 3 hours misses none |
| Look a later seasons' pack up by its first year | That is the year of its seasons, not the show's: "(S5) (2022-2024)" would find no Yellowstone of 2018 |

The budget: `RecommendationAvailabilityBudgetHttpTests.Check_keeps_a_tab_at_its_share_waiting_while_another_tab_has_works_due`,
`RecommendationAvailabilityBudgetHttpTests.Check_gives_the_share_of_tabs_with_nothing_due_to_the_tabs_that_have_works_due`,
`RecommendationAvailabilityBudgetHttpTests.Check_runs_no_search_past_the_daily_limit`,
`RecommendationAvailabilityBudgetHttpTests.Check_runs_no_search_at_all_for_a_user_whose_limit_is_0`,
`RecommendationAvailabilityBudgetHttpTests.Check_runs_at_most_four_searches_a_pass_serving_the_users_in_turn`,
`RecommendationAvailabilityBudgetHttpTests.Check_frees_a_search_of_the_budget_24_hours_after_it_ran`,
`RecommendationSearchBudgetTests.ShareOf_splits_the_limit_evenly_with_the_remainder_to_the_first_tabs`
and `RecommendationSearchBudgetTests.NextTab_gives_the_unused_share_to_the_tabs_over_theirs_once_no_tab_under_its_share_has_anything_due`.
What is due: `RecommendationAvailabilityScheduleHttpTests.Check_searches_a_work_found_again_a_day_later_and_not_before`,
`RecommendationAvailabilityScheduleHttpTests.Check_searches_a_work_not_found_again_a_week_later_and_not_before`,
`RecommendationAvailabilityScheduleHttpTests.Check_of_a_work_for_one_user_is_shared_with_every_other`,
`RecommendationAvailabilityScheduleHttpTests.Check_never_searches_a_work_for_a_user_who_is_not_interested_but_does_for_another`,
`RecommendationAvailabilityScheduleHttpTests.Check_never_searches_a_work_of_a_tab_the_user_has_off`
and `RecommendationAvailabilityScheduleHttpTests.Check_searches_the_works_never_checked_first_then_by_score`.
The search and the match: `RecommendationAvailabilityHttpTests.Check_asks_a_film_with_the_movie_search_alone_by_its_original_title_and_year`,
`RecommendationAvailabilityHttpTests.Check_asks_a_show_with_the_tv_search_alone_and_counts_the_releases_of_that_show`,
`RecommendationAvailabilityHttpTests.Check_searches_by_the_original_title_in_latin_script_else_by_the_catalog_title`,
`RecommendationAvailabilityHttpTests.Check_counts_only_releases_of_the_work_by_its_kind_and_an_id_or_its_title_and_year`,
`RecommendationAvailabilityHttpTests.Check_counts_for_a_held_series_only_the_releases_of_a_missing_season_or_of_no_season_named`,
`RecommendationAvailabilityHttpTests.Check_stores_the_count_best_resolution_ukrainian_audio_and_most_seeders_of_a_film`,
`RecommendationAvailabilityHttpTests.Check_that_no_source_answers_stores_nothing_counts_against_the_budget_and_stops_the_pass`
and `RecommendationAvailabilityHttpTests.Check_without_an_enabled_release_source_searches_nothing`.
The newest releases: `ReleaseSearchPlanningTests.ForLatest_asks_the_general_search_with_no_text_in_the_categories_of_what_is_wanted`,
`TorznabAdapterSearchLatestTests.Search_of_the_newest_releases_sends_a_general_search_with_no_text_in_the_categories_planned_from_the_caps`,
`RecommendationFeedHttpTests.Read_asks_each_source_once_for_its_newest_releases_without_text_in_the_categories_the_tabs_want`,
`RecommendationFeedHttpTests.Read_does_nothing_at_all_without_a_metadata_catalog`,
`RecommendationFeedHttpTests.Read_keeps_each_release_once_and_a_second_read_adds_none_and_keeps_when_it_was_first_seen`,
`RecommendationFeedHttpTests.Read_forgets_the_entries_no_source_listed_for_30_days`,
`RecommendationFeedResolutionHttpTests.Read_resolves_an_entry_by_the_original_title_its_description_lists_with_its_year`,
`RecommendationFeedResolutionHttpTests.Read_resolves_a_show_by_its_seasons_listing_as_a_show`,
`RecommendationFeedResolutionHttpTests.Read_queues_a_run_only_once_an_entry_turned_out_to_be_a_catalog_work`,
`RecommendationFeedResolutionHttpTests.Read_resolves_at_most_100_entries_a_pass_the_newest_first`
and `RecommendationFeedResolutionHttpTests.Read_leaves_the_entries_for_the_next_pass_when_the_catalog_fails`.
The limit itself: `RecommendationSettingsRulesTests.Default_has_every_tab_on_and_sixty_searches_a_day`
and `RecommendationSettingsRulesTests.ValidateDailySearchLimit_accepts_a_whole_number_from_0_to_500`.

## Consequences

**The lists fill in over days.** At 60 searches a day, one user's list of a few hundred works is
checked within days and a list of thousands within weeks; until then most cards say "Not checked
yet", and a tab's fallback works appear as they are found. A second user with the same tabs adds
their budget to the same shared results, so the lists fill faster, never twice.

**Known limits, left on purpose.** A work's row says what the sources carried when it was checked,
up to a day ago for a found work and a week for one not found; the Search page asks again. A search
by a title the tracker spells otherwise finds nothing and is tried again in a week. The feed sees a
source's latest 90 releases every 3 hours, so a release that passes through faster is missed. A
source added later is searched for a work only when the work is next due.

## Anti-consequence

The budget limits only what the app asks by itself: a person's own search on the Search page counts
against nothing and is never refused for it. A work found is not downloaded, picked or queued: the
card says what the sources have, and **Releases** opens the Search page
([ADR 0023](0023-a-recommendation-only-proposes-explains-every-reason-and-never-sends.md)).

## The catalog's title in its language (added 0.5.0, MAM-46)

**Nothing here changes but the language of the catalog's title.** The TMDb integration's catalog
language is the owner's setting now
([ADR 0025](0025-the-catalog-is-kept-apart-from-the-library.md)'s MAM-46 section), `uk-UA` for every
integration stored before 0.5.0. So the Decision's "else the catalog's Ukrainian title" reads "else
the catalog's title, in the catalog's language": `RecommendationQueryTitle` still sends the original
title when every letter of it is Latin, and the catalog's title otherwise, and a release still counts
by an id, or by its title and year matched against the work's titles, the original among them. With
a catalog language other than Ukrainian, Toloka's Ukrainian part of a name matches no catalog title,
so a match rests on the original part, which Toloka's names carry beside it; a work whose original
title is in another script is searched by its title in the catalog's language. "Resolved once"
gains one exception: a save that changes the catalog's language marks unresolved again the entries
first seen within the last 14 days that were resolved to no work, so the next pass resolves them in
the new language, through title lookups that are fresh only in the language they were asked in
(ADR 0025's MAM-46 section); an entry resolved to a work keeps it. The budget, the pacing and the
rows are unchanged. `RecommendationQueryTitleTests` is unchanged, and
`RecommendationAvailabilityHttpTests` runs as before on a catalog seeded in `uk-UA`;
`CatalogLanguageChangeHttpTests.Language_change_resolves_again_a_newest_release_the_old_language_matched_to_no_work`
and `IntegrationsCatalogLanguageHttpTests.Update_that_changes_the_language_has_the_newest_releases_matched_to_no_work_lately_resolved_again`
pin the exception.
