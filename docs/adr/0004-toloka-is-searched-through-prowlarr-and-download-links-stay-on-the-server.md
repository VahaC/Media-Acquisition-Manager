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
