# Vendor samples

Real answers from the reference installation, collected on 2026-09-29 for MAM-6, so the connection
tests of MAM-7 and the adapters of MAM-8 to MAM-11 are tested against what the services actually send
rather than against their documentation. MAM-17 added Jellyfin's series, seasons and episodes on
2026-10-04, from the same Jellyfin, for the import of MAM-32; MAM-32 added Prowlarr's Toloka TV searches
on the same day. MAM-33 added TMDb's answers, read with Vasyl's own token, and Prowlarr's newest Toloka
releases on 2026-10-05. MAM-34 added, the same day, Jellyfin's users, a movie and a series item read
whole, written back with new ids and read again, the refusals of a read and a write, and the movie
after two refreshes — the first samples of a write to Jellyfin, made on the reference installation by
a live check Vasyl ran —, and TMDb's details of a short and of Planet Earth with their runtimes, IMDb
ids and other titles. The MAM-34 follow-up adds, from a second live check Vasyl ran on 2026-10-06,
Jellyfin's answers to a metadata lookup of a movie and of a series item (`POST /Items/{id}/Refresh`),
the answer to one more write of the series, and TMDb's details of two episodes with their own IMDb
ids — every one of them recorded, none made by hand. The MAM-34 Identify rework adds, from the live check
Vasyl ran on 2026-10-06 against Jellyfin 12.1.0 (he typed `y` for each Apply), Jellyfin's `RemoteSearch`
answers for a film, a show and a documentary episode, their `Apply` answers, the three items read again and
their image lists — every one recorded, none made by hand. MAM-35 adds, from a read-only live check Vasyl ran on
2026-10-07 against the same Jellyfin, the sampled movies, series and episodes read again with the sync's new query, which
asks for each item's primary image tag, and the image route's answers for a poster, for an item without one and for an
id Jellyfin doesn't know — every one recorded, none made by hand.
`RepositoryPaths.VendorSamples` points here.

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
| `Jellyfin/users.json` | `GET /Users` (allowlisted: `Name`, `Id`, `Policy.IsAdministrator`, `Policy.IsDisabled`; 2026-10-05, MAM-34) | Two users, one an enabled administrator: the one the app reads an item as |
| `Jellyfin/item.json` | `GET /Items/{id}?userId={administrator}` | The movie item "120. Landing Stripling" whole, 47 fields — `Etag`, `MediaSources`, `People`, `LockedFields`, `LockData`, `UserData` among them —, with empty `ProviderIds` and `RunTimeTicks` 3,730,210,000 (6.2 minutes): what the app reads before an Identify, and the body the earlier ids-only write sent back, less its `People` |
| `Jellyfin/item-update-response.txt` | `POST /Items/{id}` with `item.json`, only its `ProviderIds` set to `Tmdb` 223021 and `Imdb` tt0056164 | `204 No Content`, no body. History since the 0.2.1 Identify rework replaced this write: no test reads it |
| `Jellyfin/item-after-update.json` | The same read again | The new ids; besides them only `Etag`, `ExternalUrls` and `UserData.Key` (now "223021") changed. History: no test reads it |
| `Jellyfin/item-after-default-refresh.json` | The read after `POST /Items/{id}/Refresh` with `metadataRefreshMode=Default` | Nothing changed: the ids kept. History: no test reads it |
| `Jellyfin/item-after-missing-refresh.json` | The read after "Search for missing metadata" (`metadataRefreshMode=FullRefresh`, `replaceAllMetadata=false`) | The ids kept, `Tvdb` and `TvdbSlug` added, and the overview, genres, people, studios, tags and ratings Jellyfin filled in: 53 fields. Its two people (Gene Deitch, William L. Snyder) are what the earlier write left out of the body. History: no test reads it |
| `Jellyfin/item-update-wrong-key-response.txt` | `POST /Items/{id}` with a wrong key | `401 Unauthorized` with no body. History: no test reads it |
| `Jellyfin/item-update-unknown-item-response.txt` | `POST /Items/{id}` of an item that doesn't exist | `404 Not Found` with JSON problem details. History: no test reads it |
| `Jellyfin/item-read-unknown-item-response.txt` | `GET /Items/{id}?userId=…` of an item that doesn't exist | The same `404` |
| `Jellyfin/item-read-without-user-response.txt` | `GET /Items/{id}` with the key and no user | `400 Bad Request`, `text/plain`, "Error processing request." |
| `Jellyfin/series-item.json` | `GET /Items/{id}?userId={administrator}` of a series | The Penguins of Madagascar's series item whole, 41 fields — `AirDays`, `ChildCount`, `RecursiveItemCount`, `DateLastMediaAdded` among them —, named after its folder, "The Penguins Of Madagascar (Season 1-3) (2008-2015) WEBDL 1080p [Ukr,Eng] Sub [Eng] [ Hurtom ] by Jatx 11", with no ids |
| `Jellyfin/series-item-update-response.txt` | `POST /Items/{id}` with it, only `ProviderIds.Tmdb` set to 7869 | `204 No Content`, after 4.5 seconds. History: no test reads it |
| `Jellyfin/series-item-after-update.json` | The same read again | `ProviderIds` `{ "Tmdb": "7869" }`; besides it only `Etag` and `ExternalUrls` changed on the series item. History: no test reads it |
| `Jellyfin/series-item-update-2026-10-06-response.txt` | The same `POST /Items/{id}` of the series, by then holding `Tmdb` 7869 and five other ids and rated `TV-Y7`, sent back as read without its `People` (2026-10-06, MAM-34 follow-up) | `204 No Content` after 9.5 seconds (`X-Response-Time-ms` 9484.99): the save that gave all 3 seasons and 149 episodes the series' rating (below). Kept as the check's record; no test reads it |
| `Jellyfin/item-refresh-response.txt` | `POST /Items/{id}/Refresh?metadataRefreshMode=FullRefresh&imageRefreshMode=FullRefresh&replaceAllMetadata=false&replaceAllImages=false` — Jellyfin's own "Search for missing metadata" — of the movie item "Greatest Events of World War II in Colour.S01E01.Blitzkrieg" (2026-10-06, MAM-34) | `204 No Content`, no body, in 0.6 ms: queued, not done. History: no test reads it |
| `Jellyfin/item-refresh-unknown-item-response.txt` | The same of an item that doesn't exist | `404 Not Found` with JSON problem details, as an update of one answers. History: no test reads it |
| `Jellyfin/item-refresh-wrong-key-response.txt` | The same with a wrong key | `401 Unauthorized` with no body. History: no test reads it |
| `Jellyfin/series-refresh-response.txt` | The same of the Penguins of Madagascar's series item, right after its update | `204 No Content` in 0.8 ms. History: no test reads it |
| `Jellyfin/series-item-after-removal.json` | The read after posting it back without the `Tmdb` key | The key gone; only `Etag` differs from `series-item.json`. Kept as the check's record; no test reads it |
| `Jellyfin/remote-search-movie-by-tmdb-response.txt` | `POST /Items/RemoteSearch/Movie` with `{"SearchInfo": {"ProviderIds": {"Tmdb": "223021"}}, "ItemId": "750eaa45-b083-6d08-e4ae-4b35750b93ca", "SearchProviderName": "TheMovieDb"}` (2026-10-06, MAM-34 Identify) | `200 OK` after 0.12 s, a JSON array of one row: "Landing Stripling" (1962) with `Tmdb` and `Imdb`, a premiere date, an `ImageUrl`, the overview and an empty `Artists`. The fake Jellyfin's row of a film (`JellyfinSamples.RemoteSearchMovieByTmdbRows`) and the row the adapter applies |
| `Jellyfin/remote-search-movie-by-tmdb-without-item-response.txt` | The same without `ItemId`: `{"SearchInfo": {"ProviderIds": {"Tmdb": "223021"}}, "SearchProviderName": "TheMovieDb"}` | `200 OK` after 0.02 s, the same row: a search works without the item. Kept as the check's record; no test reads it |
| `Jellyfin/remote-search-movie-by-tmdb-all-providers-response.txt` | The same without `SearchProviderName`: `{"SearchInfo": {"ProviderIds": {"Tmdb": "223021"}}, "ItemId": "750eaa45-b083-6d08-e4ae-4b35750b93ca"}` | `200 OK` after 0.20 s, the same row: only TheMovieDb answered an id search by `Tmdb`. Kept as the check's record; no test reads it |
| `Jellyfin/remote-search-movie-by-tmdb-lowercase-key-response.txt` | The same with the key spelled `tmdb` | `200 OK` after 0.06 s with `[]`: the key must be exactly `Tmdb`. The fake Jellyfin answers it so, and the adapter spells it so |
| `Jellyfin/remote-search-movie-unknown-tmdb-id-response.txt` | The same with `Tmdb` 999999999 | `200 OK` after 0.15 s with `[]`: no match, not an error. Read by the adapter's no-match test |
| `Jellyfin/remote-search-movie-by-tv-id-response.txt` | `POST /Items/RemoteSearch/Movie` with the TV id: `Tmdb` 7869 | `200 OK` after 0.14 s with one **unrelated** film, "The Fest" (2005): an id searched in the wrong namespace answers whatever has that number (`JellyfinSamples.RemoteSearchMovieByTvIdRows`) |
| `Jellyfin/remote-search-series-by-tmdb-response.txt` | `POST /Items/RemoteSearch/Series` with `{"SearchInfo": {"ProviderIds": {"Tmdb": "7869"}}, "ItemId": "9ac821b3-ddea-72fd-14ba-1835b4153e50", "SearchProviderName": "TheMovieDb"}` | `200 OK` after 0.16 s, one row: "The Penguins of Madagascar" (2008) with `Tmdb`, `Imdb` and `Tvdb`. The fake Jellyfin's row of a show (`JellyfinSamples.RemoteSearchSeriesByTmdbRows`) |
| `Jellyfin/remote-search-series-by-movie-id-response.txt` | `POST /Items/RemoteSearch/Series` with the film id: `Tmdb` 223021 | `200 OK` after 0.15 s with one unrelated show, "Ham.W.Shaha": the same trap the other way |
| `Jellyfin/remote-search-movie-by-imdb-omdb-response.txt` | `POST /Items/RemoteSearch/Movie` with `{"SearchInfo": {"ProviderIds": {"Imdb": "tt9103946"}}, "ItemId": "03299393-01cb-93b1-c47e-ea6efe4975c2", "SearchProviderName": "The Open Movie Database"}` | `200 OK` after 0.18 s, one row: "Blitzkrieg" (2019) with `Imdb` only, a Media-Amazon `ImageUrl` and **no overview**: an episode Jellyfin lists as a film, found by its own IMDb id (`JellyfinSamples.RemoteSearchOmdbRows`) |
| `Jellyfin/remote-search-movie-by-imdb-tmdb-response.txt` | The same with `SearchProviderName` `TheMovieDb` | `200 OK` after 0.13 s with `[]`: TMDb finds no episode by its IMDb id. The fake Jellyfin answers it so |
| `Jellyfin/remote-search-movie-by-imdb-all-providers-response.txt` | The same without `SearchProviderName` | `200 OK` after 0.16 s, the same single Open Movie Database row. Kept as the check's record; no test reads it |
| `Jellyfin/remote-search-wrong-key-response.txt` | `POST /Items/RemoteSearch/Movie` with a wrong API key | `401 Unauthorized` with no body, in 0.03 s. Read by `VendorResponses.Recorded` |
| `Jellyfin/item-remote-search-apply-response.txt` | `POST /Items/RemoteSearch/Apply/{itemId}?replaceAllImages=true` with the row of "Landing Stripling" as the body (2026-10-06, MAM-34). Recorded as `remote-search-apply-response.txt`, renamed to pair with the two below | `204 No Content`, no body, with a `Content-Language` header, after 1.8 s: Apply is synchronous, and a film takes seconds. Read by `VendorResponses.Recorded` |
| `Jellyfin/series-remote-search-apply-response.txt` | The same with the row of "The Penguins of Madagascar" (3 seasons, 149 episodes) | `204 No Content` after **159.3 s**: about a second per episode. Apply is not a request a 15-second client can wait for |
| `Jellyfin/episode-movie-remote-search-apply-response.txt` | The same with the Open Movie Database's row of "Blitzkrieg" | `204 No Content` after 21.3 s |
| `Jellyfin/remote-search-apply-unknown-item-response.txt` | The same of an item that doesn't exist (`782f4e68cd195fc78596a7a040f90bb7`) with a dummy body | `404 Not Found` with JSON problem details; the all-zero id answers 400 instead, so a test uses another |
| `Jellyfin/remote-search-apply-wrong-key-response.txt` | The same with a wrong API key | `401 Unauthorized` with no body |
| `Jellyfin/item-after-identify.json` | `GET /Items/{id}?userId={administrator}` of "120. Landing Stripling" after its Apply | The item whole: TMDb's `Name` ("Landing Stripling", the file's was "120. Landing Stripling"), `Etag` new, a new Primary poster and Backdrop, the overview, `ProviderIds` `Tmdb`, `Imdb`, `Tvdb` and `TvdbSlug` (`JellyfinSamples.ItemAfterIdentify`) |
| `Jellyfin/series-item-after-identify.json` | The same of the Penguins series item after its Apply | `Name` already TMDb's, all five images with new tags, `Etag` new, 3 seasons and 149 episodes untouched, `UserData` kept (15.4 % played) (`JellyfinSamples.SeriesItemAfterIdentify`) |
| `Jellyfin/episode-movie-after-identify.json` | The same of the episode Jellyfin lists as a film ("Blitzkrieg") after its Apply through the Open Movie Database | `Name` "Blitzkrieg" and year 2019 corrected, the Primary image replaced by a 1920x1080 one, the overview (168 characters) and `ProviderIds` `{ "Imdb": "tt9103946" }` kept (`JellyfinSamples.EpisodeMovieAfterIdentify`) |
| `Jellyfin/item-images-after-identify.json` | `GET /Items/{id}/Images` of the film after its Apply | A Primary poster (1112x1668) and a Backdrop (1280x720), files under Jellyfin's own `/config/metadata/library`. Kept as the check's record; no test reads it |
| `Jellyfin/series-images-after-identify.json` | The same of the series | Primary, Logo, Thumb, Banner and Backdrop. Kept as the check's record; no test reads it |
| `Jellyfin/episode-movie-images-after-identify.json` | The same of the episode listed as a film | One Primary image, 1920x1080. Kept as the check's record; no test reads it |
| `Jellyfin/movies-page-with-images.json` | `GET /Items?ids={the four of movies-page.json}&recursive=true&includeItemTypes=Movie&collapseBoxSetItems=false&fields=Path,MediaSources,ProviderIds,OriginalTitle&sortBy=SortName,DateCreated&sortOrder=Ascending&enableImages=true&enableImageTypes=Primary&imageTypeLimit=1&enableUserData=false&enableTotalRecordCount=true&startIndex=0&limit=200` — the sync's query since MAM-35, for the recorded items (2026-10-07) | The same four movies in the order Jellyfin listed them (13-й район, The Continental, the two "Чужі"), each with its own `ImageTags.Primary`. The envelope is the real one, so `TotalRecordCount` is the 4 in the file (`JellyfinSamples.MoviesWithImages`) |
| `Jellyfin/series-page-with-images.json` | The same with `includeItemTypes=Series`, `fields=ProviderIds,OriginalTitle` and the five ids of `series-page.json` | The five series, each with a primary image; the Penguins of Madagascar named and pictured as its Identify left it, with the tag `series-item-after-identify.json` holds (`JellyfinSamples.SeriesWithImages`) |
| `Jellyfin/episodes-page-with-images.json` | The same with `includeItemTypes=Episode` and the six ids of `episodes-page.json` | Each episode with its own `ImageTags.Primary` — Daybreak's `c46fbb41…`, not its series' `7060953c…` —, the virtual special "Follow the Mouse" with `ImageTags` `{}`; every episode of a series also carries its series' and season's tags (`SeriesPrimaryImageTag`, `ParentPrimaryImageItemId`, `ParentPrimaryImageTag`), which the app never reads (`JellyfinSamples.EpisodesWithImages`) |
| `Jellyfin/image-primary-response.txt` | `GET /Items/bad3e58c7b81156c7bccd6235599accd/Images/Primary?tag=7060953c5bc967a2e947ecc514b77a37&format=Jpg&maxWidth=342&quality=90` with `Accept: image/jpeg` and **no key**: Yellowstone's poster (2026-10-07, MAM-35) | `200 OK`, `image/jpeg`, 34,972 bytes, `Cache-Control: public, max-age=31536000, immutable`, the asked tag as its `ETag`, `Vary: Accept`, `Content-Disposition: attachment`. The status line and headers only: the poster's bytes are not kept, so `FakeJellyfinServer.RecordedPoster` answers the test JPEG under these headers |
| `Jellyfin/image-primary-missing-response.txt` | The same route for the virtual special "Follow the Mouse" (`1728f2c0514906e2c522eb130d401193`), which has no primary image | `404 Not Found`, `application/json; charset=utf-8`, the body a JSON string: "Follow the Mouse does not have an image of type Primary". Read by `VendorResponses.Recorded` |
| `Jellyfin/image-primary-unknown-item-response.txt` | The same route for the id `00000000000000000000000000000000` | `400 Bad Request`, `text/plain`, "Error processing request.": an id Jellyfin doesn't know is a 400 here, not a 404. Read by `VendorResponses.Recorded` |
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
| `Tmdb/movie-details-short.json` | `GET /3/movie/223021?append_to_response=credits,recommendations,alternative_titles,external_ids` (2026-10-05, MAM-34) | Landing Stripling (1962): `runtime` 6, IMDb id `tt0056164` at the top and under `external_ids`, no collection, directed by Джин Діч, genres 16 and 35, and no alternative titles |
| `Tmdb/search-movie-without-year.json` | `GET /3/search/movie` for "Landing Stripling" with no year | The one film, 223021 |
| `Tmdb/search-tv-planet-earth.json` | `GET /3/search/tv` for "Planet Earth" | The first 20 of 41 shows: Planet Earth (1044, 2006) first, then Planet Earth II and III |
| `Tmdb/tv-details-planet-earth.json` | `GET /3/tv/1044?append_to_response=credits,recommendations,alternative_titles,external_ids` | Planet Earth: `episode_run_time` `[]` and `last_episode_to_air.runtime` 53, `external_ids.imdb_id` `tt0795176`, seasons 0 (24 episodes) and 1 (11), genre 99, and 16 alternative titles under `alternative_titles.results`, "Planet Earth" three times among them |
| `Tmdb/tv-episode-wwii-s01e01.json` | `GET /3/tv/91724/season/1/episode/1?append_to_response=external_ids&language=uk-UA` (2026-10-06, MAM-34) | The first episode of Greatest Events of World War II in Colour: "Серія 1" — TMDb's fallback, as it has no Ukrainian title —, aired 2019-07-12, 50 minutes, a director and four archive-footage guests, and `external_ids.imdb_id` `tt9103946`, the id Vasyl set by hand in Jellyfin |
| `Tmdb/tv-episode-planet-earth-s01e08.json` | `GET /3/tv/1044/season/1/episode/8` the same way | Planet Earth's "Джунглі", aired 2006-11-19, 51 minutes, with a Ukrainian overview and `external_ids.imdb_id` `tt0905877` |
| `Tmdb/tv-episode-not-found-response.txt` | The same of an episode TMDb doesn't have (`tv/91724/season/9/episode/99`) | `404` with TMDb's JSON, `status_code` 34, as for any resource it doesn't have. Read by `VendorResponses.Recorded` |
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
- **`torrents/info?includeFiles=true` has no recorded sample yet** (MAM-24). Its shape was read from
  qBittorrent 5.2's source (Web API 2.15.1; `includeFiles` came with 2.11.8, and 5.1 ignores it
  without a word): each torrent `torrents/info` lists, with a `files` array whose entries are what
  `torrents/files` gives (`index`, `name`, `size`, `progress`, `priority`, `availability`,
  `piece_range`), emitted only for a torrent whose metadata qBittorrent has, so a missing `files`
  means not known yet, never no files. `FakeQBittorrentServer` follows it — `files` only from 5.2 on
  and only for a torrent with `HasMetadata`, `has_metadata` on every torrent —, and
  `QBittorrentAdapterJobsWithFilesTests` reads hand-written answers of that shape. A live read on
  2026-10-09 of the reference installation's qBittorrent, 5.2 or later, through the app's own
  adapter confirmed it without recording the answer: 358 torrents, every one with `files`, 848
  files, each `name` relative to the save path with the torrent's folder first, as `torrents/files`
  names them.
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
- **Jellyfin reads one item only as a user, and its API keys are an administrator's** (MAM-34,
  [ADR 0026](../../../docs/adr/0026-a-tmdb-id-set-by-hand-is-the-owners-word-kept-per-library-item-and-written-into-the-library-as-ids-only.md)).
  `GET /Items/{id}` with the key and no user answers 400 "Error processing request."; with the
  `userId` of an administrator, or as `/Users/{user}/Items/{id}`, it answers the whole item. `GET
  /Users` with the key lists every user with `Policy.IsAdministrator` and `Policy.IsDisabled`, so the
  app reads an item as the first enabled administrator.
