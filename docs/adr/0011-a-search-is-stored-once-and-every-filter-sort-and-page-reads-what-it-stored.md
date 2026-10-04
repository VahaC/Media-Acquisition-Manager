# 11. A search is stored once, and every filter, sort and page reads what it stored

Status: accepted (0.1.0, MAM-12)

## Context

MAM-12's ticket asks for one page that shows the library's copies beside the available releases,
with "server-side filters and sort (source, language, size, quality)", where "a failed source shows
its own error while the others still render". MAM-10's `POST /api/releases/search` already asks
every enabled source at once and stores each release found (ADR 0006, MAM-10 section), but it
answers the whole list in one go, and nothing ties the rows it stored back to the search that found
them: a filter worked out on the server would have to ask the sources again.

Asking again on every change of a filter, a sort or a page is the obvious shape, and on the
reference installation it goes wrong in four ways:

- **Each ask costs the tracker.** Toloka through Prowlarr is asked `t=caps` and up to two searches,
  with the owner's forum account (ADR 0004), and Prowlarr pauses an indexer after failures and
  answers 429 for a while. Someone trying three filters would make three searches of the tracker.
- **The answer moves between two asks.** A new upload or a seeder count that changed between page 1
  and page 2 of two different answers shows a release twice or skips one, and a sort reshuffles
  under the reader.
- **Each ask is new rows with new ids.** Every search stores its releases as new rows (ADR 0006),
  so the release whose **Details** were open is not the one the next filter shows.
- **A reload waits for the slowest source again.** A source call may take 15 seconds after a
  5-second connect, and Torznab asks caps first; F5 or Back would cost it every time.

Filtering in the browser instead answers what the ticket rules out, and sends every release of
every source to every page view.

## Decision

**`POST /api/searches` asks the library and every enabled release source once, at the same time,
and stores the search, how each source answered and every release it listed; `GET /api/searches/{id}`
and `GET /api/searches/{id}/releases` only read what it stored, so no filter, sort, page or reload
asks a source again.**

**What is stored.** A `Searches` row: the caller, the title, year and ids as asked, trimmed, and the
instant its releases were stored (`CreatedUtc`). A `SearchSources` row per source the search
covered, in name order (`Position`): its status, why the app didn't ask it, and its failure's
sentence masked and cut to 1000 characters, with the integration's name copied, the audit-row
pattern, so the search still names the source after a rename or a removal (which sets the link
null). Every release is a MAM-10 row as before, linked to its search source (`SearchSourceId`) and
keeping what the list reads: its place in its source's answer, its seeders and peers, and the ids
its listing gives (ADR 0012).

**What is not stored.** The library's side, matched again on every read of the search (ADR 0012),
so a sync since shows and a work it removed is no longer offered. And what a release's title
claims — quality, languages, titles, year — read from the title again on every read, as MAM-10
does, so a better reading needs no migration. That is why the list is filtered, sorted and paged on
the server in memory rather than in SQL: a search holds at most 100 releases per source
(`ReleaseSearchPlanning.MaximumResults`), and the browser still gets one page. Every order is total
— a value the listing doesn't give last, then the source's place in the search, then the release's
place in its source's answer — so a page never reshuffles between two reads.

