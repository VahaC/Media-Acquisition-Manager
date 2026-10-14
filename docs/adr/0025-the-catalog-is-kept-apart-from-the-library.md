# 25. The catalog is kept apart from the library

Status: accepted (0.2.1, MAM-33)

## Context

Recommendations are built from a metadata catalog, TMDb under Vasyl's own key (decision 1): a
work's details with its genres, collection, directors or creators, seasons and recommended works, a
collection's parts, a person's works, the genre lists, searches, finds by IMDb id, discovery and
posters ([ADR 0023](0023-a-recommendation-only-proposes-explains-every-reason-and-never-sends.md)). A
run needs the details of every work the library holds — the reference library's 640 TMDb movie ids
took 943 calls and about three minutes on 2026-10-05 — and the catalog's facts move slowly.

The obvious home for those facts is the library's own works, and it goes wrong. The library is
what Jellyfin reported, each title and id a row naming its source
([ADR 0006](0006-every-title-identifier-and-file-the-core-stores-names-its-source.md)), replaced by
the next sync ([ADR 0008](0008-a-library-sync-replaces-its-sources-snapshot-only-after-reading-all-of-it.md)).
A TMDb id the app found for "120. Landing Stripling" by title and year is a guess: written as a
work's identifier, the Library page would show a match nobody made, a search would match releases by
it ([ADR 0012](0012-a-search-or-a-release-matches-a-library-work-by-identifier-then-title-and-year-then-title-alone-and-every-match-is-listed.md)),
and the guess would sit beside Jellyfin's word as if it were a fact. The catalog also holds thousands
of works the library doesn't have, which are no library works at all. Asked live on every page, the
catalog would make each page wait on the internet, and a run would ask it the same hundreds of
questions every time. And a poster shown straight from TMDb's image host would need the
Content-Security-Policy opened to it, and would tell TMDb which works each viewer looks at.

## Decision

**What the catalog said is kept in tables of its own, each parent row naming the metadata integration
as its source, read again after 30 days; nothing from them is ever written to the library's works,
titles or identifiers or shown on the Library page; and posters are cached on the app's own disk and
served by the app to signed-in users only.**

**Tables of their own.** The migration `AddRecommendations` creates 19 tables; eleven are the
catalog. `CatalogWorks` — one row per film or show, unique by kind and catalog id, as TMDb numbers
the two apart (ADR 0017): its title in Ukrainian and its original title, original language, release
date, rating with its vote count, poster path, IMDb id, collection, and when its details were read
(`DetailsUtc`, null for a work known from lists only) — with its genres, its directors or creators,
its seasons (number, air date, episode count) and its recommended works, the last named by kind and
catalog id with no key. `CatalogGenres` (each kind's list with the role a genre plays: Animation,
Documentary or another), `CatalogCollections` with their parts, `CatalogPeople` with their directed
films and created shows, and `CatalogLookups`, the cached answers to "which catalog work is this" by
a key such as `Movie|imdb|tt0071562` or `Series|title|YELLOWSTONE|2018`: the work, and the evidence it
matched by, or nothing. The other eight are the recommendations' own: runs, candidates, reasons, the
availability per work, the budget's searches, dismissals, each user's settings and the newest
releases of the sources ([ADR 0024](0024-release-sources-are-asked-for-recommendations-within-a-per-user-budget.md)).

**Every parent row names its source (ADR 0006).** Works, genres, collections, people and lookups are
`ProvenancedEntity` rows of source kind `Metadata`: the integration (set to null when it is removed),
its name copied, the catalog's own id of the record — `movie/118340`, `tv/73586`,
`collection/284433`, `person/24`, `genre/movie/16`, or the lookup's key — and when it last reported
it. A child row is part of its parent's report, replaced with it and deleted with it, and carries no
provenance of its own.

**Keyed by the catalog's ids, whichever integration reported them.** One catalog type exists, and
its ids don't change with the token it is reached by, so a TMDb integration removed and added again,
or a second one, finds the same rows.

**Read through, refreshed after 30 days** (`CatalogStore.RefreshAge`). The genres, a work's
details, collections, people and lookups a run needs go through `CatalogStore`: rows younger than 30
days answer — a work's details by `DetailsUtc`, everything else by `ObservedUtc`, a genre list as old
as its oldest genre — and an older or missing one is asked again and stored. A list that names a work
refreshes its summary; only details refresh what details alone say. A lookup is cached whatever it
found, nothing included, so a title the catalog doesn't have isn't asked about again for 30 days; one
whose work's details then answer "not found" is forgotten. Two calls are made on every run all the
same: the connection test that gates the run, asked outside `CatalogStore`, and the TopRated
fallback's discovery pages — `DiscoverPages` per wanted kind, for each tab short of seeds — of which
only the works' summaries are stored, never the page.

