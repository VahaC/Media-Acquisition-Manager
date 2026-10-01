# 8. A library sync replaces its source's snapshot only after reading all of it

Status: accepted (0.1.0, MAM-8)

## Context

MAM-8 imports the Jellyfin library: its libraries, then every movie with its identifiers, media
sources and paths, which `/Items` hands out in pages (870 movies on the reference installation).
Writing each page as it arrives leaves half a library behind whenever Jellyfin stops answering,
the key is refused or the app restarts, and the next step — dropping what the sync didn't report —
would then delete the half it never read. Paging by offset has a second trap: a library scan that
adds or removes a movie meanwhile shifts every later item, so one is read twice and another never.

The rows must also outlive a sync. Later tickets point at works and versions (search results, jobs,
replacements), and a file upgraded in place comes back under a new Jellyfin item id with the same
TMDb id. Jellyfin 10.9 to 10.11 return merged versions as separate items that each list *every*
source of the group, and 12.1 does the same for a merge across libraries. The samples have two
"Чужі" items with one TMDb id (ADR 0006). The first design compared only the first and last page's
totals and keyed a version by its item; its review replaced both.

## Decision

**A sync reads the whole library first — its libraries, then every movie in pages that overlap by
one — and only then, in one transaction together with the run's outcome, replaces the rows that
integration reported as a media library; a failure, or any sign that the library changed while it
was read, stores nothing.**

**Read all, then replace.** The runner tests the connection (the version gate, ADR 0005), lists the
libraries, reads pages of 50 movies, and hands everything to `LibrarySnapshotStore.ApplyAsync`.
Inside one transaction it reads the integration again (removed meanwhile: nothing written), upserts,
deletes the rows no upsert reached, deletes the works left empty, and marks the run `Succeeded` with
its counts, committed together. Every terminal write — succeeded or failed, by the runner, the
worker or the reconciler — is a conditional update on a run still queued or running, never
cancelled. A shutdown before the commit stores nothing, and the reconciler fails the run at the next
start.

**Overlapping pages show a library that changes.** Each page after the first starts at the previous
page's last movie (start + 49), which must come back first; Jellyfin applies `startIndex` and
`limit` exactly, in `SortName, DateCreated` order. Every page's total must equal the first one's,
and a short or empty page before the end, a moved boundary movie, or fewer distinct movies than the
total fail the run with "…its library changed while it was being read. Nothing was changed here…".

**Works are grouped per source, by identifiers only.** Union-find over shared (scheme, value),
trimmed and in any letter case. A component with at most one value per scheme is one work.
Otherwise its **maximal** identifier sets (not a proper subset of another set in it) are each a
work; an item whose set is a proper subset of exactly one maximal set joins it, any other stays
alone. Items without identifiers, and an item with two values of one scheme, are alone; a title and
a year never merge. A sync only uses works that hold no fact of another source, so two libraries
with the same movie keep two works: joining them waits for a second library.

**A version is its media source.** Jellyfin's source id is unique per file on the server, so a
version is keyed by it within the connection. `LibraryVersionOwnership` gives each source id to one
item first — the item named after it, else the first item by id — so merged items give two
versions, not four. A version without a source id is keyed by its item and position.

**Ids are stable.** A library is found again by its id, a title by (item, kind), an identifier by
(item, scheme), a file by (version, path); a row found again keeps its id and `CreatedUtc`, and gets
the new `ObservedUtc` and the integration's current name. A row no key reached is deleted: a
dropped identifier, a cleared original title, a removed source, a moved file. A group keeps the
work its items were on, or one that held its identifiers without contradicting them (an upgraded
file); pairs are assigned by score, ties to the older work. Works left without a title, identifier
or version are deleted, and a group whose items report nothing gets no work.

| Rejected option | Why not |
| --- | --- |
| Write each page as it is read | A failure half-way leaves half a library, and dropping what wasn't reported deletes the rest |
| Compare the first and last totals only | One movie removed and another added keeps the total while items shift across a boundary |
| Delete and re-insert the snapshot each sync | New ids every time: what later tickets point at, and every first-seen time, would be lost |
| Key a version by its item | Merged items that each list every source give N² versions |
| Group by title and year, or merge a conflict on the majority's ids | Guesses; specification §3.1 matches on reliable identifiers, and a conflict blocks |
| Join works across libraries now | One library exists; how two that disagree are joined is decided when a second arrives |

The reading is pinned by
`LibrarySyncFailureHttpTests.Sync_whose_second_page_fails_stores_nothing_of_the_first`,
`LibrarySyncFailureHttpTests.Sync_of_a_library_that_shifts_between_two_pages_fails_with_the_library_changed_sentence`,
`LibrarySyncFailureHttpTests.Sync_whose_middle_page_comes_back_short_fails_with_the_library_changed_sentence`
and
`LibrarySyncRunHttpTests.Sync_of_a_library_of_several_pages_reads_them_overlapping_by_one_and_reports_progress_after_each`;
the grouping by
`LibraryWorkGroupingTests.Group_of_a_conflicting_component_makes_each_maximal_identifier_set_a_work`,
`LibrarySnapshotGroupingHttpTests.Sync_of_two_items_that_each_list_both_media_sources_stores_two_versions_not_four`
and `LibrarySnapshotGroupingHttpTests.Sync_of_two_libraries_with_the_same_movies_keeps_every_work_to_one_library`;
the stable ids by
`LibrarySnapshotHttpTests.Resync_keeps_every_id_and_first_seen_time_and_refreshes_last_seen_and_the_source_name`
and `LibrarySnapshotGroupingHttpTests.Resync_of_a_movie_under_a_new_item_id_with_the_same_identifiers_keeps_its_work`;
the restart by
`LibrarySyncWorkerHttpTests.RunQueued_stopped_by_a_shutdown_mid_sync_leaves_the_run_running_until_the_reconciler_fails_it`.

## Consequences

A failed sync leaves the Library as the last successful one stored it, and the page says when that
was. While Jellyfin scans, syncs fail and are started again afterwards. The whole library sits in
memory until it is written: a few megabytes for 870 movies. Not every change shows: a removal and
an addition that both sort before a boundary already read, between two page requests, leave the
total, every boundary and the count of different movies in place and go unnoticed. The run then
stores a stale row for the removed movie and none for the new one — as stale as a snapshot taken a
moment earlier — which the next sync corrects, and nothing real is deleted: a sync only writes this
app's rows.

## Anti-consequence

This rule is about one source's snapshot. It doesn't make a work with facts of one library the
work of every source: release search (MAM-10) and a second library match against works by the
identifiers of ADR 0006, under their own rules.
