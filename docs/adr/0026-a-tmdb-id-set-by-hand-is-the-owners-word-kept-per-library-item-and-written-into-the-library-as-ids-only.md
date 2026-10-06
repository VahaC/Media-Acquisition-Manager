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

## The ids, then the library's own lookup; an episode's own IMDb id; a series still kept (added 0.2.1, MAM-34)

The text above records MAM-34 as it first shipped, and stays as it was. Vasyl reversed two of its
decisions on 2026-10-06 — "ids only, no refresh" and "a series is never written" —; this section
records what replaced the first and why, and why the second still holds: the recording made the same
day showed the premise of writing series to be false, so series writes wait for Vasyl to decide again.

**Context.** Written alone, the ids changed nothing anyone could see in Jellyfin: Jellyfin fetches an
existing item's metadata only when it refreshes it. A Tom & Jerry film the app had linked held IMDb
`tt0035226` and TheMovieDb `40163` in Jellyfin on 2026-10-06 — the ids written, only the lookup
missing —, and the links the app kept in the app, every series' and every episode's, gave Jellyfin
nothing at all. Vasyl's live checks that day on Jellyfin 12.1.0: the documentary file "Greatest Events
of World War II in Colour.S01E01.Blitzkrieg", which Jellyfin lists as a movie in «Документалки» — a
library whose movie metadata fetchers are TheMovieDb and The Open Movie Database —, had its IMDb Id set
by hand to its episode's own IMDb id, `tt9103946` (TMDb `tv/91724`, season 1, episode 1); "Refresh
metadata → Search for missing metadata" then gave it an overview, a year, genres and a poster — The
Open Movie Database finds an episode by its IMDb id —, its Name kept as it was and its "TheMovieDb
Movie Id" left empty. On 2026-10-05 the same search on "120. Landing Stripling" had kept its ids and
its Name and filled Overview, Genres, People, Studios, Tags, OfficialRating and PremiereDate. Jellyfin's
source (v12.1, `ItemUpdateController.UpdateItem`, lines 315–356) still rewrites every season and
episode of a series it saves — `SeriesName`, `CustomRating`, and `OfficialRating` unless locked. Vasyl
accepted that for series on the word that the lookup asked after the save would fill the seasons and
episodes from TMDb; a live check he ran the same day recorded that it doesn't (below).

**Decision.** **After a write that leaves a link's ids in its item — confirmed by the read again, or
finding every edit already in place —, the app asks the library, once, to look up the item's missing
metadata with its own providers; and an episode's link on a film item, with its season and episode,
writes the episode's own IMDb id. A series' link stays in the app, as the text above decided, until
Vasyl decides again on the recorded facts.**

- **The lookup** is a new member of the capability, `IMediaLibraryIdentifierWriter.RequestMetadataLookupAsync`
  ([ADR 0005](0005-the-core-knows-a-service-only-through-a-registered-adapter-type.md)). Jellyfin's
  adapter sends one `POST /Items/{id}/Refresh?metadataRefreshMode=FullRefresh&imageRefreshMode=FullRefresh&replaceAllMetadata=false&replaceAllImages=false`
  — exactly what Jellyfin's own "Search for missing metadata" sends, which fills what is missing and
  replaces nothing —, never "Replace all metadata", and never again by itself; Jellyfin queues it and
  answers 204, a 404 is the item gone, a 401 the refused key, a 403 or 400 a refusal with its own
  sentence. When it was asked and why Jellyfin didn't take it are kept on the link (`LookupRequestedUtc`,
  `LookupError`, the owner's only), with an audit row per request (`LibraryMetadataLookupRequested`).
  A lookup Jellyfin doesn't take never changes the link's state — the ids are in —; **Retry** and
  **Write again**, for a work or one link, now also run on a link whose ids are in place, which sends no
  change and asks for the lookup again. Such a write that reaches nothing — Jellyfin switched off,
  unreachable or refusing — leaves a link whose written key the last sync read in its item `InLibrary`,
  the sentence as its `LookupError`: the ids are there as far as the app knows, as a sync would judge,
  and only the lookup was missed; any other link is `NotWritten` with the sentence, as before. A link
  kept in the app, or a write that isn't confirmed, asks for none. A link saved over another — replaced
  — starts with no lookup: the one asked for the link it replaces was by other ids, and a kept link
  would show one it never gets.
