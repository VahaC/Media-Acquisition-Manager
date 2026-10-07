# 27. A library work's poster is the library's own image, read by the sync's tag and served by the app

Status: accepted (0.2.1, MAM-35)

## Context

Until MAM-35 the Library page's cards showed no picture, while the Recommendations page's show each
work's TMDb poster, cached and served by the app
([ADR 0025](0025-the-catalog-is-kept-apart-from-the-library.md)). Vasyl's decisions of 2026-10-06: a
Library card shows the library's own picture of the work — Jellyfin's **Primary** image of the item
—, at the recommendations' size, a 6rem box of a poster's 2:3 shape beside the title; a series card
shows the series' picture, the episodes of an opened season none, and an episode of no series its
own, or a placeholder; and the Search page's **In your library** cards show it too, as they show a
work exactly as the Library page does
([ADR 0006](0006-every-title-identifier-and-file-the-core-stores-names-its-source.md),
`SearchesLibraryMatchingHttpTests.Create_answers_a_works_card_exactly_as_the_library_page_shows_it`).

Each road to a picture runs into an earlier decision. TMDb's poster is the catalog's data, never
shown on the Library page (ADR 0025), and a work with no TMDb id would have none. Jellyfin's picture
of an item is the library's, as its title is; for an item the owner identified, Jellyfin took it
from TMDb itself, in the Identify the app asked for with `replaceAllImages=true`
([ADR 0026](0026-a-tmdb-id-set-by-hand-is-the-owners-word-kept-per-library-item-and-written-into-the-library-as-ids-only.md)'s
Identify section), and from then on it is the library's data. An `<img>` pointing at Jellyfin would
need the Content-Security-Policy's `img-src 'self' data:` opened to the address the owner saved on
the server — a LAN address or a container name the browser may not reach — and either an anonymous
route or the key in a URL
([ADR 0007](0007-an-integration-credential-is-sealed-to-its-row-and-never-leaves-through-the-api.md)).
And a request to Jellyfin per card per view, with nothing to say whether the picture changed, could
never be cached for good.

