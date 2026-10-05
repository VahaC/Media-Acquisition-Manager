# Vendor samples

Real answers from the reference installation, collected on 2026-09-29 for MAM-6, so the connection
tests of MAM-7 and the adapters of MAM-8 to MAM-11 are tested against what the services actually send
rather than against their documentation. MAM-17 added Jellyfin's series, seasons and episodes on
2026-10-04, from the same Jellyfin, for the import of MAM-32; MAM-32 added Prowlarr's Toloka TV searches
on the same day. MAM-33 added TMDb's answers, read with Vasyl's own token, and Prowlarr's newest Toloka
releases on 2026-10-05. `RepositoryPaths.VendorSamples` points here.

| Service | Version |
| --- | --- |
| Jellyfin | 12.1.0 (`jellyfin/jellyfin:latest`) |
| qBittorrent | 5.2.3, Web API 2.15.1 (`lscr.io/linuxserver/qbittorrent`) |
| Prowlarr | 2.6.5.5623, serving the Toloka.to indexer as Torznab |
| ffprobe | jellyfin-ffmpeg inside the Jellyfin image |
| TMDb | API v3 at `api.themoviedb.org`, read on 2026-10-05 with `language=uk-UA` |

How the services see the media folder, and what follows from it, is in
[ADR 0003](../../../docs/adr/0003-service-paths-reach-the-app-through-an-explicit-prefix-mapping.md);
the Toloka route is in [ADR 0004](../../../docs/adr/0004-toloka-is-searched-through-prowlarr-and-download-links-stay-on-the-server.md).

## Sanitizing

File, folder and release names are real; credentials are not. A refreshed sample must be sanitized
the same way, or `VendorSamplesTests` fails the build:

