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

## Implementation (added 0.1.0, MAM-11)

**A library's root folders join its snapshot, replaced rather than kept.** For the destination check
([ADR 0010](0010-a-destination-is-a-download-clients-own-category-never-a-folder.md)) the sync now
stores, with each library it upserts, the root folders Jellyfin listed for it
(`LibraryLocationFolders`: the library, the folder's place in Jellyfin's order, its path as Jellyfin
reports it), written in the same transaction as the rest, so a failed sync keeps the folders of the
last good one. Unlike every other row of the snapshot they are removed and added again on each sync,
with new ids each time, which the rejected "delete and re-insert" option above seems to forbid: that
rule protects the rows later tickets point at, and nothing points at a folder — it is nothing but its
place in its library's list, and its library row keeps the stable id and the provenance. A library
the sync no longer reports takes its folders with it, and so does the purge when the integration is
removed, both by the database's cascade (the purge's `ExecuteDelete` included). Which library holds a
file is still decided from the folders the sync has just read, never the stored ones.

`LibraryLocationFoldersHttpTests.Resync_replaces_every_librarys_folders_with_those_the_server_lists_now`,
`LibraryLocationFoldersHttpTests.Sync_that_fails_after_reading_the_libraries_keeps_the_folders_the_last_good_sync_stored`,
`LibraryLocationFoldersHttpTests.Delete_of_the_library_integration_leaves_none_of_its_folders_and_keeps_the_others`
and `LibraryPersistenceTests.Deleting_a_library_integration_with_its_libraries_leaves_no_folders` pin
it.

## Three lists (added 0.2.0, MAM-32)

**A sync reads the library's movies, series and episodes, each as the Decision reads the movies.**
Jellyfin's `/Items` lists one item type at a time, so the runner reads three lists
(`LibraryPageRequest.Kind`): first the first page of each, so the progress knows the whole library's
total from the third request on, then the rest of the movies, of the series and of the episodes.
Each list has its own total, its own overlapping pages and its own count of different items, and a
change in any of them fails the run with the Decision's sentences naming the list ("Jellyfin's
list of series shifted between two pages…"). Everything read goes into the one transaction as
before, so a sync stores all three or nothing. A virtual episode is read and counted, so the pages
add up, and stored as nothing
([ADR 0019](0019-a-series-is-a-work-its-episodes-point-at-its-seasons-come-from-their-numbers-and-an-episode-without-a-file-is-left-out.md)).

**Works are grouped per kind, and an episode's position is a key.** The grouping rule is unchanged
but runs on one kind at a time, series first, so a movie and a series sharing a TMDb number stay two
works. An episode's keys add, for each id of its series item, that id with its season and episode
numbers, so the halves of an episode are one work and an episode that carries another's ids under
other numbers is not. The assignment keeps a work across syncs by the same keys — the series work
and the numbers for a position — never moves an item onto a work of another kind, and a run counts
its series apart (`SeriesCount`); its works are its movies and episodes.

**Positions are rows like the others.** `MediaEpisodePositions` (one per library item: the episode
work, its series work, its numbers) is found again by its item, keeps its id and `CreatedUtc`, gets
the new `ObservedUtc`, and is deleted when no item reports it; the purge of a removed integration
takes them too. A series work that goes leaves its episodes' positions without a series.

`LibrarySnapshotTvHttpTests.Sync_whose_tv_list_changes_while_it_is_read_fails_and_stores_nothing`,
`LibrarySnapshotTvHttpTests.Sync_of_an_unchanged_tv_library_keeps_every_row_and_moves_only_the_last_seen_time`,
`LibrarySnapshotTvHttpTests.Sync_keeps_an_episodes_work_when_its_file_comes_back_under_a_new_item_at_its_position`
and `LibrarySnapshotTvHttpTests.Sync_puts_an_item_the_library_now_calls_an_episode_on_a_new_work`
pin it, and `IntegrationsDeleteHttpTests.Delete_of_a_media_library_purges_its_series_episodes_and_their_positions`
the purge.

## The owner's links (added 0.2.1, MAM-34)

**A link counts in the grouping and is never stored as the library's.** The owner's TMDb link of a
library item ([ADR 0026](0026-a-tmdb-id-set-by-hand-is-the-owners-word-kept-per-library-item-and-written-into-the-library-as-ids-only.md))
joins the Decision's grouping: a linked item is grouped with the ids it counts as holding — the
library's, less the id the link replaces while the link isn't in Jellyfin yet, plus the linked id,
and the IMDb id TMDb gave only where the library gives none — while the upserts store the item as
Jellyfin reported it. A third value of the scheme makes two values, so the item stays alone, as the
Decision says. An episode-to-show link with a season and an episode adds a key of the show and the
numbers, so the episodes of one show linked from film items stay apart. "A sync only uses works that
hold no fact of another source" holds: a link is no fact on a work.

**Inside the transaction.** The links of the integration's items are read in the sync's transaction
with their file identities, moved, judged and attached, and saved by its one `SaveChangesAsync`: a
sync that fails changes no link. The sync stays GET-only towards Jellyfin.