**The library is only read.** A run reads the library's works, titles, ids, episode positions and
the names the folder scan found without tracking them, and writes no library table. A possible match
— a library work without a catalog id found by its IMDb id, or by the one title-and-year result —
seeds recommendations and keeps works out of them, and nothing more: it is never written to
`MediaWorkIdentifiers`, never shown on the Library page or among a search's matches, and never used
to match a release.

**Posters through the app.** A recommendation's `posterUrl` is `/api/recommendations/posters/<file>`.
The endpoint takes a signed-in user and only a poster file name (`RecommendationPosterNames`:
letters, digits, `_` and `-`, then `.jpg`, `.jpeg` or `.png`, so no path can leave the folder) that
some catalog work's poster path names, so it is no proxy to the image host. It answers from
`<data>/posters/`, a catalog connected or not, and on a miss fetches the file once through
`IMetadataCatalog.GetPosterAsync` — from TMDb's image host at `https://image.tmdb.org/t/p/w342`, with
no credential, keeping only a JPEG or a PNG — and writes it there whole, through a temporary file;
the folder is never pruned. A name that is no poster's, one no catalog work has, one the image host
doesn't have, and one not kept while no catalog is connected are a bare 404. A fetch that fails is
502 with the catalog's sentence, masked of the token, for the owner, and "The metadata catalog
couldn't send this poster." for everyone else, as the sentence can quote the owner's address. The
answer is `image/jpeg` or `image/png` by the extension, cached by the browser
(`Cache-Control: private, max-age=2592000, immutable`). The web app fetches it with the user's token
and shows it as a `data:` URL, so the Content-Security-Policy's `img-src 'self' data:` is unchanged,
no endpoint is anonymous, and the browser never contacts TMDb.

| Rejected option | Why not |
| --- | --- |
| Write the resolved TMDb ids onto the library's works | A guess becomes a library fact the Library page shows and the search matches by, beside Jellyfin's word |
| Ask the catalog live for every page and run | Hundreds of calls a run, and every page waits on the internet |
| Keep the catalog in memory only | Lost at every restart, and asked again in full |
| A cache per integration | Two tokens for one catalog would store every work twice |
| No cache for a lookup that found nothing | Every run would ask again about every library work TMDb can't name |
| Provenance on every child row | A work's genres or seasons are one report, written and replaced with it |
| `<img src>` pointing at TMDb's image host | The CSP opened to another host, and TMDb told which works each viewer looks at |
| An anonymous poster endpoint | An endpoint that answers without a session, for an `<img>` that can't send the token |
| Poster bytes in the database | `mam.db` and every backup of it grow by tens of kilobytes a work |

`RecommendationCatalogCacheHttpTests.Recompute_stores_the_catalogs_rows_apart_from_the_library_with_their_provenance`,
`RecommendationCatalogCacheHttpTests.Recompute_never_writes_the_librarys_works_titles_or_identifiers`,
`RecommendationCatalogCacheHttpTests.Recompute_reads_the_catalog_again_only_once_its_rows_are_older_than_30_days`,
`RecommendationCatalogCacheHttpTests.Recompute_looks_an_id_less_work_up_by_its_imdb_id_once_and_caches_the_answer`,
`RecommendationCatalogCacheHttpTests.Recompute_uses_no_title_two_works_of_the_year_share_and_caches_that_as_nothing`
and `RecommendationCatalogCacheHttpTests.Recompute_skips_a_seed_the_catalog_no_longer_has_and_forgets_the_lookup_that_named_it`
pin the catalog; `TmdbAdapterPostersTests.GetPoster_fetches_the_w342_poster_from_tmdbs_image_host_without_the_token`,
`TmdbAdapterPostersTests.GetPoster_of_anything_but_a_jpeg_or_a_png_is_an_unexpected_response`,
`TmdbAdapterPostersTests.GetPoster_answered_with_a_redirect_follows_none` and
`TmdbAdapterPostersTests.GetPoster_with_a_path_tmdb_never_gives_throws_without_sending_anything` the
poster fetch, and `RecommendationPostersHttpTests.Poster_first_asked_for_is_fetched_from_the_image_host_without_the_token_and_kept`,
`RecommendationPostersHttpTests.Poster_asked_for_again_is_read_from_disk_without_asking_the_image_host`,
`RecommendationPostersHttpTests.Poster_kept_on_disk_is_served_with_no_catalog_connected`,
`RecommendationPostersHttpTests.Poster_no_catalog_work_has_is_a_bare_404_and_asks_nothing`,
`RecommendationPostersHttpTests.Poster_with_a_name_that_is_no_poster_file_name_is_a_bare_404_even_when_a_work_has_it`,
`RecommendationPostersHttpTests.Poster_whose_fetch_fails_is_502_with_a_plain_sentence_for_anyone_but_the_owner`
and `RecommendationPostersHttpTests.Poster_without_a_token_is_401_and_asks_nothing` what the endpoint
serves and to whom.

