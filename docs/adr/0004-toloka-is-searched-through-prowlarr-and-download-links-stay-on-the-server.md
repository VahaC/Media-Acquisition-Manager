# 4. Toloka is searched through Prowlarr, and download links stay on the server

Status: accepted (0.1.0, MAM-6)

## Context

Specification §2 proposed reaching Toloka "as a Torznab indexer through Jackett if that works" and
left question 5 open: which route works with the owner's account, and whether another container is
acceptable. Toloka has no API for programs; something has to sign in with the owner's account, keep
the session and read the forum's pages.

MAM-6 found that the reference installation already runs Prowlarr 2.6.5 next to Jellyfin and
qBittorrent, with the Toloka.to indexer (id 1) configured and enabled, and no Jackett. On
2026-09-29 the owner chose to keep Prowlarr for this rather than add Jackett. A live check through
Prowlarr's Torznab endpoint for that indexer, `GET /1/api?t=search&q=Interstellar`, answered 17
releases in 2.7 seconds; `t=caps` lists a movie search that takes only `q`.

The answers carry two credentials that the app must not spread:

- Every download link is Prowlarr's own, with its API key in the query:
  `http://192.168.1.195:9696/1/download?apikey=…&link=…&file=…`.
- A topic link (`<guid>`, `<comments>`) can carry the owner's Toloka forum session. Every topic
  link of the `t=search` answer had it (`https://toloka.to/t699400?sid=…`); the `t=movie` answer
  taken seconds later listed the same topics without it.

Specification §4 says never to put tracker cookies or keys "in plaintext logs or a URL exposed to
the browser".

## Decision

**The release search talks plain Torznab to Prowlarr's per-indexer endpoint (`/<id>/api`) and
holds no code specific to Prowlarr or Toloka; a release's download link is fetched by the server
when the release is sent to the client and never reaches the browser, and a topic link loses any
`sid` parameter before it is stored, logged or shown.**

**Why Prowlarr.** It already signs in to Toloka and keeps up with the site, and the owner keeps it.
The app stays a Torznab client, so Jackett or any other Torznab source should work the same way.

**Why download links stay behind.** The browser gets a release's id and its public details; the app
fetches the `.torrent` or magnet itself at submission (MAM-13).

**Why topic links are cleaned rather than hidden.** The specification wants a source-page link for
every result (§3.1). Prowlarr's own `t=movie` answer gives the same topic links without the
session, so a link without `sid` is the complete public link.

**Searching by text.** Toloka's movie search takes only `q`, so the app searches Toloka by title
(and year) and matches identifiers on its own side. A text search returns every section (the
Interstellar search also found an audio-only release), so results are filtered by category.

| Rejected option | Why not |
| --- | --- |
| Add a Jackett container, as the specification proposed | A second indexer manager beside the Prowlarr the owner already runs |
| A direct Toloka adapter in the app | The app would keep the owner's forum password and parse the site's pages, and break whenever they change; specification §2 allows it only on demonstrated need |
| Hand Prowlarr's download link to the browser | Gives every viewer Prowlarr's API key |

`VendorSamplesTests.Sample_carries_no_credential` keeps both values out of the samples the adapter
is tested against. The adapter's own tests (MAM-10, MAM-13) will pin the stripping, against both
Torznab samples since only one of them has `sid`, and the server-side fetch.

## Consequences

Search needs Prowlarr to be up; when it isn't, or Toloka fails behind it, the Toloka source shows
its own error while the other sources keep answering (the acceptance bar of Phase A). By Prowlarr's
source, a failed search makes it pause the indexer and answer `429` for a while; the app shows that
as the source's error too.

Prowlarr's API key is an integration credential like the others: stored encrypted (MAM-7), redacted
from vendor error text, never returned by the API.

## Note for future readers

The owner sees the app as the replacement for Prowlarr's search page and may later want to drop
Prowlarr. A direct Toloka adapter would then be a new search provider beside the Torznab one, which
the core's provider contract allows; nothing in this decision has to be undone for it.

## Implementation (added 0.1.0, MAM-7)