**A link follows its item.** A link is keyed by the integration and the item id and no sync deletes
it: its work is the one its item lands on, and none — unattached — when the item is gone. The
Decision's "Ids are stable" keys a file by its version and path, and Jellyfin gives a re-created item
a new id, so a link also records the device, inode and size the inspection read of its item's files
([ADR 0009](0009-the-app-reads-a-file-only-inside-the-allowed-roots-and-keeps-each-reading-by-the-files-identity.md)),
and an unattached link moves to the one item of its kind whose stored files hold one of them — a
series through its episodes' files —, decided for every link at once: an item two links lead to, or
one that holds a link, takes none. The same move runs after each file inspection; what is stored
there names an episode's series work, not its series item, so a series item sharing its work with
another series item of the library is given no episode's files then — only a sync, which reads each
episode's series item, gives a series link its own.

**The sync says whether the library holds the link.** A linked item read with the linked id marks
the link `InLibrary`; one written or in the library before and read without it now — no value, the
replaced one or a third — marks it `Lost`; anything else stays. A link whose state changed at or
after the moment the run started reading is left to the next sync, as its write may have landed after
the item was read, and a link being written or put back (`Writing`) to whoever claimed it, whenever
that was: its write may still be on its way, and only the claimer records what it sent. A link kept
in the app (`KeptInApp` — a series', never written as Jellyfin's save of a series rewrites all its
seasons and episodes, an episode's, or one of a library type that can't set ids) is never judged: it
stays so whatever the library reports, and the id it replaced keeps giving way in the grouping.

`LibraryLinkSyncHttpTests.Resync_puts_an_item_linked_to_a_tmdb_id_on_the_work_of_the_item_jellyfin_gave_that_id`,
`LibraryLinkSyncHttpTests.Resync_lets_the_id_a_link_replaced_give_way_while_the_link_is_not_in_jellyfin`,
`LibraryLinkSyncHttpTests.Resync_keeps_an_item_jellyfin_gives_a_third_tmdb_id_on_a_work_of_its_own`,
`LibraryLinkSyncHttpTests.Resync_keeps_items_linked_to_other_episodes_of_one_show_apart_and_drops_the_film_id_jellyfin_gave_them`,
`LibraryLinkSyncHttpTests.Resync_without_the_linked_item_keeps_its_link_unattached_with_its_files`,
`LibraryLinkSyncHttpTests.Resync_leaves_a_write_that_landed_after_jellyfin_was_read_as_written`,
`LibraryLinkSyncHttpTests.Resync_leaves_a_link_being_written_to_its_claimer_even_when_jellyfin_reports_its_id`,
`LibraryLinkSyncHttpTests.Resync_keeps_a_series_link_in_the_app_whatever_jellyfin_reports_and_lets_the_id_it_replaced_give_way`,
`LibraryLinkSyncHttpTests.Sync_that_another_hand_finished_first_changes_no_link`,
`LibraryLinkReattachHttpTests.Inspection_gives_a_series_link_none_of_another_series_items_episodes_so_it_never_moves_onto_that_item`,
`LibraryLinkReattachHttpTests.Sync_moves_a_link_to_the_item_whose_files_an_earlier_inspection_found_holding_its_identity`,
`LibraryLinkReattachHttpTests.Inspection_after_jellyfin_recreated_a_linked_series_moves_the_link_through_its_episodes_files`,
`LibraryLinkReattachHttpTests.Inspection_leaves_a_link_unattached_when_its_inode_came_back_with_another_size`
and `LinkReattachmentTests.Moves_takes_no_link_to_an_item_another_link_leads_to_whichever_comes_first`
pin it.

**Amended 2026-10-06 (ADR 0026's section added in 0.2.1).** A series' link is written like a film's
now, so a sync judges a written one by the show's id Jellyfin reports for the series
(`LibraryLinkSyncHttpTests.Resync_judges_a_written_series_link_by_the_shows_id_jellyfin_reports_for_its_series`);
one the first rules kept in the app stays `KeptInApp`, never judged, until the catch-up or **Write
again** writes it — the test named above for a series is now
`LibraryLinkSyncHttpTests.Resync_leaves_a_series_link_the_first_rules_kept_in_the_app_to_the_catch_up_whatever_jellyfin_reports_and_lets_the_id_it_replaced_give_way`.

**Amended 2026-10-06 (ADR 0026's Identify section, added in 0.2.1).** This replaces the wording of
the amendment above where it differs. The key a sync judges a link by is the one Identify leaves in the
item — `Tmdb` for a film, `TmdbTv` for a show, the episode's own `Imdb` for an episode-movie — and
a link is "put back" no more: Remove link deletes it and restores nothing. A `Writing` link is still left
to the worker that claimed it, and so is a `Queued` one. A link a worker found `NotWritten` is left
alone too: an item that holds the key is not thereby identified, so the refusal and its sentence stay
until the owner presses Retry or, when nothing was sent for it, chooses Update Jellyfin. A sync never sets `IdentifiedUtc` and never changes it, except to
clear it when it moves a link to a re-created item, which is a new item with its own file-derived
metadata; only a worker's confirmed re-read, or its finding the item already showing the match, sets
it. A link the first rules kept in the app stays `KeptInApp` until the catch-up or Retry identifies
it. The sync tests named above stay in force where they still describe the sync; the rules of this
paragraph are pinned by
`LibraryLinkSyncHttpTests.Resync_marks_a_link_in_jellyfin_once_jellyfin_reports_its_id`,
`LibraryLinkSyncHttpTests.Resync_never_changes_the_identified_time`,
`LibraryLinkReattachHttpTests.Sync_that_moves_an_identified_link_to_a_recreated_item_clears_its_identified_time` and
`LibraryLinkSyncHttpTests.Resync_leaves_a_not_written_link_with_its_sentence_even_when_the_item_holds_the_key`.