## Consequences

**A run of an unchanged library asks almost nothing.** After the first run, the details of every
seed, its collections and people and the lookups come from the rows for 30 days; the reference
library's first run is a few minutes of calls (3 min 15 s for 815 seeds on 2026-10-05), the next
ones a handful — the connection test and the discovery pages of the tabs short of seeds. A new
season shows within a month.

**Known limits, left on purpose.** The catalog's rows stay when the TMDb integration is removed, with
its name (the MAM-7 rule), until a catalog is connected again and refreshes them; nothing prunes a
work no run names any more. A possible match the catalog got wrong keeps seeding the wrong work's
neighbours for 30 days. The posters directory is a cache nothing prunes: it grows by a few dozen
kilobytes a poster shown, can be deleted, and is fetched again as cards are shown, while a catalog
is connected. The catalog is TMDb's word, in Ukrainian where TMDb has a translation: a title TMDb
spells otherwise than the library is a different title to the matching (ADR 0012).

## Anti-consequence

This keeps the catalog out of the library, not the library out of the catalog's reach: a run reads
the library whole, every time. It doesn't make the catalog an authority — a catalog id is never a
reason to replace, move or rename anything (specification §3.1) — and it doesn't stop a person from
setting an id in Jellyfin, which the next sync then imports as the library's own.

## The owner's links (added 0.2.1, MAM-34)

**The rule keeps the app's guesses out, not the owner's word.** The Decision's "nothing from them is
ever written to the library's works, titles or identifiers or shown on the Library page" holds for
whatever the app finds by itself: a possible match is still never shown or written. A TMDb film or
show the owner picks for a work is no guess; it is the person setting an id in Jellyfin that the
Anti-consequence leaves open, done from the app
([ADR 0026](0026-a-tmdb-id-set-by-hand-is-the-owners-word-kept-per-library-item-and-written-into-the-library-as-ids-only.md)).
Even then nothing is written to `MediaWorks`, `MediaWorkTitles` or `MediaWorkIdentifiers`: the link
is a row of its own, written into Jellyfin as ids alone when it is a film's — a series' stays in the
app —, and the next sync imports what Jellyfin then holds. The rejected option "Write the resolved TMDb ids onto the library's works" holds.

**The catalog shows to the owner when the owner links.** The Library page's TMDb dialog and the TMDb
page — both the owner's alone — show each candidate's poster, title, year, overview and runtime. A
card shows its link, the id with TMDb's title and the write state, and none of the catalog's other
data.

**More of the catalog is kept.** `CatalogWorks` gains a runtime, an overview (2,000 characters), the
other titles TMDb knows a work by (`CatalogWorkAlternativeTitles`, at most 50, cleaned and distinct)
and a show's IMDb id, and `DetailsVersion`: a work's details are fresh only when younger than 30 days
and read by this version's reading (`CatalogStore.CurrentDetailsVersion`, 1), so every work stored
before 0.2.1 is read again the next time a run or a suggestion needs it, with no rebuild and no SQL
written by hand. A search or a find the owner's suggestions, search or pasted link make stores each
work it lists as a summary — so its poster is served by the Decision's rule, being some catalog
work's — but caches no answer; the suggestions keep what they searched in tables of their own
(`LibraryLinkSearches`, `LibraryLinkCandidates`: a work's queries, when, and its candidates by kind
and catalog id, with no key into the catalog), going with their work and searched again after 30
days or when the work's titles, folder or year change.

`CatalogStoreTests.GetWork_stores_a_shows_runtime_overview_alternative_titles_and_imdb_id_and_serves_them_back_from_the_rows`,
`CatalogStoreTests.Search_stores_every_work_it_lists_so_their_posters_are_served_and_answers_in_the_catalogs_order`,
`AddLibraryLinksMigrationTests.Upgrade_keeps_every_cached_catalog_work_with_its_details_marked_to_be_read_again`,
`RecommendationCatalogCacheHttpTests.Recompute_reads_again_within_30_days_the_details_a_reading_before_mam_34_stored`,
`RecommendationCatalogCacheHttpTests.Recompute_never_writes_the_librarys_works_titles_or_identifiers`
(no link row either), `LibraryLinkSuggestionRunnerHttpTests.Posters_of_the_candidates_are_served` and
`LibraryLinkSuggestionRunnerHttpTests.Run_searches_again_a_work_whose_search_is_older_than_a_month`
pin it.

