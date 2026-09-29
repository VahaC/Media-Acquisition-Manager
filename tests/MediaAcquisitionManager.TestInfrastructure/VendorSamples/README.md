# Vendor samples

Real answers from the reference installation, collected on 2026-09-29 for MAM-6, so the adapters of
MAM-8 to MAM-11 are tested against what the services actually send rather than against their
documentation. `RepositoryPaths.VendorSamples` points here.

| Service | Version |
| --- | --- |
| Jellyfin | 12.1.0 (`jellyfin/jellyfin:latest`) |
| qBittorrent | 5.2.3, Web API 2.15.1 (`lscr.io/linuxserver/qbittorrent`) |
| Prowlarr | 2.6.5.5623, serving the Toloka.to indexer as Torznab |
| ffprobe | jellyfin-ffmpeg inside the Jellyfin image |

How the services see the media folder, and what follows from it, is in
[ADR 0003](../../../docs/adr/0003-service-paths-reach-the-app-through-an-explicit-prefix-mapping.md);
the Toloka route is in [ADR 0004](../../../docs/adr/0004-toloka-is-searched-through-prowlarr-and-download-links-stay-on-the-server.md).

## Sanitizing

File, folder and release names are real; credentials are not. A refreshed sample must be sanitized
the same way, or `VendorSamplesTests` fails the build:

- **Replaced with `REDACTED`:** API keys, sessions (qBittorrent's cookie, the Toloka forum session
  `sid=`), Prowlarr's protected download links (`link=`), any other key-like URL parameter, URL user
  info (`scheme://REDACTED@host`) and any URL percent-encoded inside another one.
- **Cut to the host:** a tracker URL keeps its scheme and host, and its path becomes `/REDACTED`
  (`http://bt.toloka.to/REDACTED`), because the passkey can sit in the path.
- **Removed:** the tracker parameters of a magnet link (`tr`, `ws`, `xs`, `as`); only `xt`, `dn` and
  `xl` stay.
- **Left out:** every qBittorrent preference except those about paths and seeding, and the values of
  Prowlarr's indexer settings.

## Files

| File | Request | What it shows |
| --- | --- | --- |
| `Jellyfin/system-info-public.json` | `GET /System/Info/Public` (no key) | The version, before any credential is checked |
| `Jellyfin/system-info.json` | `GET /System/Info` | Answers only with a valid key |
| `Jellyfin/virtual-folders.json` | `GET /Library/VirtualFolders` | Five libraries; `movies` and `tvshows` have a collection type, the cartoon and documentary libraries have none (mixed), and the `boxsets` library lives in Jellyfin's config, not in the media folder |
| `Jellyfin/movies-page.json` | `GET /Items?recursive=true&includeItemTypes=Movie&collapseBoxSetItems=false&fields=Path,MediaSources,ProviderIds,OriginalTitle,DateCreated&sortBy=SortName&sortOrder=Ascending&startIndex=…&limit=100&enableImages=false&enableUserData=false&enableTotalRecordCount=true` (four picked from the 870) | Four movies, see below. The envelope is rebuilt around them, so `TotalRecordCount` is the library's 870, not the items in the file: paging tests need pages of their own |
| `QBittorrent/app-version.txt`, `webapi-version.txt`, `build-info.json` | `GET /api/v2/app/...` | Versions |
| `QBittorrent/categories.json` | `GET /api/v2/torrents/categories` | Four categories, each saving to the folder of one Jellyfin library, under qBittorrent's `/downloads` |
| `QBittorrent/preferences.json` | `GET /api/v2/app/preferences` (allowlisted) | Automatic torrent management on, no separate folder for incomplete downloads, content layout `Original` |
| `QBittorrent/torrents-info.json` | `GET /api/v2/torrents/info` (four picked) | A three-movie pack, and a single-file torrent for each category that has torrents (the documentary one has none) |
| `QBittorrent/torrent-files-multi.json` | `GET /api/v2/torrents/files?hash=…` | The pack's files: one torrent, three movies |
| `Torznab/prowlarr-toloka-caps.xml` | `GET /1/api?t=caps` | Toloka's categories and search modes |
| `Torznab/prowlarr-toloka-search.xml` | `GET /1/api?t=search&q=Interstellar` | 17 releases, not all of them movies |
| `Torznab/prowlarr-toloka-movie-search.xml` | `GET /1/api?t=movie&q=Interstellar` | The same releases through the movie search, this time with topic links that carry no `sid` |
| `Ffprobe/the-continental-s01e01.json` | `ffprobe -show_format -show_streams -show_chapters` | HEVC Main 10, four audio and four subtitle tracks, eight chapters |
| `Ffprobe/taken-2008-bdremux.json` | the same | H.264 remux, DTS and AC-3 tracks, text and PGS subtitles, 24 chapters |

The four movies in `movies-page.json`:

- **The Continental (2023) S01E01**: an episode that sits in the movie library, in a season folder
  inside the `John Wick. Collection` folder, so Jellyfin lists it as a `Movie` with only an IMDb id.
- **13-й район (2004)**: a movie in the `District 13. Dilogy` folder, with IMDb, TMDb and TMDb
  collection ids.
- **Чужі (1986)**, twice: the theatrical cut and the special edition in one `Alien. Collection`
  folder. Jellyfin keeps them as two items with the same TMDb id rather than as two versions of one
  item.

## What the samples taught

- **Jellyfin hides movies behind their collections by default.** Asked with an API key,
  `GET /Items?recursive=true&includeItemTypes=Movie` answered 670 items instead of 870: the movies
  of a collection were replaced by one `BoxSet` item each. In a run whose output was not kept, such
  an item was a folder under `/config/data/collections` with no media sources and the TMDb
  *collection* id as its TMDb id. Whether the server's "group movies into collections" setting or
  the missing user caused it was not checked. `collapseBoxSetItems=false` returns every movie; the
  inventory must always send it.
- **Jellyfin never merged versions here.** None of the 870 movies has more than one media source;
  15 TMDb ids belong to two or more items instead. 222 movies have no TMDb id and 210 no IMDb id.
- **Jellyfin 12** accepted the API key in `Authorization: MediaBrowser Token="…"`. By its source,
  `X-Emby-Token` and `api_key` work only while legacy authorization is switched on, and 12 switches
  it off by default.
- **qBittorrent 5.2**, by its source, answers a good sign-in with `204` and a cookie named
  `QBT_SID_<port>` (`200 Ok.` and `SID` before 5.2), and a wrong one with `401` rather than
  `200 Fails.`; the sign-in here took the first cookie whatever its name. Seen in the samples: in
  `torrents/files`, `is_seed` appears on the first file only, and `content_path` is the file itself
  for a single-file torrent and the torrent's folder otherwise.
- **Prowlarr's download link carries its API key** (`/1/download?apikey=…&link=…`), and a Toloka
  topic link can carry the owner's forum session (`?sid=…`): every one in the `t=search` answer did,
  none in the `t=movie` answer taken seconds later. Toloka's movie search takes only `q`, so it
  cannot search by IMDb id, and a text search returns every section: the Interstellar search found
  an audio-only release (`3020`) and a phone-sized rip filed under the standard `2000` as well as
  movies (`2000`, `2040`). Each release also has a Toloka category (`100xxx`; the phone rip is in
  `100237`, video for mobile devices), so filtering may need both.
- **ffprobe reports cover art as a video stream** (`mjpeg` with `disposition.attached_pic: 1`),
  after the real video stream; it is not a second video track.