- **A series is still never written.** `WritableKinds` holds films only, a series stays a caller's
  mistake for the write and the lookup alike, and a series' link stays "Kept in the app" with the
  library's sentence, through a save, "Accept all confident", **Write again**, the background writer and
  the catch-up — as the text above decided. Vasyl accepted series writes on 2026-10-06 on the word that
  the lookup after the save would fill what it rewrites in the seasons and episodes; recorded the same
  day, it doesn't (below), and what the children held before a save is recorded nowhere and beyond any
  removal's reach. The choice is Vasyl's again, on those facts.
- **An episode's own IMDb id.** A link of a film item to an episode of a show, naming its season and
  episode, writes the IMDb id TMDb gives that episode (`IMetadataCatalog.GetEpisodeAsync`: `GET
  /3/tv/{id}/season/{s}/episode/{e}?append_to_response=external_ids&language=uk-UA`, its
  `external_ids.imdb_id`, kept on the link as `EpisodeImdbId`) where the item holds no IMDb id, and
  nothing else — never a TMDb id, which on a movie item names a film. Without both numbers ("Without a
  season and an episode the app has no id of this episode to write."), or when TMDb gives the episode no
  IMDb id ("TMDb knows no IMDb id of this episode, so its link stays in the app.", kept as the link's
  error, as only asking TMDb tells it), the link stays in the app; the same pick saved again asks TMDb
  again, and writes the id once TMDb gives one. An item holding another IMDb id keeps it, the link
  `NotWritten` with the sentence saying to change it in Jellyfin. An episode item no series holds keeps
  its link in the app as before. A film link the episode's link replaces is put back in the same write.
- **Each link has a written key** — a link to the work its TMDb id, an episode's link its episode's
  IMDb id — by which a sync judges it (`InLibrary`, `Lost`), the history knows what the app wrote, and
  a removal takes back only that. The film id an episode's link replaced keeps giving way whatever the
  link's state, as the write never touches it; once Jellyfin holds the episode's IMDb id, the sync reads
  it as the item's own, like any library id, and no reader adds an identifier row for the show (R8).
- **The catch-up, on the owner's click only.** The links set under the first rules are brought up to
  date by `POST /api/library/tmdb/catch-up` alone (202), counted first by `GET`: `toWrite`, the links
  that write ids of their own and Jellyfin doesn't hold yet — a numbered episode's on a film item kept
  in the app, and any link `NotWritten`, a catch-up's own failed write among them, so the next click
  takes it back —, and `toLookUp`, the written ones never looked up; attached links in libraries
  switched on, an episode TMDb already said has no IMDb id left out, one whose IMDb id is still to be
  asked counted only while a catalog is connected and switched on, and a series' link never. It queues
  them for the background writer, which writes each — asking TMDb for an episode's IMDb id when the
  link holds none, sending nothing where the ids are in place — and asks for each lookup, every link
  with its own audit rows. It writes over no id Jellyfin holds: a film link's ids are already its own
  when it is only looked up, and an episode's IMDb id goes only where the item has none. A sync leaves
  a queued link to the writer: marked "in Jellyfin" by the sync, it would never get the lookup it was
  queued for.

**What of the text above no longer holds.** The Decision's "ids and nothing else … no refresh"; the
write's "a `ShowEpisode` link is `KeptInApp`" (only an episode's that writes no id of its own, or an
episode item's, is now); the rejected option "a metadata refresh after the write" (the refresh that
replaces is still rejected; the search for missing metadata is now the decision); and the
Anti-consequence's "never runs … a refresh". Everything the text above says of a series — "every link
of a series item stays in the app", `WritableKinds` holding Jellyfin's films only, the rejected "write
a series when its seasons and episodes hold its age ratings", the known limit "a series' link never
reaches Jellyfin" and "never writes a series" — still holds, and the tests it names for it keep their
names.