- **Jellyfin's item update replaces the item with what it is sent** (MAM-34). `POST /Items/{id}` with
  the item as read and only `ProviderIds` changed answered 204 with no body, and the item read again
  held the new ids. The only other fields that changed are those Jellyfin derives from the ids:
  `Etag`, `ExternalUrls` (the IMDb, TMDB and Trakt links) and, on the movie, `UserData.Key`, which
  went from the item's id to "223021" — Jellyfin keys a user's watch data by the item's ids; this
  item had no watch history, and one with it was not tried. The series took its show's id under the
  same `Tmdb` key, in 4.5 seconds, and posting it back without the key removed it. A wrong key is 401
  with no body and an unknown item 404 with problem details, on the read and the write alike.
- **What the update does beyond the item, read in Jellyfin's source, not in the samples** (MAM-34;
  `Jellyfin.Api/Controllers/ItemUpdateController.cs`, `UpdateItem`, at tag v12.1 and the same on
  master). Every update of a series rewrites and saves each of its seasons and episodes — their
  `SeriesName` and `CustomRating` always, their `OfficialRating` unless the child locked it —, each
  save moving its `DateLastSaved` and rewriting its NFO file were there one (the reference
  installation's media folders hold none) — the likely reason for the 4.5 seconds. For that Vasyl first
  had the app write no series (2026-10-06); later that day, on the follow-up's recording (below), he
  decided that a series is written like a film all the same: age ratings don't matter to him. By that
  source,
  each of the recorded series' two writes copied its ratings — it has none — onto its 3 seasons and
  149 episodes, but for an official rating a child locked. Nobody read their ratings before; the check's
  read-only listing of 2026-10-06, made early that day, counted 152 seasons and episodes, none of them
  rated, so whether any had a rating before is unknown. By the follow-up's write later that day the
  series was rated `TV-Y7` and 88 of its 149 episodes were rated (its seasons still none): what
  changed in between wasn't recorded — most likely a metadata refresh, after the 2026-10-05 writes had
  copied the series' missing rating over the children's. An update sent `People` rebuilds the item's people from their
  names, roles and types alone (`LibraryManager.UpdatePeople`), and one without it leaves them as they
  are, so the earlier write left them out. `ProviderIds` replace the item's ids as a whole
  (`ProviderIdsExtensions.SetProviderIds`): an id Jellyfin holds that fails its own format check is
  dropped by the write, though the earlier write sent it back as read. And an item a user may not see answers
  that user's read 404, as one that is gone does.
- **A refresh keeps an id set by hand** (MAM-34). A default refresh changed nothing; "Search for
  missing metadata" kept `Tmdb` and `Imdb`, added `Tvdb` and `TvdbSlug`, and filled the overview,
  genres, people, studios and tags — Jellyfin's own doing, asked for in the check. "Replace all
  metadata" was not run.
- **Written alone, the ids change nothing anyone sees; the lookup does** (MAM-34 follow-up, Vasyl's
  live checks of 2026-10-06 on Jellyfin 12.1.0, not recorded as samples). Jellyfin fetches an existing
  item's metadata only when it refreshes it: a Tom & Jerry film the app linked held IMDb `tt0035226`
  and TheMovieDb `40163` in Jellyfin, and nothing more. The documentary file "Greatest Events of
  World War II in Colour.S01E01.Blitzkrieg", a movie in «Документалки» (whose movie metadata fetchers
  are TheMovieDb and The Open Movie Database), had its IMDb Id set by hand to its episode's own,
  `tt9103946` (TMDb `tv/91724` season 1 episode 1); "Search for missing metadata" then gave it an
  overview, a year, genres and a poster — The Open Movie Database finds an episode by its IMDb id —,
  its Name kept and its "TheMovieDb Movie Id" left empty. The recorded lookup of that item an hour later
  answered 204 at once and changed only its `Etag`: what it could find, it had found.