- **Replaced with `REDACTED`:** API keys, sessions (qBittorrent's cookie, the Toloka forum session
  `sid=`), Prowlarr's protected download links (`link=`), any other key-like URL parameter, URL user
  info (`scheme://REDACTED@host`) and any URL percent-encoded inside another one; a bearer token
  (TMDb's API Read Access Token), whether recorded as an `Authorization: Bearer` header line or as
  JSON, keeps only `REDACTED` after the scheme. The TMDb samples are answers alone and carry none.
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
| `Jellyfin/series-page.json` | `GET /Items?recursive=true&includeItemTypes=Series&collapseBoxSetItems=false&fields=Path,MediaSources,ProviderIds,OriginalTitle,DateCreated,ParentId&sortBy=SortName&sortOrder=Ascending&startIndex=…&limit=200&enableImages=false&enableUserData=false&enableTotalRecordCount=true` (five picked from the 10, 2026-10-04, MAM-17) | Yellowstone and Clarkson's Farm in the TV library, How the Universe Works and Mickey Mouse Clubhouse in the mixed libraries, and The Penguins of Madagascar, which Jellyfin named after its folder and gave no ids. The envelope is rebuilt like `movies-page.json`'s, around the real total |
| `Jellyfin/seasons-page.json` | The same with `includeItemTypes=Season` (four picked from the 25) | The seasons of the picked episodes, Mickey Mouse Clubhouse's virtual "Specials", without a path, among them |
| `Jellyfin/episodes-page.json` | The same with `includeItemTypes=Episode` (six picked from the 471) | An ordinary episode (Yellowstone S01E01); the Tom & Jerry Kids item, one episode with 65 media sources and no series; a documentary episode in a mixed library; the two halves of Mickey Mouse Clubhouse S03E09, with the same numbers and ids; and a virtual special whose one media source is a `Placeholder` without a path |
| `QBittorrent/app-version.txt`, `webapi-version.txt`, `build-info.json` | `GET /api/v2/app/...` | Versions |
| `QBittorrent/categories.json` | `GET /api/v2/torrents/categories` | Four categories, each saving to the folder of one Jellyfin library, under qBittorrent's `/downloads` |
| `QBittorrent/preferences.json` | `GET /api/v2/app/preferences` (allowlisted) | Automatic torrent management on, no separate folder for incomplete downloads, content layout `Original` |
| `QBittorrent/torrents-info.json` | `GET /api/v2/torrents/info` (four picked) | A three-movie pack, and a single-file torrent for each category that has torrents (the documentary one has none) |
| `QBittorrent/torrent-files-multi.json` | `GET /api/v2/torrents/files?hash=…` | The pack's files: one torrent, three movies, each named with the torrent's root folder and a double space the app keeps, and sized beyond 32 bits. Read whole by `QBittorrentAdapterFilesTests` (MAM-14) |
| `QBittorrent/expired-session-response.txt` | `GET /api/v2/torrents/categories` and `GET /api/v2/app/version`, each with a session cookie qBittorrent doesn't know (2026-10-01, MAM-11) | The status line, headers and body of the one answer both got: `403 Forbidden` with the text `Forbidden`. Read by `VendorResponses.Recorded` |
| `Torznab/prowlarr-toloka-caps.xml` | `GET /1/api?t=caps` | Toloka's categories and search modes |
| `Torznab/prowlarr-toloka-search.xml` | `GET /1/api?t=search&q=Interstellar` | 17 releases, not all of them movies |
| `Torznab/prowlarr-toloka-movie-search.xml` | `GET /1/api?t=movie&q=Interstellar` | The same releases through the movie search, this time with topic links that carry no `sid` |
| `Torznab/prowlarr-toloka-tv-search.xml` | `GET /1/api?t=tvsearch&q=Yellowstone` (2026-10-04, MAM-32) | 9 releases: Yellowstone (2018) whole and season by season in TV (`5000`) and TV/HD (`5040`); a BBC documentary of the same title, "Yellowstone (S1) (2009)", and two other documentaries holding the word, in TV/Documentary (`5080`). Its topic links carry `sid=REDACTED` |
| `Torznab/prowlarr-toloka-tv-season-search.xml` | The same with `&season=1` | The first six of those releases: Toloka barely narrows by the season |
| `Torznab/prowlarr-toloka-tv-cartoon-search.xml` | `GET /1/api?t=tvsearch&q=Mickey Mouse Clubhouse` | 4 releases of a cartoon series in TV and TV/HD, filed in Toloka's two «Мультсеріали» sections (`100044`, `100174`) |
| `Torznab/prowlarr-toloka-latest-movies.xml` | `GET /1/api?t=search&cat=2000&limit=100`, no `q` (2026-10-05, MAM-33) | Toloka's newest releases in the movie categories, newest first, each with Toloka's own topic title in `<description>`, Ukrainian first: the first 25 of the 90 it answered. Its topic links carry `sid=REDACTED` |
| `Torznab/prowlarr-toloka-latest-tv.xml` | The same with `cat=5000` | The first 25 of 90: anime in TV/Anime (`5070`) with season or episode markers, a Ukrainian-only documentary series in TV/Documentary (`5080`) whose title Prowlarr cut to "(2026) WEB-DL 1080p (S1E1-25 of ?)", sport in TV/Sport |
| `Torznab/prowlarr-toloka-movie-search-film-in-tv-category.xml` | `GET /1/api?t=movie&q=Schumacher '94 - Die Geburt einer Legende 2026&cat=2000,2020,2040,2070,5070,5080` (2026-10-05, MAM-33) | 5 releases of one documentary film: once in Movies, four times in Toloka's «Документальні фільми в HD», which Prowlarr lists as TV/Documentary (`5080`) |
| `Torznab/prowlarr-wrong-key-response.txt` | `GET /1/api?t=caps`, `GET /1/api?t=search&q=Interstellar` and `GET /1/download?link=x`, each with a wrong `apikey` (2026-10-01, MAM-10) | The status line and headers of the one answer all three got: a bare 401 with no body. Read by `VendorResponses.Recorded` |
| `Tmdb/authentication.json` | `GET /3/authentication` with the token | `{"success":true}`: the connection test's answer |
| `Tmdb/configuration.json` | `GET /3/configuration` | The image host (`https://image.tmdb.org/t/p/`) and its sizes, `w342` among the poster sizes |
| `Tmdb/movie-details.json` | `GET /3/movie/118340?append_to_response=credits,recommendations` | Guardians of the Galaxy (2014): in collection 284433, directed by James Gunn, with its IMDb id `tt2015381`, its whole cast and crew and 20 recommendations |
| `Tmdb/tv-details.json` | `GET /3/tv/73586?append_to_response=credits,recommendations` | Yellowstone (2018): five seasons with their air dates and episode counts, created by Taylor Sheridan and John Linson, 20 recommendations |
| `Tmdb/collection.json` | `GET /3/collection/284433` | The three Guardians of the Galaxy films, each with its release date |
| `Tmdb/person-combined-credits.json` | `GET /3/person/24/combined_credits` | Robert Zemeckis: 31 films directed and no show created, among his other crew jobs and his cast credits |
| `Tmdb/search-movie.json` | `GET /3/search/movie` for "Landing Stripling" | One result, "Landing Stripling" (1962), an animation: a cartoon the reference library holds as "120. Landing Stripling" with no id |
| `Tmdb/search-tv.json` | `GET /3/search/tv` for "The Penguins of Madagascar" | One result, "Пінгвіни Мадагаскару" / "The Penguins of Madagascar" (2008), the series Jellyfin gave no ids |
| `Tmdb/find-imdb.json` | `GET /3/find/tt0071562?external_source=imdb_id` | The Godfather Part II, one film (240) and no show |
| `Tmdb/discover-movie-documentary.json` | `GET /3/discover/movie` of Documentary (`with_genres=99`), by rating, `vote_count.gte=300` | The first page of 20 documentaries, the best-rated first |
| `Tmdb/discover-tv-animation.json` | `GET /3/discover/tv` of Animation (`with_genres=16`), by rating, `vote_count.gte=300` | The first page of 20 animated shows, the best-rated first |
| `Tmdb/genres-movie.json`, `Tmdb/genres-tv.json` | `GET /3/genre/movie/list`, `GET /3/genre/tv/list` | TMDb's 19 movie genres and 16 TV genres, in Ukrainian; Animation is 16 and Documentary 99 in both |
| `Tmdb/wrong-key-response.txt` | A request with a wrong token (2026-10-05) | The status line, headers and body of TMDb's answer: `401 Unauthorized` with `{"status_code":7,"status_message":"Invalid API key: You must be granted a valid key.","success":false}`. Read by `VendorResponses.Recorded` |
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
- **It is the missing user, not the setting** (Jellyfin's source, read for MAM-8). Asked with an
  API key, `/Items` runs without a user, and `Folder.CollapseBoxSetItems` reads the server's "group
  movies into collections" setting only for a user: without one, a query for movies collapses box
  sets whatever that setting says. So the library sync sends `collapseBoxSetItems=false` with every
  page, and turning the setting off in Jellyfin would change nothing.
- **A Jellyfin series is not a series, and an episode item not an episode** (MAM-17,
  [ADR 0017](../../../docs/adr/0017-an-episode-is-a-work-known-by-registry-ids-and-the-library-alone-says-what-an-item-is.md)).
  The Grand Tour is four series items, one per season folder, all with the same ids, and its three
  season-5 files lie in no series item at all. Jellyfin made the 65 files of the Tom & Jerry Kids
  folder, 65 different episodes, one episode with 65 media sources and no series; it gave the two
  halves of eight Mickey Mouse Clubhouse episodes (`S03E09a`, `S03E09b`) the same numbers and ids,
  and "The Golden Boo Boo" (S03E31) the ids of "Donald Hatches an Egg" (S03E33). The mixed
  libraries hold movies and series side by side (Мульфільми: 194 movies, 314 episodes in 2 series
  and one episode item in none; Документалки: 35 movies, 1 series with 16 episodes), and 38 movie
  items are episodes by their names: The Continental's S01E01–03 in the movie library, sharing one
  IMDb id, and in the documentary library Greatest Events of World War II in Colour's S01E01–10
  and the 25 episodes of Planet Earth, Planet Earth II and Planet Earth III, without ids.
- **Episodes carry other ids than movies** (MAM-17). Of the 471 episodes, 287 have an IMDb id and
  301 a TVDB id of their own, none a TMDb id; 9 of the 10 series items have IMDb, TMDb and TVDB ids,
  the Penguins of Madagascar none. `SeriesId`, `SeasonId`, `ParentIndexNumber` and `IndexNumber`
  came without being asked for in `fields`.
- **A virtual episode has a media source too** (MAM-17). The 31 virtual episodes (`LocationType`
  `Virtual`), 22 of them specials, each list one media source with protocol `File` and no path (the
  sampled one is of type `Placeholder`): an import must not take it for a file.
- **Jellyfin never merged movie versions here.** None of the 870 movies has more than one media
  source; 15 TMDb ids belong to two or more items instead. 222 movies have no TMDb id and 210 no
  IMDb id.
- **Jellyfin 12** accepted the API key in `Authorization: MediaBrowser Token="…"`. By its source,
  `X-Emby-Token` and `api_key` work only while legacy authorization is switched on, and 12 switches
  it off by default.
- **qBittorrent 5.2**, by its source, answers a good sign-in with `204` and a cookie named
  `QBT_SID_<port>` (`200 Ok.` and `SID` before 5.2), and a wrong one with `401` rather than
  `200 Fails.`; the sign-in here took the first cookie whatever its name. Seen in the samples: in
  `torrents/files`, `is_seed` appears on the first file only, and `content_path` is the file itself
  for a single-file torrent and the torrent's folder otherwise.
- **qBittorrent answers a session it doesn't know with 403, never 401** (MAM-11): `torrents/categories`
  and `app/version` with a forgotten cookie both got `403 Forbidden` and the text `Forbidden`, as a
  request without any cookie does. By its source, every version from 4.3 on answers alike, whether the
  session sat idle past its timeout (an hour by default) or qBittorrent restarted (it keeps sessions in
  memory only). So a 403 to a session the app kept means "sign in again", once; a 403 to a session it
  has just been handed is a failure.
- **A category's `savePath` is what was saved, not where it saves** (MAM-11, qBittorrent's source). The
  samples' four are absolute, but it may be empty — the default save path and the category's name; from
  5.2 a subcategory goes under its parent's folder instead, and 5.0 and 5.1 do either, as
  `use_subcategories` says — or relative to the default save path. The app works the folder out the
  way qBittorrent does, reading the default save path from `app/defaultSavePath` and never from
  `app/preferences`, which also carries the Web UI's API key (5.2) and the proxy and mail passwords.
- **qBittorrent 4.4.0 lists a category's folder as `save_path`** rather than `savePath` (qBittorrent
  issue #15969, fixed in 4.4.1), so the app works with 4.4.1 and later.
- **`torrents/add` answers differently by version** (MAM-13). This was read from qBittorrent's source
  (`torrentscontroller.cpp`), not recorded live yet, because a recording means adding a torrent to the
  reference installation. 4.4.1 to 5.1 answer `200` with the text `Ok.` when anything was added and
  `Fails.` when nothing was, and `415` when a .torrent doesn't load. 5.2 answers `200` with a JSON object
  (`success_count`, `failure_count`, `pending_count` and `added_torrent_ids`, whose ids are TorrentIDs
  like `torrents/info`'s `hash`), `202` while something is still pending, `409 Conflict` when nothing
  was added, and `415` for a bad .torrent. A torrent qBittorrent already has is never added again. A
  category it doesn't have is created silently, with no folder of its own, and a category decides the
  folder only under automatic torrent management, so the app sends `autoTMM=true` with it.
  `torrents/info` reads `hashes` as TorrentIDs joined by `|`. A TorrentID is the v1 info hash, or the
  v2 hash cut to 40 characters; a hybrid torrent gets the v2 one on libtorrent 2
  (`info_hash_t::get_best`).
- **Prowlarr's download link carries its API key** (`/1/download?apikey=…&link=…`), and a Toloka
  topic link can carry the owner's forum session (`?sid=…`): every one in the `t=search` answer did,
  none in the `t=movie` answer taken seconds later. Toloka's movie search takes only `q`, so it
  cannot search by IMDb id, and a text search returns every section: the Interstellar search found
  an audio-only release (`3020`) and a phone-sized rip filed under the standard `2000` as well as
  movies (`2000`, `2040`). Each release also has a Toloka category (`100xxx`; the phone rip is in
  `100237`, video for mobile devices), so filtering may need both.
- **Prowlarr refuses a wrong API key with a bare 401** (MAM-10): `t=caps`, `t=search` and a download
  link alike, an empty body and no content type — not the `<error code="100">` the Torznab
  specification describes. The adapter reads both as a refused key. The search asks for the movie
  categories only (the standard `2000`–`2999`) and keeps a release that lists one of them, so the
  audio-only one is left out and the phone rip stays: it is filed under `2000`.
- **Prowlarr rewrites Toloka's TV titles** (MAM-32). Toloka's "Єллоустоун (Сезони 1-5) / Yellowstone
  (Seasons 1-5) (2018-2024) WEB-DL-AVC …" arrives as "Yellowstone (S1-5) (2018-2024) WEB-DL-AVC …": the
  Ukrainian title is left out (it stays in `<description>`), the seasons become `S1-5` or `S5`, and
  most titles repeat that marker at the end, "… WEB-DL 1080p (S5)". The year of a season pack is its
  seasons' span ("Yellowstone (S5) (2022-2024)") or one year ("(S4) (2021)"), not the year the show
  began. None of the TV releases gives an IMDb, TMDb or TVDB id, so they match the library by their
  titles and years alone; the documentary "Yellowstone (S1) (2009)" has the title of the 2018 show and
  only its year tells them apart.
- **Prowlarr answers Toloka's search with no text with its newest releases** (MAM-33,
  [ADR 0024](../../../docs/adr/0024-release-sources-are-asked-for-recommendations-within-a-per-user-budget.md)).
  `t=search` without `q` answered Toloka's latest 90 releases, newest first, every one with
  `<description>`: in the movie categories (`cat=2000`) 90 published from 2026-09-26 to 10-05, in the
  TV categories (`cat=5000`) 90 from 09-28 to 10-05. Asked with `offset=50` or `offset=100` it
  answered no release at all, so there is nothing to page through. Toloka's own sections filter too
  (`cat=100019,100193,100139`), and `t=movie` without `q` answered the same as `t=search`.
- **`<description>` holds Toloka's own topic title** (MAM-33). Where Prowlarr's title keeps the
  English part ("Those Who Wish Me Dead (2021) BDRip 1080p …"), the description keeps Toloka's whole
  title, Ukrainian first ("Ті, хто бажають моєї смерті / Those Who Wish Me Dead (2021) …"); for a
  Ukrainian-only show Prowlarr's title is cut to "(2026) WEB-DL 1080p (S1E1-25 of ?)", and only the
  description names it.
- **TV/Anime and TV/Documentary hold films and series both** (MAM-33,
  [ADR 0020](../../../docs/adr/0020-a-release-is-tv-by-its-category-matches-only-series-and-its-tracker-section-picks-the-destination-offered-first.md)).
  Of the latest 90 in `5070` and `5080`, 12 named no season: films ("Akira (1988)", "Schumacher '94",
  the Demon Slayer "Infinity Castle" film) and series named by their episodes alone ("Death Note (E1-31
  of 37)", "Candy Candy (E01-40 of 115)", "Котодракон / Neko to Ryuu (серії 1-4 з 12)"), so a season
  marker alone wouldn't tell them apart. A movie search sent with `5070` and `5080` among its
  categories found the documentary "Schumacher '94" that Toloka files in TV/Documentary, which the
  movie categories alone (the MAM-10 bullet above) would have left out.
- **TMDb takes its v4 token as a bearer header** (MAM-33,
  [ADR 0005](../../../docs/adr/0005-the-core-knows-a-service-only-through-a-registered-adapter-type.md)).
  Vasyl's API Read Access Token is 239 characters, inside the 256 an integration's key may hold, and
  travels as `Authorization: Bearer`; a wrong one gets 401 and TMDb's JSON with `status_code` 7. A
  work, collection or person TMDb doesn't have answers 404 with its own JSON, `status_code` 34, which
  a bare 404 from a wrong address lacks.
- **TMDb sent no rate-limit header** (MAM-33). The library's 640 TMDb movie ids took 943 calls in
  about 3 minutes and met no 429; TMDb's documentation puts its limit "somewhere in the 50 requests
  per second range" per address, so the adapter keeps to 40.
- **Ukrainian titles are TMDb's, the original beside them** (MAM-33). Asked with `language=uk-UA`,
  TMDb answers its Ukrainian title where it has one and the original otherwise: for 12 of the 640
  movies the two were the same. A show's fields are `name`, `original_name` and `first_air_date`, a
  film's `title`, `original_title` and `release_date`; `vote_average` is `0` for a work nobody rated,
  which the adapter reads as no rating.
- **TMDb finds most of the library's id-less works, not all** (MAM-33). Of the 10 movies with only an
  IMDb id, TMDb's find named 8; a search by title and year found exactly one film for 23 of 25 Tom &
  Jerry shorts, and nothing for the Planet Earth episodes Jellyfin lists as movies.
- **The posters come from TMDb's image host without a key** (MAM-33). `image.tmdb.org/t/p/w342/<file>`
  answered `image/jpeg`, 48 KB for one poster, and a bare path 404; the reference installation's
  container reached it, and `api.themoviedb.org`, directly, with no proxy.
- **ffprobe reports cover art as a video stream** (`mjpeg` with `disposition.attached_pic: 1`),
  after the real video stream; it is not a second video track.
- **A 10-bit stream need not say whether it is HDR** (MAM-9). The Continental's HEVC Main 10 video
  declares no transfer characteristic, primaries or matrix at all, so the app shows its dynamic
  range as unknown rather than guessing SDR or HDR10 from the bit depth; Taken's declares BT.709.
- **Track titles reach ffprobe's JSON in whatever encoding the muxer used** (MAM-9). Some of the
  Ukrainian titles here were written in a legacy code page, and arrive as invalid UTF-8 (shown as
  `�`). The app reads ffprobe's answer leniently and keeps no track titles, so they never matter.
- **ffprobe picks a demuxer by a file's content, not its name** (MAM-9). A file named `.mkv` whose
  content starts `ffconcat version 1.0` is read as a concat list, and ffprobe opens the files it
  names; `-protocol_whitelist file` doesn't stop that, `-format_whitelist` does (8.0.1 and 6.1.1:
  `[concat @ 0x…] Format not on whitelist '…'`, then `file:<path>: Invalid argument`).
- **ffprobe's verdict on a file it can't read is its last line** (MAM-9), `file:<path>: <reason>`;
  the lines before it are what a demuxer said on the way ("Truncating packet of size 83 to 70",
  "[matroska,webm @ 0x…] EBML header parsing failed"). A cut-off MKV ends in "I/O error", a text or
  random file in "Invalid data found when processing input".