**The integration's address is the feed.** A Torznab integration stores the feed's address, and the
adapter uses it as entered when its last folder is `api` in any letter case (Prowlarr's per-indexer
`http://…:9696/1/api`), and appends `/api` otherwise (Jackett's copied `…/results/torznab/`): a
fixed rule about the address, not a guess about which product serves it. The adapter knows neither
Prowlarr nor Toloka. Its connection test asks for `t=caps`, reads a feed's own
`<error code description>` whatever HTTP status it came with (codes 100–102 are a refused key, 910
an API switched off), and names the movie-search parameters the feed offers: "Connected to Prowlarr
over Torznab; movie search by title." A 429 is the feed refusing requests for now, with the feed's
own description when it gave one. Prowlarr answers an indexer it paused, or one at its query limit,
with HTTP 429 and
`<error code="429" description="Indexer is disabled till … due to recent failures."/>`, so error
code 429, and any `<error>` that comes with HTTP 429, reads that way, as does an HTTP 429 without a
Torznab body.

**The key travels as `apikey` in the query**, where Torznab reads it. So the integration HTTP clients
have no logging handlers and never write a request URI, an error description that echoes the key is
masked, and the key itself is required, stored encrypted and never returned
([ADR 0007](0007-an-integration-credential-is-sealed-to-its-row-and-never-leaves-through-the-api.md)).

**The release is ready for the server-side fetch.** `ReleaseEntity.PayloadLocatorEncrypted` holds a
release's download link sealed like a credential, for its own release
(`release/{id:N}/payload-locator`), and `ReleasePayloadLocator` prints masked. Nothing writes a
release yet: stripping `sid` and fetching the link stay with MAM-10 and MAM-13, whose tests pin them
as said above.

`TorznabAdapterTests.EndpointFor_uses_an_address_ending_in_api_and_appends_api_otherwise`,
`TorznabAdapterTests.TestConnection_maps_each_torznab_error_code` (Prowlarr's 429 among them),
`TorznabAdapterTests.TestConnection_masks_an_api_key_the_error_description_echoes` and
`IntegrationsInfrastructureExtensionsTests.Every_integration_client_has_no_logging_or_retry_handler`
assert it.

## Implementation (added 0.1.0, MAM-10)

**The search asks what the feed can do, every time.** Each search reads the feed's `t=caps` first
(answered from the gateway's memory) and the core plans from it (`ReleaseSearchPlanning`): an IMDb or
TMDb id alone where the movie search takes one, then the title with the year, then the title alone,
each tier only when the one before found nothing. Toloka's movie search takes only `q`, so it is asked
`t=movie&q=Interstellar 2014` and, if that finds nothing, `q=Interstellar`; an id never reaches it.
Matching the releases found to a library work by its identifiers, which the Decision above puts "on
the app's own side", belongs with the search page that sets them side by side (MAM-12); MAM-10 reads
no identifier out of a listing.

**Movies only, by category.** The search sends the caps' movie categories (Torznab's 2000–2999: 2000,
2020, 2040, 2070 for Toloka) as `cat=`, and keeps a release only when one of the categories it lists is
one of those, or in the thousand of a parent asked for, whether or not the gateway honoured `cat`: the
audio-only Interstellar (3020) is left out of the sample's 17. A feed asked for at most 100 releases
(`limit=`, or its own maximum when smaller), since each is a stored row.

**The session is removed from every link, and a link that could carry a credential is not kept.** A
topic link loses every `sid` pair, in any letter case, after `&` or `;`, percent-encoded or in the
fragment, so the `t=search` and `t=movie` samples give the same id and page link for each of the 17
releases (`https://toloka.to/t699400`). The page link comes from `<comments>` only and is refused when
it names a credential, looks like a download (`.torrent`, a `download`, `dl` or `rss` segment), or
points at the gateway itself, whose address members must not see. The id is the guid when that is the
page link, and otherwise a SHA-256 digest of it: Prowlarr writes the tracker's own download link,
passkey in the path, as the guid of an indexer that gives none.

**Download links stay on the server, and without the key.** The search stores each release with its
link sealed to the release's row (ADR 0007) and the API key replaced by a placeholder, which the fetch
replaces with the integration's current key; the browser gets the release's id. The fetch is
`FetchPayloadAsync`, built here and called by MAM-13: a magnet is handed over as it is; an http(s) link
is fetched only from the feed's own origin (scheme, ASCII host, port) and only once — a `magnet:`
redirect is the payload, every other redirect is refused, naming its scheme and host only. A feed that
lists its links at another origin (a reverse proxy, a base-URL override) fails its search with a
sentence naming that origin, rather than listing releases that could never be fetched. Prowlarr's
download errors (410 for a release or an indexer it no longer serves, 429, 500) are read from their
`<error>` body; Jackett's bare 404 says the release may be gone or the feed failed to fetch it. The
Decision's "MAM-13 fetches the .torrent or magnet" holds: MAM-13 asks for it; the rule of where it may
come from is here. The MAM-7 section's "nothing writes a release yet" no longer holds: every search
writes its releases (ADR 0006).

**A feed's error text is cut before it is shown.** Jackett's error 900 and Prowlarr's download error
500 quote the tracker's URL, passkey and all; every address in a description is cut to its scheme and
host, and every credential-named pair (`passkey=`, `sid=`, …) is masked wherever it is, before the
description is cleaned and cut to 200 characters. The connection test's sentences change with it.

**The live search.** Pending: the owner runs `TorznabLiveTests` against the reference Prowlarr (see
README, Tests); its result is recorded here.

`TorznabAdapterSearchTests.Search_gives_both_samples_the_same_ids_and_page_links_without_the_session`,
`TorznabAdapterSearchLinksTests.Search_removes_the_session_from_a_page_link_and_an_id` and
`TorznabAdapterSearchLinksTests.Search_of_a_guid_that_is_the_trackers_download_link_stores_a_digest_and_no_page`
pin the links; `TorznabAdapterSearchTests.Search_of_the_sample_in_the_movie_categories_leaves_the_audio_release_out`
the categories; `ReleaseSearchPlanningTests.For_toloka_searches_its_movie_search_by_title_and_year_in_the_text_then_by_title`
and `ReleaseSearchHttpTests.Search_sends_the_title_and_year_to_the_movie_search_in_the_movie_categories`
the plan; `TorznabAdapterPayloadTests.FetchPayload_refuses_a_link_off_the_feeds_origin_without_sending_anything`,
`TorznabAdapterPayloadTests.FetchPayload_redirected_to_the_tracker_refuses_and_names_only_its_host` and
`TorznabAdapterPayloadTests.FetchPayload_maps_each_error_the_feed_answers_a_download_with` the fetch;
and `ReleaseSearchSecretsHttpTests.Search_answers_no_planted_credential_or_ciphertext_to_the_owner_or_a_member`
that none of it reaches the browser.