- **A series' save gives every season and episode the series' rating, and the lookup leaves it**
  (MAM-34, Vasyl's second live check of 2026-10-06). The Penguins of
  Madagascar's series item — by
  then holding `Tmdb` 7869, `Imdb`, `Tvdb`, `TvRage`, `TvMaze` and `TvdbSlug`, and rated `TV-Y7` —,
  posted back as read without its `People`, answered 204 after 9.5 seconds
  (`series-item-update-2026-10-06-response.txt`) and changed only its `Etag`.
  Its 3 seasons, none rated before, and its 149 episodes, 88 rated before, all carried the series'
  `TV-Y7` afterwards, as `UpdateItem` copies the series' official rating onto every child it hasn't
  locked; their names
  and overviews (146 of 149) stayed. The "Search for missing metadata" asked right after changed
  nothing on the series in 180 seconds and gave no rating back: it fills only what is missing, and a
  rating copied from the series isn't. Whether any of the 88 had another rating before is unknown —
  the check counted them —, and nothing the app or Jellyfin keeps can put them back. Vasyl accepted it:
  the app writes a series like a film (2026-10-06,
  [ADR 0026](../../../docs/adr/0026-a-tmdb-id-set-by-hand-is-the-owners-word-kept-per-library-item-and-written-into-the-library-as-ids-only.md)).
- **Identify is a search by id and one synchronous Apply, and it replaces title, overview and images**
  (MAM-34, Vasyl's live check of 2026-10-06 on Jellyfin 12.1.0,
  [ADR 0026](../../../docs/adr/0026-a-tmdb-id-set-by-hand-is-the-owners-word-kept-per-library-item-and-written-into-the-library-as-ids-only.md)).
  This replaces the decision to write ids alone; the records above stand as they were made.
  1. **Calls.** `POST /Items/RemoteSearch/Movie|Series` with
     `{"SearchInfo":{"ProviderIds":{"Tmdb":"223021"}},"ItemId":"<dashed guid>","SearchProviderName":"TheMovieDb"}`
     answers 200 with one row (`Name`, `ProviderIds`, `ProductionYear`, `PremiereDate`, `ImageUrl`,
     `SearchProviderName`, `Overview`, `Artists`); it works without `ItemId` and without a provider name (then
     only TheMovieDb answered a `Tmdb` search); the key must be `Tmdb` — `tmdb` answers 200 `[]`, as an unknown id
     does; a wrong key is 401 with no body (0.03 s). `POST /Items/RemoteSearch/Apply/{id 32-hex}?replaceAllImages=true`
     with that row as the body answers 204 (empty body, a `Content-Language` header) **after the work is done**; an
     unknown item is 404 problem+json (the all-zero guid is 400), a wrong key 401.
  2. **Namespace trap.** A Movie search with the TV id 7869 returned an unrelated film, "The Fest" (2005); a Series
     search with the film id 223021 an unrelated show. An id may only go to the search of its own kind.
  3. **Episodes as films.** `RemoteSearch/Movie` with `{"Imdb":"tt9103946"}` and `SearchProviderName` `The Open Movie
     Database` returned one row (`Blitzkrieg`, 2019, `Imdb` only, a Media-Amazon image, no overview); with
     `TheMovieDb` `[]`; with no provider name the same OMDb row.
  4. **Timings.** Film 1.8 s; the Penguins of Madagascar (3 seasons, 149 episodes) 159.3 s (about a second per
     episode); the episode through OMDb 21.3 s; no timeouts. Jellyfin's Apply uses `CancellationToken.None`, so it
     carries on when the client gives up.
  5. **What changed.** Film: Name, Etag, Primary (768x576 → 1112x1668), Backdrop; Overview/Genres/Studios/Tags/
     People/UserData unchanged (an earlier refresh had filled them); `ProviderIds` kept their keys (Tvdb and TvdbSlug
     re-added by other providers). Series: five images re-downloaded, Etag changed, 3 seasons and 149 episodes
     unchanged, episode names/overviews/images unchanged, episodes lost their `TvMaze` id, `UserData` preserved
     (15.4 % played). Episode-movie: Name and year corrected (2025 → 2019), Primary replaced by a **1920x1080 image
     — not OMDb's 960x1440 poster; presumably the Screen Grabber's video frame** (not isolated) —, overview kept
     (168 characters), `ProviderIds` and `UserData.Key` unchanged.
  6. **Files.** The folder listings of the three items before and after (`find -printf` inside the container:
     162 / 153 / 11 entries) were identical; Jellyfin's images are files under its own `/config/metadata/library`.
  7. **Libraries** (`virtual-folders.json`): the four media libraries `SaveLocalMetadata` false, `MetadataSavers`
     `[]`, no subtitle download languages; «Колекції» (boxsets) `SaveLocalMetadata` true and no `MetadataSavers` list — the one
     that writes beside the media. `PreferredMetadataLanguage`: Фільми `uk`, Мульфільми and Документалки empty,
     Серіали `en`; Apply has no language field. Movie fetchers (cartoons): AniList, TheMovieDb, The Open Movie
     Database, TheTVDB; documentary: TheMovieDb, The Open Movie Database.
  8. **Language (source-read, not exercised live).** The search body may carry `SearchInfo.MetadataLanguage` /
     `MetadataCountryCode`; when blank Jellyfin falls back to the server's (`ProviderManager.cs:969-977`; `ItemId`
     only picks the providers), while Apply refreshes the item in its library's language. All three recorded items
     were in libraries with a blank language or `en`.
