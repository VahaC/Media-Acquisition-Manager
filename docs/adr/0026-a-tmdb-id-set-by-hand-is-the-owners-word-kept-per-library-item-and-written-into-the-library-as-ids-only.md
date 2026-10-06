# 26. A TMDb id set by hand is the owner's word, kept per library item and written into the library as ids only

Status: accepted (0.2.1, MAM-34)

## Context

Much of the reference library has no registry id. 222 of its 870 movies have no TMDb id and 210 no
IMDb id; in the mixed libraries 168 of «Мульфільми»'s 194 movies lack a TMDb id and all 35 of
«Документалки»'s movie items lack one, among them the 25 Planet Earth films Jellyfin lists as
movies (MAM-17). Without an id a work groups with nothing, a release matches it by title and year at
best, the recommendations guess at it, and the Library page keeps it "missing an id" for ever. TMDb
knows most of them: a search by title and year found exactly one film for 23 of 25 Tom & Jerry
shorts (MAM-33), and "Planet Earth" names the show 1044 first, whose episodes the documentaries are.

Each road to the id runs into an earlier decision. The catalog is kept apart from the library, and
a guess of the app's is never written to it nor shown on the Library page
([ADR 0025](0025-the-catalog-is-kept-apart-from-the-library.md)), whose Anti-consequence leaves one
door open: a person setting an id in Jellyfin, which the next sync imports as the library's own. A
match is an identifier, a title and year, or a title alone, with no distance and no year ± 1
([ADR 0012](0012-a-search-or-a-release-matches-a-library-work-by-identifier-then-title-and-year-then-title-alone-and-every-match-is-listed.md)).
No file or folder name decides what an item is
([ADR 0017](0017-an-episode-is-a-work-known-by-registry-ids-and-the-library-alone-says-what-an-item-is.md)).
A sync rebuilds its library's works from what the library reported and uses no work holding another
source's fact ([ADR 0008](0008-a-library-sync-replaces-its-sources-snapshot-only-after-reading-all-of-it.md)),
so an id stored on a work would be swept away or would split it. And Jellyfin, checked live on
2026-10-05 (12.1.0), reads one item only as a user — `GET /Items/{id}` with the API key and no user
is 400 —, replaces an item with whatever `POST /Items/{id}` is sent, and answers 204; read again,
the item shows the new ids, and besides them only what Jellyfin derives from them changed (`Etag`,
`ExternalUrls`, and a movie's `UserData.Key`, which keys a user's watch data). A default refresh,
and "Search for missing metadata", kept the ids. Jellyfin's source — `UpdateItem` in
`Jellyfin.Api/Controllers/ItemUpdateController.cs`, read at tag v12.1 and the same on master — says
more that the live check couldn't see. Every `POST` of a series rewrites and saves each of its
seasons and episodes — their `SeriesName` and `CustomRating` always, their `OfficialRating` unless the
child locked it —, and each save moves the child's `DateLastSaved` and would rewrite its NFO file,
were there one (the reference installation's media folders hold none); the live series write took
4.5 s against 72 ms for a film. That check wrote and then removed the Penguins of Madagascar
series' TMDb id twice on 2026-10-05, before this was read: its 152 seasons and episodes may have lost
their age ratings then. Nobody read their ratings before; the check's read-only listing of 2026-10-06
found none of them rated, as the series isn't — what such a save leaves —, so whether any lost one is
unknown. Sent `People`, the update rebuilds the
item's people from their names, roles and types alone (`LibraryManager.UpdatePeople`); left out, the
cast stays as it is. It replaces the item's `ProviderIds` as a whole
(`ProviderIdsExtensions.SetProviderIds`), so Jellyfin itself drops any id it held that fails its own
format check. And an item a user may not see (a parental limit, a blocked tag, a library they can't
open, administrators included) answers that user's read with the same 404 as an item that is gone.

## Decision

**A library item's TMDb id is set only by the owner — a suggestion picked, a link pasted or a search
result chosen, or a list of confident suggestions accepted with one click —, kept as a link of that
item in a table of its own, counted by every reader as the library's own id would be, and written
into the library as the item's ids and nothing else: its TMDb id, and the IMDb id TMDb gives only
where the item has none (Vasyl's decision of 2026-10-05: ids only — no Identify, no refresh, no
images, titles or locks). A link of a film item, or of an episode of no series, to a TMDb show stays
in the app, and so does every link of a series item, as Jellyfin's save of a series rewrites all its
seasons and episodes (Vasyl's decision of 2026-10-06).**

**The owner's word is a source of its own.** `ProvenanceSourceKind` gains `Person` (6). A link is a
row of `LibraryItemLinks`, one per library item (`IX_LibraryItemLinks_OneLinkPerItem` on the
connection and the item id), not a fact on the work: its target (`Work` — the item is that film or
show — or `ShowEpisode`, with an optional season and episode), the scheme (`Tmdb`, or `TmdbTv` for a
show) and the value, TMDb's title, the IMDb id TMDb gave with TMDb as its source, who set it and
when, the names copied like an audit row's, the work its item sits on (`null` once the item is gone)
and the item's write state. Cards show the id "set by" the owner — the name to the owner alone —,
and `MediaWorkIdentifiers` never holds it.

**Re-applied at every sync, per item.** A sync groups each linked item as if it held the linked id
(`LinkedIdentifiers.Effective`), so an item linked to TMDb 223021 joins the work of another item of
the same library Jellyfin gave 223021, and two items one wrong id grouped split at the next sync once
one of them is linked; the library's rows are stored as Jellyfin reported them. The id a link replaces
(`SupersededValue`) gives way while the link isn't in Jellyfin yet; a third value Jellyfin gives
keeps the item alone and is shown as a conflict, neither id winning. An episode-to-show link with
its numbers groups by the show, season and episode, so eleven Planet Earth films linked to one show
stay eleven works. An item the library no longer reports leaves its link unattached, never deleted;
an item that came back under a new id takes the link back by its files — one device, inode and size
the link recorded —, decided for the whole sync at once, and a series by its episodes' files; an
item two links lead to, or one that holds a link, takes none.

**Written in five steps, once.** The link and its audit row (`LibraryLinkSet` or
`LibraryLinkReplaced`, carrying the ids the item held and the ids linked) are saved first, in one
transaction: from then on the link counts, whatever the library does. Then, when the library's type
implements the optional capability `IMediaLibraryIdentifierWriter`
([ADR 0005](0005-the-core-knows-a-service-only-through-a-registered-adapter-type.md)) and the
integration is switched on, the connection test gates the write (MAM-8), the Jellyfin adapter reads
`GET /Users` for the first enabled administrator, reads `GET /Items/{id}?userId=` as that user,
sends the item back as read with only `ProviderIds` changed and without its `People` — sent, Jellyfin
would rebuild the cast from less than it holds —, once and never again, and reads it again; the write
is recorded (`LibraryIdsWritten`, before and after) whenever it left the app, confirmed or not. The
link is then `Written` when the read again shows it and `NotWritten` with the sentence otherwise,
Retry sending it once more from a fresh read. A link of a show to a film item would write a TV id
under the movie's `Tmdb` key, so a `ShowEpisode` link is `KeptInApp`, as is every link of a library
type without the capability. A series is never sent: the capability names the kinds of item it
writes (`WritableKinds`, Jellyfin's films only) with the library's own sentence for the rest
(`UnwritableKindReason`), so the service, not a check of the product, keeps a series' link `KeptInApp`
— the dialog saying why, Retry and Write again having nothing to write, the background writer
settling a queued one without a call — and the adapter refuses a series before anything is sent. An
item the administrator can't see is told apart from one that is gone by the same user-less list
(`GET /Items?ids=`) and refused with its own sentence.

**The library says whether it holds a link.** A sync that reads the linked id marks the link
`InLibrary`, where it is the library's own as well; one that reads a link Jellyfin held without it
marks it `Lost` ("Jellyfin lost it", **Write again**). A link whose state changed after the sync
started reading is judged by the next one, and a link being written or put back (`Writing`) by no
sync at all: only the request or the worker that claimed it settles it, so a removal finds the link it
claimed and records what it sent back. A link kept in the app — a series', an episode's, any of a
library type without the capability — was never written, so no sync judges it: it stays `KeptInApp`
whatever Jellyfin reports, and the id it replaced keeps giving way.

**Removing puts back only what the app wrote.** Before a link goes, each id the app wrote is put
back to what Jellyfin held before the app's first write — only where Jellyfin still holds what the
app last wrote, so an id someone changed since stays — and the IMDb id only when the app added it;
`LibraryLinkRemoved` and `LibraryIdsRestored` are written with the deletion. What the app last wrote
is read per id from the writes that changed it: the read after a write also shows an id the write
left alone — an IMDb id someone set since —, which is never taken for the app's. A write a restart
cut may have landed: the reconciler records it at the next start as what it meant to leave, so a
removal or a replace starts from it whatever a sync or a Retry makes of the link meanwhile. A library
that can't be changed back keeps the link — an item the administrator can no longer see too —, and
the answer says why. Replacing a link builds its write from the same history. A link never written —
a series' — has nothing to put back: it goes even while its library is switched off, and a removal
of it that a restart cut leaves it `KeptInApp`, with no write recorded for it.

**Suggestions rank, and only the owner decides.** After each sync a background run searches TMDb for
every work without an id by its cleaned titles and its folder's name, keeping ten candidates a work.
Their evidence — how alike the titles are, the years apart, the runtime against the measured
duration, the library's genre, a linked neighbour's collection, director or show — orders what the
owner reads and is computed when read. That ordering uses a similarity and a year ± 1, which ADR
0012 forbids a match: no match comes of it, nothing is linked by it, and a search or a release never
sees it. **Confident** is ADR 0012's own rule plus a measurement: the one candidate of the right
kind whose title key equals one of the work's and whose year is the work's, the only such one, with
a runtime that agrees with a duration ffprobe measured. "Accept all confident" links only the works
the owner ticked, checks each against the suggestions computed again then, and leaves the writes to
a background writer, one link at a time, each with its own audit rows.

**The owner's alone.** Every endpoint under `Controllers/Library/Links` — reads included, as they
call the owner's catalog and show why the owner's library refused a write — is owner-only by where
it lives (`LibraryLinksAreOwnerOnlyConvention`).

| Rejected option | Why not |
| --- | --- |
| Jellyfin's Identify, or a metadata refresh after the write | Changes titles, images and people on the owner's library; Vasyl asked for the ids only |
| Store the link as a `MediaWorkIdentifiers` row | The next sync would delete it, or keep the work apart as another source's |
| Keep the link on the work | A work regroups at every sync; the item is what the owner pointed at, and two items of one work may be different films |
| Link confident suggestions without the owner's click | A guess written into the library, which ADR 0025 forbids |
| Write a show's id onto a film item | Jellyfin's `Tmdb` key on a movie names a film: the item would claim another film |
| Let a title similarity or a year ± 1 decide | A look-alike becomes the library's id (ADR 0012) |
| Overwrite an IMDb id the item holds | The library's own word; the app writes it only where there is none |
| Restore by writing back what was read before, whatever Jellyfin holds now | Would undo a change someone made in Jellyfin since |
| Retry a write whose answer was lost | It may have landed; the next sync shows what Jellyfin holds |
| Write a series when its seasons and episodes hold its age ratings (what MAM-34 first did) | Every save of a series rewrites and saves all its seasons and episodes — their series name and custom rating always, the official rating unless locked — whatever their ratings: far more than ids, recorded nowhere and beyond a removal's reach (Vasyl, 2026-10-06) |
| Send the item's `People` back as read | Jellyfin rebuilds the cast from the names, roles and types it is sent, dropping what else it keeps of them |
| Decide in the service that a Jellyfin series isn't written | A check of the product in the core's callers (ADR 0005); the library's capability says which kinds it writes and why not the rest |
| Take Jellyfin's 404 to an administrator's read as "the item is gone" | A parental limit hides an item the same way; a removal would delete its link and leave the app's ids in Jellyfin |

`LibraryLinkSyncHttpTests.Resync_keeps_a_link_on_its_item_and_its_work_and_stores_none_of_its_ids_as_jellyfins`,
`LibraryLinkSyncHttpTests.Resync_puts_an_item_linked_to_a_tmdb_id_on_the_work_of_the_item_jellyfin_gave_that_id`,
`LibraryLinkSyncHttpTests.Resync_keeps_an_item_jellyfin_gives_a_third_tmdb_id_on_a_work_of_its_own`,
`LibraryLinkSyncHttpTests.Resync_keeps_items_linked_to_other_episodes_of_one_show_apart_and_drops_the_film_id_jellyfin_gave_them`,
`LibraryLinkSyncHttpTests.Resync_marks_a_link_in_jellyfin_once_jellyfin_reports_its_id`,
`LibraryLinkSyncHttpTests.Resync_marks_a_link_jellyfin_held_lost_once_jellyfin_reports_it_no_more`,
`LibraryLinkSyncHttpTests.Resync_leaves_a_write_that_landed_after_jellyfin_was_read_as_written`,
`LibraryLinkSyncHttpTests.Resync_leaves_a_link_being_written_to_its_claimer_even_when_jellyfin_reports_its_id`,
`LibraryLinkSyncHttpTests.Resync_without_the_linked_item_keeps_its_link_unattached_with_its_files`,
`LibraryLinkSyncHttpTests.Sync_with_links_to_judge_reads_jellyfin_with_gets_only`,
`LibraryLinkReattachHttpTests.Inspection_after_jellyfin_recreated_a_linked_item_moves_the_link_to_the_new_item_and_its_work`,
`LibraryLinkReattachHttpTests.Inspection_gives_a_series_link_none_of_another_series_items_episodes_so_it_never_moves_onto_that_item`
and `LibraryLinkReattachHttpTests.Sync_and_inspection_leave_two_links_whose_files_ended_up_under_one_item_unattached`
pin the link at a sync;
`LibraryLinkSetHttpTests.Set_of_a_film_saves_the_link_with_its_audit_row_and_writes_it_into_jellyfin_confirmed`,
`LibraryLinkSetHttpTests.Set_of_a_show_on_a_film_item_is_an_episode_link_kept_in_the_app_that_replaces_the_wrong_film_id_and_calls_nothing`,
`LibraryLinkSetHttpTests.Set_keeps_an_imdb_id_jellyfin_holds_and_records_the_ids_it_found`,
`LibraryLinkSetHttpTests.Set_whose_write_jellyfin_takes_but_does_not_confirm_is_not_written_and_records_what_was_sent`,
`JellyfinAdapterIdentifiersTests.WriteIdentifiers_reads_the_users_then_the_item_as_the_administrator_posts_it_once_and_reads_it_again`,
`JellyfinAdapterIdentifiersTests.WriteIdentifiers_posts_the_movie_as_read_without_its_people_and_with_only_its_provider_ids_changed`,
`JellyfinAdapterIdentifiersTests.WriteIdentifiers_leaves_the_people_jellyfin_holds_out_of_the_body_so_it_keeps_its_cast`,
`JellyfinAdapterIdentifiersTests.WriteIdentifiers_writes_the_imdb_id_only_where_the_item_holds_none`,
`JellyfinAdapterIdentifiersTests.WriteIdentifiers_whose_answer_is_lost_is_sent_and_not_confirmed_and_never_sent_again`,
`JellyfinAdapterIdentifiersTests.WriteIdentifiers_of_a_series_item_is_refused_with_its_reason_before_anything_is_sent`,
`JellyfinAdapterIdentifiersTests.WritableKinds_are_films_only_and_every_other_kind_says_why_its_link_stays_in_the_app`,
`JellyfinAdapterIdentifiersTests.WriteIdentifiers_of_an_item_hidden_from_the_administrator_is_refused_and_posts_nothing`,
`AdapterKindContractTests.Every_identifier_writer_writes_only_films_or_shows_and_says_why_it_keeps_the_rest_in_the_app`
and `LibraryIdentifierWriterWritesIdsOnlyTests.No_identifier_writer_member_is_named_for_refreshing_identifying_images_titles_locks_or_deleting`
the write; `LibraryLinkSetHttpTests.Set_of_a_show_on_a_series_keeps_the_link_in_the_app_says_why_and_calls_nothing`,
`LibraryLinkReplaceHttpTests.Replace_of_a_series_link_keeps_it_in_the_app_takes_the_id_it_replaces_from_the_library_and_calls_nothing`,
`LibraryLinkReviewHttpTests.Accept_all_of_a_confident_show_links_its_series_kept_in_the_app_and_leaves_the_writer_nothing_to_send`,
`LibraryLinkWriteAgainHttpTests.Write_of_a_series_link_is_409_as_jellyfin_rewrites_every_season_and_episode_of_a_series_it_saves`,
`LibraryLinkWriteWorkerHttpTests.RunQueued_keeps_a_queued_series_link_in_the_app_and_sends_nothing`,
`LibraryLinkWriteWorkerHttpTests.Reconcile_gives_a_series_link_a_cut_removal_held_back_to_the_app_and_records_no_write_for_it`,
`LibraryLinkRemoveHttpTests.Remove_of_a_series_link_sends_nothing_even_while_jellyfin_is_switched_off_as_it_was_never_written`
and `LibraryLinkSyncHttpTests.Resync_keeps_a_series_link_in_the_app_whatever_jellyfin_reports_and_lets_the_id_it_replaced_give_way`
a series kept in the app; `LibraryLinkRemoveHttpTests.Remove_puts_back_the_ids_jellyfin_held_then_deletes_the_link_with_its_audit_rows`,
`LibraryLinkRemoveHttpTests.Remove_leaves_an_id_someone_changed_in_jellyfin_since_the_app_wrote_it`,
`LibraryLinkReplaceHttpTests.Replace_leaves_an_imdb_id_someone_set_after_the_app_added_its_own_and_so_does_a_removal_after_it`,
`LibraryLinkRemoveHttpTests.Remove_of_a_link_whose_cut_write_a_sync_then_found_in_jellyfin_still_puts_back_jellyfins_own_ids`
and `LibraryLinkRemoveHttpTests.Remove_that_jellyfin_refuses_keeps_the_link_with_the_sentence_and_is_502`
the removal; `LinkSuggestionRankingTests.Rank_makes_the_one_exact_title_of_the_year_with_an_agreeing_runtime_confident`,
`LinkSuggestionRankingTests.Rank_makes_nothing_confident_when_two_exact_titles_share_the_year`,
`LinkSuggestionRankingTests.Rank_makes_nothing_confident_a_year_off`,
`LinkSuggestionRankingTests.Rank_makes_nothing_confident_without_a_measured_duration`,
`LinkSuggestionRankingTests.Rank_never_makes_a_show_confident_for_a_film` and
`LibraryLinkReviewHttpTests.Accept_all_links_only_what_is_still_confident_writes_only_that_and_skips_the_rest_with_reasons`
the suggestions; and `LibraryLinksAreOwnerOnlyHttpTests.Every_tmdb_link_endpoint_as_a_signed_in_non_owner_is_403_with_the_owner_sentence`
the gate.

## Consequences

**A linked work behaves as if Jellyfin had the id.** It leaves the "Missing an id" filter and loses
its badges, groups with the library's other items of the id, is found by the id on the Library and
Search pages, matches releases by it and seeds the recommendations by it instead of a guess; once a
sync reads the id from Jellyfin, the link and the library agree and nothing more happens. The owner
sees each item's state on its card and in the dialog, and the TMDb page lists the links that need
him: in conflict or unattached; a link not in Jellyfin yet or lost is marked on its card and in the
dialog, with Retry or Write again. A film id Jellyfin gives an item after the owner linked it as an
episode of a show is a conflict too: the card and the Conflicts list show it, and the recommendations
seed by neither.

**Known limits, left on purpose.** "Replace all metadata" in Jellyfin was not tried live: whether
it keeps an id set by hand is unknown; should it drop one, the next sync shows the link `Lost`. Nor
was an item with watch history: Jellyfin keys a user's watch data by the item's ids
(`UserData.Key` moved from the item id to "223021" in the live write), so a played mark may not
follow the first write. Removing the Jellyfin integration removes its links (the audit rows stay)
and puts nothing back in Jellyfin. A link whose item is gone can be re-attached only by files the
inspection has read, so only inside the allowed media folders; without a duration measured there,
nothing is confident. An episode-to-show link never matches a release
([ADR 0020](0020-a-release-is-tv-by-its-category-matches-only-series-and-its-tracker-section-picks-the-destination-offered-first.md)),
as it gives the item none of the show's ids. A linked id groups items inside one library only;
joining two libraries' copies is MAM-20's. A series' link never reaches Jellyfin: it counts in the
app — grouping, matching, the recommendations — but Jellyfin, and whatever reads Jellyfin's ids,
never sees it. What the live check's two series writes did to the Penguins' 152 seasons and episodes
is unknown (see Context). Jellyfin itself drops, as it saves an item's ids, any id it held that fails
its own format check; the app sends every other id back as read. What Jellyfin does with a series'
children, an item's people and its ids was read in its source, not seen live. When one work holds
several series items of one library, which of them an episode belongs to is known only to a sync, so
their links get their episodes' file identities at the next sync rather than when they are set.

## Anti-consequence

This lets the owner tell the library what an item is, by its ids; it doesn't make the catalog an
authority. The app never links anything on its own, never runs Identify or a refresh, never touches
a title, an image, a lock, a file or a folder, never writes a series or an item's people, never
changes what kind of item the library says something is, and never sends a write again whose fate it
doesn't know. A suggestion stays evidence
for the owner to read (ADR 0012's Anti-consequence).