**One way to ask and store (Vasyl's decision 3).** MAM-10's `POST /api/releases/search` stays
exactly as it is, its request, its answer and its tests. Both searches go through one
implementation: `ReleaseSources` asks (the integrations read in name order, every source started at
once, the never-throws contract, the redaction and the log lines, moved and not rewritten) and
`ReleaseStore` stores in one transaction and prunes. A plain release search stores its rows without
a search source.

**At once.** The library and the sources are started together, and the search waits for the slower
of the two, not their sum (the ticket's "queried concurrently"). The library is read through a
database context of its own, from a scope of its own: a context is not safe to share between
concurrent calls, and the sources read their rows through the request's.

**A day, the lifetime of its releases.** A search lives as long as the releases it found:
`ReleaseStore.UnusedReleaseLifetime`, the 24 hours MAM-10 gives a release no download job uses. A
search older than that answers 404 at once, whether or not a prune has removed it yet, so it never
lists rows that any other search's prune may take away under it; one exactly a day old still
answers. Every search, stored or plain, prunes after it has stored: first MAM-10's releases older
than a day that no job points at, then the searches older than a day in one `ExecuteDelete`, their
sources going by cascade and a release a job keeps losing its link to them (SetNull) — it is the
job's evidence of what was sent and outlives its search. A step that fails is logged as a warning
and fails no search; the next one tries again.

**A search is its creator's.** Every signed-in user may search (MAM-10's choice), and a search is
theirs: asked for by anyone else, the installation owner included, it answers the same bare 404 as
one that never was or has expired, checked before any rule, so a wrong filter on someone else's
search is 404 too. Deleting a user deletes their searches (cascade). The failure's sentence stays the
owner's, as in MAM-10. Nothing here is owner-only, so the controller lives in `Controllers/Search`,
outside `Controllers/Settings` and `Controllers/Library`, whose writes are owner-only by convention.

| Rejected option | Why not |
| --- | --- |
| Ask the sources again on every filter, sort or page | Costs the tracker each time, the answer moves between pages, every ask has new release ids, a reload waits for the slowest source |
| Answer every release and filter in the browser | Not server-side, and every page view carries every source's whole answer |
| Store what each title claims in columns and filter in SQL | A better reading of titles would need a migration and a rewrite of stored rows, for at most 100 releases per source |
| Keep a search until its user removes it | Its releases go after a day anyway (ADR 0006), so an older search would list nothing, or keep every release of every search for good |
| Answer an expired search until a prune removes it | Any other user's search may prune its releases while it is being read |
| One user's search readable by every user, or by the owner | Tells others what someone searched for, and nobody asked for it |

`SearchReleasesListHttpTests.Releases_reads_what_the_search_stored_without_asking_a_source_again`,
`SearchesFailingSourcesHttpTests.Get_as_the_owner_shows_a_failed_sources_stored_sentence_without_asking_it_again`,
`SearchesCreateHttpTests.Create_and_a_get_of_the_search_answer_the_same_body` and
`SearchesLibraryMatchingHttpTests.Get_matches_the_library_again_as_it_is_now` pin the reads;
`SearchesCreateHttpTests.Create_sends_each_source_what_the_release_search_sends` and
`ReleaseSearchHttpTests.Search_stores_each_releases_place_seeders_peers_and_listed_ids_outside_any_stored_search`
the shared asking and storing; `SearchesConcurrencyHttpTests.Create_asks_the_library_and_every_source_at_once`
holds the library's first read until each source is asked, and
`SearchesConcurrencyHttpTests.Create_reads_the_library_on_a_context_of_its_own_while_the_sources_rows_are_read`
the library's own context;
`SearchesAccessHttpTests.Get_of_another_users_search_is_a_bare_404_even_for_the_installation_owner`,
`SearchesAccessHttpTests.Releases_of_another_users_search_is_a_bare_404_whatever_the_query` and
`SearchesAccessHttpTests.Deleting_a_user_removes_their_searches_and_their_sources` the ownership; and
`SearchesAccessHttpTests.Reads_of_a_search_a_day_old_still_answer`,
`SearchesAccessHttpTests.Reads_of_a_search_older_than_a_day_are_a_bare_404_before_any_prune`,
`SearchesAccessHttpTests.Create_removes_every_search_older_than_a_day_with_its_sources_and_releases`,
`SearchesAccessHttpTests.Create_keeps_a_release_a_download_job_sent_after_its_search_is_removed` and
`SearchesAccessHttpTests.Release_search_removes_the_searches_older_than_a_day_too` the lifetime.

## Consequences

The page keeps the search's id in its address (`/search?id=…`), so a reload, Back and a second tab
show the same search, and a filter, a sort or a page costs a read of the database, not of the
sources. What it shows is as old as the search: a new upload or a changed seeder count shows only
once you search again, and searching again is a new search with new rows. After a day the search is
gone, and the page says so ("This search is no longer available. Search again.").

Each search stores up to 100 releases per source for a day, so the database grows with how much
people search, not with the library. The release list's foreign key to its search source made
`AddStoredSearches` rebuild the `Releases` table on upgrade in three transactions, the concern that
kept ADR 0009 from a foreign key on `PhysicalFiles`; it is accepted here because the table holds a
day of search results, a few thousand rows at most.

Gaps left on purpose: there is no list of earlier searches and no refresh of one in place. The
facets (the audio languages and resolutions the filters offer) cover all of the search's releases,
not the ones the current filters leave. A well-formed source id that is not one of this search's,
such as one a page kept from an earlier search, lists nothing (200, no items) rather than failing,
the precedent of an id no library has. And if reading the library failed unexpectedly while the
sources were asked, the search answers 500 and stores nothing of what the sources answered: the
sources' own failures never do this, only a bug in the app.

## Anti-consequence

Stored is not shared or kept: a search is no history, no watch list and no cache for anyone else,
and two people searching for the same film ask the sources twice. Keeping a release for its search
still says nothing about downloading it; what a download sends, and how it recognises a release it
already sent, is MAM-13's (ADR 0006, MAM-10 section).

## Implementation (added 0.1.0, MAM-13)

**A release is sent only from its creator's search, within the search's day.** The Anti-consequence
left what a download sends to MAM-13. The send looks the release up as this record's reads do: one
of the caller's own searches' from the last day, through its search source. Another user's release,
an older search's, or one MAM-10's plain search found (it belongs to no search) answers 404 "This
release isn't available any more: a search keeps what it found for a day. Search again." on the
Download dialog's choices and on the send alike, so nothing confirms that it exists. A job is its
sender's in the same way: another user's answers a bare 404.

**The job keeps the release past the prune.** A job points at the release it sent, which the day's
prune then leaves (MAM-10's "that no download job points at"), and the search's removal sets the
release's link to it null: the release outlives its search as the job's evidence of what was sent,
as the Decision planned. Whether a release was already sent is decided across searches, by its
source's id for it or its info hash, never by the row (ADR 0006, MAM-13 section)
([ADR 0013](0013-a-download-is-written-down-before-anything-is-sent-and-one-request-sends-it-at-most-once.md)).

`DownloadsValidationHttpTests` sends another user's release, one of a search more than a day old and
one the plain search found, and gets the 404 with nothing written, fetched or sent.

## A search made from its address (added 0.1.0, MAM-16)

The Consequences above say the page keeps the search's id in its address. MAM-16's search field in
the header adds a second address: Enter there opens `/search?q=<the line>`, and the page makes that
search as it opens, then puts the search's id in place of the line, replacing the history entry
(`setSearchParams({ id }, { replace: true })`). Back from the search therefore goes to the page
before it, and F5 reads the stored search; neither asks a source again. A `?q=` beside an id is
ignored: the id names the search.

**Once per history entry.** The page makes a `?q=` search in an effect that runs once per history
entry (the location's key). React's StrictMode runs effects twice in development, and a second run
would be another search of every source — for Toloka, more searches with the owner's forum account
(Context, first point). An answer moves the page only while it is still on the entry the search was
made from: when the person searched again or went Back before it answered, the newest line wins,
and the search they left goes into the cache unshown. While the page stays open it remembers, per
`?q=` entry, the search made from it and how that ended, so Forward to the entry again waits for a
search still being made, or moves to the answer that came meanwhile (`replace` again, from the
cache), instead of asking a second time.

**What still asks.** An address with `?q=` and no id is a request to search. Opening it — a link, a
bookmark, F5 on a `?q=` whose search the server refused, Back or Forward to one the server refused,
or to one left for another page (the Search page opens afresh and knows of no search) — asks every
source, as any new search does. ProtectedRoute keeps the address through sign-in, so a `?q=` link
opened while signed out searches once the person has signed in. It searches for whoever opens it,
as every search is its creator's.

`SearchPage.test.tsx` pins it: "makes the search in ?q= once, though StrictMode runs the page's
effects twice", "goes Back from a search made from ?q= to the page before it, never to the ?q= that
would ask again", "moves to the answer of the newest ?q= only, when the person searched again
before the first answered", "moves to the answer that came while the person was Back when they go
Forward to its ?q=, without asking again" and "asks again for a ?q= the server refused when the
person goes Back and then Forward to it"; `App.test.tsx` "searches from the header field: …" does
it through the browser's history.

## TV and a season (added 0.2.0, MAM-32)

**A search stores its season and what each release is.** `Searches` gains `Season`, asked in a field
of its own (a number from 0 to 9999, 0 for the specials) and answered as `season`; with a season,
only the TV queries are sent. Each release keeps its `Kind`, its `CategoryIds` and its `TvdbId`, and
a TV release's `TmdbId` is a show's
([ADR 0020](0020-a-release-is-tv-by-its-category-matches-only-series-and-its-tracker-section-picks-the-destination-offered-first.md)).
MAM-10's `POST /api/releases/search` takes the season too and asks and stores through the same
code, as the Decision's "one way to ask and store" wants; its answer is unchanged and says no kind.

**What a title claims is still read, not stored.** The seasons, episodes and span of years a TV
title claims are read from it on every read, as its quality is, and each TV match's held count is
worked out from the library's episodes as they are now. The list's `kind` filter (`Movie` or `Tv`,
checked right after `source`) and `facets.kinds` read the stored `Kind`. A search now holds at most
100 releases of each kind per source, still filtered, sorted and paged in memory. The Download
dialog's suggested destination reads the release's stored categories and the sections mapped now,
and asks no source.

`SearchesTvHttpTests.Create_of_a_season_stores_and_echoes_it_and_asks_the_tv_search_alone`,
`SearchReleasesTvMatchingHttpTests.Releases_of_a_kind_lists_only_the_releases_of_that_kind`,
`SearchReleasesTvMatchingHttpTests.Create_offers_the_kinds_its_sources_listed_movie_before_tv` and
`SearchReleasesTvMatchingHttpTests.Releases_of_a_kind_of_someone_elses_search_is_a_bare_404_whatever_the_kind`
pin it.