Vasyl's read-only checks of Jellyfin 12.1.0 on 2026-10-06 and 2026-10-07, the answers recorded in
`VendorSamples/Jellyfin` (`image-primary*-response.txt`, `*-page-with-images.json`), say what the
route does. `GET /Items/{id}/Images/Primary` answers without a key. Asked with
`tag=…&format=Jpg&maxWidth=342&quality=90` and `Accept: image/jpeg`, Yellowstone's poster came back
200 `image/jpeg`, 342x513, 34,972 bytes, `Cache-Control: public, max-age=31536000, immutable`;
without `quality=90` the same picture was 115 KB, and without `format=Jpg`, asked with
`Accept: image/webp,*/*`, Jellyfin chose WebP. The tag is not checked: a wrong one answered 200 with
the same image and echoed the asked tag as its `ETag`, so it only versions the URL. An item without
the image answers 404 with a JSON string ("Follow the Mouse does not have an image of type
Primary"), an id Jellyfin doesn't know 400 `text/plain` ("Error processing request."), and the
route doesn't redirect. The sync's list, asked with
`enableImages=true&enableImageTypes=Primary&imageTypeLimit=1`, gives each item its own
`ImageTags.Primary`; an episode also carries its series' and season's
(`SeriesPrimaryImageTag`, `ParentPrimaryImageItemId`, `ParentPrimaryImageTag`), and the virtual
special "Follow the Mouse" has `ImageTags` `{}`. Not every primary image is a poster: the
episode-movie "Blitzkrieg"'s is a 1920x1080 frame of the video since its Identify, the Tom & Jerry
Kids episode's is 342x237, and "120. Landing Stripling" answered a 342x256 frame on 2026-10-06,
before its Identify that day gave it a 1112x1668 poster. Read live on 2026-10-07, the items with a
primary image were 635 of the 650 movies of «Фільми»; all 194 movies, both series and all 284
episodes of «Мульфільми», its 29 virtual episodes left out; all 35 movies, the one series and the 16
episodes of «Документалки»; and all 8 series and 140 episodes of «Серіали».

## Decision

**A Library or Search card shows the primary image the library holds for the item the card stands
for, never the catalog's poster: the sync reads each item's own image tag and stores it as a fact of
the work, and the app fetches the image that tag names once, from the library at the address the
owner saved, as a JPEG at most 342 pixels wide, keeps it on its own disk and serves it to signed-in
users, so the browser never contacts the library.**

**The tag is a fact of the snapshot.** `LibraryItem.PosterTag` is the item's own image tag — the
Jellyfin adapter reads `ImageTags.Primary` alone, 1 to 64 letters and digits or nothing, none for an
item whose id isn't a Guid, the only id its poster call sends, and never a series' or season's tag
lent to an episode —, and `MediaWorkPosters` holds it: a `ProvenancedEntity` row per library item
with a picture, of source kind `MediaLibrary`, its `SourceItemId` the item whose image it is, on the
item's work (removed with it), with its `Tag` (64 characters at most). A sync stores it like the
item's other facts
([ADR 0008](0008-a-library-sync-replaces-its-sources-snapshot-only-after-reading-all-of-it.md)):
found again by its item, replaced in place when the library gives the image a new tag, deleted when
the image or the item goes, moved with the item's work, all in the one transaction, and purged with
the integration. The row holds the tag, never the picture, and alone neither makes a work nor keeps
one.

**Which picture a work shows.** A work may hold several items. `LibraryWorkDto.PosterUrl`
(`posterUrl`, after `link`) is the URL of the poster of the item whose displayed title the card
shows, else of the items of its other displayed titles in title order, then of its original titles'
items, then of any other by source name and item id (`LibraryMapper`): the picture of the item the
card is named after, the same on every read; `null` when no item has one. Every reader of works
carries it — the Library list, a season's episodes, the Search page's library block, the owner's TMDb
dialog and the confident list —, and the Library and Search cards show it. An episode inside an
opened season shows none, and a season has no picture, as no season item is read
([ADR 0019](0019-a-series-is-a-work-its-episodes-point-at-its-seasons-come-from-their-numbers-and-an-episode-without-a-file-is-left-out.md)).

**Served by the app.** The URL is `/api/library/posters/{itemId}-{tag}.jpg` (`LibraryPosterNames`:
two halves of 1 to 64 letters and digits, a dash and `.jpg`, so no path can leave the folder).
`LibraryController` serves it to every signed-in user — outside `Controllers/Library/Links`, as the
works are everyone's to read — and only for a pair some library's `MediaWorkPosters` row holds, so
it is no proxy to the library. It answers from `<data>/library-posters/`, whatever the library's
state now; on a miss it asks the library that reported the pair, a switched-on one first, then by
id, through `IMediaLibraryProvider.GetPosterAsync`. The Jellyfin adapter sends
`GET Items/{id:N}/Images/Primary?tag={tag}&format=Jpg&maxWidth=342&quality=90` with
`Accept: image/jpeg` and the key in the `Authorization: MediaBrowser Token=…` header, on the shared
`jellyfin` client and its 15 seconds, keeps only a non-empty `image/jpeg`, and takes 404 and 400 as
no poster. No connection test goes first: a row exists only from a sync that passed the version gate
(MAM-8), and a test per image would double the requests of a page of cards. The picture is kept
whole through a temporary file (`PosterFiles.KeepAsync`, MAM-33's keep, now shared) and never
pruned. A name that is no poster's, a pair no library reported — a removed library's too, as its
rows go with it —, a library switched off with nothing kept, a type that can't read a library, an
image the library doesn't have and an item it no longer knows are a bare 404; a fetch that fails is
502 with the library's sentence, masked of
the key, for the owner, and "The media library couldn't send this poster." for everyone else. The
answer is `image/jpeg`, cached by the browser (`Cache-Control: private, max-age=2592000, immutable`):
the tag in its name makes a new picture a new URL. The web app fetches it with the user's token —
`libraryApi.poster` refuses, before sending, a URL outside `/api/library/posters/` or one holding
`..` — and shows it as a `data:` URL, so the CSP stays `img-src 'self' data:`.

**A member of the provider, not a capability.** `GetPosterAsync(settings, sourceItemId, posterTag)`
answers a `LibraryPoster` or `NotFound`, and an id or a tag the library never gives throws before
anything is sent. Reading the library is what a provider does, so every library provider implements
it ([ADR 0005](0005-the-core-knows-a-service-only-through-a-registered-adapter-type.md)); it is
never a member of `IMediaLibraryItemIdentifier`, whose types
`LibraryItemIdentifierIdentifiesOnlyTests` keeps free of image words. Identify stays the app's only
change to Jellyfin's images (ADR 0026's Identify section); MAM-35 only reads them.

**The card.** `PosterBox`, the 6rem 2:3 box taken out of the recommendations' `CatalogPoster`, sits
beside the title block in a row of its own (`.library-work-lead`): the badges keep the header's right
edge and the versions, table and seasons the card's width, and on a phone the poster stays beside the
title with the badges under both. The picture is shown whole (`object-fit: contain`), so a frame
isn't cropped; a film or TV icon fills the box while it loads, when there is none and when it can't
be fetched or decoded. It is asked for once the box comes within about a screen of the window, under
a query key beside the works' (`['library', 'posters', url]`), not under them, so a sync, an
inspection, a link change or **Refresh all** asks for no picture again.

| Rejected option | Why not |
| --- | --- |
| TMDb's poster on the Library card | The catalog's data on the Library page (ADR 0025); a work with no TMDb id would have none, and the picture the library holds would be ignored |
| `<img src>` pointing at Jellyfin | The CSP opened to the saved address, which the browser may not reach, and the key in a URL or an anonymous route |
| An anonymous poster endpoint | An endpoint that answers without a session, for an `<img>` that can't send the token (ADR 0025) |
| Ask Jellyfin for an item's picture without the sync's tag | Nothing versions the URL: no lasting cache, a request per card per view, and a changed picture never noticed |
| Read an item's tag when its card is shown | One more request per card per view, for what the sync already reads of every item |
| A connection test before each fetch | Doubles the requests of a page of cards, for a library a sync that passed the gate reported |
| An episode's `SeriesPrimaryImageTag` or `ParentPrimaryImageTag` | A card would show another item's picture (Vasyl: an episode's own, or none) |
| Season pictures | No season item is read (ADR 0019) |
| An optional capability | Every library the app reads has pictures; a capability adds a lookup and a third outcome no type needs |
| A member of `IMediaLibraryItemIdentifier` | The capability that changes items names no image (`LibraryItemIdentifierIdentifiesOnlyTests`) |
| WebP, or Jellyfin's default quality | WebP comes only when the `Accept` header allows it; the default quality made a poster 115 KB instead of 35 |
| Crop the picture to fill the box | A 4:3 or 16:9 frame would lose its sides |
| Poster bytes in the database | `mam.db` and every backup of it grow by tens of kilobytes a work (ADR 0025) |
| The poster's query under the works' key | Every finished sync, inspection, link change, poll and **Refresh all** would fetch every visible poster again |

`JellyfinAdapterPostersTests.ListItems_reads_each_sampled_episodes_own_poster_tag_and_none_for_the_virtual_special`,
`JellyfinAdapterPostersTests.ListItems_reads_no_poster_tag_for_an_episode_without_its_own_image_whatever_its_series_has`,
`JellyfinAdapterPostersTests.ListItems_reads_no_poster_tag_the_app_could_not_keep_and_still_reads_the_item`,
`JellyfinAdapterPostersTests.ListItems_reads_no_poster_tag_for_an_item_whose_id_is_not_a_guid_and_still_reads_the_item`,
`JellyfinAdapterPostersTests.GetPoster_asks_for_the_items_primary_image_as_a_342_wide_jpeg_with_the_key_in_the_media_browser_header`,
`JellyfinAdapterPostersTests.GetPoster_of_anything_but_a_jpeg_is_an_unexpected_response`,
`JellyfinAdapterPostersTests.GetPoster_answered_with_the_recorded_404_or_400_is_not_found`,
`JellyfinAdapterPostersTests.GetPoster_answered_with_a_redirect_follows_none_and_masks_the_key`
and `JellyfinAdapterPostersTests.GetPoster_with_an_id_jellyfin_never_gives_throws_without_sending_anything`
pin the adapter;
`LibrarySnapshotPostersHttpTests.Sync_of_the_samples_with_images_stores_each_items_own_poster_tag_on_its_work_with_its_provenance`,
`LibrarySnapshotPostersHttpTests.Resync_after_the_library_gives_an_items_image_a_new_tag_replaces_its_poster_in_place`,
`LibrarySnapshotPostersHttpTests.Resync_after_an_items_image_is_gone_removes_its_poster_and_keeps_its_work`,
`LibrarySnapshotPostersHttpTests.Sync_of_an_item_with_nothing_but_a_poster_stores_no_work_and_no_poster_for_it`
and `LibrarySnapshotPostersHttpTests.Delete_of_the_library_integration_purges_its_posters_and_keeps_another_librarys`
the tags; `LibraryWorksPostersHttpTests.Works_as_a_member_carries_the_poster_url_of_each_movie_series_and_episode_of_no_series`,
`LibraryWorksPostersHttpTests.Works_of_a_work_of_two_items_shows_the_poster_of_the_item_whose_title_it_shows`,
`LibraryWorksPostersHttpTests.Works_of_a_work_whose_titled_item_has_no_poster_shows_the_next_items`,
`LibraryWorksPostersHttpTests.Works_of_a_work_whose_displayed_titles_item_has_no_poster_shows_the_original_titles_item_before_an_untitled_ones`
and `SearchesLibraryPostersHttpTests.Create_lists_a_library_work_with_the_poster_url_of_its_library_card`
the choice; `LibraryPostersHttpTests.Poster_without_a_token_is_401_and_asks_nothing`,
`LibraryPostersHttpTests.Poster_first_asked_for_by_a_member_is_fetched_from_the_saved_jellyfin_with_the_key_in_the_header_and_kept`,
`LibraryPostersHttpTests.Poster_asked_for_again_is_read_from_disk_without_asking_jellyfin`,
`LibraryPostersHttpTests.Poster_kept_on_disk_is_served_with_its_library_switched_off`,
`LibraryPostersHttpTests.Poster_of_a_tag_no_library_reported_is_a_bare_404_and_asks_nothing`,
`LibraryPostersHttpTests.Poster_with_a_name_that_is_no_poster_file_name_is_a_bare_404_even_when_a_row_holds_its_halves`
and `LibraryPostersHttpTests.Poster_whose_fetch_fails_is_502_with_a_plain_sentence_for_anyone_but_the_owner`
what the endpoint serves and to whom; and the front end's `LibraryPoster.test.tsx`,
`LibraryWorkCard.poster.test.tsx` and `library-api.test.ts` the card, the lazy fetch and the URL
guard.

## Consequences

**A card looks like the library.** What Jellyfin shows of an item is what its card shows, and a
picture changed in Jellyfin reaches the card with the next sync, as a new tag and so a new URL. The
sync's pages carry one tag more per item; a card's first view makes the server fetch about 35 KB
once, and every later view is the app's disk, or the browser's cache for 30 days. Every movie and
series of the reference installation's mixed and TV libraries has a picture, and 15 of «Фільми»'s 650
movies none.

**Known limits, left on purpose.** `<data>/library-posters/` is never pruned: an old tag's file stays
after the picture changes; the folder can be deleted, and is fetched again as cards are shown while
the library is switched on. A primary image that is a frame of the video (Blitzkrieg, the Tom &
Jerry Kids episode) is shown whole, with bands of the box's colour around it. A member's first view
of a card makes the server send the owner's Jellyfin key — in a header, to the saved address, for a
pair a sync stored —, which before only what the owner started did (ADR 0007). Posters appear only
after each library's first sync with this version (**Sync now**, or the owner's **Refresh all**). An
Identify (ADR 0026) gives an item a new picture, but no sync follows it and `LibraryIdentification`
may name no image, so the card keeps the old tag's URL until the next sync; as Jellyfin doesn't check
the tag, a first view in that window keeps the new picture under the old tag's file name, which is
harmless: the next sync moves the URL to the new tag. A switched-off library's pictures show only
where they were kept before, and a removed library's go with its rows, their files staying on disk
unserved.

## Anti-consequence

This reads the library's pictures; it doesn't let the app choose or change them. The app never
uploads, sets, deletes or refreshes an image in Jellyfin for a card — Identify, for a link the owner
saved, is still the only request that changes one (ADR 0026's Identify section) —, never shows the
catalog's posters on the Library or Search cards and never fetches a card's picture from TMDb (ADR
0025), never lets the browser talk to Jellyfin, and never reads a `poster.jpg` beside the media: the
picture comes from Jellyfin over HTTP — on the reference installation from Jellyfin's own
`/config/metadata/library` store —, and the scan of the media folders still leaves poster files alone
([ADR 0021](0021-the-scan-of-the-media-folders-follows-links-only-inside-the-allowed-roots-and-keeps-one-name-of-each-file-no-library-reported.md)).