| Rejected option | Why not |
| --- | --- |
| Write the ids and leave the lookup to the owner in Jellyfin | Nothing changed anyone could see; the owner would refresh every linked item by hand |
| "Replace all metadata", or Identify | Replaces the titles, images and people Jellyfin holds and the owner may have set; never tried live |
| Write a series and let the lookup fill its seasons and episodes (Vasyl's acceptance of 2026-10-06) | Recorded the same day: the save gives every season and episode the series' rating and the lookup gives none back; what they held before is recorded nowhere, so no removal could put it back. Held back until Vasyl decides again on these facts |
| Write a series only when its save would change none of its children (read them first, refuse otherwise) | Not built: the rule is Vasyl's to choose on the recorded facts; it would cost a read of every season and episode before each series' write |
| Write an episode's show id and numbers into the movie item | Jellyfin's `Tmdb` key on a movie names a film: the item would claim a film, and the lookup would fetch it |
| Overwrite an IMDb id the item holds with the episode's | The library's own word, as for a film (the text above); the owner changes it in Jellyfin |
| Count a lookup Jellyfin didn't take as a write that failed | The ids are in; "Not in Jellyfin yet" would offer to write again what is there |
| Turn a link Jellyfin holds into "Not in Jellyfin yet" when a write meant only for its lookup reaches nothing | The last sync read its ids there; the owner would see a link Jellyfin holds as missing, and the catch-up would count it as one to write |
| Keep the old link's lookup on the link that replaces it | It was asked by other ids: the dialog would say the new link was looked up, and the catch-up would never count it |
| Ask for a lookup again by itself | House rule 8: a lookup asked twice is one Jellyfin runs twice; **Retry** asks |
| Run the catch-up by itself after the upgrade | Nothing is written into the owner's library without his click |
| Count only the links kept in the app, not those not written | A catch-up whose writes failed would leave them in neither count, and the page would say there is nothing to update |
| Ask TMDb again, at every catch-up, for an episode it gave no IMDb id | It would keep the link in the app all the same and the count would never reach zero; the same pick saved again asks again |

`LibraryIdentifierWriterWritesIdsOnlyTests.The_one_name_beyond_ids_is_the_missing_metadata_lookup_and_it_takes_nothing_that_could_replace`,
`JellyfinAdapterMetadataLookupTests.RequestMetadataLookup_posts_search_for_missing_metadata_once_with_no_body_and_jellyfin_queues_it`,
`JellyfinAdapterMetadataLookupTests.RequestMetadataLookup_answers_each_other_refusal_with_its_status_and_sentence_and_sends_it_once`,
`JellyfinAdapterMetadataLookupTests.RequestMetadataLookup_refuses_a_callers_mistake_without_sending_anything`
(a series' among them),
`TmdbAdapterEpisodesTests.GetEpisode_reads_the_episodes_numbers_title_air_date_runtime_and_its_own_imdb_id`,
`LibraryLinkSetHttpTests.Set_of_a_film_saves_the_link_with_its_audit_row_and_writes_it_into_jellyfin_confirmed`,
`LibraryLinkLookupHttpTests.Set_whose_lookup_jellyfin_refuses_keeps_the_link_written_with_the_lookup_error_and_records_the_request`,
`LibraryLinkLookupHttpTests.Write_again_of_a_link_whose_lookup_failed_sends_no_change_asks_jellyfin_again_and_clears_the_error`,
`LibraryLinkLookupHttpTests.Replace_of_a_looked_up_link_shows_no_lookup_until_its_own_and_the_catch_up_counts_it_once_jellyfin_holds_it`,
`LibraryLinkLookupHttpTests.Replace_of_a_looked_up_film_link_with_an_episode_link_kept_in_the_app_shows_no_lookup`,
`LibraryLinkEpisodeHttpTests.Set_of_an_episode_on_a_film_item_writes_the_episodes_own_imdb_id_only_and_asks_jellyfin_to_look_the_item_up`,
`LibraryLinkEpisodeHttpTests.Set_of_an_episode_with_a_season_alone_keeps_the_link_in_the_app_says_why_and_asks_tmdb_for_no_episode`,
`LibraryLinkEpisodeHttpTests.Set_of_an_episode_tmdb_gives_no_imdb_id_keeps_the_link_in_the_app_says_why_and_sends_nothing`,
`LibraryLinkEpisodeHttpTests.Set_again_of_an_episode_tmdb_gave_no_imdb_id_once_it_gives_one_writes_it_and_asks_jellyfin_to_look_the_item_up`,
`LibraryLinkEpisodeHttpTests.Set_of_an_episode_whose_item_holds_another_imdb_id_leaves_it_and_is_not_written_with_the_sentence`,
`LibraryLinkEpisodeHttpTests.Resync_marks_a_written_episode_link_in_jellyfin_by_the_episodes_imdb_id_and_lost_once_jellyfin_drops_it`,
`LibraryLinkEpisodeHttpTests.Remove_of_a_written_episode_link_takes_back_only_the_imdb_id_the_app_wrote`,
`LibraryLinkEpisodeHttpTests.Replace_of_a_written_film_link_with_an_episode_link_puts_the_film_id_back_and_writes_the_episodes_imdb_id_in_one_write`,
`LinkedIdentifiersTests.Effective_of_an_episode_link_adds_nothing_and_drops_the_wrong_film_id_in_every_state`,
`LibraryLinkCatchUpHttpTests.CatchUp_counts_the_kept_and_not_written_links_that_write_ids_and_the_written_links_never_looked_up_and_nothing_else`
(a series' link among those it never counts),
`LibraryLinkCatchUpHttpTests.CatchUp_queues_them_and_the_writer_writes_each_fetches_the_episodes_imdb_id_and_asks_jellyfin_to_look_each_up`,
`LibraryLinkCatchUpHttpTests.CatchUp_jellyfin_cannot_take_leaves_the_links_it_holds_in_it_with_the_lookup_missed_and_counts_every_link_again`,
`LibraryLinkCatchUpHttpTests.CatchUp_counts_no_episode_link_whose_imdb_id_is_still_to_be_asked_while_no_catalog_is_connected`,
`LibraryLinkCatchUpHttpTests.CatchUp_of_an_episode_tmdb_gives_no_imdb_id_keeps_it_in_the_app_saying_why_and_counts_it_no_more`,
`LibraryLinkCatchUpHttpTests.CatchUp_left_waiting_through_a_sync_is_still_the_writers_and_its_lookup_still_asked`,
`LibraryLinkWriteWorkerHttpTests.Reconcile_turns_a_cut_episode_write_into_not_written_recording_it_only_once_the_episodes_imdb_id_was_known`
and `AddLibraryLinkLookupsMigrationTests.Upgrade_rebuilds_no_table` pin it; the text above's series tests
— `JellyfinAdapterIdentifiersTests.WriteIdentifiers_of_a_series_item_is_refused_with_its_reason_before_anything_is_sent`,
`LibraryLinkSetHttpTests.Set_of_a_show_on_a_series_keeps_the_link_in_the_app_says_why_and_calls_nothing`,
`LibraryLinkWriteAgainHttpTests.Write_of_a_series_link_is_409_as_jellyfin_rewrites_every_season_and_episode_of_a_series_it_saves`
and the rest — still pin that a series is never written.

**Consequences.** A linked item shows in Jellyfin what Jellyfin's providers know of it, filled in
Jellyfin's own time after the write; a documentary episode Jellyfin lists as a film gets its episode's
overview, year, genres and poster by its IMDb id, its name left as it is. A series' link still gives
Jellyfin nothing: what its item shows is Jellyfin's own match. The links set before are brought up to
date only by the owner's catch-up, one at a time in the background, and a link it couldn't write is
counted again for the next click.

**Recorded after the decision (Vasyl's live check of 2026-10-06; its answers are in VendorSamples:
`Jellyfin/item-refresh*-response.txt`, `Jellyfin/series-item-update-2026-10-06-response.txt`,
`Jellyfin/series-refresh-response.txt` and `Tmdb/tv-episode-*`).** The lookup answers 204 at once (queued), 404 for an
unknown item and 401 for a wrong key; TMDb gives the World War II episode its IMDb id `tt9103946` under
`external_ids` (its title TMDb's fallback "Серія 1", 50 minutes) and Planet Earth's "Джунглі"
`tt0905877`. The Penguins series' write answered 204 after 9.5 seconds and changed only its `Etag`;
its 3 seasons (none rated before) and 149 episodes (88 rated before) were all rated `TV-Y7` afterwards
— `UpdateItem` copies the series' official rating onto every child that hasn't locked it —, and the
lookup asked right after changed nothing on the series in 180 seconds and gave no rating back: it fills
only what is missing, and a rating copied from the series isn't. So the lookup does not undo what a
series' save rewrites. Whether any of the 88 episodes had another rating before is unknown, as the
check counted them; the read-only listing earlier that day had found none of the 152 seasons and
episodes rated and the series unrated, and what rated the series and 88 episodes in between was not
recorded. The decision to write series was made before this was recorded; this section holds it back.
The Penguins' seasons and episodes keep the series' rating that check's write gave them, and what the
two saves of MAM-34's live check of 2026-10-05 took from them is unknown: this change repairs none of
it.

**Known limits, left on purpose.** What the lookup finds, and when, is Jellyfin's: the app records that
Jellyfin took the request, never what came of it; the next sync reads it. An episode whose item holds
another IMDb id stays "Not in Jellyfin yet" until the owner changes that id in Jellyfin — and is
counted by every catch-up meanwhile, each click reading the item again and sending nothing. TMDb's "no
IMDb id" for an episode is kept on the link: should TMDb give one later, the owner saves the same pick
again (the catch-up and **Retry** don't ask again). A link whose write reached nothing is judged
"in Jellyfin" by the last sync's read of its item, which may be older than what Jellyfin holds now;
the next sync reads it again.

**Anti-consequence (amended).** The app still never links anything on its own, never runs Identify or
a refresh that replaces anything, never touches a title, an image, a lock, a file or a folder, never
writes a series, never sends an item's people, never changes what kind of item the library says
something is, never writes a TMDb id onto an item of the other kind, and never sends a write or a
lookup again whose fate it doesn't know.

**Decided again, 2026-10-06 — Vasyl's final word: a series is written like a film.** Told the facts
recorded above — the Penguins of Madagascar's save took 9.5 seconds, and afterwards all 3 seasons and
149 episodes carried the series' `TV-Y7` (88 of the episodes had a rating before, their values not
recorded), which the lookup asked right after didn't change back —, Vasyl decided that age ratings
don't matter to him. So Jellyfin's `WritableKinds` is `[Movie, Series]`. A series' link writes the
show's TMDb id under the item's `Tmdb` key — on a series item that key names a show, and a sync reads
it back as `TmdbTv` (ADR 0017) — and the IMDb id TMDb gives for the show only where the item holds
none, `People` left out of the body, exactly as a film's write; then the same "Search for missing
metadata" is asked for the series (`POST /Items/{id}/Refresh?metadataRefreshMode=FullRefresh&imageRefreshMode=FullRefresh&replaceAllMetadata=false&replaceAllImages=false`).
A save, **Retry** and **Write again**, "Accept all confident", the background writer and the catch-up
treat it as any link that writes ids of its own: the catch-up's `toWrite` counts and its click queues
a series' link the first rules kept in the app, and `toLookUp` a series' link written but never looked
up. A removal puts back the show's id the series held before and takes back the IMDb id the app added,
as for a film. The app asks for no confirmation and shows no warning about ratings anywhere; what
Jellyfin's save copies onto the seasons and episodes — the series' name, its custom rating, and its
official rating where a child hasn't locked it — is accepted, neither recorded nor undone. An episode
item still keeps its link in the app, with the library's sentence now "Jellyfin's ids are written on
movie and series items only, so a link on an episode stays in the app."; the sentence for a series is
gone. Everything else of this section stands: films, an episode's own IMDb id with the lookup, the
catch-up, and the links kept in the app — an episode item's, and an episode's without its numbers or
whose episode TMDb gives no IMDb id.

What no longer holds: of this section, "a series still kept" in its title, its Decision's last
sentence, the bullet "A series is still never written", the catch-up's "a series' link never", the
rejected options "Write a series and let the lookup fill its seasons and episodes" — now the decision,
without the lookup's promise — and "Write a series only when its save would change none of its
children", the Consequences' "A series' link still gives Jellyfin nothing", "this section holds it
back", and the amended Anti-consequence's "never writes a series"; of the text above, everything it
says of a series — "every link of a series item stays in the app", the rejected "write a series when
its seasons and episodes hold its age ratings", the known limit "a series' link never reaches
Jellyfin" and the Anti-consequence's "never writes a series". The tests named above for a series kept
in the app are replaced:
`JellyfinAdapterIdentifiersTests.WriteIdentifiers_of_a_series_posts_it_once_as_read_without_its_people_with_the_shows_id_under_tmdb_and_reads_it_again`,
`JellyfinAdapterIdentifiersTests.WriteIdentifiers_of_a_series_sets_its_imdb_id_only_where_it_holds_none_and_keeps_its_other_ids`,
`JellyfinAdapterIdentifiersTests.WritableKinds_are_films_and_series_and_an_episode_item_says_why_its_link_stays_in_the_app`,
`JellyfinAdapterMetadataLookupTests.RequestMetadataLookup_of_a_series_posts_the_same_search_once_and_jellyfin_queues_it`,
`LibraryLinkSetHttpTests.Set_of_a_show_on_a_series_writes_the_shows_ids_into_jellyfin_confirmed_asks_it_to_look_the_series_up_and_the_next_sync_finds_them`,
`LibraryLinkDialogHttpTests.Dialog_says_a_series_is_written_into_jellyfin_like_a_film_with_no_reason_against_it`,
`LibraryLinkReplaceHttpTests.Replace_of_a_written_series_link_writes_the_new_shows_id_over_the_one_the_app_wrote_and_swaps_the_imdb_id_it_added`,
`LibraryLinkRemoveHttpTests.Remove_of_a_written_series_link_puts_back_the_show_id_jellyfin_held_and_takes_back_the_imdb_id_the_app_added`,
`LibraryLinkRemoveHttpTests.Remove_of_a_series_link_set_before_series_were_written_sends_nothing_even_while_jellyfin_is_switched_off`,
`LibraryLinkWriteAgainHttpTests.Write_of_a_series_link_the_first_rules_kept_in_the_app_writes_it_into_jellyfin_and_asks_it_to_look_the_series_up`,
`LibraryLinkReviewHttpTests.Accept_all_of_a_confident_show_links_its_series_and_the_writer_writes_it_into_jellyfin_and_asks_it_to_look_it_up`,
`LibraryLinkWriteWorkerHttpTests.RunQueued_writes_a_queued_series_link_into_jellyfin_in_the_name_of_whoever_queued_it_and_asks_it_to_look_the_series_up`,
`LibraryLinkWriteWorkerHttpTests.Reconcile_turns_a_written_series_link_a_cut_removal_held_into_not_written_and_records_no_second_write`,
`LibraryLinkSyncHttpTests.Resync_judges_a_written_series_link_by_the_shows_id_jellyfin_reports_for_its_series`
and `LibraryLinkSyncHttpTests.Resync_leaves_a_series_link_the_first_rules_kept_in_the_app_to_the_catch_up_whatever_jellyfin_reports_and_lets_the_id_it_replaced_give_way`;
`LibraryLinkCatchUpHttpTests.CatchUp_counts_the_kept_and_not_written_links_that_write_ids_and_the_written_links_never_looked_up_and_nothing_else`
and `LibraryLinkCatchUpHttpTests.CatchUp_queues_them_and_the_writer_writes_each_fetches_the_episodes_imdb_id_and_asks_jellyfin_to_look_each_up`
now count, queue and write a series' link the first rules kept in the app.