**Amended 2026-10-06 (ADR 0026's Identify section, added in 0.2.1).** "Written into Jellyfin as ids alone
when it is a film's — a series' stays in the app —" had been stale since a series' link began to be
written, and is replaced: the owner's pick is identified in Jellyfin — its title, overview and images
become TMDb's, by Vasyl's decision —, and the next sync imports what Jellyfin then holds. The rule's
other half holds: the app's own guesses are never written and never shown, and nothing is written to
`MediaWorks`, `MediaWorkTitles` or `MediaWorkIdentifiers`.

## The library's own pictures (added 0.2.1, MAM-35)

**A Library card shows a picture, and it is never the catalog's.** The Library and Search pages' cards
now show each work's picture: the primary image Jellyfin holds for the item, read from Jellyfin by the
app ([ADR 0027](0027-a-library-works-poster-is-the-librarys-own-image-read-by-the-syncs-tag-and-served-by-the-app.md)).
No card takes the catalog's poster — never `CatalogWorks.PosterPath`, `<data>/posters/` or
`/api/recommendations/posters/` —, and the app never fetches a card's picture from TMDb. For an item
the owner identified
([ADR 0026](0026-a-tmdb-id-set-by-hand-is-the-owners-word-kept-per-library-item-and-written-into-the-library-as-ids-only.md)'s
Identify section, `replaceAllImages=true`) that image is the one Jellyfin itself took from TMDb: the
library's data, as the title it took is, which the Anti-consequence's "a person setting an id in
Jellyfin, which the next sync then imports as the library's own" already left open. The Decision's
"nothing from them is ever … shown on the Library page" holds.

**Two caches, kept apart.** The catalog's posters stay in `<data>/posters/`, named by
`RecommendationPosterNames` and served at `/api/recommendations/posters/`; the library's are in
`<data>/library-posters/`, named by `LibraryPosterNames` and served at `/api/library/posters/`
(`LibraryPosterUrls`, never `CatalogPosterUrls`). A name of one is never served by the other; only the
way a fetched picture is kept whole (`PosterFiles.KeepAsync`) and shown — through the API with the
user's token, as a `data:` URL — is shared.

`LibraryWorksPostersHttpTests.Works_as_a_member_carries_the_poster_url_of_each_movie_series_and_episode_of_no_series`,
`LibraryPostersHttpTests.Poster_first_asked_for_by_a_member_is_fetched_from_the_saved_jellyfin_with_the_key_in_the_header_and_kept`
and `RecommendationPostersHttpTests.Poster_first_asked_for_is_fetched_from_the_image_host_without_the_token_and_kept`,
unchanged, pin it.

## A work's description (added 0.4.0)

**The Recommendations page's Description shows TMDb's overview, asked for on the click, in the reader's
language, and kept apart from the details' own.** Vasyl's decisions of 2026-10-14: a card's
**Description** opens the work's overview; it is asked of the catalog when someone opens it, not with
the lists — every card has one at once, at the cost of a short wait the first time — and in English
for now, as not every user of an installation reads Ukrainian.

`CatalogWorks.Overview` can't serve it. It is part of the details, read in the catalog's language
(Ukrainian, `uk-UA`) for the works whose details a run or the owner's TMDb links read — 45 of 3,732
recommended works on Vasyl's installation that day —, and TMDb had a Ukrainian overview for 730 of the
1,296 works whose details were read. Filling it in English would change what the owner's suggestions
are ranked by. So a description has its own table, `CatalogWorkOverviews`: one row per work and
language, the text `null` when TMDb has none in it — that answer kept too, so a work with nothing to
say isn't asked about on every click —, removed with the work, read again once older than the
catalog's refresh age (30 days), and served however old while the catalog can't be asked.

**The language is the reader's, with English beneath it.** The endpoint takes the language (`en`,
`uk-UA`; English when none is given) and falls back to English when TMDb has none in it, saying in the
answer which language the text is in. The web app sends its UI language's code — `en` today — so a
UI that speaks Ukrainian or another language one day asks in it, and gets English where TMDb has
nothing in it, with no change here; the dialog marks the text with its language for screen readers.
Only a code of the shape TMDb takes reaches the request, and only a work the catalog has is asked
about, so the endpoint is no open proxy to TMDb.

`RecommendationOverviewsHttpTests` and `TmdbAdapterOverviewTests` pin it.