- **A library poster is one GET that needs no key, and its tag only versions the URL** (MAM-35, Vasyl's
  read-only live check of 2026-10-07 on Jellyfin 12.1.0). `GET /Items/{id}/Images/Primary` answered without a key.
  `format=Jpg` makes it a JPEG — without it, asked with `Accept: image/webp,*/*`, Jellyfin chose WebP —; `maxWidth=342`
  keeps the image's own shape (a poster is 342x513, the Tom & Jerry Kids episode's frame 342x237); and `quality=90` made
  Yellowstone's poster 35 KB where Jellyfin's default quality made it 115 KB. The tag is not checked: a wrong one answered
  200 with the same image and echoed the asked tag as its `ETag`, so neither says which version came back — the tag the
  sync reads (`enableImages=true&enableImageTypes=Primary&imageTypeLimit=1`) says which is current. An item without the
  image answers 404 with a JSON string, an id Jellyfin doesn't know 400 `text/plain`, and the route doesn't redirect. An
  episode carries its own `ImageTags.Primary` beside its series' and season's tags; the sync reads its own only.
- **TMDb gives an episode its own IMDb id under `external_ids`** (MAM-34). Asked with
  `append_to_response=external_ids`, an episode's details carry `external_ids.imdb_id` (and TheTVDB's
  id), no `imdb_id` of their own; its `name` is TMDb's fallback "Серія 1" where it has no Ukrainian
  title. An episode it doesn't have answers its 404 with `status_code` 34.
- **TMDb gives a short's runtime, and a show's only through its episodes** (MAM-34). Landing
  Stripling's `runtime` is 6 minutes, against 6.2 for the file Jellyfin lists; Planet Earth's
  `episode_run_time` is `[]`, so its runtime is its latest episode's, 53 (its season 1 episode 8,
  "Джунглі", runs 51). A film's other titles are under `alternative_titles.titles`, a show's under
  `alternative_titles.results`, and a show's IMDb id only under `external_ids`. A search for "Planet
  Earth" — the cleaned title of "Planet_Earth_E08 - Jungles" — names 1044 first, before Planet Earth
  II and III.
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
