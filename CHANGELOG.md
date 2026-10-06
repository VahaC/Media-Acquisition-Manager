# Changelog

All notable changes to Media Acquisition Manager are recorded here. The format is loosely based on
[Keep a Changelog](https://keepachangelog.com/), and the project follows
[semantic versioning](https://semver.org/) — released as tags of the Docker image
`vahac/media-acquisition-manager`.

## [0.2.1] — 2026-10-05

### Added

- **The owner connects TMDb as a metadata catalog, the fourth kind of integration (MAM-33, ADR 0005,
  ADR 0007).** The Integrations page adds an integration of type **TMDb** (`tmdb`), kind `Metadata`
  ("Metadata catalog"), at `https://api.themoviedb.org`, with TMDb's API Read Access Token in the API
  key field (Vasyl's decision 1): the dialog says "TMDb's API address, https://api.themoviedb.org. In
  the API key field, enter TMDb's API Read Access Token from themoviedb.org → Settings → API, not the
  shorter API key." and shows TMDb's attribution, "This product uses the TMDB API but is not endorsed
  or certified by TMDB.". `GET /api/settings/integrations/adapter-types` lists it with `apiKey`
  `Required`, no user name or password, and `reportsFilePaths` `false`; the list's kind filter takes
  `Metadata` and offers **Metadata catalogs**. The adapter appends `/3` to an address that doesn't end
  in it, sends the token only as `Authorization: Bearer`, asks everything in Ukrainian
  (`language=uk-UA`) with the original title beside it, and only ever sends GETs. **Test connection**
  asks `GET /3/authentication` and answers "Connected to TMDb.", or for a refused token "TMDb refused
  the key (401 Unauthorized). Enter the API Read Access Token from themoviedb.org → Settings → API,
  not the shorter API key.", and for an address that isn't TMDb's "TMDb's API wasn't found at this
  address (404 Not Found). Use https://api.themoviedb.org." or "The service at this address answered,
  but not the way TMDb's API does. Check the address: TMDb's is https://api.themoviedb.org.". Requests
  start at least 25 ms apart, whoever sends them; a 429 holds every request back for its
  `Retry-After` (2 seconds without one, 10 at most) and the same GET is sent again at most three
  times, then the call fails with "TMDb is asking the app to slow down (429 Too Many Requests), and
  still was after three retries. Try again in a minute.". Adding a TMDb integration switched on, or
  switching one back on, queues a computation of the recommendations; the integration's save writes
  its one audit row as before, and the queuing none.
- **The app computes recommendations from the library and the catalog in the background (MAM-33, ADR
  0023, ADR 0025).** A run (202 + run row + reconciler) starts after every successful library sync
  (`LibrarySynced`), when a TMDb integration is added switched on or switched back on
  (`CatalogConnected`), when a user switches on a tab no other user had on (`TabEnabled`), when the
  newest releases of a source resolve to new works (`FeedRead`), and on the owner's **Recompute
  now** (`Requested`); a trigger that finds a run waiting joins it, and without a catalog nothing is
  queued. At most one run is queued and one running (`IX_RecommendationRuns_OneRunPerActiveStatus`).
  `GET /api/recommendations/runs/latest` gives every signed-in user the running run, else the newest
  by when it was queued, as `{ id, status, trigger, requestedByUserName, createdUtc, startedUtc,
  finishedUtc, seedCount, candidateCount, stage, stageDone, stageTotal, error }` — `status` `Queued`,
  `Running`, `Succeeded` or `Failed`, `seedCount` the library works it started from and
  `candidateCount` the works it recommends, both `null` unless it succeeded, `requestedByUserName`
  and `error` for the owner only —, or 204 with no body before the first run. A running run says how
  far it is, as Vasyl asked after a first computation of his library took 3 min 15 s: `stage` is
  `ReadingLibrary`, `FindingLibraryWorks`, `ReadingWorks`, `ReadingCollections`, `ReadingPeople`,
  `Discovering` or `Saving`, `null` when the run isn't running, and `stageDone` of `stageTotal` the
  stage's items done (240 of 815), both `null` for a stage without counts; the run row
  records them at each change of stage and at most every 2 seconds within one
  (`RecommendationRunProgressHttpTests.Recompute_writes_each_stage_in_order_with_its_counts_and_ends_with_none`,
  `RecommendationRunProgressHttpTests.Recompute_writes_a_stages_count_at_most_every_two_seconds_and_always_at_its_end`
  and `RecommendationRunProgressHttpTests.LatestRun_while_a_run_reads_the_catalog_shows_its_stage_and_counts_to_the_owner_and_a_member`).
  `POST /api/recommendations/runs` is owner-only (403 "Only
  the installation owner can do this." for anyone else) and answers 202 with the queued run, or with
  the one already waiting; 409 "Connect a metadata catalog under Integrations first." without a
  switched-on catalog of a type this version serves, and 503 "Too many recommendation runs are
  waiting. Try again in a minute." when the queue is full, the run kept as failed with that sentence.
  A run takes the first switched-on catalog by name, tests its connection before anything else, and
  asks it only for the kinds some user's tabs want — none at all when every tab of every user who can
  sign in is off. A failure of the catalog fails the run with the catalog's sentence, masked of the
  token, and keeps the previous recommendations; a run a restart interrupted fails with "The app
  restarted before these recommendations were computed. The previous ones are kept.", one that threw
  with "The recommendations stopped computing because of an error in the app. Its log says more.",
  and a queued one is queued again; the start queues none of its own. A run replaces every
  recommendation in one transaction at its end. The log gives ids and counts, never a title or an
  address.
- **Each recommendation says why it is there, in four lists ordered by a fixed score (MAM-33, ADR
  0023).** A run starts from the library's films and series the catalog knows — by their own TMDb or
  TMDb TV id, else, as a possible match, by their IMDb id or by the one result their title, cleaned
  of what a file name adds ("120. Landing Stripling" is "Landing Stripling"), and year match — and
  keeps each reason as a chip: **Collection**, a collection's released parts the library lacks
  ("Guardians of the Galaxy · 2 of 3"); **Director** and **Creator**, the released works of a person
  two or more of the library's films or shows are by ("Ridley Scott · 12 in your library");
  **Recommended**, what TMDb recommends beside the library's works, with up to five of their titles
  and how many of the library's catalog works recommend it, a film the library holds twice counting
  once ("Because you have X, Y and 3 more"); **MissingSeasons**, a held series' aired seasons it
  holds no episode of ("Missing seasons 4–5"), season 0 never, a partly held season never;
  **NewAtSource**, a work the app first saw among a release source's newest releases in the last two
  weeks ("New on Toloka · 2 days ago", dated by the source's publish date); and **TopRated**, for a
  tab with fewer than 10 of the library's works, TMDb's best-rated works of the tab among those at
  least 300 people rated, listed only once a release source carries one. A documentary goes to **Documentaries**, else an animation, anime included, to
  **Cartoons**, else a film to **Movies** and a show to **Series** (Vasyl's decision 2). Reasons of
  taste — Recommended, Director, Creator, TopRated — are kept only for a work whose rating holds when
  pulled toward 5.0 by 50 imagined votes (`votes × (rating − 6.0) ≥ 50`); the others whatever the
  rating. The score is `8·MissingSeasons + 6·Collection + 3·(Director or Creator) + 2·log2(1 +
  recommenders) + 1·NewAtSource + 0.5·TopRated` plus the shares of the library's works sharing a
  genre and the decade (0 to 1 each), plus `1.5·(weighted rating − 6)`, the weighted rating being
  `(votes × rating + 50 × 5.0) / (votes + 50)`; then votes, title and id break ties. The weights were
  set after the first computation on the reference library, whose first formula — recommenders one
  point each up to ten, a hundredth of the rating — put films rated around 6.2 on top of Movies and
  buried "2 of 3" collection parts at ranks 17, 51 and 98; now Guardians of the Galaxy Vol. 3 (7.9,
  "2 of 3" and its director), Back to the Future Part II and Blade Runner 2049 lead, and those parts
  stand at 1, 6 and 4. A work not released yet is never listed; a work the library holds — by its
  TMDb or IMDb id, or a work of its kind it matches by title and year — and one a scanned file's name
  claims by title and year are left out, a held series keeping only its missing seasons.
- **The catalog's answers are kept in tables of their own and read again after 30 days (MAM-33, ADR
  0025, ADR 0006).** Films and shows, genres, collections, people and the answers to "which work is
  this" — nothing found included — are stored with the TMDb integration as their source and the
  catalog's own record id (`movie/118340`, `tv/73586`), and asked again only when older than 30 days,
  so a run of an unchanged library makes a handful of calls: the connection test, and for a tab with
  few of the library's works the discovery pages of its best-rated works, which are asked every run.
  Nothing is written to the library's works, titles or identifiers, and a possible match is never
  shown on the Library page or used by a search.
- **The Recommendations page (MAM-33, ADR 0023).** **Recommendations** in the header, after
  **Library**, opens `/recommendations` for every signed-in user, and is hidden while all four of
  their tabs are off. Its tabs are the user's lists that are on, the tab in the address (`?tab=`); its
  filters are the reason, the genre, the decade, films or series in **Cartoons** and
  **Documentaries**, and **Available only**, each select counting its values over the whole tab; its
  **Sort** — **Best match** (the score), **Rating**, **Most votes**, **Newest**, **Oldest**, **Title
  A–Z** or **Most releases** — is kept in the address too (`&sort=`). A
  card shows the poster, the Ukrainian and the original title, year, genres, rating with its votes,
  the reasons as chips, what the release sources carry — "4 releases · up to 2160p · Ukr", "Not found
  (checked 3 days ago)" or "Not checked yet" — and **Downloading** while a download job of it is
  followed. **Releases** opens the Search page with the title, the year and a film's TMDb id
  (`/search?q=…&tmdbId=…`), from which the Download dialog sends it as any release; **Not
  interested** hides it for this user alone, with **Undo** (Vasyl's decision 3). Nothing is ever sent
  by itself. The page shows when the lists were computed — while a computation runs, its stage, how
  far through it is ("240 of 815") with a progress bar, and how long ago it started — and, to the
  owner, **Recompute now**,
  disabled with "Connect a metadata catalog under Integrations first." without a catalog; a member
  reads "The last computation failed. The owner can see why." for a failed one. TMDb's attribution
  closes the page.
  `GET /api/recommendations?tab=&reason=&available=&genreId=&decade=&kind=&sort=Score&page=1&pageSize=25`
  answers `{ items, totalCount, page, pageSize, facets: { reasons, genres, decades, kinds },
  catalogConnected, lastRun }`, each item `{ id, kind, catalogId, title, originalTitle, year, genres,
  rating, voteCount, posterUrl, reasons: [{ kind, name, held, total, titles, seasons, dateUtc }],
  availability: { checkedUtc, found, releaseCount, bestResolution, hasUkrainianAudio, maxSeeders },
  downloading, searchText, searchTmdbId }`, filtered, sorted and paged on the server (25 a page by
  default, at most 100), without asking the catalog or a source. `sort` is `Score` by default — the
  score, then votes, title and catalog id —, `Rating` (the weighted rating, so a 10.0 of three votes
  doesn't top the tab), `Votes`, `Newest` or `Oldest` (by release date, a work of no known date last),
  `Title` (in Ukrainian alphabetical order, as the library's works) or `Releases` (the availability
  check's release count: the works found first, then those not found, then those never checked),
  ties falling back to the score's order (`RecommendationListSortHttpTests.List_sorted_breaks_a_tie_by_the_score_then_votes_then_title_then_catalog_id`;
  one test a sort, and `RecommendationListSortHttpTests.List_with_a_sort_it_doesnt_take_is_400_with_why`). The facets count the caller's whole list of the tab before
  the filters, whatever the sort, the reasons in a card's order and the genres by name. 400 with the first broken rule's sentence, the value cut at 32
  characters: "Choose a tab: Movies, Series, Cartoons or Documentaries.", "“x” is not a recommendation
  tab. Use Movies, Series, Cartoons or Documentaries.", "“x” is not a recommendation reason. Use
  Collection, Director, Creator, Recommended, MissingSeasons, TopRated or NewAtSource.", "“x” is not
  an availability filter. Use true or false.", "“x” is not a genre id, which is a number such as
  18.", "“x” is not a decade. Use its first year, such as 1990.", "“x” is not a kind of work. Use
  Movie or Series.", "“x” is not a sort. Use Score, Rating, Votes, Newest, Oldest, Title or
  Releases.", "Page must be 1 or more.", "Page size must be between 1 and 100." and "This tab
  is switched off in your profile.". Every user's download jobs count, matched to a work by its kind
  and an id or the title and year the release claims, for a held series by a missing season or none:
  a followed job (`Created`, `Submitted`, `Downloading`, `Paused`) shows **Downloading**, one that
  ended without failing (`Downloaded`, `NeedsLinking`, `Cancelled`) leaves the work out, even beside a
  followed one, and a failed one changes nothing. `PUT` and
  `DELETE /api/recommendations/dismissals/{Movie|Series}/{catalogId}` mark and unmark "Not
  interested" for the caller alone, 204 either way and with no audit row, or 400 "“x” is not a kind of
  work. Use Movie or Series." or "“x” is not a catalog id, which is a number such as 603.".
  `GET /api/recommendations/posters/{fileName}` answers a catalog work's poster, `image/jpeg` or
  `image/png` with `Cache-Control: private, max-age=2592000, immutable`: fetched once from TMDb's
  image host without any credential and kept in `<data>/posters/`, served from there with or without
  a catalog; a bare 404 for a name that is no poster's or no catalog work's, one the image host
  doesn't have, or one not kept while no catalog is connected; 502 when the catalog can't send it,
  with its masked sentence for the owner and "The metadata catalog couldn't send this poster." for
  everyone else.
- **The release sources are asked about recommended works within each user's daily limit (MAM-33,
  ADR 0024).** A background check runs as the app starts, then 15 minutes after each pass ends, and
  searches at most 4 recommended works a pass for the whole installation; it asks nothing while no
  release source is switched on, and needs no catalog. A work is due when it was never checked, or
  was checked a day or more ago and found, or 7 days or more ago and not found; one check per work is
  shared by every user. Each search serves one user's list and counts against that user's limit over
  the last 24 hours (Vasyl's decision 5), even when it fails: the limit is split evenly between their
  tabs that are on, the remainder to the first tabs (62 over four tabs is 16, 16, 15 and 15), the due
  tab furthest under its share goes first, and a tab at its share gets more only once no tab under its
  share has a work due. Users take turns, the one searched for least recently first, then by user
  name; within a tab the works never checked go first, then by score. A user's "Not interested" works
  and the works of a tab they have off are never due for them, and a limit of 0 searches nothing. A
  film is asked through the movie search with its year and a show through the TV search with none, by
  the original title when every letter of it is Latin, else by the catalog's Ukrainian title, and no
  id is sent; a release counts when it is of the work's kind and its title or description claims the
  work by its TMDb or IMDb id or by title and year — a pack "(S5) (2022-2024)" counts for the show
  begun in 2018 — and, for a held series, a missing season or no season. Per work the app keeps how
  many releases, the best resolution claimed, whether one claims Ukrainian audio, the most seeders and
  when; what the sources that answered listed is stored, and when none answered nothing is stored, the
  work stays due and the pass stops. These searches store no search and no release, and the log of
  them is forgotten after two days: the Search page asks again when you choose **Releases**.
- **New on Toloka: each release source's newest releases are read every 3 hours (MAM-33, ADR 0024,
  ADR 0004).** A read runs as the app starts, then 3 hours after each read ends, while a catalog is
  connected and some user has a tab on: each enabled source gets one general search with no text in
  the categories of the kinds the tabs want — Toloka answers its latest 90 — and each release is kept
  by its source and id (a listing without one is left out), updated when it is listed again, with no
  download link. Up to 100 releases not yet resolved, newest first, are looked up in the catalog by
  the titles in their description, then in their title, each with its year; a pack of a show's later
  seasons is looked up by its title alone and taken only when exactly one show of that title began by
  its first year. A failure of the catalog leaves the rest for the next read, and when any release
  turned out to be a work a computation is queued. A work first seen there in the last 14 days is
  recommended as "New on Toloka". A release no source listed for 30 days is forgotten, and the
  entries go with their source; the reads count against no user's limit.
- **Every user sets their own lists and daily limit in Profile (MAM-33, ADR 0024).** The Profile page
  gains a **Recommendations** card: **Movies**, **Series**, **Cartoons** and **Documentaries**, each
  on for a user who never saved (Vasyl's decision of 2026-10-05), the **Daily search limit** (60
  until changed, 0 for none) and "N searches in the last 24 hours.", with its own **Save**.
  `GET /api/profile/recommendations` answers `{ movies, series, cartoons, documentaries,
  dailySearchLimit, searchesLast24Hours }`; `PUT` takes the first five, every one required, and
  answers 200 with the settings, or 400 "Say whether the Movies tab is on." (and so for Series,
  Cartoons and Documentaries), "The daily search limit is a whole number from 0 to 500.", or for a
  body that isn't the settings "Send the settings as JSON: movies, series, cartoons and documentaries
  true or false, and dailySearchLimit a whole number from 0 to 500.". Each user reads and saves only
  their own, and a save writes no audit row. A save that switches on a tab no other enabled user has
  on — a user who never saved has every tab on — queues a computation in the saver's name
  (`TabEnabled`) or joins the one waiting; a full queue never fails the save.
- **The Search page takes a film's TMDb id in its address (MAM-33).**
  `/search?q=Alien (1979)&tmdbId=348` narrows the search it creates by that id, as the **TMDb id**
  field does; anything but digits is ignored.
- **Assigning a TMDb id is Jellyfin's Identify, and Remove link forgets only (MAM-34, Vasyl's
  decision of 2026-10-06, ADR 0026).** The first builds of this version wrote a link's ids into the
  item and then asked Jellyfin to look up what the item lacked; the ids changed nothing anyone could
  see, as 217 of the 220 items the reference installation had linked still showed the title their
  file name gave. Saving a link now makes Jellyfin do what the **Identify** button of its own web
  page does, with the same two calls and one read: `POST /Items/RemoteSearch/Movie` (a film, and an
  episode Jellyfin lists as a film) or `POST /Items/RemoteSearch/Series` (a show) with the id under
  Jellyfin's exact `Tmdb` or `Imdb` key, then `POST
  /Items/RemoteSearch/Apply/{id}?replaceAllImages=true` with the one row it found, then the item
  read again to confirm it. Jellyfin replaces the item's title, overview and images with TMDb's, in
  the metadata language of the item's library: that is the library's own setting and the app doesn't
  choose it — «Фільми» is Ukrainian on the reference installation, «Мульфільми» and «Документалки»
  have none set, so Jellyfin uses its server's default, English in the live check —, and a field
  locked in Jellyfin stays as locked: an item locked as a whole, or by its name or overview, is
  refused with the reason. A TMDb id goes only to the search of its own kind, as the other kind's
  search answers an unrelated work for it (a film search for the show's id 7869 answered “The
  Fest”). A library that saves artwork or metadata next to the media — an `Nfo` saver, "save artwork
  into media folders", trickplay or downloaded subtitles saved with the media, or options the app
  can't read — is refused before any search, so the app writes no file or folder and moves or
  renames nothing; the live check found the folder listings of three identified items identical
  before and after. Identify has no undo, and the app sends it only when the owner saves a link,
  retries one, accepts the confident suggestions or chooses **Update Jellyfin**. In Vasyl's live
  check of 2026-10-06 on Jellyfin 12.1.0, where he typed `y` for each apply, a film took 1.8 s, an
  episode through The Open Movie Database 21.3 s and The Penguins of Madagascar (3 seasons, 149
  episodes) 159.3 s, which is why the app never waits for an identification inside a request.
- **The owner links a library work to its TMDb film or show, and Jellyfin identifies the item as it
  (MAM-34, ADR 0026, ADR 0005).** Each card of the Library page gains **TMDb…**, the owner's (anyone
  else sees it disabled with "Only the owner can link works to TMDb."), which opens a dialog: the
  work's items with the ids Jellyfin reports and their links, the prepared suggestions, a field for
  a pasted link and a free search. Above **Save** it says what Jellyfin will replace — the item's
  title, overview and images —, that there is no undo and that files and folders are not touched.
  `GET /api/library/works/{id}/tmdb` answers it as `{ work, items, catalogConnected, suggestions,
  search, searchTitle, searchYear }`: `work` as the works list gives it; each item `{ linkId,
  sourceItemId, sourceName, itemTitle, itemKind, canWrite, cannotWriteReason, target, catalogKind,
  catalogId, catalogTitle, seasonNumber, episodeNumber, imdbId, writeState, writeError,
  writeStateUtc, setUtc, setByUserName, libraryIdentifiers, conflictingValue, episodeImdbId,
  identifiedUtc }`, `libraryIdentifiers` being what the last sync read for the item,
  `conflictingValue` a third TMDb id Jellyfin holds beside the link, `canWrite` whether Jellyfin
  would identify the item — it does for a film or a series item of a library whose adapter can,
  while the integration is switched on, and `cannotWriteReason` says why not — and `identifiedUtc`
  when the app last read the item showing the link's match, `null` for a link it never did, which is
  every link set under the first rules; `search` `{ searchedUtc, noneFitsUtc, pending, inPopulation
  }`; `searchTitle` and `searchYear` what the free search starts from — the work's title cleaned as
  the suggestions' searches clean it ("Landing Stripling" of "120. Landing Stripling", "The Penguins
  Of Madagascar" of a series named after its release's folder), and the year that title names in
  brackets (the first of a span, 2008 of "(2008-2015)") else the work's, as TMDb finds nothing by
  the raw title; a bare 404 for an unknown work. `PUT /api/library/works/{id}/tmdb` with `{
  catalogKind, catalogId, seasonNumber, episodeNumber, sourceItemIds }`, every field a string and
  `sourceItemIds` left out for every item of the work, links each item — one link per library item,
  as a work can hold several — to the TMDb film (`Movie`) or show (`Series`), or replaces its link,
  and answers 200 with the dialog at once, each item to be identified `Queued`: the request never
  waits for Jellyfin, and the dialog asks again every 2 seconds while an item waits or is being
  identified. A film item with a film and a series item with a show are linked as that work (`Work`)
  and identified by their TMDb id, a series' apply also refreshing its seasons and episodes: on The
  Penguins of Madagascar they kept their names, overviews and images, and the series its played
  state. A film item, or an episode of no series, with a show is an episode of it (`ShowEpisode`),
  optionally with a season (0–999) and an episode (1–9999), identified as the next bullet says. 400
  "Choose a TMDb film (Movie) or show (Series).", "“x” is not a TMDb kind. Use Movie or Series.",
  "Give the TMDb id to link.", "“x” is not a TMDb id.", "“x” is not a season number. Use 0 to 999.",
  "“x” is not an episode number. Use 1 to 9999.", "An episode number needs a season number.",
  "Season and episode numbers go only with a show linked to a film or to an episode.", "A show or an
  episode can be linked only to a TMDb show.", "Link its series instead: an episode is known by its
  series' ids." or "“x” is not an item of this work."; 404 "TMDb has no film 223021." (or "show");
  409 "Connect a metadata catalog under Integrations first.", "No library reports this work, so
  there is nothing to link." or, while one of its links is being identified, "A write to the library
  is under way for this work; try again in a moment."; 502 with TMDb's sentence, masked of the
  token, only when TMDb can't be read. A save that changes nothing records and queues nothing. Every
  link and its audit row (`LibraryLinkSet` or `LibraryLinkReplaced`, with the ids the item held and
  the ids linked) are saved first, in one transaction. Then a background worker identifies each
  queued link whose library can identify its kind of item and is switched on, the owner's own clicks
  before a batch's: the connection test, `GET /Users` for the first enabled administrator, `GET
  /Items/{id}?userId=` as that user, `GET /Library/VirtualFolders` for the options of the item's
  library, the search — one request, the id under its exact key, with the library's metadata
  language when it has one —, one apply carrying the row found, on a client of its own with a
  20-minute timeout, never sent a second time whatever becomes of its answer, and the same read of
  the item again. The identification is confirmed only when the item shows the row's name and year,
  holds its id under the identification's key, has a changed `Etag` or primary image and, when the
  row has an image, a primary image: an item that took only the ids, or nothing, is `NotWritten`
  with a sentence saying so, never "Written". An item that already shows the whole match — the name,
  the year when the row has one, the id, a primary image when the row has an image and, when the row
  has an overview, TMDb's — is marked identified with no apply at all. An apply that left the app is recorded (`LibraryItemIdentified`, with the
  ids before and after) whether or not it was confirmed. A link the owner sets or retries replaces
  the ids Jellyfin holds, as Jellyfin's own Identify does; one queued by **Accept all** or **Update
  Jellyfin** does not (below). A set, a replace or a removal queues a computation of the
  recommendations (`LinkChanged`). A series link takes its episodes' file identities from a sync
  when its work holds several series items of the library, as which episode is whose is known only
  there.
- **Each linked item says whether Jellyfin identified it (MAM-34, ADR 0026, ADR 0008).** A link's
  `writeState` is `KeptInApp` (an episode's link that has no id to identify it by, an episode item's
  link, or a library type that can't identify: "“x” can't identify items, so a link stays in the
  app."), which no sync changes, `Queued`, which no sync changes either, `Writing` (being
  identified), `NotWritten` with `writeError` (owner only), `Written` once the app confirmed the
  identification — `identifiedUtc` says when —, `InLibrary` once a sync read the link's id from
  Jellyfin, or `Lost` when a sync reads an identified link without it; the card and the dialog show
  "Kept in the app", "Waiting to identify", "Identifying…", "Not identified yet", "Identified in
  Jellyfin" and "Jellyfin lost it". A link set under the first rules, whose item may hold its id,
  shows "Not identified yet" until it is identified, on its Library card too: the card's `link` says
  so with two booleans, `identified` and `underWay`, and the page asks again while an identification
  runs. A switched-off Jellyfin is never asked: the link is `NotWritten` with "“Jellyfin” is
  switched off. Switch it on under Integrations, then retry.". Every other refusal or failure is
  `NotWritten` with its sentence, nothing identified: "Jellyfin's library “x” saves {what} next to
  the media, so identifying this item could change files in your media folders; the app left it
  alone. Switch that off in the library's settings in Jellyfin, then retry." (an `Nfo` saver,
  artwork and metadata, trickplay images or downloaded subtitles saved with the media), "The app
  couldn't tell what this item's Jellyfin library saves next to the media (…), so it left the item
  alone.", "This item is locked in Jellyfin (the whole item, its name or its overview), so Jellyfin
  wouldn't take the match. Unlock it there, then retry.", "Jellyfin found no film with TMDb id
  223021. Check the id, and that TheMovieDb is among this library's metadata sources in Jellyfin."
  (and the show's, and the Open Movie Database's, and "Jellyfin found more than one result for …, so
  the app picked none."), "Jellyfin took the match, but the item read again doesn't show it (its
  name, year or id): it may be locked, or its metadata sources failed. Its own log says why; Retry
  checks again.", and, when the answer to the apply was lost, late or a 5xx, "The match was sent to
  Jellyfin, but its answer never came, and the item read again doesn't show it. Jellyfin may still
  be working on it: Retry in a few minutes checks again before sending anything." (and "… didn't
  answer within 20 minutes …", "Jellyfin answered the match with …"); Jellyfin's own refusals say
  why: "Jellyfin refused to identify the item. Check that the API key belongs to an administrator."
  (403), "Jellyfin refused the match it was sent; nothing was changed." (400), "Jellyfin no longer
  has this item; sync the library and link it again." (404), "Jellyfin has no enabled administrator
  account to read the item as.", "Jellyfin's administrator account can't see this item (a parental
  limit, a blocked tag or no access to its library), so the app can't read it as that account." —
  told apart from an item that is gone by the user-less `GET /Items?ids=` —. The apply is sent at
  most once: a **Retry** a few minutes after a lost answer reads the item first, and marks it
  identified without a second apply when it shows the match; a series whose apply was sent less than
  10 minutes ago waits instead, with "Jellyfin may still be working on this series after the last
  attempt; try again after … UTC.", nothing asked of Jellyfin. A sync never erases a refusal: it
  judges only `Written`, `InLibrary` and `Lost` links, so a `NotWritten` link stays so, with its
  sentence, while its item holds the id. An item's **Retry**, **Identify again** and **Identify in
  Jellyfin** queue its link again with `POST /api/library/tmdb/links/{linkId}/write`; `POST
  /api/library/works/{id}/tmdb/write` does the same for every link of the work; each answers 200
  with the work's dialog, the items `Queued`, and each asks for a confirmation in the dialog first,
  as it replaces the item's title, overview and images and can't be undone. The owner's click lifts
  the protection a batch put on the link (below). 409 "There is nothing to write: no item of this
  work is linked.", "There is nothing to write: " with why a link stays in the app ("Without a
  season and an episode the app has no id of this episode to identify it by.", "TMDb knows no IMDb
  id of this episode, so its link stays in the app.", "Jellyfin identifies movie and series items
  only, so a link on an episode stays in the app." or "“x” can't identify items, so a link stays in
  the app."), "There is nothing to write: the library no longer reports this item.", the
  switched-off sentence, or the identification-under-way one; a bare 404 for an unknown work or
  link.
- **Removing a link forgets it in the app only (MAM-34, ADR 0026).** **Remove link** in the dialog,
  after a confirmation that says Jellyfin keeps what the identification gave the item: `DELETE
  /api/library/works/{id}/tmdb` deletes the work's links with `LibraryLinkRemoved` (the link's ids
  before, none after) and calls Jellyfin never, so it works while the integration is switched off or
  gone; 200 `{ dialog }`, 204 when the work has no link, 409 "A write to the library is under way
  for this work; try again in a moment." while one of its links is being identified, a bare 404 for
  an unknown work. A link still waiting in the queue is simply deleted, and the worker passes it
  over. Nothing is put back: the title, overview, images and ids the identification gave the item
  stay in Jellyfin — and the ids an earlier build that only wrote ids put there —, and replacing a
  link is a new identification of the new id. `DELETE /api/library/tmdb/links/{linkId}` removes one
  link the same way — the **Unattached** list's **Remove**, whose item Jellyfin no longer lists —:
  204, 409 as above, a bare 404 for an unknown link.
- **TMDb suggestions are prepared in the background after every sync (MAM-34, ADR 0026, ADR 0025).**
  A run (202 + run row + reconciler) starts after every successful library sync (`LibrarySynced`)
  while a catalog is connected, when a TMDb integration is added switched on or switched back on
  (`CatalogConnected`), and on the owner's **Prepare again** (`Requested`), which searches every work
  again; a trigger that finds a run waiting joins it, at most one is queued and one running
  (`IX_LibraryLinkSuggestionRuns_OneRunPerActiveStatus`). It searches TMDb for every film without a
  TMDb id, series without a TMDb TV id and episode of no series, none with a link: by its titles
  cleaned of what a file name adds and its folder's name, with its year and without, films and shows
  for a film or an episode, shows for a series; keeps ten candidates a work, each read through the
  catalog cache, and the search for 30 days unless the work's titles, folder or year change — one
  question asked once a run, however many works of a folder ask it —; and reads up to 50 identified
  works of each library so its genre profile is known. A catalog failure
  ends it with the catalog's masked sentence, keeping the works done; "The app restarted before these
  TMDb suggestions were prepared. The works searched so far are kept.", "The TMDb suggestions stopped
  being prepared because of an error in the app. Its log says more." and "The metadata catalog was
  removed while the TMDb suggestions were prepared. The works searched so far are kept." end the
  others. `GET /api/library/tmdb/runs/latest` answers `{ id, status, trigger, requestedByUserName,
  createdUtc, startedUtc, finishedUtc, worksTotal, worksDone, error }`, the running run else the
  newest, or 204 before the first; `POST /api/library/tmdb/runs` 202 with the run, 409 without a
  catalog, 503 "Too many TMDb suggestion runs are waiting. Try again in a minute.". The dialog shows
  the best five candidates, each `{ catalogKind, catalogId, title, originalTitle, year, overview,
  runtimeMinutes, posterUrl, imdbId, isConfident, evidence, seasonNumber, episodeNumber }`, its
  `evidence` computed when read: the title (how alike, exact, and which of TMDb's titles), the years
  apart, the runtime against the closest duration ffprobe measured of the work's versions ("6 min ≈
  6 min" when within 2 minutes or a tenth of TMDb's), the kind, the library's genre (animation or
  documentary, when 60% of a library's identified works, three or more, are) and a linked work in
  the same folder that shares its collection, a director or its show, which ranks first. A
  suggestion is **confident** when it is the one candidate of the work's kind with the work's title
  key and year, the only such, with a runtime that agrees (Vasyl's decision 8); it is always among
  the five. A show suggested for a film item comes with the season and episode its name gives
  (`S01E08`, `_E08`).
- **Paste a link or search TMDb yourself (MAM-34).**
  `GET /api/library/works/{id}/tmdb/lookup?text=&kind=` reads a TMDb film or show link
  (`themoviedb.org/movie/223021-landing-stripling`, `themoviedb.org/tv/1044/season/1/episode/8` with
  its season and episode), an IMDb link or id
  (`tt0056164`), or a bare TMDb number with `kind` `Movie` or `Series`, and answers `{ items }`, the
  film or show ranked for the work — both when an IMDb id names a film and a show. 400 "Paste a TMDb
  or IMDb link, an IMDb id like tt0056164, or a TMDb number.", "Choose film or show for a bare
  number." or "“x” is not a TMDb kind. Use Movie or Series."; 404 "TMDb has no film 223021." (or
  "show"), or "TMDb has no film or show with that IMDb id. If it is an episode's, paste the show's link
  instead.". `GET /api/library/works/{id}/tmdb/search?title=&year=` answers the first page of TMDb's
  films and shows of the kinds the work could be, of the year when given, ranked the same way; 400
  "Type a title to search for." or "“x” is not a year.". Both: 409 without a catalog, 502 with the
  catalog's masked sentence, a bare 404 for an unknown work; every film or show they show is stored
  as a catalog work, so its poster is served.
- **The TMDb page reviews the works without an id (MAM-34, ADR 0026).** **TMDb** in the header, right
  after **Library**, opens it for the installation owner — a member isn't offered the link, as every
  route behind the page answers them with a 403 — and the Library's link is marked only on
  `/library`, the TMDb one on `/library/tmdb`. "Review works without a TMDb id", beside the Library
  page's **Missing an id** filter, opens `/library/tmdb` too, with five tabs.
  **Review** walks the works one at a time with the dialog inline: 1–5 pick a suggestion, Enter
  saves, 0 marks that none fits, → skips, / goes to the search field — `GET
  /api/library/tmdb/queue?index=` answers `{ totalCount, index, current }`, the works without an id,
  a link or the owner's "none fits" by title, 400 "“x” is not a place in the queue. Use 0 or more.";
  `PUT` and `DELETE /api/library/works/{id}/tmdb/none-fits` set and clear the mark, 204, a bare 404
  for an unknown work. **Confident** lists every work with a confident suggestion, all ticked, with
  each library's numbers: `GET /api/library/tmdb/confident?locationId=&page=1&pageSize=25` answers
  `{ items: [{ work, candidate }], totalCount, page, pageSize, locations: [{ locationId,
  locationName, populationCount, confidentCount }] }`, `locations` the libraries the Library page's
  filter offers — those that hold a file, so not Jellyfin's box sets library —, works marked "none
  fits" left out; 400 "“x” is not a library id." or a page's sentence. **Link N works** sends `POST
  /api/library/tmdb/confident` with `{ items: [{ workId, catalogKind, catalogId }] }`, 1 to 100:
  each work is linked only while that is still its confident suggestion, computed again then, and
  202 `{ batchId, items: [{ workId, outcome, reason, links }] }` says what became of each —
  `Linked`, or `Skipped` with "It is no longer the work's confident suggestion.", "You marked that
  none fits.", "It was linked meanwhile.", "Its library reports a TMDb id for it now.", "The work is
  no longer in the library." or the catalog's or the save's own sentence; 400 "Tick at least one
  work to link.", "At most 100 works can be linked at once.", "Each ticked work needs its work, its
  kind and its TMDb id.", "Give the work to link.", "“x” is not a work id." or "“x” is ticked
  twice."; 409 without a catalog. Each work's links are saved as it is done and the worker woken
  then, so a request the browser cancels halfway still has the works saved before it queued and
  identified. Its links are saved `Queued`, each with its audit row, and a background worker
  identifies them — two films or episodes at a time, a series alone, a link the owner queued from a
  dialog before one a batch queued —, each with its own audit rows in the name of whoever queued it;
  the page polls `GET /api/library/tmdb/batches/{batchId}` (a bare 404 for an unknown click) every 2
  seconds until none is waiting or being identified. That answer holds only the linked works, as
  only links are stored: each row keeps its skip reason from the click's own answer, a link is
  followed by its id whichever work a sync files it under, and a click that linked no work is never
  asked for. A confident show of a series is queued and identified like a film, in the series' own
  lane. At a start an identification a restart cut is `NotWritten` with "The app restarted while
  identifying the item in Jellyfin. Jellyfin may still be working on it: Retry in a few minutes
  checks what it holds before anything is sent.", a link the rules keep in the app that a restart
  caught claimed goes back to `KeptInApp`, and the queued ones are identified. A batch whose
  identifications fail to confirm twice in a row is stopped, as the catch-up's is (below). The
  worker counts each link by where its identification left it, and a database failing under it ends
  its pass with the links still queued rather than stopping the app. **Not identified** lists the
  attached links Jellyfin or the app refused or failed to identify, each with its reason and its
  work's **TMDb…** to Retry, to link another film or show, or to remove the link. **Conflicts** and
  **Unattached** list the links whose item Jellyfin gives another TMDb id — or a film id given after
  the owner linked the item as an episode of a show, which the card flags too and the
  recommendations seed by neither — and those whose item is gone, the latter with **Remove**: `GET
  /api/library/tmdb/links?filter=&page=1&pageSize=25`, `filter` `Unattached`, `Pending`, `Conflict`,
  `NotWritten` or `Lost`, answers `{ items, totalCount, page, pageSize }`, newest first, each `{
  linkId, workId, workTitle, sourceName, sourceItemId, itemTitle, target, catalogKind, catalogId,
  catalogTitle, seasonNumber, episodeNumber, writeState, writeError, setUtc, setByUserName,
  conflictingValue, identifiedUtc }`; `NotWritten` lists attached links only, as an unattached one
  has no work to open; 400 "Choose which links to list: Unattached, Pending, Conflict, NotWritten or
  Lost." or "“x” is not a link filter. Use Unattached, Pending, Conflict, NotWritten or Lost.". A
  status line shows how the suggestions were prepared, with **Prepare suggestions** (**Prepare
  again** once they have been), disabled with the reason while a preparation is under way.
- **An episode Jellyfin lists as a film is identified through The Open Movie Database by its own
  IMDb id (MAM-34, Vasyl's decision of 2026-10-06, ADR 0026, ADR 0005).** A film item's link to an
  episode of a show that names its season and episode is identified by the episode's own IMDb id,
  which TMDb gives (`GET
  /3/tv/{id}/season/{season}/episode/{episode}?append_to_response=external_ids`; each dialog item
  carries it as `episodeImdbId`): `POST /Items/RemoteSearch/Movie` with the id under the `Imdb` key
  and the provider "The Open Movie Database", then the same apply. No TMDb id is ever sent for it,
  as on a movie item it names a film, so the library needs The Open Movie Database among its movie
  metadata downloaders. On "Greatest Events of World War II in Colour.S01E01.Blitzkrieg" Vasyl's
  live check corrected the name and the year ("Blitzkrieg", 2019), kept the overview, as the row has
  none, and replaced the image by a 1920x1080 one — probably a frame of the video, not the 960x1440
  poster; it took 21.3 s. Without both numbers the link stays in the app with "Without a season and
  an episode the app has no id of this episode to identify it by."; for an episode TMDb gives no
  IMDb id, with "TMDb knows no IMDb id of this episode, so its link stays in the app." (both as the
  item's `cannotWriteReason`). An item holding another IMDb id — typically its show's — is refused
  when a batch queued the link, with the held-id sentence below, and identified anyway by the
  owner's own save or **Retry**; a Save whose episode TMDb can't be asked about is 502 with TMDb's
  sentence, nothing saved. A sync judges an episode's link by that IMDb id, and keeps reading a film
  id the item holds as giving way to the link. An episode item no series holds keeps its link in the
  app: "Jellyfin identifies movie and series items only, so a link on an episode stays in the app."
  Saved again with the same season and episode once TMDb gives that episode an IMDb id, a link kept
  for having none is queued. Nothing but reads, the film and series searches and the apply is ever
  sent to Jellyfin about a link: no refresh, no item update, no image, no lock
  (`LibraryItemIdentifierIdentifiesOnlyTests`).
- **Update Jellyfin repairs the links set before: it identifies them, on the owner's click (MAM-34,
  Vasyl's decision of 2026-10-06, ADR 0026).** `GET /api/library/tmdb/catch-up` answers `{
  toIdentify, progress }`: `toIdentify` the attached links, in a library switched on, that Jellyfin
  would identify and whose item the app never read showing the link's match (`identifiedUtc` empty)
  — every such link set before the upgrade, kept in the app under the first rules or not; a
  `NotWritten` one only when no apply was ever sent for it —, and `progress` `{ batchId, total, done }` for the batch under way, or
  `null`. `POST` queues them under one new batch (202, the same shape: what it queued and its batch,
  `progress` `null` when nothing waited), and the background worker identifies each — one apply per
  item, none for an item that already shows the match —, every apply with its own audit row in the
  name of whoever set the link; the progress survives a reload, as the batch's id is kept on the links,
  and the links list's `Pending` filter follows them. Nothing happens without the click, and the
  page asks to confirm it first. A batch is protected: an item that holds a TMDb or IMDb id other
  than its link's — or an IMDb id when TMDb gives the link none to compare it with — is refused with
  "Jellyfin holds IMDb id … for this item, not …, and the app doesn't replace a library's own ids on
  its own. If the link is right, press Retry to identify it anyway; otherwise change the link.",
  nothing sent, and **Retry** is the owner's override; the same protects **Accept all**. A link
  refused or failed before anything was sent is taken again by the next click; a link whose apply
  was sent and not confirmed is not, as a second apply would delete the item's images again — it
  waits for **Retry**, and the **Not identified** list shows it; so does one a restart cut while it
  was identifying (Jellyfin goes on with that apply after the app is gone, and the audit row is
  written only after its answer). A batch whose identifications fail
  to confirm twice in a row is stopped: its queued links are `NotWritten` with "The app stopped this
  batch after Jellyfin failed to confirm two identifications in a row, and sent nothing for this
  item. Check Jellyfin, then Retry, or press Update Jellyfin again.". Both endpoints are the owner's
  alone, by where their controller lives.
- **Every TMDb link endpoint is the owner's, reads included (MAM-34, ADR 0026).** The controllers
  live in `Controllers/Library/Links`, and `LibraryLinksAreOwnerOnlyConvention` gates every action
  there: 403 "Only the installation owner can do this." for anyone else.
  `LibraryLinksAreOwnerOnlyHttpTests` sends each of them as a member and without a token.
- **An audit row names the ids it changed (MAM-34, ADR 0006, ADR 0026).** `AuditAction` gains
  `LibraryLinkSet`, `LibraryLinkReplaced`, `LibraryLinkRemoved` and `LibraryItemIdentified` (23), on
  a target of kind `LibraryLink` named after the work's title (200 characters at most, cut with
  "…"), each with the ids before and after in `AuditEventIdentifiers`. `LibraryItemIdentified` is
  written once for every apply that left the app, confirmed or not, with the ids the item held
  before and after — or, when the item couldn't be read again, the ids the identification meant to
  leave —, in the name of whoever set or queued the link; an item already showing the match, a
  refusal, a search that found no match and a failure before the apply write none, and no title,
  overview or image is recorded. `LibraryIdsWritten`, `LibraryIdsRestored` and
  `LibraryMetadataLookupRequested` are history: the first builds of this version wrote them, nothing
  writes them now, and the values stay so old rows read.
- **The Library sorts its works, and its sort says "Sort by" (MAM-8, Vasyl's request).** A **Sort by**
  select stands beside the Library page's filters, at the right, with **Title A–Z** (as before),
  **Title Z–A**, **Year, newest first**, **Year, oldest first**, **Largest on disk** and **Recently
  added**. `GET /api/library/works?sort=` takes `Title` (when blank), `TitleDescending`, `Newest`,
  `Oldest`, `Largest` or `Added`, by name in any letter case; `Newest` and `Oldest` go by the year the
  card shows, a work with none last either way; `Largest` by the sizes its files report added up, a
  series by every episode's, a work with no file or no size last; `Added` by when the app first saw the
  work, a series by its latest episode, so a new season brings the show up. Every order falls back to
  the title order, then the id, so pages never repeat or drop a work. 400 `{ error }` "“x” is not a
  sort. Use Title, TitleDescending, Newest, Oldest, Largest or Added." for another value, a number
  included. The sort is no filter: **Clear filters** leaves it, a new one starts at page 1, and the
  episodes a season lists keep their season and number order. `LibraryWorksSortHttpTests`,
  `LibraryPage.sort.test.tsx` and `SortSelect.test.tsx` pin it.

- **Refresh all runs the three steps one after another (MAM-36, Vasyl's decision of 2026-10-06).** One
  button in the Library page's Sources card, the owner's (anyone else sees it disabled with "Only the
  owner can refresh the library."), starts a sync of every enabled library the app can read, then a
  scan of the media folders, then an inspection of the files, and starts each only once the one before
  it has ended; the inspection goes last because it reads the files the syncs and the scan stored. It
  runs from the page, on the endpoints that start each step on its own, and waits on the sources the
  page already polls. A sync refused because it is already under way is joined, not a failure; any
  other refusal, or a run that ends Failed, stops the chain at that step with a red line under the bar
  ("Refreshing stopped. The sync of Home Jellyfin failed.") and starts no step after it. Without an
  allowed media folder only the syncs run, without a readable enabled library only the scan and the
  inspection, and with neither the button is disabled ("Connect a media library or choose a media
  folder first."). The button says the step it is on while it runs ("Syncing libraries…", "Scanning
  folders…", "Inspecting files…"). Leaving the page stops the chain after the step under way — that
  run goes on in the API, which queues an inspection after a sync and after a scan by itself — and
  opening the page again doesn't resume it. A chain that survives a closed tab would need an endpoint
  of its own, which is not built. No endpoint or table changed. `LibrarySyncPanel.refreshAll.test.tsx`
  pins it.

### Changed

- **A film filed in TV/Anime or TV/Documentary is a movie, and the movie search finds it (MAM-33, ADR
  0020, ADR 0004).** Toloka files films beside series in TV/Anime (5070) and TV/Documentary (5080). A
  release listed there and in no other TV category is TV only when its title or its description names
  seasons or episodes — "S1-5", "Сезон 1", "E1-31 of 37", "серії 1-4 з 12" — and a movie otherwise
  (Vasyl's decision 6), and the movie search asks those two categories too
  (`cat=2000,2020,2040,2070,5070,5080` for Toloka). So "Akira (1988)" or a documentary film filed
  there is listed as a movie and matches the film you have by title and year, while "Death Note
  (E1-31 of 37)" stays TV. A marker of episodes alone now also ends a TV release's title, as a season
  marker does, so "Death Note (E1-31 of 37) (2006)" is read as "Death Note" and matches the series in
  your library, and TMDb's, by its title. 0.2.0's rule, a release is TV "when its source lists it in a
  TV category", now holds for every TV category but those two; a listing in no category keeps it.
- **A release shows its source's own title under its own (MAM-33, ADR 0004, ADR 0006).** Toloka's
  topic title, Ukrainian first, comes in each listing's `<description>`, where Prowlarr's title keeps
  the English part alone. Each release now keeps it (`Releases.Description`) when it says more than the
  title, and `POST /api/releases/search` and `GET /api/searches/{id}/releases` answer it as
  `description`, after `title` and before `kind`, `null` when there is none. The Search page shows it
  under the release's title and in **Details**.
- **The contracts learn the catalog and the newest releases (MAM-33, ADR 0005).** `IntegrationKind`
  gains `Metadata` and `ProvenanceSourceKind` `Metadata`; `IMetadataResolver` is implemented for the
  first time, by the TMDb adapter, beside the new `IMetadataCatalog` (a work's details with credits
  and recommendations, a collection, a person's works, search, find by IMDb id, discovery by genre,
  genres and posters). `ReleaseSearchMode` gains `Latest`, planned by `ReleaseSearchPlanning.ForLatest`
  and sent as `t=search` with no `q`; `ReleaseSearchHit` gains `Description`.
- **The Integrations list's kind filter takes Metadata (MAM-33).** An unknown kind is 400 "“x” is not
  a kind of integration. Use MediaLibrary, ReleaseSearch, DownloadClient or Metadata.".
- **Pages are wider on a desktop (Vasyl's choice of 2026-10-05).** Every page's column is at most
  80rem (1,280 px) wide, its padding included, instead of 64rem (1,024 px). The Recommendations page's
  cards are at least 30rem wide instead of 24rem, so a desktop shows two a row, never three, and a
  window narrower than about 1,020 px one.
- **The media library contract learns to identify an item, as an optional capability (MAM-34, ADR
  0005, ADR 0026).** `IMediaLibraryItemIdentifier` is a further interface of a library's adapter,
  found by the registry like a kind contract and required of none: `IdentifyAsync` takes an item,
  its kind and the one id the identification is by — a film's TMDb id, a show's TMDb TV id, or an
  episode's IMDb id on a film item, and no other pair, so an id never reaches the search of the
  other kind — and answers the ids before and after, whether anything was sent, and what became of
  it; `IdentifiableKinds` names the kinds of item the library identifies and
  `UnidentifiableKindReason` says why not the others, so the app keeps such a link in the app
  because the library says so, not by checking which product it is. It replaces
  `IMediaLibraryIdentifierWriter`, with its edit rules and its metadata lookup. Jellyfin's adapter
  implements it for films and series, never an episode item; its apply goes out on a named client of
  its own, `jellyfin-identify`, with a 20-minute timeout, chosen by an optional client name on
  `HttpIntegrationAdapter`, while every other call keeps the shared client's 15 seconds; its class
  summary no longer says it only reads. `IMetadataCatalog` gains `GetEpisodeAsync`, an episode of a
  show with its own IMDb id. `ProvenanceSourceKind` gains `Person`, `AuditTargetKind` `LibraryLink`,
  `RecommendationRunTrigger` `LinkChanged`, and `CatalogWork` a runtime, an overview and its other
  titles, with a show's IMDb id read too.
- **The Library list counts a linked id as the library's own (MAM-34, ADR 0026, ADR 0006).** `GET
  /api/library/works` gives each identifier `sourceKind` — `MediaLibrary`, `Person` for an id the
  owner set, `Metadata` for the IMDb id TMDb gave the link (only where Jellyfin gives none) — and
  `setUtc`; a linked id's `sourceName` is the name of who set it for the owner and `null` for
  everyone else. Each work gains `link` (`null` without one) `{ target, catalogKind, catalogId,
  catalogTitle, seasonNumber, episodeNumber, imdbId, setByUserName, setUtc, writeState, writeError,
  conflictingValue, identified, underWay }`, the worst write state of its items' links,
  `setByUserName` and `writeError` for the owner only, `identified` whether the app confirmed every
  item of the work whose id Jellyfin holds — the card says "Identified in Jellyfin", else "Not
  identified yet" — and `underWay` whether any item's link waits or is being identified, which makes
  the page ask again while one does; the two booleans are everyone's. A linked work misses no id:
  `missingIdentifiers` is `[]` and `identifiers=Missing` leaves it out, and the episodes of a linked
  series too (Vasyl's decision 9). `search` finds a work by its linked id and by the IMDb id TMDb
  gave. The id a link replaces is hidden until Jellyfin holds the link; a third TMDb id Jellyfin
  gives is shown beside it as a conflict. The cards show a linked id once whatever its sources, its
  tooltip saying who reported it and who set it when, the link's state, an episode link as "Episode
  of Планета Земля · S1 E8", and "TMDb ids disagree: 1234 in Jellyfin, 223021 linked" for a
  conflict.
- **A search and its releases match a linked id (MAM-34, ADR 0012).** `POST /api/searches` and
  `GET /api/searches/{id}` count a work's linked ids like its library ids: a linked id finds the work
  and matches a release that gives it, `matchedIdentifiers` items gain `sourceKind` and `setUtc` and
  say "set by" on the page, and a linked id beside another library id of its scheme is flagged as a
  conflict. The id a link replaces matches nothing until Jellyfin holds the link.
- **A sync re-applies the owner's links (MAM-34, ADR 0008).** A sync groups a linked item with its
  linked id, so it joins the work of the library's other items of that id, and a third value keeps
  it alone; it stores nothing of the link as Jellyfin's. A link follows its item to whatever work it
  lands on, stays unattached when the item is gone, and, when Jellyfin re-created the item under a
  new id, moves to it by the device, inode and size of its files — after the next file inspection,
  or at the following sync —, a series by its episodes' files, never when two items or two links
  claim one file. The sync judges a link by the id its identification sets — a film's TMDb id, a
  show's TMDb TV id, an episode's IMDb id — and marks a link `InLibrary` once Jellyfin reports it
  and `Lost` once it reports an identified link without it; a link kept in the app stays so whatever
  Jellyfin reports, and so does a `NotWritten` one, with its sentence, even when its item holds the
  id, as the app never identified it; the id it replaced giving way. A sync never changes when a
  link was identified, but a link it moves to an item Jellyfin re-created is not identified again,
  as the new item is a fresh one. A successful sync also queues a preparation of the TMDb
  suggestions.
- **Recommendations seed by a link (MAM-34, ADR 0023).** A work with a link seeds and is held by its
  linked id, and is never looked up by title; a work whose link and library disagree seeds nothing
  and holds both ids; an episode link holds and seeds its show, its season numbers counting as held.
- **TMDb's details carry a runtime, an overview, other titles and a show's IMDb id (MAM-34, ADR
  0025).** `GET /3/movie/{id}` and `/3/tv/{id}` append `alternative_titles,external_ids`: a film's
  `runtime`, a show's first `episode_run_time`, else its latest episode's runtime (Planet Earth's is
  `[]` and 53), the overview cut to 2,000 characters, at most 50 other titles cleaned and each once,
  and a show's `external_ids.imdb_id`. `CatalogWorks` keeps them, and details stored before this
  version are read again when next needed (`DetailsVersion`). The poster endpoint serves the
  candidates the TMDb dialog shows, as each is stored as a catalog work.
- **The order of the Search and Recommendations lists stands beside the filters, at the right, under
  "Sort by" (MAM-8, Vasyl's request).** The order was a select with no label, left among the filters,
  that read like one more filter — and, as one of the items of the row they wrap in, it could drop onto a
  line of its own between the filters and the list or its empty state, tearing the filters and **Clear
  filters** apart. It now stands outside that row (`FilterBar`), after a sort icon and **Sort by**, as
  the Library's does, and the filters wrap among themselves; under 48rem it goes above the filters
  instead of beside them, so it is never between them and their answer. The Library's search field
  gives up a little width so the filters, the **Review works without a TMDb id** button and the order
  meet on one line on a wide window. The Search page's label was "Sort". One shared `SortSelect` makes all three,
  and its select keeps its own accessible name, "Sort by".

- **The Library page's Sources card is one status bar, and its rows are under Details (MAM-36, Vasyl's
  pick of 2026-10-06).** The card was about 480 px of three rows, each of three or four sentences with
  a button of its own, and the same numbers three times; the works started below the first screen. It
  is now one bar: an item for each media library, for the media folders and for the file inspection,
  each with an icon (done, running, failed, never run) and when it last ran — "syncing 600 of 1,365",
  "scanning… 800 files", "inspecting 700 of 1,396" while it runs —, amber chips for what needs a look
  (files without an app path, files in no library, folders not scanned, files that couldn't be read),
  and at the end what all the files take ("1,396 files · 7.33 TiB"). **Details** opens the rows as
  they were — the sentences and the buttons of each library, of Media folders and of the inspection —
  under the card's description, in place of the bar, and **Hide details** brings the bar back; it is
  closed to begin with, on every visit. What a state of its own says stays in view with the details
  closed: loading, a failed load or refresh, the sentence about a missing media library, and a
  stopped Refresh all. Without an allowed media folder the bar says so, with the way to Media folders
  for the owner. On a phone the items stack and the buttons go under them, as icons. The heading
  "Sources" stays, for the page's outline and a screen reader. No API changed.
  `LibrarySyncPanel.bar.test.tsx` pins the bar; the other panel and page tests open Details first.

### Notes for upgraders

- **Applying `AddReleaseDescription`, `AddRecommendations` and `AddRecommendationRunProgress`
  rebuilds no table, so it logs no warning 20410 (MAM-33).** `AddReleaseDescription` adds one
  nullable column, `Releases.Description`; `AddRecommendations` only creates tables — the catalog's
  eleven and the recommendations' eight — and their 28 indexes; `AddRecommendationRunProgress` adds
  three nullable columns to `RecommendationRuns`, a running run's stage and its two counts.
- **Nothing is recommended until the owner connects TMDb (MAM-33).** Create a free account at
  themoviedb.org, open **Settings → API**, and copy the **API Read Access Token** (the long one, not
  the API key). In the app, open your user menu → **Integrations** → **Add integration**, choose
  **TMDb** as the service, name it, enter `https://api.themoviedb.org` as the address and the token
  as the API key, choose **Test connection**, then **Add integration**.
  The first computation starts by itself; on a library of about 900 films it takes a few minutes of
  calls to TMDb (3 min 15 s on the reference installation), its progress shown on the
  Recommendations page, later ones a handful. Every user then sees all four lists and up to 60 searches a day
  for them; each changes that under **Profile → Recommendations**.
- **The release sources are asked more (MAM-33).** Each user's lists may cost up to their daily limit
  of searches — 60 by default —, at most 4 in each pass of 15 minutes for the whole installation, plus
  one query per source every 3 hours for its newest releases. The first pass of each runs as the app
  starts. Set a limit to 0 to ask nothing for that user.
- **Releases stored before the upgrade have no description (MAM-33).** They go with the next day's
  prune of a search's releases, unless a download job keeps them.
- **Posters are cached in the data directory (MAM-33).** `<data>/posters/` holds every poster the
  Recommendations page has shown, a few dozen kilobytes each, and is never pruned; it needs no backup,
  as a missing one is fetched again while TMDb is connected.
- **Applying `AddLibraryLinks` rebuilds no table, so it logs no warning 20410 (MAM-34).** It adds
  three columns to `CatalogWorks` — `RuntimeMinutes`, `Overview` and `DetailsVersion` (0) — and
  creates seven tables, `LibraryItemLinks`, `LibraryItemLinkFiles`, `AuditEventIdentifiers`,
  `LibraryLinkSuggestionRuns`, `LibraryLinkSearches`, `LibraryLinkCandidates` and
  `CatalogWorkAlternativeTitles`, with their 16 indexes. Every cached TMDb film and show keeps its row
  and is read again the next time a run needs it, so the first computation of the recommendations
  after the upgrade reads every seed's details from TMDb again, a few minutes as the first one took.
- **Applying `AddLibraryLinkLookups` rebuilds no table, so it logs no warning 20410 (MAM-34).** It
  adds three nullable columns to `LibraryItemLinks`: `EpisodeImdbId`, `LookupRequestedUtc` and
  `LookupError`. Every link comes through whole, none of them set.
- **Applying `AddLibraryLinkIdentified` rebuilds no table, so it logs no warning 20410 (MAM-34).**
  It adds one nullable column, `IdentifiedUtc`, to `LibraryItemLinks`; every link comes through
  whole with none set, so every attached link counts for **Update Jellyfin**. `LookupRequestedUtc`
  and `LookupError` stay as dormant columns that nothing writes or reads, so the previous image
  still runs on the migrated database.
- **Bring the links set before up to date with Update Jellyfin (MAM-34, Vasyl's decision of
  2026-10-06).** Links set under the first rules put ids on items and, later, asked Jellyfin to look
  up what they lacked; Jellyfin still shows those items the titles, overviews and images it took
  from their file names. Nothing changes them by itself. Identify has no undo, so back up Jellyfin's
  data folder first if a way back matters. Each Jellyfin library gives its items the metadata
  language set on it, and one with none set uses the server's default: set the language of a library
  before the run if you want another. On the reference installation «Фільми» is Ukrainian and the
  others have none; the live check ran only in libraries with none or English, so identify one film
  of a library with a language first — open its work's **TMDb…** dialog and choose **Identify in
  Jellyfin** — and look at it in Jellyfin. Then open the TMDb links page: the panel counts the links
  not identified yet (223 on the reference installation), and **Update Jellyfin**, after one
  confirmation, makes Jellyfin identify each of those items in the background — two films or
  episodes at a time, a series alone and for about a second per episode: about 15 minutes for the
  223 — while the panel counts "Identifying in Jellyfin: 41 of 223 links…". The page can be closed
  or reloaded meanwhile. A link Jellyfin or the app refuses stays as it is with its reason, listed
  under **Not identified**; the refusals include any item that holds an IMDb or TMDb id other than
  its link's, which a repair never overwrites — **Retry** on it identifies it anyway, after you have
  read the sentence —, and a library that saves beside the media, which is refused before any
  search.
- **Nothing is linked until the owner links it (MAM-34).** TMDb must be connected (see above); the
  suggestions are then prepared after the next library sync, or at once with **Prepare again** on
  the TMDb page (Library → **Missing an id** → **Review works without a TMDb id**). The runtime
  evidence, and so every confident suggestion, needs the media folders allowed under **Media
  folders** and the files inspected. To have Jellyfin identify items, the integration's API key must
  be an administrator's — one made under Jellyfin's **Dashboard → API Keys** is —, Jellyfin needs an
  enabled administrator account to read an item as, and the item's library must not save artwork or
  metadata next to the media: no `Nfo` metadata saver, no saving artwork into media folders, no
  trickplay or downloaded subtitles saved with the media. The app refuses to identify in a library
  that does, and says so on the item. A series with watch history kept its played state on the live
  check (The Penguins of Madagascar, 15.4 % played); a film with watch history was not tried.
- **Back up `mam.db` (MAM-34).** The links are the owner's work and live only there. The suggestions
  are a cache the next run rebuilds. Identify changes Jellyfin's own data — titles, overviews and
  images — and can't be undone, and removing a link doesn't change Jellyfin back: back up Jellyfin's
  data folder before **Update Jellyfin** if you want a way back.

## [0.2.0] — 2026-10-04

### Added

- **Duplicate detection will cover episodes as well as movies, and where the library's files come
  from was measured (MAM-17, ADR 0017, ADR 0018).** Specification §7 questions 3 and 6 are answered
  against the reference installation. Vasyl chose movies and TV in full: series, seasons and
  episodes will be imported from Jellyfin in every library (MAM-32). An episode is a work known by
  its own IMDb and TVDB ids and by its series' ids with its season and episode numbers, never by
  Jellyfin's series item, as The Grand Tour is four of them; a series' TMDb id never matches a
  movie's. Whether an item is a movie or an episode is the library's word alone: the 38 episodes
  Jellyfin lists as movies stay movies, and no file name changes that. On the media disk no file
  has a second name; 822 of the 1394 video files in the library folders are a seeding torrent's own
  data and 571 belong to no torrent; Radarr has never imported a file; and no two of those video
  files have the same size. A seeding file is a duplicate like any other, so Phase B reads no
  download client. Jellyfin's answers for series, seasons and episodes are new samples in
  `tests/MediaAcquisitionManager.TestInfrastructure/VendorSamples/Jellyfin/`, among them an episode
  item that holds 65 different episodes and a virtual episode whose only media source is a
  placeholder without a path.
- **A library sync imports the series and episodes of every Jellyfin library beside its movies
  (MAM-32, ADR 0019, ADR 0008).** A sync reads three lists — the movies, the series and the episodes
  (`includeItemTypes=Movie`, `Series`, `Episode`) — each in pages that overlap by one, with its own
  total and its own checks, the first page of each read first so the progress knows the whole
  library's total from the third request on, and stores all of it in one transaction or nothing. A
  series is a work that holds the show's titles and its IMDb, TMDb and TVDB ids and no version, its
  TMDb id in a scheme of its own (`TmdbTv`) that never matches a movie's. An episode is a work with its versions and files, known by
  its own IMDb and TVDB ids and by its series' ids with its season and episode numbers, and placed in
  its series by a row per library item (`MediaEpisodePositions`: the series work, the season and the
  episode number), named after its source like a title. Each kind is grouped on its own, series
  first, so The Grand Tour's four series items are one series, the two halves of a Mickey Mouse
  Clubhouse episode are one episode with two versions, and a movie and a series that share a TMDb
  number stay two works. An episode Jellyfin lists without a file (`LocationType` `Virtual`) is read,
  so the pages add up, and stored as nothing (Vasyl's decision 1). A run counts its series apart
  (`seriesCount` on `GET /api/library/syncs/{runId}`); its works are its movies and episodes.
  Removing the Jellyfin integration removes its series, episodes and their positions too.
- **The Library page lists each series as one card among the movies, with its seasons (MAM-32, ADR
  0019).** A series card shows "Series", the show's titles, its ids and the ids it lacks, and its
  seasons as rows that open — "Season N" in order, then "Specials" (season 0), then "No season" —
  each with how many episodes it holds; the seasons are read from the episodes' own numbers, never
  from Jellyfin's season list (Vasyl's decisions 2 and 3). Opening a season loads its episodes from
  the server a page at a time, with the list's search, library and identifiers filters, each as "N.
  Title" with the ids it lacks and its versions as rows; several seasons can be open at once, and the
  pager shows only past 10 episodes, its page size kept per device. An episode of no series, such as
  the Tom & Jerry Kids item, is a card of its own with its place, "Season 1 · Episode 4".
  `GET /api/library/works` lists the movies, the series and the episodes of no series in title
  order, a series once: it is listed when at least one of its stored episodes passes the search and
  the filters; a search or `identifiers=Missing` that the series itself answers keeps all its
  episodes, and `locationId` goes by the episodes' files. Each work gains `episode`
  (`{ seriesId, seasonNumber, episodeNumber }`, for an episode) and `seasons`
  (`[{ number, episodeCount }]`, for a series, counting the episodes the list keeps), and
  `missingIdentifiers` names the ids of its kind: IMDb and TMDb for a movie, IMDb and TVDB for an
  episode, IMDb, TMDb TV and TVDB for a series. `kind=Movie` or `kind=Series`, in any letter case,
  keeps one kind (`Series` takes the episodes of no series too), with a **Kind** filter on the page
  (Movies and series, Movies, Series); any other value is 400 "“x” is not a kind filter. Use Movie
  or Series.". `GET /api/library/works/{id}/episodes` pages a series' episodes, searched and
  filtered as the works are, in the order of its seasons, then by episode number (a missing one
  last), then by title: `season` is a number up to 9999 (0 for the specials), `None` for the
  episodes without one, or blank for all, and anything else is 400 "“x” is not a season. Use a
  season number, or None for the episodes without one."; an id that isn't a series' is a bare 404,
  checked first. The scheme labels now include TVDB and TMDb TV, and the versions table is offered
  for 2 to 6 versions only, so the 65 versions of one item show as rows.
- **The release search finds TV releases beside the movies, and takes a season (MAM-32, ADR 0020,
  ADR 0004).** Every enabled source gets the movie queries, as before, then the TV queries, one after
  the other: in the source's TV categories (5000 to 5999), an IMDb id first where its TV search takes
  one, then the title without the year — a TV release names the years of its seasons, so the show's
  year would find only the packs that hold its first season — through the TV search (`t=tvsearch`)
  or, when that takes no title, the general search. A source without a TV category gets no TV
  query. The movie releases come first, then the TV ones, and a listing both find is kept once; any
  failing query fails the source, as before; each source lists up to 100 releases of each kind.
  `POST /api/searches` and `POST /api/releases/search` take `season`, a number from 0 to 9999, 0 for
  the specials, blank for none, checked after the ids ("“x” is not a season. Use a season number,
  such as 1, or 0 for the specials."); with one, only the TV queries run, with `season=N` where the
  TV search takes it. The stored search answers `season` after `tmdbId`, and the Search page's
  narrow form has a **Season (optional)** field after Year. Each stored release keeps its kind — TV
  when its source lists it in a TV category, or in none to a TV query —, its categories in the
  listing's order, its TVDB id and, on a TV release, its show's TMDb id, and
  `POST /api/releases/search` gives each release `kind` (`Movie` or `Tv`) after `title`. A source
  that offers no search the app can use says "“X” offers no search this app can use: no search by
  text, and no movie or TV search by title or by an id that was asked for.", or with a season "“X”
  offers no search this app can use for a season: no TV category, or neither a search by text nor a
  TV search by title or by an id that was asked for."
- **A TV release's title is read for its seasons, and the release is matched to the series in your
  library only (MAM-32, ADR 0020, ADR 0012).** The first season marker gives the seasons and
  episodes a title claims — `S01E01`, `S01E01-E10`, `S1E1-9 of 9`, `S1-3`, `S01-S03`, "Season 1",
  "Seasons 1-2", "Сезон 1", "Сезони 1-3", ", серії 1-9 з 9", bare or in brackets — and a bracketed
  span such as "(2018-2024)" ends the title as a year does, so Prowlarr's "Yellowstone (S1-5)
  (2018-2024) WEB-DL-AVC" is "Yellowstone", seasons 1 to 5, of 2018 to 2024; a movie's title is read
  as before. A TV release is matched to series only — by a show's IMDb, TMDb TV or TVDB id, then by a
  title with a year its seasons allow, then by the title alone — and a movie release to movies only,
  so a film and a show of one title, or of one TMDb number, never match each other's releases, and
  an episode of no series is matched by none. A release names its seasons' years, never the show's
  (Vasyl's choice): with season 1 among the seasons its first year must be the series' year, so
  "Yellowstone (S1-5) (2018-2024)" is Yellowstone (2018) by title and year and the BBC's "Yellowstone
  (S1) (2009)" by title alone; with later seasons only, or the specials alone, the series may have
  begun in that first year or before, so "(S5) (2022-2024)" and "(S4) (2021)" are Yellowstone (2018)
  by title and year too — as they would be for an older show of the same title; with no season, any
  year of the span, as for "Yellowstone (2018-2024)". Each TV match carries `heldEpisodeCount`: how
  many of the series' episodes sit in the seasons or episodes the title claims, `null` when it claims
  no season. `GET /api/searches/{id}/releases` takes `kind=Movie` or
  `kind=Tv` in any letter case, checked right after `source` ("“x” is not a kind filter. Use Movie or
  Tv."); each release gains `kind` after `title`, `claimedWork` gains `lastYear` and `episodes`
  (`{ firstSeason, lastSeason, firstEpisode, lastEpisode }`), each library match
  `heldEpisodeCount`, and the search's `facets` gain `kinds`, Movie before Tv. **In your library**
  shows a series as the Library page's card with its seasons, and an episode of no series as its own
  card: an episode the search finds by a title, its IMDb id or a file path lists its series once,
  on the stronger evidence of the series' own and the episode's, and a series counts once towards
  the 20 works listed by their words. On the Search page a TV release's **Claimed** chips
  start with "TV", then its seasons ("Seasons 1–5", "Season 1", "Specials") and episodes ("Episodes
  1–9"); a TV match says how many of its episodes your library holds ("None of its episodes in your
  library" when none); **Details** adds "Listed as", the span ("2018–2024") and the seasons and
  episodes read from the title, with the matched series' seasons; and the list has a **Kind** filter
  (Movies and TV, Movies, TV) of the kinds the search found.
- **The owner maps a release source's sections to a destination, and the Download dialog opens on it
  (MAM-32, ADR 0020).** In the **Add destination** and **Edit** dialogs of the Destinations page,
  **Tracker sections (optional)** lists each release source as a row that opens to its sections —
  its own sections, then the standard categories — read from the source each time the dialog opens,
  the categories of one name one section, Prowlarr's leading "|-" dropped: Toloka's three
  «Мультсеріали» categories are one. A section ticked for another destination says "now Cartoons"
  and moves here when ticked, and a source that couldn't be asked says why beside the sections
  already mapped to it. `GET /api/settings/destinations/sections` (owner only, 200 whatever the
  sources answered) gives `{ sources: [{ id, name, sections: [{ name, categoryIds, isSourceSpecific,
  destinationId, destinationName }], error }] }` for every release source by name, a switched-off
  one unasked with its mapped sections. `POST` and `PUT /api/settings/destinations` take
  `sections: [{ releaseSourceId, categoryIds, name, isSourceSpecific }]`, written in the
  destination's own save and transaction with its one audit row: on an update, left out keeps them
  and `[]` removes them; each category of a source is mapped to at most one destination, and one
  mapped elsewhere moves here. A section that breaks a rule is 400 ("Pick the release source of each
  section.", "A section needs its name.", "A section's name can't contain line breaks or other
  control characters.", "A section needs at least one category of its release source.", "“x” is not
  a category id of a release source."), and one whose source isn't a release source any more is 404
  "A release source you picked sections of no longer exists. Reload the page and pick them again.";
  a save never asks the source. The rows go with their destination or their source.
  `GET /api/downloads/options` gains `suggestedDestinationId` (Vasyl's decision 5): among the
  destinations it offers, the one the release's stored categories are mapped to, the source's own
  section winning over a standard category; none when two different destinations tie at that rank;
  else the default, when it is offered. The Download dialog opens on it and its client; with none,
  on the placeholders, but a lone client is picked, and so is its destination when it has one only.
- **The owner scans the media folders for the files no library reported (MAM-18, ADR 0021, ADR
  0009).** `POST /api/library/scans` answers 202 with the queued scan and its `Location`
  (`/api/library/scans/{runId}`), or with the one already waiting, which the request joins; one asked
  for while a scan runs is queued behind it. It is owner-only (`[RequireInstallationOwner]`, swept by
  `LibraryWritesAreOwnerOnlyHttpTests`: 403 "Only the installation owner can do this." for anyone
  else), 409 with "No media folders are allowed yet. Add the folders the app may read under Media
  folders in the user menu, then scan again." while no folder is allowed, and 503 with "Too many
  folder scans are waiting. Try again in a minute." when the queue is full, the run kept as failed
  with that sentence. At most one scan is queued and one running
  (`IX_FolderScanRuns_OneRunPerActiveStatus`). `GET /api/library/scans/{runId}` gives every
  signed-in user `{ id, status, requestedByUserName, createdUtc, startedUtc, finishedUtc,
  filesFound, filesUnreported, problemCount, problem, error }` — `filesFound` the files the walk has
  reached so far, each once however many links lead to it, `filesUnreported` how many no library
  reported once it stored them, `problem` the first place it didn't scan — the requester, the problem
  and the error for the owner only; a bare 404 for an unknown run. A scan runs only when the owner
  asks — no timer, not after a sync, not at the start — and reads no download client (ADR 0018). The
  reconciler fails a scan a stop interrupted ("The app restarted before this scan finished, so
  nothing it found was stored. Scan the folders again.") and queues a queued one again, but queues
  none of its own; a running scan the next claim finds dead fails with "The scan stopped because of
  an error in the app. Its log says more.", and one whose folders were all removed before it started
  with "No media folders are allowed any more, so there was nothing to scan. Add one under Media
  folders in the user menu." The log gives counts, never a path.
- **The scan walks every allowed folder, follows a link only inside them and opens no file (MAM-18,
  ADR 0021).** The local file inventory walks the roots in the owner's order, depth first, each
  folder's entries in ordinal name order, hidden ones included, and lists every media file: a regular
  file whose name ends in `mkv`, `mp4`, `m4v`, `avi`, `mov`, `ts`, `m2ts`, `mts`, `mpg`, `mpeg`,
  `vob`, `wmv`, `asf`, `flv`, `webm`, `ogv`, `rm`, `rmvb` or `iso`, letter case ignored (Vasyl's
  decision 2), the clips of Blu-ray and DVD folders included. A symbolic link or junction inside a
  root is resolved as a look at a file resolves it (40 links at most) and followed when it ends inside
  a root, its files listed under the link's own path; one leading out is never followed, and a folder
  that leads back to the one it is in or one above it — by its path, and on Linux by its device and
  inode too — isn't walked again. On Linux each folder is opened from `/` one name at a time with
  `O_NOFOLLOW` and listed through `/proc/self/fd/<n>`, so a folder swapped for a link after the walk
  checked it is refused. A root that is missing or lists nothing ("“/media” is empty; if it is a
  mount, it may be away, so the files found in it before are kept."), a folder the app may not list
  or that changed while it was checked, and a link that leads nowhere now are places the scan
  couldn't list, and what it found there before is kept; a link leading out, a loop ("“…” leads back
  to a folder above it, so the app doesn't walk it again."), a name that isn't valid text ("A name in
  “…” isn't valid text, so the app skips it.") and a path longer than 4096 characters are places it
  refuses. Both kinds are counted as problems, never a failure. A walk lists at most 100,000 folders
  and gives at most 200,000 entries; past either the scan fails with "The media folders hold more than
  200,000 media files or 100,000 folders, or links inside them lead to the same folders many times
  over, so the scan stopped.", storing nothing and queuing no inspection.
- **A scan stores one name, with no version, of each file no library reported (MAM-18, ADR 0021,
  ADR 0006).** The names that lead to one file through links are one file. A file is reported when
  one of its names is where the app finds a file a library reported — a movie's or an episode's,
  through its integration's current path mappings — by path alone, never by a file name, a title or
  an id. Each file no library reported keeps one `PhysicalFiles` row: source kind `FileInspection`,
  no connection, no version, the app's own path as `ServicePath`, the row one of its names already
  had (its id, first-seen time and readings kept, its last-seen time moved) or else its first name.
  In one transaction with the run's outcome, a name stored before goes when the walk reached it under
  a library's path or as another name of a file, stays when it lies outside every root this scan
  walked or at or under a place it couldn't list, and goes otherwise; a scan that fails or is
  stopped stores nothing. Such a name belongs to no work: the works list with its search and filters,
  the list of Jellyfin libraries, each library's counts and a search's library matches never show it
  (Vasyl's decision 1).
- **The Library page has a Media folders row with Scan folders (MAM-18).** Under **Sources**, after
  the Jellyfin libraries, the row **Media folders** says how the scan stands: "Waiting to scan the
  media folders…", "Scanning the media folders… N files found", "Scanned <time>: N files, U in no
  library.", "Scan failed <time>: …" — "The last scan failed. The owner can see why." for everyone
  else — or "The media folders have not been scanned yet.", and that another scan follows the
  running one when one waits. A scan with problems warns "N folders or links weren't scanned:" with
  the first one's sentence to the owner, and "N folders or links weren't scanned. Ask the owner
  which." to everyone else. "Files in no library: I inspected · P couldn't be read · W not inspected
  yet" counts the scan's names by the app's last look at each, as each library's row counts its
  files, with the first unreadable one's sentence for the owner. With no folder
  allowed the row says "Scanning is off: no media folder is allowed yet.", with **Choose media
  folders** for the owner. **Scan folders** is disabled with its reason for anyone but the owner,
  with no folder allowed and while a scan waits, and stays enabled while one runs; a refused start
  shows its sentence under the row. The page asks again every 2 seconds while a scan is under way
  (Vasyl's decision 3).
- **A scan queues the file inspection, which records each file's size and modification time
  (MAM-18, ADR 0009).** Once a scan has stored its names it queues an inspection with the new trigger
  `FolderScanned`, or joins the one waiting, as a sync does; when none can be queued, the scan still
  succeeds and the log says why. The inspection reads the scan's names with ffprobe through the
  same cache as the library's, so a file already read isn't read again. Every name it looks at, a
  library's too, now keeps the size and the modification time the disk said
  (`PhysicalFiles.ReadSizeBytes` and `ReadModifiedUtc`) with its identity: empty until the app has
  looked, and empty again when its last look found nothing there.
- **The file inspection ends by hashing the files that could be byte-identical copies (MAM-19, ADR
  0022, ADR 0009).** Once a run has looked at every name and read what the cache didn't hold, it
  lists the files a copy could hide among: one per identity, size and modification time, named by
  its first name in path order, whose size a file of another identity shares and whose media facts
  don't tell the two apart — the duration, the number of tracks and, track by track, the kind,
  codec, width, height, channels and language, a value either reading lacks, or a file ffprobe
  couldn't read, ruling nothing out. A file whose size no other file shares is never read, nor are
  two hardlink names of one file alone. Each candidate is read whole for its SHA-256, one at a time
  (`FileInspectionRunner.MaximumConcurrentHashes`, Vasyl's decision 3), inside the allowed roots and
  from the file the checks opened, and kept in the new `FileHashes` table under the file's device,
  inode, size and modification time, the readings' key: a file is read whole once, and again only
  when one of the four changes, while a renamed file, or another name of it, finds its hash without
  a read. A file that changed while it was read ("“…” changed while the app was reading it for its
  hash, so the hash can't be said to belong to either version."), and one gone, outside the roots or
  unreadable, keeps nothing and is tried again by the next run, counted as a problem, never a
  failure of the run; one that changed between the look and the hash keeps the hash of what was read
  under its new key and counts "A file changed after this inspection looked at it, so its hash
  counts from the next inspection." A hash goes once neither a name carries its key nor a kept
  reading has it, so a mount that is away costs no hash of a file the app has a reading of, and a
  changed file's old hash goes with its old reading. Hashing is the last step of every inspection —
  after a sync, after a scan, at the start and on **Inspect files** (Vasyl's decision 1) —: a
  restart fails the run as before ("The app restarted before this inspection finished. What it had
  read is kept."), and the start's run answers the stored hashes from the cache and reads the rest.
  A file a torrent seeds is hashed like any other, and no download client is asked (ADR 0018). The
  hashes are kept, not shown yet: grouping the copies is MAM-20's.
- **The Library page says how much room the files take, each file counted once (MAM-19, ADR 0018,
  ADR 0022).** Under its files line and the warnings about it, every Jellyfin source's row and the
  **Media folders** row say "Takes <size> on disk: N files.", then "M more names lead to files
  counted once." when files have several names and "U files not measured yet." for the names the app
  has no measure of; once a media folder is allowed, the file inspection's row says "All files take
  <size> on disk: N files." for every stored name, so a file a library and the scan, or two
  libraries, name counts in each row and once in the total. A file is its device and inode, and its
  size the one the app's last look read, never the size a library reported; a row none of whose
  files the app has measured shows no such line (Vasyl's decision 2). Every signed-in user reads the
  same figures.
- **The Library page follows the hashing (MAM-19).** While a run hashes, the file inspection's
  status line reads "Hashing the files that could be copies… C of T"; once the run has succeeded, a
  line under it says "Files that could be copies: H of T hashed, R read in full this time." or "No
  file needed a hash: no two files of one size could be copies of each other.", and "N files
  couldn't be hashed:" with the first one's sentence warns the owner, "N files couldn't be hashed.
  The owner can see why." everyone else. The page asks again every 2 seconds while the run is under
  way, as before.

### Changed

- **A sync's progress and its result count items, works and series, and its failures name the list
  (MAM-32, ADR 0008).** The Library page counts a running sync in items ("Syncing… N of M items")
  and a finished one as "Synced <time>: read N items into W works and S series, V versions, F
  files.", the items being movies, series and episodes and the works movies and episodes; a sync
  from before the upgrade keeps its line without the series. A list that changes while it is read
  fails the run with the list's noun: "Jellyfin's list of series shifted between two pages, so its
  library changed while it was being read. Nothing was changed here; sync again once Jellyfin's
  library scan has finished."
- **The media library and release search contracts learn TV (MAM-32, ADR 0005).**
  `LibraryPageRequest` names the list it reads (`Kind`), and `LibraryItem` gains where an episode
  sits (`Episode`) and whether the library holds no file for it (`IsMissing`). `ReleaseCategory`
  gains `HoldsTv` (Torznab's 5000 to 5999) and `IsSourceSpecific` (ids of 100000 and up),
  `ReleaseSearchCapabilities` the source's `TvSearch`, `ReleaseSearchQuery` a `Season` and the mode
  `Tv`, and `ReleaseSearchHit` its `Kind`. The core plans the TV queries
  (`ReleaseSearchPlanning.ForTv`), and the Torznab adapter sends them as `t=tvsearch` with
  `season=`, `tvdbid=` or a show's `tmdbid=`. Its connection test names the TV search: "Connected to
  Prowlarr over Torznab; movie search by title, TV search by title, season and episode."
- **The texts that said "movie" cover series (MAM-32).** The header field says "Search a movie or a
  series by title, e.g. Interstellar (2014)"; the Library and Search pages' leads, empty states and
  filters speak of movies and series, or of works; the Library page's identifiers filter offers
  "Any identifiers" and "Missing an id", an id of the work's kind; and removing a media library
  integration says its "movies, series, versions and files" leave the Library page.
- **`IFileInventory` walks the allowed roots (MAM-18, ADR 0005).** The contract gains
  `ListMediaFilesAsync(allowedRoots)`, an `IAsyncEnumerable` of `FileListingEntry` — a status
  (`MediaFile`, `Unlisted`, `Skipped` or `LimitReached`), the app path, where a media file really
  is, and a sentence for anything else — and which names are media files is the core's
  `MediaFileNames`. The platform's file reader gains `OpenFolder`.
- **`GET /api/library/sources` gains `scan` (MAM-18).** `{ activeRun, lastFinishedRun,
  hasQueuedRun, files }`: the scan under way — the running one, else the queued one —, the latest
  that succeeded or failed, each as `GET /api/library/scans/{runId}` gives it, whether one waits, and
  `files` `{ inspectedCount, problemCount, notInspectedCount, problem }` counted when read over the
  scan's names that no file a library reported maps to now, so a sync that starts reporting a path,
  or a fixed path mapping, takes a name off at once, without another scan; `problem` for the owner
  only. A scanned name never counts in a library's own row.
- **A file name may have no version (MAM-18, ADR 0006).** `PhysicalFiles.MediaVersionId` is nullable,
  null only on the names a scan found that no library reported; a library's name still has its
  version and goes with it. Everything that reads the library's files keeps those names out.
- **The file inspection reads every stored name and records more of each (MAM-18, ADR 0009).** A run
  looks at the names the scans stored as well as the libraries', and writes on every name the size
  and modification time the disk said beside its identity. `GET /api/library/inspections/{runId}`'s
  `trigger` may be `FolderScanned`.
- **The Library page's Sources card shows its rows without a media library too (MAM-18).** With no
  Jellyfin connected, the card says how to connect one, then shows the Media folders row and the
  file inspection's row, as neither needs a library. Its description, and the Media folders page's,
  say what **Scan folders** does.
- **`GET /api/library/sources` gains `space`, and every file inspection run gains `hashing` (MAM-19,
  ADR 0022).** Each item, `inspection` and `scan` gain `space` `{ sizeBytes, fileCount, nameCount,
  unmeasuredCount }`, counted when read — over the integration's file names, over every stored file
  name, and over the scan's names no library reports now —, each file once by device and inode, with
  the size the app read; `unmeasuredCount` is the names it has no identity or size for, left out of
  `sizeBytes`. Every signed-in user gets the same. `GET /api/library/inspections/{runId}`, the 202 of
  `POST /api/library/inspections` and the runs in `GET /api/library/sources` gain `hashing`
  `{ candidates, checked, read, problemCount, problem }`: how many files need a hash (`null` until
  the step has listed them), how many it has dealt with, how many it read in full in this run, how
  many it couldn't hash, and the first one's sentence, for the owner only.
- **`IFileInventory` hashes a file (MAM-19, ADR 0005).** The contract gains
  `HashAsync(appPath, allowedRoots)`, answering a `FileContentHash` — the file's facts and the
  SHA-256 of every byte in lowercase hex — after the checks of `DescribeAsync`, or
  `ChangedWhileReading` when the facts asked again after the read differ or the read didn't end at
  their size. The local inventory reads 1 MiB at a time from the file its platform reader opened,
  which gains `ReadAsync`.
- **The file inspection's log line counts the hashing (MAM-19).** A finished run logs "File
  inspection {RunId} finished: {Checked} of {Total} files checked, {Probed} read by the media
  inspector; {HashCandidates} files could be copies, {HashRead} read whole for their hash,
  {HashProblems} not hashed." — counts only, never a path.

### Notes for upgraders

- **Applying `AddSeriesAndTvReleases` rebuilds no table, so it logs no warning 20410 (MAM-32).** It
  adds `Searches.Season`, `Releases.Kind` (`Movie` for every release already stored),
  `Releases.CategoryIds`, `Releases.TvdbId` and `LibrarySyncRuns.SeriesCount`, and the tables
  `MediaEpisodePositions` and `DestinationSections` with their indexes. A release stored before the
  upgrade has no categories, so the Download dialog offers it the default destination; the next
  search a day later removes it, unless a download job keeps it.
- **Series and episodes appear after the next sync (MAM-32).** After the upgrade, choose **Sync
  now** on the Library page; until then the Library page and the Search page's library side list the
  movies alone. A sync reads two more lists than before, so it takes longer on a library with many
  episodes.
- **Tracker sections are picked by the owner, and none is at first (MAM-32).** Until a section is
  ticked, the Download dialog opens on the default destination, as before. Open **Destinations** in
  the user menu, choose **Edit** on a destination, open the release source under **Tracker sections
  (optional)**, tick the sections whose releases go there — for example «Мультсеріали» and
  «Мультфільми» for Cartoons, «Телесеріали» for TV Shows, the «Документальні фільми» sections for
  Documentary — and choose **Save changes**.
- **Each search asks every source with TV categories for TV too (MAM-32).** The TV queries go after
  the movie ones — one more for Toloka —, so a source's answer takes longer, and a search stores up
  to 100 movie and 100 TV releases per source. Searching needs nothing turned on.
- **Applying `AddFolderScan` rebuilds the `PhysicalFiles` table, and logs EF Core warning 20410
  once: back up `mam.db` before you upgrade (MAM-18).** Letting a file name have no version makes
  SQLite rebuild `PhysicalFiles`, which EF Core does with foreign keys off outside the migration's
  transaction, so the log shows `Microsoft.EntityFrameworkCore.Migrations[20410]` for
  `PRAGMA foreign_keys = 0`, as for `AddStoredSearches`. Unlike `AddLibraryInventory`'s rebuild of
  the same table, empty then, this one copies every file name your libraries reported, with what the
  app read of each: every value comes through, but a process killed in the middle leaves the
  migration half applied, and the next start can't apply it again without a repair by hand. Back up
  the data directory with the container stopped (README, Deployment → Data and backups), then let
  the first start after the upgrade finish before you stop the container. It also adds
  `PhysicalFiles.ReadSizeBytes` and `ReadModifiedUtc`, empty until the next inspection, and the
  `FolderScanRuns` table.
- **Nothing is scanned until the owner asks (MAM-18).** With a media folder allowed under **Media
  folders** in the user menu, choose **Scan folders** on the **Media folders** row under **Sources**
  on the Library page; the inspection it queues then reads the files it found, which takes a while
  the first time. Scan again after you add files no library lists: the app doesn't watch the folders.
- **Applying `AddFileHashes` rebuilds no table, so it logs no warning 20410 (MAM-19).** It adds the
  `FileHashes` table with its unique index on `(DeviceId, Inode, SizeBytes, ModifiedUtc)`, and to
  `FileInspectionRuns` the columns `HashCandidates`, `HashCandidatesChecked`, `HashCandidatesRead`,
  `HashProblemCount` and `HashProblem`: a run from before the upgrade lists no file to hash, with
  empty candidates and counts of 0.
- **The first inspection after the upgrade reads whole the files that could be copies (MAM-19).**
  With a media folder allowed, the inspection at the next start hashes every file whose size a file
  of another identity shares and whose facts don't tell them apart: on a library where many files
  share a size, that reads each of them in full once, one after the other, and keeps the disk busy
  for as long as that takes; later runs read only what is new or changed, and a file a new file of
  its size could be a copy of. On a library where no two
  files share a size — the reference installation's, as MAM-17 measured — it reads no file whole.
  The room the files take is counted from what the inspection read, so a file counts in it once an
  inspection has looked at it. There is nothing to turn on.

## [0.1.0] — 2026-09-28

### Added

- **The solution is scaffolded: an ASP.NET Core API, a React web app and their test projects
  (MAM-1).** `MediaAcquisitionManager.slnx` holds `Api`, `Core` (no package references) and
  `Infrastructure` under `src/`, one test project per source project plus a shared
  `TestInfrastructure` harness under `tests/`, and the React 19 web app (Vite, strict TypeScript,
  React Compiler, React Router 7, TanStack Query) under `frontend/`. The version of record is
  `frontend/package.json`; the Api project's `0.0.0-dev` is only the fallback for unstamped builds.
- **The API serves the web app, and every failure outside the health endpoints answers
  `{ "error": "…" }` (MAM-1).** Paths outside `/api/` fall back to the web app's `index.html`, so
  client-side routes survive a reload. An unknown `/api/` path answers 404 JSON instead of that
  page, so a mistyped API call can't look like a success. An unhandled exception answers 500 with a
  trace id to quote and never echoes the exception text. The health endpoints answer with their own
  report instead (see below). JSON uses camelCase names, enums as strings, and UTC instants with a
  trailing `Z`.
- **Security headers and a hash-pinned Content Security Policy on every response (MAM-1).**
  `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY` and `Referrer-Policy: no-referrer`
  are set unless an endpoint set its own value. The CSP forbids inline scripts except the theme
  script in `frontend/index.html`, which it allows by SHA-256; `SecurityHeadersHttpTests`
  recomputes the hash from the file, so editing the script without updating the hash fails the
  test suite.
- **Health endpoints (MAM-1).** `GET /api/health` reports that the process is up and runs no
  checks. `GET /api/health/ready` checks that the database opens and has every migration applied,
  and answers 503 with a readable reason per failing check ("The database has 1 pending
  migration.").
- **SQLite storage that sets itself up (MAM-1).** The database `mam.db` lives in the data directory
  (`Storage:DataDirectory`, default `Data`, relative to the content root). Every connection runs in
  WAL mode with foreign keys on and a 5-second busy timeout, and migrations are applied on startup.
  `dotnet ef` uses a design-time factory, and no `dotnet ef` command creates the data directory or
  writes secrets. A migration lock that a killed process left behind is removed once it is two
  minutes old, so an interrupted upgrade can't block every later start.
- **Secrets are generated on first run (MAM-1).** When `Encryption:Key` or `Jwt:Secret` is blank,
  the API reads it from `<data>/.secrets/` or generates it there once (directory 0700, files 0600
  on Linux and macOS), with an atomic write that never overwrites an existing file. Generating
  happens under a lock on `.secrets/.creating.lock`, because on Linux a move without overwrite is
  not atomic: two instances starting at once could each keep a key of their own while only one
  reached the disk (`FirstRunSecretsTests` failed 23 runs in 40 in a Linux container). An empty key
  file stops the startup with a message instead of being replaced, because a new key would orphan
  data encrypted with the old one. An explicit value always wins and writes no file. `Jwt:Secret`
  signs the access tokens of MAM-4; `Encryption:Key` has no consumer until the encrypted integration
  credentials of MAM-7.
- **The web app shell (MAM-1).** A header with the logo (a clapperboard with a download arrow on an
  indigo tile, also the browser-tab icon), the app name and the version, a home page whose service
  status card polls readiness every minute and names the failing check, and a not-found page. Error
  boundaries catch a crashed page, and a code chunk that fails to load after a deploy triggers one
  reload instead of a blank screen. The theme follows the operating system's dark-mode preference
  from the first paint.
- **Test harness (MAM-1).** API tests run over real HTTP through `WebApplicationFactory`, each
  against its own copy of a migrated SQLite template database and a controllable clock.
  `run-tests.sh` runs the .NET suite as parallel shards with TRX and JUnit reports. The frontend
  runs Vitest with Testing Library, including a check that every release note is well-formed.
- **Project docs (MAM-1).** A README, this changelog, a ROADMAP with phases A–D, the development
  specification moved to `docs/`, an ADR template, `.env.example`, and a `.gitattributes` that keeps
  text files LF (Windows scripts stay CRLF).
- **Every text in the web app comes from a translation resource (MAM-2, ADR 0001).** react-i18next
  reads English resources from `frontend/src/locales/en/`, one JSON file per feature, bundled so the
  first render already has its text. English is the only language; the UI runs in the first locale
  the browser prefers whose language has resources, and there is no language picker until a second
  one exists. Adding a language means adding its resource folder and registering it in
  `lib/i18n.ts`. Keys are typed, so a mistyped key fails the build. Dates, numbers and file sizes are
  formatted with Intl in the UI locale (`lib/format.ts`): a British browser gets `28/09/2026`, and
  sizes count in 1024 steps as `7.94 GiB`, the same number qBittorrent and Toloka show. A lint rule
  refuses literal text in component markup. API messages stay English sentences. The `<html lang>`
  attribute and the tab title follow the UI locale.
- **One Docker image, tested by Jenkins CI and published to Docker Hub by hand (MAM-3).**
  `vahac/media-acquisition-manager` holds the API with the built web app in its `wwwroot`, for
  `linux/amd64` and `linux/arm64`, and brings `ffprobe` (Ubuntu's FFmpeg 6.1) for the file
  inspection of MAM-9. The app runs as user `mam` (uid 10001), can't modify its own files, and
  keeps everything persistent in `/data`: the database and the secrets. It listens on port 8118
  inside the container and out, clear of the 8080 that qBittorrent's web UI and many other
  self-hosted services use. The image's health check probes `/api/health/ready`, so `healthy` means
  the database is open and fully migrated, and SQL commands now log at Warning outside Development,
  so that probe doesn't write its queries to `docker logs` every 30 seconds. `docker-compose.yml`
  runs the published image with a `mam-data` volume and read-only media mounts as commented
  examples; `docker-compose.build.yml` builds it from the checkout as `:local`. Both publish the port
  on `127.0.0.1` only by default, because the app speaks plain HTTP and its setup page makes whoever
  opens it first the owner; `APP_BIND_ADDRESS` opens it, and `APP_PORT` changes the host port.
  `deploy.sh` pulls the image, recreates the container and waits until it is healthy. The
  `Jenkinsfile` runs the .NET build and tests (skipped when only documentation changed) and the
  frontend lint, build and tests on every commit to `main`, and pushes nothing. A release is
  `Jenkinsfile.publish`, started by hand with a `VERSION`: it refuses a version that differs from
  `frontend/package.json`, runs the same checks as CI, and pushes `X.Y.Z`, `X.Y` and `latest`.
  Starting it again with a version that is already out rebuilds that release and moves its tags to
  the new image. The API is stamped with the version and the commit (`0.1.0+abc1234`).
  `.env.example` documents the Compose variables, and tests keep it covering every setting of
  `appsettings.json` and every variable the Compose files read. See [PUBLISHING.md](PUBLISHING.md).
- **Accounts and sign-in: the first visitor creates the installation owner, and everyone signs in
  with a user name and a password (MAM-4, ADR 0002).** Until an owner exists every page leads to the
  setup page. `POST /api/setup` writes the owner, the installation row and a `SetupCompleted` audit
  row in one save and signs the owner in; from then on it answers 409 ("This installation is already
  set up. Sign in instead."), also to a second setup racing the first. User names are 3–32 ASCII
  letters, digits, `.`, `-` and `_`, compared ignoring letter case and surrounding spaces; passwords
  are 8–128 characters of any kind, spaces included, with no composition rules (NIST SP 800-63B),
  stored as PBKDF2-HMAC-SHA256 with 600 000 iterations and a 16-byte salt. Sign-in returns a
  15-minute access token (HS256 with `Jwt:Secret`; issuer, audience and algorithm pinned) that the
  web app keeps in memory only, and a refresh token in the `mam_refresh` cookie: `HttpOnly`,
  `SameSite=Strict`, `Path=/api/auth`, and `Secure` only over HTTPS, because a browser won't store a
  `Secure` cookie from a plain-HTTP address. A session lasts 30 days from its last use. Every
  refresh rotates the cookie, and only its SHA-256 hash is stored; the replaced cookie gets 409
  within 30 seconds (two tabs refreshing at once, and the web app retries) and revokes the session
  after that, as a copied cookie would. Every authenticated request checks its session and its user
  in the database, so sign-out, disabling or deleting a user and a password reset take effect on the
  next request rather than when the token expires; sign-out also ends a session that a racing
  refresh had just rotated. Every failed sign-in — unknown user, wrong password, disabled account,
  blank field, or a reset, disable or delete that commits while the password is being checked — is
  the same 401 ("The user name or password is incorrect.") after exactly one password hash, so
  neither the body nor the time tells which user names exist. Sign-in and setup allow 10 attempts a
  minute per client address (429 "Too many attempts. Wait a minute and try again." with
  `Retry-After: 60`); refresh has no limit. The limit counts the connection's address and ignores
  `X-Forwarded-For`, which any client can write: behind a reverse proxy, or when Docker's own proxy
  relays the connection, all clients arrive from one address and share the count, so ten failures a
  minute from anyone keep everyone from signing in; behind a TLS-terminating proxy the refresh
  cookie isn't `Secure` either (README, Security). The web app gains the setup and sign-in pages, a
  gate that restores the session on load (retrying every 3 seconds while the server is unreachable)
  and returns you to the page you opened once signed in — except after a deliberate sign-out — and a
  user menu with Sign out; one refresh runs at a time across tabs (under a Web Lock where the
  browser has one), and signing out in one tab signs out the others. `MAM_Jwt__Secret` has its
  consumer now: rotating it makes the issued access tokens stop validating and open tabs quietly
  fetch new ones, so it signs nobody out, and a value shorter than 32 bytes is refused with a
  message naming the variable.
- **The installation owner manages users, and settings writes are owner-only by where their
  controller lives (MAM-4).** `/api/settings/users` lists users with server-side search (part of the
  name in any letter case, `%` and `_` taken literally) and paging (page size 1–100, default 25),
  creates a user (409 when the name exists in any letter case), disables and enables one, resets a
  password and deletes one. Disable ends every session of the user at once, and enabling doesn't
  revive them; a reset ends every session of the user except the caller's, so the owner resetting
  their own password stays signed in on that device; a delete takes the user's sessions with it. The
  owner can't be disabled or deleted (409). Each change writes its audit row in the same
  transaction, and only when something changed (`UserCreated`, `UserDisabled`, `UserEnabled`,
  `UserPasswordReset`, `UserDeleted`), with the actor's and the target's names copied so the row
  outlives both; the table is generic, for the downloads and replacements of later tickets, and has
  no screen yet. The Users endpoints are owner-only for reading too. Beyond them, a model convention
  makes every action of a controller under `Controllers/Settings` that isn't limited to GET or HEAD
  owner-only — an action with no HTTP method counts as a write — so a settings controller added
  later is gated without an attribute to remember; anyone else gets 403 ("Only the installation
  owner can do this.") and a logged warning. Every API action declares `[Authorize]` or
  `[AllowAnonymous]`, which a test enforces, and an unknown `/api/` path still answers 404
  `{ "error": … }` to everyone, signed in or not. An id that doesn't exist answers a bare 404 with
  no body: the framework's problem-details body for client errors is off API-wide
  (`SuppressMapClientErrors`; a 415 is bodiless too), so it adds no second error format beside
  `{ error }`. A blank or missing field in setup, create or reset answers its rule's sentence, not a
  validation report. The Users page (**Users** in the owner's user menu) searches as you type, pages
  with a page size remembered per device, adds users and resets passwords in dialogs, disables and
  enables at once, and deletes after a confirmation; the owner's own Disable and Delete are
  unavailable, with the reason on hover.
- **The real installation is described, and its services' own answers are the adapters' test
  samples (MAM-6, ADR 0003, ADR 0004).** Jellyfin 12.1, qBittorrent 5.2.3 and the app share one
  media folder, which Jellyfin and the app see as `/media` and qBittorrent as `/downloads`: each
  integration's paths will reach the app through an explicit prefix mapping, and the README and
  `docker-compose.yml` no longer ask for the same path in every service, which that layout makes
  impossible. Toloka is searched through the Prowlarr 2.6.5 already running beside them, not a new
  Jackett, and a live search worked. Prowlarr's download links carry its API key, so they stay on
  the server; Toloka's topic links can carry the forum session, which is dropped before a link is
  stored or shown. Sanitized answers of every service (Jellyfin
  libraries and movies, qBittorrent categories, preferences, torrents and files, Toloka's Torznab
  caps and search, ffprobe of two files) live in
  `tests/MediaAcquisitionManager.TestInfrastructure/VendorSamples/` with a README on where each came
  from and what it taught, among others that Jellyfin hides a collection's movies behind one item
  unless `collapseBoxSetItems=false` is sent. `VendorSamplesTests` fails the build if a sample
  carries an API key, a session, a password, a tracker URL or a magnet's tracker list. The questions
  left open (TV shows, the quarantine policy, whether library files are seeding originals) are
  listed under Phases B and C.
- **The installation owner connects Jellyfin, a Torznab source and qBittorrent under Integrations,
  and the core gets a media model and adapter contracts that name no product (MAM-7, ADR 0005,
  ADR 0006, ADR 0007).** One migration (`AddIntegrationsAndMediaModel`) adds ten tables:
  integrations, their path mappings and download destinations, works with their titles and
  identifiers, versions, physical files, releases and download jobs. Only integrations and path
  mappings are written yet; the rest wait for MAM-8 to MAM-14, their shape pinned by persistence
  tests. Every title, identifier, version, file and release names its source — the kind, the
  integration, that integration's name copied, the source's own item id and when it was observed —
  so two sources that disagree stay two rows, and removing an integration keeps what it reported
  with its name. The model refuses three assumptions by its columns: a work has no title, year or
  library id of its own, so a Jellyfin item is only the source of a version (two items of one movie
  can be two versions of one work, one item with two media sources two versions sharing its id); a
  release has no file list and no work, as one release can hold several of both; a physical file row
  is one name of a file, so hardlinks are rows sharing `(DeviceId, Inode)` and count once. A file's
  path inside the app is worked out through its integration's current path mappings when it is
  needed, never stored. Removing a download client keeps its jobs with the client's name; its
  destinations go with it. A release's download link is stored encrypted like a credential, ready
  for the server-side fetch of MAM-13. Core holds `IIntegrationAdapter` and five contracts with
  their Phase A members only: four integration kinds — `IMediaLibraryProvider`,
  `IReleaseSearchProvider`, `IDownloadClient` and `IMetadataResolver` — whose calls return one
  `IntegrationCallStatus`, and `IFileInventory`, the app's own look at its local mounts rather than
  a connection, which answers its own `FileInventoryStatus`; none throws on an expected failure.
  `CoreIsVendorNeutralTests` refuses a product name in any Core namespace, type or member. Adapter types are ids registered by Infrastructure and stored on each
  integration (`jellyfin`, `torznab`, `qbittorrent`); the registry picks the adapter by id
  (Strategy), and a type serves a kind by implementing its contract. The three adapters implement
  only a connection test for now: Jellyfin reads `/System/Info` with the key in its `MediaBrowser
  Token` header ("Connected to Jellyfin 12.1.0 (VahaC media)."); Torznab asks the feed for `t=caps`,
  at the address as entered when its last folder is `api` (Prowlarr's `/1/api`) and with `/api`
  appended otherwise (Jackett's `…/torznab/`) ("Connected to Prowlarr over Torznab; movie search by
  title."); qBittorrent signs in once when a user name is set and reads its version ("Connected to
  qBittorrent v5.2.3.", or "… without signing in" when it lets the app's address in). A failure gets
  a fixed sentence for what happened — unreachable, no connection accepted within 5 seconds or no
  answer within 15, credentials refused, refused for now, the service's own error, or an answer that isn't the
  service's — never an exception's text. The Integrations page (**Integrations** in the owner's user
  menu) searches by name, filters by kind and pages on the server, with the page size remembered per
  device; its dialog picks the service, shows only the credentials it takes, tests the unsaved
  settings with **Test connection**, edits the path mappings of a service that reports file paths,
  and has a **Use this integration** switch that is kept but not yet read by anything; a row's
  **Test** shows its sentence in a banner, and **Remove** asks first and changes nothing in the
  service. The endpoints under `/api/settings/integrations` are owner-only, reads included; a
  connection test answers 200 `{ status, message }` whatever the service said, because the test ran,
  and 409 when a stored secret can't be read or this version no longer connects to the type, whose
  integrations can then only be removed; `PUT` answers 204 and keeps a switch or mappings it isn't
  sent. Credentials are sealed with AES-256-GCM as `v1:` + base64 of a fresh 12-byte nonce, the
  ciphertext and the tag, with the integration's id and the field as associated data, so a value
  copied to another row or column doesn't open. `MAM_Encryption__Key` has its consumer now; like
  `Jwt:Secret` it is read on first use, so a value that isn't base64 of 32 bytes fails the first
  save or test that needs it with a trace id and a log line naming the variable instead of stopping
  the app. No answer carries a secret, only `hasPassword` and `hasApiKey`; a secret is set, cleared,
  or kept by leaving it out, and keeping one never decrypts it. A stored secret that can't be read
  fails only its own integration's test with 409 and a sentence asking to enter it again, is never
  cleared on its own, and is logged as a warning. An API key is 8–256 visible ASCII characters
  without spaces, quotes or backslashes, so it can be masked and sent in a header; a password is up
  to 256 characters without line breaks. Every test message is masked of the call's secrets in the
  adapter and again in the service, and text a service sent is masked before it is cut to length,
  where half a key would no longer match. The integration HTTP clients follow no redirect (the test names
  where it pointed, without the query), keep no cookies, retry nothing and log no request URL,
  because a Torznab key rides in the query; answers are capped at 4 MiB, and XML with a DTD is
  refused. An address is a full http or https address without user info, query or fragment; the
  owner may enter a private one, as these services live on the LAN. Path mappings are ordered pairs
  per integration, at most 20, saved by the dialog's one Save and only for services that report file
  paths; paths are Linux- or Windows-style, matched folder by folder (`/downloads` never covers
  `/downloads2`), minding letter case for Linux paths and not for Windows ones, and `.` or `..` is
  refused. A path mapped twice, service paths of two styles, nested pairs that send one folder to two
  places, or nested pairs from Windows service paths onto Linux paths (they disagree as soon as the
  service reports another letter case) are refused at save, and a stored set that still disagrees resolves as ambiguous rather
  than by the longest prefix; with pairs, a path none covers is not covered, and without any it is
  taken as is (ADR 0003, Implementation). Adding, editing and removing an integration write the
  audit rows `IntegrationCreated`, `IntegrationUpdated` (only when something changed) and
  `IntegrationDeleted` with its name copied; a connection test writes none. The paging rules of the
  Users and Integrations lists are shared (`PagingRules`).
- **The installation owner imports the Jellyfin library, and a sync replaces that library's
  snapshot only after reading all of it (MAM-8, ADR 0008).**
  `POST /api/library/sources/{integrationId}/syncs` answers 202 with the queued run and its
  `Location` (`/api/library/syncs/{runId}`), and the sync runs in the background (Queue/Worker: a
  run row, a queue of 64, one worker taking the runs one at a time, and a reconciler that fails every
  run still queued or running at startup with "The app restarted before this sync finished. Start it
  again."). The start is owner-only; it lives outside `Controllers/Settings`, so it declares
  `[RequireInstallationOwner]`, and `LibraryWritesAreOwnerOnlyHttpTests` sends every library
  endpoint that isn't a GET as a non-owner and expects 403. An unknown integration, or one that isn't
  a media library, is a bare 404; a switched-off one, a type this version can't read a library from,
  or a sync already under way is 409 with its sentence — two starts racing queue one run, as a
  partial unique index (`IX_LibrarySyncRuns_OneActiveRunPerIntegration`) allows one active run per
  integration — and a full queue is 503, its run kept as failed. A sync tests the connection first
  (the version gate, see Changed), then reads Jellyfin's libraries (`/Library/VirtualFolders`) and
  every movie in pages of 50 that overlap by one (`/Items` with `collapseBoxSetItems=false`, and
  `/Videos/{id}/AdditionalParts` for a movie stacked from several files), with GET requests only. A
  total that changes between pages, a short page before the end, a boundary movie that moved, or
  fewer different movies than listed fails the run ("…its library changed while it was being read.
  Nothing was changed here…"), and a failure of any kind stores nothing. Only then, in one
  transaction with the run's success and counts, the library's rows are replaced. Its items are
  grouped into works per library, by IMDb and TMDb id only: items linked by shared ids are one work,
  and a group with two values of one scheme splits by its maximal identifier sets, never by title
  and year. A version is its media source, so merged versions that Jellyfin 10.9 to 10.11 list
  under every item of the group are stored once. Rows are found again by their provenance keys, so
  a work, a version or a file keeps its id and first-seen time across syncs, and an upgraded file
  under a new Jellyfin item id keeps its work through its ids; what the library no longer reports is
  deleted, and so are the works it leaves empty. A sync never puts its facts on a work another
  source holds. Every end state of a run is a conditional write on a run still under way, never
  cancelled; a failure's sentence is masked of the key again, cut to 1000 characters, and logged as
  a Warning with its status only. The adapter reads Jellyfin's answers field by field: the
  displayed and original title with the year, the IMDb and TMDb ids (never a collection id), each
  media source as a version with its path exactly as sent and its size, an edition label only when
  an item has two or more sources, and a file only for a local video file — a DVD or Blu-ray
  folder, a `.strm` link or a remote source gives a version without one. An item that isn't a movie,
  or has no id, fails the page instead of being skipped. One migration (`AddLibraryInventory`) adds
  the `LibraryLocations` and `LibrarySyncRuns` tables, the library of each file and the search
  columns below.
- **The Library page lists every movie the media libraries reported, searched, filtered and paged by
  the server (MAM-8).** **Library** in the header opens it for every signed-in user; the header's
  links and the user menu's entries now come from one list (`lib/nav-items.ts`).
  `GET /api/library/works` pages the works (page size 1–100, default 25) by title in Ukrainian
  alphabetical order, letter case ignored, with Latin titles after the Cyrillic ones: a
  `LIBRARY_TITLE` collation comparing with the `uk` culture is registered on every SQLite
  connection, and falls back to the invariant culture where ICU is missing. `search` finds part of
  a title or an original title in any letter case (upper-cased copies in `NormalizedText` and
  `NormalizedServicePath`, as SQLite folds only ASCII letters; `%` and `_` are taken literally),
  part of an IMDb or TMDb id, a year, or part of a file path. `locationId` keeps the works with a
  file in that Jellyfin library, `identifiers=Missing` those lacking an IMDb or a TMDb id, and a
  filter that is neither answers 400 with its sentence. Each work has its title, original title and
  year (`null`, shown as unknown, when the library gave none), its identifiers with their source,
  the schemes it lacks, and its versions with their files: the path as the service reported it, the
  path in the app worked out through the integration's current path mappings when the list is
  read and never stored (`PathMapper`'s first user, ADR 0003), how it mapped and, without an app
  path, why. `GET /api/library/locations` lists the Jellyfin libraries that hold a file,
  `GET /api/library/sources` every media library integration with its latest and last successful
  run and the number of its files the current mappings give no app path, and
  `GET /api/library/syncs/{runId}` one run (bare 404 when there is none). A run's error can name the
  service's address, a source's or a file's path problem quotes the owner's path mappings, and who
  started a run is the owner's sign-in name, so only the owner gets them; everyone else gets `null`,
  and the page says the owner can see why. The page polls the sources every 2 seconds
  while a sync is under way and refreshes the works once it succeeds. Each movie is a card with its
  identifier badges, a warning badge per missing id, and its versions and files; the sync panel
  shows each library's progress or outcome, the last successful sync after a failure, a warning
  when files have no app path, and **Sync now**, disabled with its reason for anyone but the owner,
  for a switched-off integration, for a type that can't be read and while a sync is under way.
- **The app reads the library's own files, only inside the folders the owner allows, and keeps
  what ffprobe read by each file's identity (MAM-9, ADR 0009).** An inspection runs in the
  background after every successful library sync, when the app starts, and when the owner asks:
  `POST /api/library/inspections` answers 202 with the queued run and its `Location`
  (`/api/library/inspections/{runId}`, readable by every signed-in user, owner-only fields `null`
  for the rest). It is owner-only (`[RequireInstallationOwner]`, swept by
  `LibraryWritesAreOwnerOnlyHttpTests`), 409 with "No media folders are allowed yet. Add the folders
  the app may read under Media folders in the user menu, then inspect again." while no folder is allowed,
  and 503 when the queue is full. At most one run is queued and one running
  (`IX_FileInspectionRuns_OneRunPerActiveStatus`): a request or trigger that finds one queued joins
  it. A run maps every file name the libraries reported through its integration's path mappings
  (`NoAppPath` when they give none), looks at each app path through the file inventory, answers from
  the cache what it holds for the file's device, inode, size and modification time, and reads the
  rest with ffprobe, once per key and two files at a time; each file's outcome is written as it
  comes, and at the end the run deletes the readings no name points at that were its to forget —
  those orphaned before it started and a changed file's old one. A file the run found nothing at
  keeps its last reading, so a mount that was away for a run costs no reading, and a reading a sync
  orphaned while the run went waits for the run that sync queued. A file it can't read is
  an outcome with its sentence — `OutsideAllowedRoots`, `NotFound`, `NotARegularFile`,
  `Unreadable`, `MediaUnreadable` (cached like a success, so a damaged file isn't read again until it
  changes), `IdentityUnknown` or `InspectorUnavailable` (never cached; the files still waiting in the
  run get the same sentence without starting ffprobe again) — never a failure of the run. The
  reconciler fails a run a stop interrupted ("The app restarted before this inspection finished. What
  it had read is kept."), queues a queued one again and queues the start's run; a run left running
  by a worker that couldn't mark it failed is failed by the next run's claim. The local file
  inventory resolves every link on the way, folder by folder (40 at most), and refuses a path, or a
  link, that leaves the allowed roots; it reads the identity with `statx` on Linux (`st_dev` as
  glibc's `makedev`, so it matches `stat -c %d`) and the NTFS file index on Windows. It opens the file
  it checked without following a link (on Linux name by name with `O_NOFOLLOW`, a pipe or a device
  never opened for reading) and asks the open file again after ffprobe reads it, so a file that changed
  meanwhile is not cached. ffprobe reads that open file — on Linux the inherited descriptor, as
  `/proc/self/fd/<n>` — as `file:<path>` with `-protocol_whitelist file` and a `-format_whitelist` of
  the containers films come in, so neither a URL nor a file a crafted list or playlist names is
  opened, through an argument list, for at most 2 minutes and 8 MiB of answer; when it fails, the
  sentence quotes its verdict, the last line it wrote. Its JSON is read field by field: duration, overall bit rate, and
  each video, audio and subtitle track with its codec, profile, size, dynamic range (Dolby Vision from
  its configuration record, else PQ as HDR10, HLG, any other declared transfer as SDR, and unknown
  when the stream declares none), channels, layout, language tag (`und` is none) and its default,
  forced and SDH flags; cover art is not a track, and track titles are not kept. One migration
  (`AddFileInspection`) adds the `AllowedRoots`, `MediaInspections`, `MediaInspectionTracks` and
  `FileInspectionRuns` tables and the link count and inspection columns of `PhysicalFiles` (its
  identity columns came with MAM-7; MAM-9 fills them).
- **The Media folders settings page sets where the app may read files (MAM-9, ADR 0009).**
  `GET /api/settings/allowed-roots` lists the folders and `PUT` replaces them (204), both owner-only,
  as they describe the host; each save that changes them writes an `AllowedRootsChanged` audit row. None by default, so
  nothing is read until the owner names a folder as the app sees it (`/media` in the Docker image's
  examples). A list that breaks a rule answers 400 with its sentence: more than 20 folders, a path
  that isn't full, a `.` or `..` folder, the whole file system, a path in the other operating
  system's style than the app runs on, a folder listed twice or one inside another. Saving starts no
  inspection.
- **The Library page shows each file's verified facts and where the inspection stands (MAM-9).**
  Each file of a version shows its duration, resolution, video codec and profile, dynamic range and
  overall bit rate, then its audio tracks (language, codec, channels, default and forced) and its
  subtitles (language, codec, default, forced and SDH); a value the file didn't give says unknown, and
  a file not inspected yet says so. A file the app couldn't read shows why to the owner and a short
  reason by its status to everyone else. `GET /api/library/works` gives each file an `inspection`
  (`null` until the app has looked), `GET /api/library/sources` each source's inspected, problem and
  not-yet-inspected counts with the first problem's sentence for the owner, and the list where
  inspections stand: whether a folder is allowed, the run under way and the last finished one. The
  sources panel shows that line with **Inspect files**, disabled with its reason for anyone but the
  owner, with no folder allowed and while a run waits — on its own or behind the running one, which
  the line then says (`hasQueuedRun`) — and polls while a run is under way.
- **Every signed-in user searches the release sources, and each source answers for itself (MAM-10,
  ADR 0004, ADR 0005).** `POST /api/releases/search` with `{ text, year, imdbId, tmdbId }` asks every
  enabled release source at once and answers 200 `{ sources }`: each source with its `status`, the
  sentence of a failure (`error`, for the owner only, as it can name the service's address), and the
  releases it listed, in its own order — title, size, seeders and peers, publish date, the page at the
  source, its categories with their names, whether it is a magnet, and the quality and languages the
  title claims (resolution, medium, video codec, dynamic ranges, audio and subtitle languages), marked
  as claims and unknown where the title says nothing. A failing source never hides the others, and a
  source the app can't ask says why (`notSearchedReason`: a stored key it can't read, a type this build
  can't search through, a feed with no search it can use). 400 with its sentence for a missing or
  overlong title, a year that isn't one, or an id that isn't an IMDb or a TMDb id. A switched-off
  source is left alone. Every source's capabilities are read first, and the search goes the way they
  allow: an IMDb or TMDb id alone where the movie search takes one, then the title and the year, then
  the title alone; Toloka through Prowlarr takes only text, so it is asked "Interstellar 2014", in the
  movie categories, for at most 100 releases. The Torznab adapter speaks plain Torznab, with nothing
  specific to Prowlarr or Toloka; Prowlarr's bare 401 to a wrong key, recorded from the reference
  installation, reads as a refused key. Verified live on 2026-10-01: Toloka through the reference
  Prowlarr answered 16 movie releases for "Interstellar" 2014.
- **A release's download link stays on the server, and it holds no copy of the gateway's key (MAM-10,
  ADR 0004, ADR 0007).** Each release a search finds is stored as a new release row, its download
  link sealed to it with the API key replaced by a placeholder that the fetch fills with the
  integration's current key; the browser gets the release's id. Releases stored more than a day ago
  that no download job points at are removed by the next search (ADR 0006). The page link loses the
  owner's forum session (`sid`, in any form), and one that could carry a credential, looks like a
  download or points at the gateway itself is not kept; a listing's id is kept only when it is that
  page link, and is otherwise a SHA-256 digest of it, since Prowlarr writes the tracker's own download
  link, passkey and all, as the id of an indexer that gives none. The payload fetch the download step
  will use (MAM-13) is built: a magnet as it is, a link only from the feed's own origin and only once
  — a redirect to a magnet is the payload, any other redirect is refused — and a feed whose links
  point at another origin fails its search with a sentence naming it.
  `ReleaseSearchSecretsHttpTests` plants the key, the gateway's link token, the session and tracker
  passkeys wherever a feed puts them and finds none in the answers to the owner or a member, in the
  log or in the release rows outside the sealed link.
- **The installation owner keeps download destinations — each a category the download client
  confirms it has, never a folder — with one default for the whole installation (MAM-11,
  ADR 0010).** `/api/settings/destinations` lists them (part of the name in any letter case, page size
  1–100, default 25, ordered by name, then by the client's name), reads one, adds one (201 with its
  `Location`), changes one (204) and removes one (204), and
  `GET /api/settings/destinations/client-destinations/{downloadClientId}` lists what a download client
  offers right now, each category checked. All of it is owner-only, the reads included, as a row
  quotes the client's folders and the owner's path mappings. A destination is a name (up to 64
  characters, unique per client in any letter case) and the client's own category name, stored
  exactly as the client spells it — never trimmed or matched in another letter case, as it is what a
  download will send — and no save path: the table has no column for one and no request carries one.
  Adding a destination, and a change of its client or category, asks the client first: 409
  "“qBittorrent” has no category “Movies 4K”. Create it in the client first, or pick one it has." when
  it has no category spelled exactly so; 502 with the client's own sentence when it was asked and
  couldn't answer (unreachable, timed out, credentials refused, a version too old, …); 409 when it
  can't be asked at all — switched off ("“qBittorrent” is switched off. Switch it on under
  Integrations to use it for destinations."), a type this version can't send downloads to, or a
  stored secret it can't read. A rename or a change of the default alone saves without asking the
  client, so it works while the client is away. A broken rule answers 400 with its sentence ("A
  destination needs a name.", "Pick the download client this destination belongs to.", "Pick the
  category of the download client this destination saves to.", at most 64 and 256 characters, no
  control characters), an id that isn't a download client 404 with "The download client you picked no
  longer exists. Reload the page and pick one again.", and a name the client's destinations already
  have 409 ("“qBittorrent” already has a destination named “Movies”."), also to the loser of two saves
  racing for one name. The first destination becomes the default whatever it asks; a later one only
  with `isDefault: true`, which takes the default from the one that had it in the same transaction.
  Un-marking or removing the default leaves none until the owner picks one, and a partial unique index
  (`IX_DestinationMappings_OneDefault`) keeps two saves that each take it from leaving two. Adding,
  changing and removing write the audit rows `DestinationCreated`, `DestinationUpdated` (only when
  something changed; a default taken off another destination is part of that row) and
  `DestinationDeleted`, with the name copied; a download job sent to a removed destination keeps its
  name and category. One migration (`AddDestinationDefaultAndLibraryFolders`) adds the `IsDefault`
  column and the `LibraryLocationFolders` table (see Changed).
- **Each destination says where its category saves and whether that is inside a media library,
  worked out when it is read and never stored (MAM-11, ADR 0003, ADR 0010).** Every read asks each
  download client on the page once, all of them at once, and answers 200 whatever they said. A
  destination's `check` carries the folder the client says the category saves to, the same folder in
  the app through the client's path mappings, and the media libraries one of whose root folders holds
  it, folder by folder, the root itself included — or a status and its sentence: `OutsideLibraries`
  ("“/media/incoming” is outside every media library folder, so automatic replacement stays off for
  this destination."); `PathUnknown`, when the client doesn't say where or the mappings give no app
  path, with the mapper's reason; `LibrariesUnknown` ("The app doesn't know the media library folders
  yet. Sync a library from the Library page, then look again.", or, when folders are stored but none
  of them maps into the app, a sentence sending the owner to the media library's path mappings
  instead); `CategoryMissing`; `ClientDisabled`, as a switched-off client isn't asked; and
  `ClientUnavailable`, with the client's sentence masked of the call's secrets again. A folder that
  only holds a library folder (`/media` above `/media/movies`) is outside it. Every status but
  `InLibrary` will keep automatic replacement off for the destination (Phase C); it stays usable for
  downloads. On the reference installation's samples the four qBittorrent categories land in their
  four Jellyfin libraries (`/downloads/movies` → `/media/movies`, "Фільми"). The library root folders
  this needs are stored by the library sync now (see Changed), and each is mapped through its own
  Jellyfin integration's current mappings when a destination is checked. A client that fails, or
  whose answer the app can't read, costs only its own destinations their check.
- **qBittorrent lists its categories and the folders they save to (MAM-11, ADR 0005, ADR 0007,
  ADR 0010).** The adapter implements the download client contract now: its capabilities (.torrent
  files and magnet links, categories that have folders, no job id promised by a submission) and the
  destinations. A listing reads `app/version` and refuses a qBittorrent older than 4.4.1 (see
  Changed), reads `torrents/categories` field by field — each category's name as qBittorrent keys it
  and its `savePath`, nothing else — and asks `app/defaultSavePath` only when some category's folder
  sits under the default save path. It never reads `app/preferences`, which carries the Web UI's API
  key and the proxy and mail passwords, and sends nothing but the sign-in and GETs. Each folder is
  worked out the way qBittorrent's own source does for the version that answered: an absolute
  `savePath` as it is, a relative one under the default save path, an empty one as the default save
  path and the category's name — except that from 5.2 a subcategory goes under its parent category's
  folder. From 4.5 qBittorrent lists a Windows host's network folder with `/`, so a save path that
  starts with exactly two `/` is that UNC folder (`//nas/media/Movies` is `\\nas\media\Movies`, and a
  5.2 subcategory without a save path of its own goes inside it); 4.4 lists a save path as it was
  saved, where a leading `//` may be a POSIX folder too, so there it is unknown, as is a category under
  a default save path that starts with `//`. From 4.5 no Windows host lists a backslash, so a save
  path such as `D:\Torrents\Movies` comes from another host, where it is a relative folder under the
  default save path whose name holds the backslashes; under a Windows default save path it is unknown. On 5.0 and 5.1 a subcategory without a save path follows
  an option only `app/preferences` reports, so its folder is unknown, and so is the folder of a name
  qBittorrent would rewrite on disk. A category whose name the app couldn't store or send back intact
  (blank, over 256 characters, a control character) is left out of the list, and an answer that isn't
  qBittorrent's categories fails the listing ("The service at this address answered, but not with
  qBittorrent's categories."). The adapter keeps one session per saved integration in memory, for as
  long as its address and credentials stay the same, so a page that lists destinations doesn't sign
  in on every load. When qBittorrent has forgotten the session — it answers 403, recorded from the
  reference installation — the adapter signs in once more and sends that one read again, once. A
  session it refuses right after handing it out fails the call ("qBittorrent refused the session it
  had just opened.") and is dropped, so the next call signs in. No call repeats a failed sign-in, and
  a sign-in qBittorrent refused — the user name or password (401, or 200 `Fails.`), or the app's
  address banned (403) — is remembered in memory for the integration, by the same fingerprint of its
  address and credentials, so no later call sends it again while they stay the same: qBittorrent
  counts each refused sign-in towards banning the address (five in a row by default), and every page
  load, dialog and save lists destinations. Such a call sends nothing and answers the same status
  with "qBittorrent refused the user name or password the last time the app signed in, so the app
  won't try them again: each refused sign-in counts towards qBittorrent's ban of this app's address.
  Correct them under Integrations, or test the connection there once qBittorrent accepts them." or
  "qBittorrent had banned this app's address the last time the app signed in, so the app won't sign
  in again on its own. Once the ban has ended (qBittorrent's Web UI options set how long), test the
  connection under Integrations.", which the Destinations list, the dialog's categories and a save's
  502 then show. The refusal goes once the address or credentials change, once a call signs in with
  other ones, and once a connection test of the integration signs in with the very same ones; a test
  of other credentials typed into the Edit dialog leaves it, as they may never be saved. A connection
  test qBittorrent refuses is remembered the same way, but never in place of a refusal of other
  credentials. A restart forgets it. A failure qBittorrent didn't count — unreachable, timed out, a server error, an answer
  that isn't qBittorrent's — isn't remembered, and the next call tries again; an unsaved integration
  keeps neither a session nor a refusal, and the connection test still signs in afresh every time
  without asking whether its credentials were refused
  (`QBittorrentAdapterSessionsTests.ListDestinations_twice_with_refused_credentials_signs_in_once_and_answers_the_second_from_memory`).
  Only qBittorrent 5.2 was verified live — the samples and the answer to a forgotten session come
  from the reference installation — and 4.4.1 to 5.1 are accepted from reading qBittorrent's source,
  as is how a Windows host lists a network folder (with Qt's). Verified live on 2026-10-01 through the
  app against the reference installation: the connection test signed in to qBittorrent v5.2.4, the
  category listing answered its four categories, and the destination Movies (with the path mapping
  `/downloads` → `/media`) saves to `/media/movies`, inside the Jellyfin library "Фільми", after a
  live sync stored that library's folders.
- **The Destinations settings page (MAM-11).** **Destinations** in the owner's user menu, right after
  Integrations, lists the destinations with their client, their category and where they save — the
  client's folder, the app's when it differs and the media libraries it lands in, or a warning badge
  with the API's sentence — with a **Default** badge, searched and paged on the server, the page size
  remembered per device. **Add destination** and a row's **Edit** open one dialog: a name, the
  download client (a switched-off one marked), its categories asked from the client once one is
  picked, the chosen one showing where it saves before anything is saved, and **Default
  destination**. On an edit, a category the client no longer has stays choosable, marked missing, so
  the destination can still be renamed. **Remove** asks first, and warns that no destination is the
  default after the default is removed. Adding, changing or removing an integration refreshes the
  destinations too, and the Integrations page's confirmation of a download client's removal says that
  every destination using it goes with it, the default too if it is one of them, while its categories
  stay in the client. Anyone else who opens the page's address gets the API's 403 sentence.
- **Every signed-in user searches the library and every release source at once, and the search is
  stored, so every filter, sort and page reads what it found (MAM-12, ADR 0011).**
  `POST /api/searches` with `{ text, year, imdbId, tmdbId }` — every field a string, the title
  required, with MAM-10's rules and sentences — asks the library and every enabled release source at
  the same time, the library through a database context of its own, and answers 201 with its
  `Location` (`/api/searches/{id}`) whatever the sources answered: the search as asked, trimmed; the
  works of the library that match it or hold its words (see below); each source in name order with
  its `status`, its `notSearchedReason`, the sentence of a failure (`error`, for the owner only) and
  how many releases it listed; and the audio languages and resolutions those releases claim, for the
  filters. A broken rule answers 400 with its sentence, stores nothing and asks no source. The search
  is a `Searches` row of the caller's, with a `SearchSources` row per source — its name copied, its
  sentence masked and cut to 1000 characters — and every release a new MAM-10 row linked to its
  source, with its place in the source's answer, its seeders and peers and the ids its listing gives.
  `GET /api/searches/{id}` reads it again: the sources as stored, never asked again, and the library
  matched again as it is now, so a sync since shows. `GET /api/searches/{id}/releases` filters the
  stored releases by source (`source`, a search source's id), audio language (`language`, a
  three-letter code in any letter case, or `unknown`), resolution (`quality`, such as `1080`, or
  `unknown`) and size (`minSizeBytes` and `maxSizeBytes`, both inclusive, a release of unknown size
  left out once either is set), sorts them (`seeders` by default, `largest`, `smallest`, `newest`,
  `quality`; a value the listing doesn't give always last, and ties by the source's place in the
  search, then the release's in its source's answer) and pages them (page size 1–100, default 25),
  on the server and in memory, since what a title claims is read from it on every read and a search
  holds at most 100 releases per source. A value it can't read answers 400 with its sentence ("“uk”
  is not a language code. Use a three-letter code such as ukr, or unknown."), and a well-formed source
  id of another search lists nothing. Each release carries what its title claims — MAM-10's quality
  and languages, and the titles and year it is read as — the ids its listing gives, and every work of
  the search's library side it matches. A search is its creator's: asked for by anyone else, the
  owner included, it answers a bare 404 on both reads, and so does one more than a day old, even
  before a prune removes it. The next search, stored or plain, removes the searches more than a day
  old with their sources, and a release a download job sent stays without its search; deleting a
  user removes their searches. MAM-10's `POST /api/releases/search` stays as it was (owner's
  choice), and both searches ask and store through one implementation (see Changed). One migration
  (`AddStoredSearches`) adds the `Searches` and `SearchSources` tables and the new release columns
  (see Notes for upgraders). `SearchesConcurrencyHttpTests` holds the library's first read until a
  feed is asked, so a search that asked one after the other fails, and `SearchesSecretsHttpTests`
  finds no planted key, link token, session or passkey in the three answers, the stored rows or the
  log.
- **A search and each of its releases are matched to the library's works by identifier, then by
  title and year, then by title alone, and every match is listed, strongest first — never picked,
  merged or acted on (MAM-12, ADR 0012).** The library side reads the snapshot the last sync
  imported, never Jellyfin itself. A work matches by `Identifier` when it holds an IMDb or TMDb id
  asked for (trimmed, in any letter case), by `TitleAndYear` when one of its titles — displayed or
  original, from any source — has a claimed title's key and that title's own year is the year
  claimed, and by `TitleOnly`, a candidate to check, when only the key does; nothing else counts, no
  distance between titles and no year ± 1. A title's key is the title upper-cased, its apostrophes
  dropped so the letters around them join ("Ocean's Eleven" is `OCEANS ELEVEN`, like the scene's
  "Oceans.Eleven", and "Дев'ять", "Дев’ять" and "Девʼять" are one key), and every other run of
  characters that aren't letters, digits or combining marks one space. Each match carries the ids
  that matched with every source that holds them, the title that matched with its kind, year and
  source, and `conflictingSchemes`: a scheme the claim and the work both hold values of but share
  none of, or one the work itself holds two values of — flagged, never resolved, and no bar to a
  title match. The works come by evidence, then by title in Ukrainian alphabetical order, each as the
  Library page's card, versions, files and verified facts included, with the owner-only fields kept
  the owner's. A release is matched to every work its search lists — the works by words (see the
  next entry) included — by the ids its listing gives and by the titles and year read from its title
  (owner's choice): the first bracketed year with a title before it ("Interstellar (2014) BDRemux
  1080p" is "Interstellar" of 2014), else the scene's bare year ("Interstellar.2014.1080p.BluRay"),
  the part before it split at " / " into alternatives ("Інтерстеллар / Interstellar"), and no title
  at all without a year, since nothing then says where the title ends; it lists every work it
  matches, strongest first. Nothing is stored about a match.
- **After the matches, the search's library side also lists the works one of whose titles or file
  paths holds every word of the title searched for — at most 20, marked `Words`, never a match — and
  a release is matched to every work the search lists, never by words (MAM-12 review, ADR 0012).**
  Searched for "rambo", the Library page found five films and the search page one: only "Rambo"
  (2008) has the key asked for, "Rambo III" and the others hold the word in a longer title, and
  "First Blood" (1982) only in its file's path (`/media/movies/Rambo. Collection/…`). The three
  tiers stay as they were. A work none of them matches is listed with the evidence `Words` when one
  of its titles, from any source, displayed or original, or one of its files' service paths holds
  every word of the title's key, each whole, in any order: "rambo" finds "Rambo III" and the folder
  `Rambo. Collection`, and "alien" doesn't find the title "Aliens". The year doesn't narrow it, ids
  aren't searched for words, and a title with no letter or digit lists nothing. `matchedTitle` is the
  title holding the words, `null` when only a path does, and conflicting ids are flagged as for a match.
  These works come after every match, at most `LibraryMatcher.MaximumWorksByWords` (20), in the same
  order, and the answer's new `libraryUnlistedCount` says how many more hold the words; the cap is
  applied before any card is read, and the database narrows the candidates by file path as it does
  by title. A release is still matched by identifier, title and year or title alone, never by words,
  but against every work the search lists, so "Rambo III (1988) BDRip" says "In your library: Рембо
  III (1988) — matched by title and year"; a release's `evidence` is never `Words`. Look-alikes the
  matching refuses, "Interstellar Wars" for "Interstellar", show there too, labelled as no match.
  Nothing about them is stored.
- **A release's title is read without the square-bracketed tags that end it, so an edition of a film
  you own matches it by title and year (MAM-12 review, ADR 0012).** Searched for "rambo", Toloka's
  "Rambo: Last Blood [Extended Cut] (2019) BDRip-AVC" and "V / Rambo [Theatrical Cut] (2008)" matched
  nothing: the tag between the title and the year was read as part of the title, `RAMBO LAST BLOOD
  EXTENDED CUT`, which no work holds. Each title alternative, once trimmed, now loses every
  square-bracketed group that ends it, so they read as "Rambo: Last Blood" of 2019 and "V" and "Rambo"
  of 2008 and match those films by title and year; scene names the same. Only at the end (owner's
  choice): a title that opens with or holds square brackets keeps them ("[REC] 2"), a group that is
  all that is left stays ("[REC]"), and round brackets are never dropped, as they hold titles
  ("Birdman or (The Unexpected Virtue of Ignorance)"). A tag that opens a title, "[HDRip] Rambo
  (2008)", still stays in it. `claimedWork.titles` shows the titles without the tag; the release's
  `title` is unchanged.
- **The Torznab adapter reads the IMDb and TMDb ids a listing gives (MAM-12, ADR 0004).** The
  attributes `imdbid` (`tt0816692` or its digits), `imdb` (the digits, which Jackett writes without
  their leading zeros: `816692`) and `tmdbid` give a release at most one id per scheme, IMDb first,
  an IMDb id as a lower-case `tt` and its digits padded to seven, so it equals the id a library
  holds. A blank value, one of zeros only (Jackett's `0` for none) or one `MediaIdentifierRules`
  refuses is no id, and a listing whose attributes give two different values of one scheme gives none
  of it, as a listing that contradicts itself can't be matched. Both searches store the ids with the
  release (`ImdbId`, `TmdbId`). Toloka through Prowlarr lists none, so its releases match by title
  and year.
- **The Search page (MAM-12).** **Search** in the header, first, before **Library**, opens it for
  every signed-in user. Its form takes the title and, optionally, the year, the IMDb id and the TMDb
  id, each a field of its own (owner's choice), and shows the API's sentence under it; the search's
  id goes into the address (`/search?id=…`), so a reload or Back keeps it, and a search that is gone
  says "This search is no longer available. Search again." **In your library** shows each matching
  work as the Library page's card with a badge for how it matched — "Matched by IMDb id", "Matched by
  title and year", "Possible match — check it" — the matched title or id with its source on hover,
  and a warning badge per conflicting scheme. Below the matches, under a line of their own, come the
  works that hold every word searched for, each badged "Title contains “rambo” — not a match" or "A
  file path contains “rambo” — not a match"; when more than 20 do, a line says how many more, with a
  link to the Library page searched for the same text, which the Library page now reads from its
  address (`/library?search=…`). **Available releases** shows a line per source — how many releases
  it listed, its failure's status with the owner's sentence, or why it wasn't searched — then the
  filters (source, audio language, quality, size in GiB from and to, sort), each change going back
  to page 1, and each release with its listed facts, its claimed quality under a visible **Claimed**
  label, a badge for its strongest library match, and **Details** (a dialog with the
  listing, what its title was read as and its ids, beside every matching work's versions and
  verified facts), **Open source** (the source's page in a new tab, greyed out with the reason when
  there is none) and **Download**, greyed out with "Sending a release to a download client comes in a
  later update." The list is paged with a page size remembered per device; the dialog has two
  columns on a wide screen and one on a phone. The Library page's version, file and track lines are
  components of their own now, shared with the dialog.
- **Every signed-in user sends a release one of their searches found to a download client, and one
  request sends it at most once: the job is written, with the request's id and an audit row, before
  anything is fetched or sent (MAM-13, ADR 0013).** Every signed-in user may send, as everyone may
  search (MAM-13's decision 1), so the endpoints live in `Controllers/Downloads`, outside the
  namespaces whose writes are owner-only. `GET /api/downloads/options?releaseId=…` answers the
  Download dialog's choices from the database alone, asking no client: every switched-on download
  client of a type this build can send to that can take the release's payload — one whose kind the
  listing didn't state, a gateway's link, needs a client that takes a .torrent file or a magnet
  link — and has a destination, by name, each `{ id, name, typeName, destinations }` with its
  destinations by name (`{ id, name, clientDestinationKey, isDefault }`): names only, no address,
  folder or path mapping. It answers 400 "Pick the release to send." without a well-formed
  `releaseId`, and 404 "This release isn't available any more: a search keeps what it found for a
  day. Search again." when the release isn't one of the caller's own searches' from the last day —
  another user's, an older search's, or one MAM-10's plain search found. `POST /api/downloads` with
  `{ requestId, releaseId, downloadClientId, destinationId }` looks the request id up first, before
  anything else is read: the caller's same id asking for the same release, client and destination
  answers 200 with the job it made, even after the release, the client or the destination went, and
  fetches, sends and audits nothing; asking for anything else, it is 409 "This request id was
  already used to send a different download. Reload the page and try again." A new request is then
  checked in this order, each refusal writing and sending nothing: 400 for a missing id ("The
  download has no request id, which keeps a retry from sending it twice. Reload the page and try
  again.", "Pick the release to send.", "Pick the download client to send it to.", "Pick the
  destination to send it to."); 404 for the release as above, for a client that isn't there or
  isn't a download client ("The download client you picked no longer exists. Reload the page and
  pick one again.") and for a destination that isn't there ("The destination you picked no longer
  exists. Reload the page and pick one again."); 400 for a destination of another client ("“Movies”
  is a destination of another download client. Pick one of “qBittorrent”’s."); 409 for a
  switched-off client ("“qBittorrent” is switched off. The owner can switch it on under
  Integrations.") or one of a type this build can't send to ("This version of the app can't send
  downloads to “…”."); 400 for a client that can't take the payload ("“qBittorrent” can't take NZB
  files, which this release is."); 409 for a source that was removed ("The source that listed this
  release has been removed, so the release can't be fetched. Search again."), is switched off
  ("“Toloka” is switched off, so its releases can't be fetched. The owner can switch it on under
  Integrations.") or is of a type this build can't fetch from ("This version of the app can't fetch
  releases from “…”."); and 409 "This release was already sent to “qBittorrent”, so it isn't sent
  twice." when a job that didn't fail — anyone's, as the client is the household's — already sent
  it to that client, recognised by its source and the source's id for it, or by the info hash its
  listing states, since every search stores its own copy of a listing (MAM-13's decision 3,
  ADR 0006). Then the job is written `Created` — the request id, who sent it with their name copied,
  the release, and the client and the destination with their names and the category copied — with
  an audit row `DownloadRequested` (target kind `DownloadJob`, named by the release's title cut to
  200 characters with "…"), in one save, and sent (next entry); the answer is 201 with the job and
  its `Location` (`/api/downloads/{id}`) whatever came of it, a client that couldn't be reached
  included. `GET /api/downloads/{id}` reads the caller's own job; another user's answers a bare 404,
  like a missing one. A job is `{ id, releaseId, releaseTitle, sourceName, downloadClientName,
  destinationName, clientDestinationKey, status, clientJobId, error, createdUtc, statusChangedUtc }`,
  `status` being `Created` (being sent, or how that went isn't known), `Submitted` (the client took
  it; `clientJobId` is `null` while the client hasn't listed it), `NeedsLinking`, or `Failed` with
  `error` (`Downloading` and `Downloaded` are MAM-14's). Two copies of one request racing make one
  job: a unique index lets one commit, and the other answers its job. One migration
  (`AddDownloadJobRequests`) adds `RequestId`, `RequestedByUserId` (a foreign key to `Users` that
  sets null when the user is deleted, so their jobs stay with `RequestedByUserName`),
  `RequestedByUserName`, `InfoHashV1`, `InfoHashV2` and `ErrorOrigin` to `DownloadJobs`, and the unique
  index `IX_DownloadJobs_RequestedByUserId_RequestId`; a deleted user's request ids no longer collide, as
  SQLite never counts two nulls the same. It rebuilds `DownloadJobs` on upgrade, a table no build
  before MAM-13 wrote to (see Notes for upgraders).
  `DownloadsIdempotencyHttpTests.Create_twice_with_one_request_id_answers_the_same_job_and_sends_once`,
  `DownloadsIdempotencyHttpTests.Create_twice_at_once_with_one_request_id_makes_one_job_and_sends_once`,
  `DownloadsIdempotencyHttpTests.Create_with_a_request_id_used_for_another_download_is_409_and_writes_nothing`
  and `DownloadsIdempotencyHttpTests.Create_of_a_release_already_sent_to_the_client_is_409_and_sends_nothing`
  pin the request id and decision 3,
  `DownloadsValidationHttpTests.Create_to_a_client_that_can_not_take_the_release_is_400_and_asks_nothing`
  and `DownloadsValidationHttpTests.Create_to_an_unknown_destination_is_404_and_asks_nothing` the
  refusals, and
  `DownloadOptionsHttpTests.Options_lists_the_switched_on_clients_that_take_the_release_with_their_destinations`
  the dialog's choices.
- **A job's sending fetches the release, checks the category and the client's own list, and sends it
  once; the client's id or the torrent's info hash confirms it, never a name (MAM-13, ADR 0013).**
  Once the job is written, its sending runs to its end whether or not the browser is still there,
  and each step that fails ends the job `Failed` with its own sentence: the payload fetched from the
  source (MAM-10's fetch, the integration's current key put back) — the source's sentence when it
  fails, and "The release's download link can't be read with this installation's encryption key, so
  nothing was sent. Search again." for a link the installation's key can't open, logged as a warning
  naming the release's id only; its kind checked against the client ("“qBittorrent” can't take a
  magnet link, which this release turned out to be, so nothing was sent."); its info hashes worked
  out by Core's `TorrentInfoHashReader` — the SHA-1 (v1) and SHA-256 (v2, BEP 52) of a .torrent's
  info dictionary exactly as written, read defensively, or a magnet's `xt` topics (`urn:btih:` in
  hex or base32, `urn:btmh:1220…`) — and both stored on the job (`InfoHashV1`, `InfoHashV2`), as a
  client names a hybrid torrent by either; the destination's category confirmed live, since
  qBittorrent creates a category it lacks in its default folder ("“qBittorrent” has no category
  “Movies” any more, so nothing was sent: it would have created one in its default folder. Create
  the category again in “qBittorrent”, or the owner edits the destination."); the client asked
  whether it already has the torrent, which fails the job rather than adopting a download the app
  didn't add ("“qBittorrent” already has this torrent, so nothing was sent.", MAM-13's decision 4);
  and only then one submission. A receipt with the client's id makes the job `Submitted` with it.
  Without one, the client's list is read for the hashes, and once more after half a second when the
  torrent isn't listed yet: one job holding them makes it `Submitted` with that job's id, none
  `Submitted` without an id and with its hashes kept for MAM-14, and several, or a payload with no
  hash at all, `NeedsLinking` — the specification's linking by hand, which the app never guesses at
  (`ClientJobCorrelation`). A submission whose answer leaves open whether the client took it — no
  answer in time, the connection lost, a server error, an answer the app can't read — is never sent
  again: the list is read instead, once more after half a second when the torrent isn't listed yet,
  and the torrent listed there was taken (listed under several jobs, it needs linking); not listed,
  the job fails with the client's sentence. A refusal, refused credentials and a version too old
  fail it at once. Each outcome is a conditional write on a job still `Created`, never cancelled; an
  error is masked of the call's secrets once more and cut to 1000 characters with "…", and the log
  says the status only. A service's sentence — the source's or the client's, which can name its
  address — goes to the owner only, MAM-10's rule: anyone else who sent the release reads
  "“qBittorrent” couldn't take the release, so nothing was sent. Ask the owner to check
  “qBittorrent” under Integrations." (or "“Toloka” couldn't hand over the release, …" for the
  source), in the send's answer and every read of the job, and reads the app's own sentences as they
  are (`ErrorOrigin` says whose a job's error is; `DownloadsErrorVisibilityHttpTests`). A copy of a
  request racing the first, which finds the first's job only when it asks whether the release was
  already sent, is answered with that job, never refused as already sent. A send waits for the fetch
  and up to five calls to qBittorrent, each of their requests bounded at 15 seconds.
  `DownloadsSendHttpTests.Create_is_201_with_the_job_the_client_took_its_row_and_its_audit_row`,
  `DownloadsSendFailuresHttpTests.Create_whose_category_is_gone_fails_and_sends_nothing`,
  `DownloadsSendFailuresHttpTests.Create_for_a_torrent_qbittorrent_already_has_fails_and_sends_nothing`,
  `DownloadsSendFailuresHttpTests.Create_whose_add_answered_500_but_listed_the_torrent_records_it_submitted`
  and `DownloadsSendFailuresHttpTests.Create_with_two_jobs_listed_for_the_hash_needs_linking` pin
  it, `TorrentInfoHashReaderTests` and `ClientJobCorrelationTests` the hashes and the matching, and
  `DownloadsSecretsHttpTests.No_answer_log_job_or_audit_row_of_a_download_carries_a_planted_secret`
  that no API key, link token, password, session or tracker passkey reaches an answer, the log, a
  job's error or an audit row.
- **qBittorrent takes a release into the destination's category with automatic torrent management,
  and finds its torrents by info hash (MAM-13, ADR 0005, ADR 0010).** A submission signs in, or uses
  the kept session, and reads `app/version` first: the read renews a session qBittorrent has
  forgotten, so the write goes out on a session it has just accepted, and the version gate refuses a
  qBittorrent older than 4.4.1 before anything is posted. Then one `POST api/v2/torrents/add`, as
  `multipart/form-data` the way qBittorrent's Web UI sends it: the .torrent as a `torrents` file part
  (`release.torrent`, `application/x-bittorrent`) or the magnet as `urls`, `category` — the
  destination's key exactly as stored, never trimmed — and `autoTMM=true` (MAM-13's decision 2:
  under manual management, qBittorrent's default for a new torrent, a category alone decides no
  folder), and never `savepath`, `downloadPath`, `useDownloadPath`, `rename`, `tags`, `paused` or
  `stopped`. It is never sent again, a 403 included: without a session that is the sign-in
  qBittorrent asks for, and with one the session is dropped, the call fails and the next call signs
  in. The answer is read by its status and body shape, which covers 4.4.1 to 5.1 and 5.2 alike:
  `Ok.` (4.4.1 to 5.1) is taken without an id; `Fails.` is refused ("qBittorrent didn't add the
  release: it already has this torrent, or it couldn't use what it was sent."); a JSON object (5.2)
  is taken, its `added_torrent_ids` naming the job only when it lists exactly one id of 40
  hexadecimal characters; 202 (5.2) is taken and still pending; 409 (5.2) is "qBittorrent added
  nothing: it already has this torrent, or it couldn't use what it was sent."; 415 is "qBittorrent
  couldn't read the .torrent file it was sent (415 Unsupported Media Type)."; and any other 200 is
  "The service at this address answered, but not the way qBittorrent answers a new torrent." A
  magnet's text, and every tracker and source address in it as an echo would spell it, are masked
  like a session in whatever the call says, since a tracker's address can carry a passkey. The job
  lookup reads `app/version` the same way, then sends the hashes as qBittorrent names a torrent — the
  v1 hash, or the v2 one cut to 40 characters — lower-case, each once, joined by `|`, as
  `GET api/v2/torrents/info?hashes=…`, never the whole list; no hash asks nothing. It reads the
  answer as an allowlist: each torrent's hash, `infohash_v1` and `infohash_v2`, its name cleaned and
  cut to 1000 characters, its state mapped onto the contract's (`Queued`, `Downloading`, `Paused`,
  `Seeding`, `Completed` for `pausedUP` and `stoppedUP`, `Errored` for `error` and `missingFiles`,
  and `Unknown` for `moving` or a state it doesn't know), its progress from 0 to 1, its category and
  its content path; a torrent without a usable hash is skipped, and an answer that isn't an array of
  objects fails ("The service at this address answered, but not with qBittorrent's torrents.").
  `QBittorrentAdapterSubmitTests` pins the submission against every answer. Verified live on
  2026-10-02 against the reference installation (qBittorrent v5.2.4, Toloka through Prowlarr): a
  release sent from the Search page was fetched through Prowlarr, taken by `torrents/add` with the id
  qBittorrent listed, matching the v1 info hash the app worked out itself, and saved in the Movies
  category's folder (`/downloads/movies`) under automatic torrent management, with no save path sent.
  A first try of the same release failed in Prowlarr (its "Download failed" for Toloka's answer); the
  job recorded the feed's sentence, nothing reached qBittorrent, and the next Send went through. The
  answers of 4.4.1 to 5.1 to `torrents/add` are written from qBittorrent's source, and none was
  recorded.
- **The Download dialog on the Search page (MAM-13).** A release's **Download** opens it for every
  signed-in user. **Download client** lists only the clients the server offers for that release, and
  **Destination** the picked client's destinations; it opens on the default destination and the
  client that holds it, otherwise on the only client and its only destination, otherwise on the
  placeholders, with **Send** disabled and "Pick a download client and a destination first." A
  summary lists the source, the release, the client, the destination with its category, and
  "Existing copies — Kept: this version of the app never replaces or removes a file." **Send** comes
  first, **Cancel** last. The dialog makes the request id when it opens, from
  `crypto.getRandomValues` (a LAN address over plain HTTP is no secure context, so
  `crypto.randomUUID()` isn't there), sends the same id again after the server couldn't be reached
  or refused — a Send while the browser says it is offline is tried at once and fails into that
  sentence, never held back to go out on its own once the browser is online (`networkMode:
  'always'`) — and makes a new one only after a `Failed` job, whose sentence shows in the form with
  the pickers kept; a second click before **Sending…** shows sends nothing. Any other outcome
  replaces the form with a line — "qBittorrent took the release and lists it among its downloads.",
  "qBittorrent took the release, but hasn't listed it among its downloads yet.", "qBittorrent took
  the release, but the app can't tell which of its downloads it is, so it has to be linked by
  hand.", or "This release is already being sent to qBittorrent. Its outcome isn't known yet." — the
  summary and **Close**. With no client to offer, it says "No download client can take this release
  yet. The owner sets them up under Integrations and Destinations." The dialog is mounted only while
  it is open, so every opening is a new request.
- **The app follows every download it sent by reading its client every 2 seconds, until the
  download finishes, and a restart goes on where the rows are (MAM-14, ADR 0014).** A background
  service (`DownloadJobTracker`) runs a pass right after the app starts, then 2 seconds after each
  pass ends, so two never overlap; each pass reads the followed jobs from the database and keeps
  nothing in memory. Followed are `Submitted`, `Downloading`, the new `Paused`, and a `Created` job at
  least 5 minutes old — a send a stop interrupted, as no send takes that long, so a send still under
  way is never raced; `Downloaded`, `Failed`, `NeedsLinking` and the new `Cancelled` are never read
  again. Per client, one lookup of every job by its stored id and info hashes (each valid 40- or
  64-character hexadecimal value, lower-case, once, at most 50 to a request) through `FindJobsAsync`.
  A job found takes the client's word through the core's `DownloadJobFollowing`: downloading is
  `Downloading`, queued `Submitted` until the job has started and `Downloading` after, paused
  `Paused`, and a finished state is `Downloaded` — which ends the following — only with a progress of
  1, never either alone, as a re-check reports its own progress under a finished state; a finished
  state short of 1, `moving` and a state this build doesn't know move nothing but a `Created` job to
  `Submitted`, and a progress the client doesn't give keeps the stored one. The job keeps the client's
  current id, as qBittorrent renames a hybrid torrent once a magnet's metadata arrives, and the v2 hash
  the client lists when it had none; a job found twice is `NeedsLinking`. A job its ids don't find,
  sent with a v1 hash only, is looked for among its destination's jobs, once per destination and pass:
  ADR 0013's hybrid that qBittorrent built on libtorrent 2 renames to its v2 hash. An interrupted send
  with no hash stored fails without asking the client ("The app stopped while sending this download,
  before it could tell whether “qBittorrent” took it, so it isn't followed. Check “qBittorrent” before
  sending the release again."); found, it is followed from then on; found twice, it needs linking; not
  listed, it fails ("The app stopped while sending this download, and “qBittorrent” doesn't list it,
  so it isn't followed. Send the release again if you still want it."). A job's files are read when it
  has none stored or its status or progress changed, so a finished, idle or paused job isn't read
  every pass. They are read from the client before anything is written, then replaced by position in
  the same transaction as the job's own write, when anything differs, so a job never reads as
  `Downloaded` with the files of the pass before; a failed read keeps them as they are, and the job is
  written all the same. Once a files read or a destination listing times out, can't reach the client or
  throws, the pass asks that client nothing more: its other jobs' files stay as stored and a job that
  needs a listing shows that call's sentence. Every write a pass makes to a job — its row and its files
  together — is conditional on the status the pass read, so a cancel racing a pass wins and leaves both
  as they were; `StatusChangedUtc` changes only with the status, and a job nothing happened to is not
  written. Each status change logs the job's id with both statuses, never a title, a path, an address
  or a sentence; one client's bug costs that client the pass, and a pass that throws is logged and
  the next one runs as usual. One migration (`AddDownloadJobTracking`) adds `Progress`, `BlockReason`
  and `BlockReasonOrigin` to `DownloadJobs`, the table `DownloadJobFiles` (the job, the file's place in
  the client's list — unique per job — its path exactly as the client names it, relative to the job's
  folder, its size, its progress and whether it is wanted, deleted with its job), a partial index of
  the followed jobs (`IX_DownloadJobs_Followed`, its filter the core's list of followed statuses) and
  `IX_DownloadJobs_RequestedByUserId_CreatedUtc` for the Jobs list; it rebuilds no table (see Notes for
  upgraders). `DownloadJobTrackingHttpTests.Poll_maps_what_qBittorrent_reports_onto_the_job`,
  `DownloadJobTrackingHttpTests.Poll_of_a_finished_download_ends_its_following_and_never_asks_about_it_again`,
  `DownloadJobTrackingHttpTests.Poll_stores_a_jobs_files_and_reads_them_again_only_when_the_job_moved`,
  `DownloadJobTrackingHttpTests.Poll_reads_the_files_before_the_job_turns_downloaded_and_writes_them_with_it`,
  `DownloadJobTrackingHttpTests.Poll_of_a_client_that_stops_answering_after_the_lookup_asks_it_nothing_more_that_pass`,
  `DownloadsCancelHttpTests.Poll_racing_a_cancel_while_it_reads_the_files_writes_neither_the_job_nor_its_files`
  and `DownloadJobTrackingHttpTests.Poll_writes_nothing_for_a_job_nothing_happened_to` pin the
  following,
  `DownloadJobRecoveryHttpTests.Poll_after_a_restart_follows_the_jobs_a_previous_run_left_and_asks_about_no_ended_one`,
  `DownloadJobRecoveryHttpTests.Poll_of_an_interrupted_send_qBittorrent_took_follows_it_from_then_on`
  and `DownloadJobRecoveryHttpTests.Poll_finds_a_hybrid_torrent_qBittorrent_renamed_after_its_magnet_through_its_destination`
  the restart and ADR 0013's gaps, `DownloadJobFollowingTests` and `ClientJobCorrelationTests` the
  mapping and the matching, and `DownloadJobPersistenceTests` the new rows and indexes.
- **A followed job that isn't moving on keeps its status and says why, and stays followed (MAM-14's
  decisions 2 and 3, ADR 0014).** A torrent qBittorrent reports an error for (`error`,
  `missingFiles`) shows "“qBittorrent” reports an error for this download, so it isn't moving on.
  Check it in “qBittorrent”: the job goes on by itself once the error clears."; one qBittorrent no
  longer lists, "“qBittorrent” doesn't list this download any more. It is still followed in case it
  comes back; to send the release again, cancel this job first.", gone once the torrent comes back
  with the same hash; and one taken without an id and still unlisted a minute later — a qBittorrent
  before 5.2 lists a torrent a moment after it took it — "“qBittorrent” took this download but hasn't
  listed it since. It is still followed; to send the release again, cancel this job first." A client
  that can't be asked never makes a job missing: every followed job of it shows why and keeps its
  status — "The download client “qBittorrent” was removed, so this download can't be followed. Cancel
  the job to stop tracking it.", "“qBittorrent” is switched off, so this download isn't followed for
  now. The owner can switch it on under Integrations.", "This version of the app can't follow
  downloads in “…”.", the sentence for a stored secret the app can't read, qBittorrent's own sentence
  for a call that failed, masked of the call's secrets once more, or "The app couldn't read what
  “qBittorrent” answered." for an adapter that throws, logged with its type and stack but never its
  message. A reason is written only when it differs from the stored one, and cut to 1000 characters
  with "…". As for a failed job's error (MAM-13), qBittorrent's own sentence is the owner's: anyone
  else reads "“qBittorrent” couldn't be asked about this download. Ask the owner to check
  “qBittorrent” under Integrations." (`BlockReasonOrigin`). After qBittorrent refused the
  credentials, the passes never sign in again until the owner changes them or a connection test with
  them works, so they never add to its ban.
  `DownloadJobTrackingHttpTests.Poll_of_a_torrent_qBittorrent_reports_an_error_for_keeps_its_status_with_why_until_the_error_clears`,
  `DownloadJobMissingHttpTests.Poll_of_a_torrent_removed_from_qBittorrent_keeps_the_job_with_why_and_follows_it_until_it_comes_back`,
  `DownloadJobMissingHttpTests.Poll_of_a_client_that_can_not_be_asked_never_makes_a_job_missing`,
  `DownloadJobMissingHttpTests.Poll_after_qBittorrent_refused_the_credentials_never_signs_in_again_until_the_owner_tests_them`,
  `DownloadJobMissingHttpTests.Poll_of_a_removed_torrent_whose_destination_listing_fails_shows_the_clients_sentence_never_missing`,
  `DownloadJobRecoveryHttpTests.Poll_of_an_interrupted_send_whose_destination_listing_fails_stays_created_with_why_until_it_answers`
  and `DownloadJobMissingHttpTests.Get_and_list_show_a_clients_own_sentence_to_the_owner_and_the_apps_to_a_member`
  pin it: a failed destination listing, like a failed lookup, decides no job.
- **Every signed-in user lists their own download jobs and a job's files, and cancels a job, which
  only stops following it: nothing is sent to the client (MAM-14, ADR 0014).** A job stays its
  sender's (MAM-14's decision 1): the list, the files and Cancel answer the caller's own jobs, the
  installation owner's included, and another user's job is the same bare 404 as one that doesn't
  exist. `GET /api/downloads?status=&page=1&pageSize=25` answers
  `{ items, totalCount, page, pageSize }`, newest first (by when it was sent, then by id), each item a
  job as `GET /api/downloads/{id}` answers it; `status` is a status by name in any letter case, a
  blank one lists every status, and anything else is 400 "“x” is not a download status. Use Created,
  Submitted, Downloading, Paused, Downloaded, Failed, NeedsLinking or Cancelled."; a page below 1 or a
  page size outside 1–100 is 400. `GET /api/downloads/{id}/files?page=1&pageSize=25` answers the job's
  files as its client last listed them, in the client's order, each
  `{ position, path, sizeBytes, progress, isWanted }`, and an empty page while the client hasn't
  listed any; the job is checked before the paging. `POST /api/downloads/{id}/cancel` makes a job
  still followed, or one that needs linking, `Cancelled`, clearing its reason, with an audit row
  `DownloadCancelled` (target kind `DownloadJob`, named by the release's title), in one transaction,
  and answers 200 with the job; a job that already ended is 409 "This download is already finished,
  so there's nothing to stop following.", "This download has already failed, so there's nothing to
  stop following." or "This download is already cancelled, so there's nothing to stop following.",
  and changes nothing. A `Created` job counts as followed only once it is 5 minutes old, as for the
  poller: younger, its send may still be under way, and it is 409 "This download is still being sent,
  so it can't be cancelled yet. Try again in a few minutes.", changing nothing — the age is checked in
  the same conditional update, so a cancel never throws away the outcome of a send still working. It is a conditional update: a pass that moved the job on since it was read
  doesn't stop it, and no later pass undoes it. qBittorrent is never asked: `IDownloadClient` has no
  member that removes, deletes, stops or pauses anything, and `DownloadClientNeverRemovesTests` keeps
  it so. `DownloadsListHttpTests.List_answers_the_callers_jobs_newest_first_with_every_field`,
  `DownloadsListHttpTests.List_of_the_installation_owner_holds_only_their_own_jobs`,
  `DownloadJobFilesHttpTests.Files_of_the_callers_job_are_200_in_qBittorrents_order_as_the_poller_read_them`,
  `DownloadsCancelHttpTests.Cancel_is_200_with_the_job_cancelled_its_row_and_its_audit_row`,
  `DownloadsCancelHttpTests.Cancel_sends_nothing_to_qBittorrent_and_no_later_pass_asks_about_the_job`,
  `DownloadsCancelHttpTests.Poll_racing_a_cancel_never_undoes_it`,
  `DownloadsCancelHttpTests.Cancel_of_a_created_job_still_being_sent_is_409_with_why_and_changes_nothing`
  and `DownloadsCancelHttpTests.Cancel_of_another_users_job_is_a_bare_404_even_for_the_installation_owner`
  pin them, and `DownloadsSecretsHttpTests.No_answer_log_job_or_audit_row_of_a_download_carries_a_planted_secret`
  now also runs two passes, the second against a redirect that echoes the session and the password,
  and reads the list, the files and a cancel.
- **qBittorrent reads a torrent's files and a category's torrents (MAM-14, ADR 0005).** Both sign in,
  or use the kept session, and read `app/version` with its gate first, as every read does. A torrent's
  files are `GET api/v2/torrents/files?hash=…` with the torrent's id, lower-case and cut to 40
  characters, read as an allowlist in qBittorrent's order: the `name` kept exactly — never trimmed or
  cleaned, as qBittorrent's own names carry double spaces — and up to 4096 characters, a file without a
  usable one failing the whole answer; the `size` as a whole number of bytes, beyond 32 bits too; the
  `progress` from 0 to 1; and `priority` 0, qBittorrent's "do not download", as not wanted. A 404 there
  means the torrent is gone ("qBittorrent doesn't have this torrent any more."), a torrent still
  fetching its metadata lists `[]`, and an answer that isn't an array of objects fails ("The service at
  this address answered, but not with qBittorrent's files."). A category's torrents are
  `GET api/v2/torrents/info?category=…`, the key sent exactly as stored, read as the lookup by hash
  reads them; a blank key is refused before anything is sent, as qBittorrent would read it as "no
  category". A key holding a `+` is never sent: qBittorrent's request parser percent-decodes a query
  value and only then reads `+` as a space, so `%2B` arrives as a space too and would ask for another
  category; such a key asks for every torrent and keeps those of exactly that category. The test
  qBittorrent decodes a query the same way. The adapter still sends one write, `torrents/add`.
  `QBittorrentAdapterFilesTests.ListJobFiles_reads_the_three_files_of_the_sample_exactly` reads the
  recorded pack whole, and `QBittorrentAdapterFilesTests` and `QBittorrentAdapterDestinationJobsTests`
  pin every answer.
- **The Jobs page and a job's details (MAM-14).** **Jobs** in the header, last, for every signed-in
  user, lists their own jobs, newest first: the release, which opens the job; its status, with why a
  followed job isn't moving on as a warning line and why it failed as an error line; its progress as a
  bar and a percentage that never rounds up to 100 before the download is complete, or "Not known yet";
  where it went (the client and the destination); and when it was sent. It filters by status on the
  server, starting again at page 1, pages with a page size remembered per device, turns into cards on
  a phone, and asks again every 2 seconds while it shows a job the app still follows. With no job
  yet it says "No download jobs yet. Send a release from Search, and it is followed here until it
  finishes.", linking to Search. A job's page shows its source, client, destination with its category,
  status, progress, when it was sent and its status last changed, and the client's id, then its files
  — the path, the size, the progress, and **Skipped** for one the client doesn't download — paged,
  asked for again when the job moves or while a followed job lists none yet. **Cancel job** is on both
  pages for a job still followed or one that needs linking; a `Created` one gets it only once it is 5
  minutes old, judged at the time of the page's last answer, since the server refuses a younger one
  with its sentence. It asks first — "The torrent and its files
  stay in qBittorrent: nothing is paused, stopped or removed there." — with **Cancel job** and **Keep
  following**. A job that isn't the caller's, or an address that isn't a job's, reads "There is no
  download job of yours at this address. It may belong to someone else, or the link is wrong."
  `JobsPage.test.tsx`, `JobsPage.polling.test.tsx`, `JobDetailsPage.test.tsx` and
  `JobProgress.test.tsx` pin it.
- **Phase A's acceptance bar holds on the reference installation, and the README and the release
  note say which integrations were tried live and which weren't (MAM-15).** Checked on 2026-10-03
  through the app run from the repository against Jellyfin 12.1.0, Prowlarr 2.6.5 serving the
  Toloka.to indexer, and qBittorrent 5.2.4. The header search "Sherlock Holmes (2009)" found the
  movie inside `/media/movies/Guy Ritchie. Collection` where it is, matched by title and year,
  beside Toloka's 12 releases, six of them matched to it. With the Torznab feed pointed for one
  search at an indexer Prowlarr doesn't have (`/999/api`, its key kept), the Search page still
  showed that match and said under Available releases "Prowlarr · Answered in a way this app can't
  read. Nothing answers Torznab at this address (404 Not Found)…"; the address was then put back,
  and both changes are `IntegrationUpdated` audit rows. MAM-13's request of 2026-10-02, sent again
  with its request id, answered 200 with that same job, and the same release (Toloka's topic
  `t126881`) from a new search answered 409 "This release was already sent to “qBittorrent”, so it
  isn't sent twice."; the database still held two jobs and two `DownloadRequested` audit rows, and
  the following passes still found the torrent missing from qBittorrent, removed there since
  MAM-13's check, so nothing was added. The destination Movies is qBittorrent's category `Movies`,
  saving to `/downloads/movies` (`/media/movies` for the app) inside the Jellyfin library "Фільми",
  and MAM-13's send landed there under automatic torrent management with no save path. A library
  sync afterwards (876 items, 872 files) listed each of the 866 files of the 2026-10-01 sync at the
  same path with the same size; the six new files are a collection added outside the app. Not tried
  live yet: Jackett and other gateways and indexers, a feed that searches by IMDb or TMDb id, lists
  them or gives magnet links, qBittorrent before 5.2 or on a Windows host, a Jellyfin older than
  12.1 or one that merges versions, a download followed to `Downloaded`, a paused one, a job's files
  and a job that needs linking, and the file inspection on the installation's own files. The README
  (**Tested with**) and the release note list both; the README's and the roadmap's MAM-12 lines no
  longer say the Search page wasn't tried against the reference installation.
- **Every signed-in user changes their own password by giving the current one, which ends their
  other sessions and keeps this one (MAM-16, ADR 0016).** `POST /api/auth/password` takes
  `{ currentPassword, newPassword }` and answers 204. In one transaction it stores the new hash,
  writes a `UserPasswordChanged` audit row (the user is both actor and target) and revokes every
  other session of the user, so another device is refused with its next request and its refresh;
  the session the request came through, named by the access token's `sid`, stays, and the refresh
  cookie is never read. A blank current password is 400 "Enter your current password." and a new
  one that breaks the rule is 400 with the rule's sentence, both before any hash runs; a wrong
  current password is 400 "The current password is incorrect." after exactly one hash, or none for
  one over 128 characters. It is never a 401, which the web app would take for an expired token. An
  owner's reset, disable or delete that commits while the current password is being checked wins,
  and the change gets the wrong-password answer, as a sign-in does. Ten attempts a minute per user,
  from whatever address and counted apart from sign-in, then 429 with the sign-in sentence and
  `Retry-After: 60`: a stolen access token can't spread its guesses over many addresses, and a
  mistyped password doesn't use up the sign-ins of everyone behind the same router. `GET /api/auth/me`
  now answers `{ id, userName, isOwner, createdUtc, lastSignInUtc, sessionExpiresUtc }`, the last
  being the end of the token's own session; sign-in, refresh and setup answer the user as before.
  `AuditAction` is stored by name, so there is no migration, and ADR 0002 gains a section on this
  fifth way a session ends.
  `AuthPasswordHttpTests.ChangePassword_keeps_the_calling_session_and_ends_the_users_other_sessions_at_once`,
  `AuthPasswordHttpTests.ChangePassword_overtaken_by_a_change_to_the_user_during_the_password_check_is_400_and_the_owners_change_stands`,
  `AuthPasswordHttpTests.ChangePassword_is_rate_limited_per_user_whatever_the_client_address` and
  `AuthMeHttpTests.Me_says_when_the_calling_session_ends_not_another_session_of_the_user` pin it.
- **A Profile page for every signed-in user (MAM-16).** **Profile** in the user menu, first under
  the user's name, which links there too and is tinted while the page is open, opens `/profile`:
  the user's initial, name (with an Owner badge for the owner) and what their role may do; an
  Account card with the user name, the role, when the account was created and when it last signed
  in on any device, in the browser's locale; a Password card; the theme, with its labels, saved in
  this browser; and a Session card that says when this session ends unless it is used again, with
  **Sign out**. **Change password** opens a form for the current password, the new one (8–128
  characters) and the new one again. It checks those before sending anything and puts each sentence
  under its field; a 400 the server still answers can only be about the current password, so its
  sentence goes there, while too many attempts or an unreachable server show in a banner above the
  buttons. On success the form closes and a banner at the top says the other devices are signed out
  and this one stays signed in. The web app now refreshes an expired access token for
  `/api/auth/me` and `/api/auth/password` as for any other call, and skips that only for sign-in,
  refresh, sign-out and setup, so Profile left open past the 15-minute token doesn't read "Sign in
  to continue." `ProfilePage.test.tsx`, `ChangePasswordCard.test.tsx`, `UserMenu.test.tsx`,
  `api.test.ts` and `App.test.tsx` pin it.
- **Light, Dark or System, picked in the user menu (MAM-16).** MAM-1 applied the operating
  system's dark-mode preference before the first paint, and nothing could change it. The menu's
  **Theme** row now stores Light or Dark in `localStorage` under `mam:theme`, the key the pre-paint
  script in `index.html` already read, and removes the key for System, so a reload opens in the
  picked theme with no flash while the script and its CSP hash stay as they were. A store
  (`lib/theme-store.ts`) sets `.dark` on `<html>`, follows the operating system live while the
  theme is System, whether or not the menu is open, and takes a choice made in another tab. The
  switch is a shared `SegmentedControl` of native radio buttons: one tab stop, and the arrow keys
  move the choice. `theme-store.test.ts`, `ThemeSwitch.test.tsx` and `SegmentedControl.test.tsx`
  pin it.
- **A strip under the header says when the server can't be reached, and goes once it answers
  (MAM-16).** Before, only the session gate said so, while a tab restored its session; a page open
  when the server stopped filled with red lines. Now the first request that gets no answer — or a
  502, 503 or 504 with no JSON body, which is what the Vite dev proxy and a reverse proxy answer for
  a stopped API — shows a strip under the header, sticky with it: "The server could not be
  reached. Check that it is running, then try again." and "Trying again every few seconds."
  with **Retry now**. It asks `GET /api/health` at once and every 5 seconds, also while the browser
  says it is offline, and goes on the first answer to any request; the page keeps what it showed,
  and the lists and panels that failed meanwhile are asked for again by themselves. An action taken
  meanwhile reads the same sentence, now for the proxies' empty 502, 503 and 504 too. The strip
  stays hidden while the session gate or the sign-in page says it themselves. One axios interceptor
  in `lib/api.ts` feeds `lib/connection-store.ts`. `OfflineStrip.test.tsx`, `connection-store.test.ts`,
  `api.test.ts` and `utils.test.ts` pin it.

### Changed

- **Removing a media library integration removes the library it imported (MAM-8, ADR 0006).**
  MAM-7 kept everything an integration had reported, with its name. Now
  `DELETE /api/settings/integrations/{id}` also deletes, in the same transaction as the removal and
  its audit row, the titles, identifiers, versions, files and libraries the integration reported as
  a media library, and the works they leave empty. What any other kind of source reported, the
  download jobs sent to it and the library's sync runs stay, with its name. The Integrations page
  says so in the confirmation of a media library's removal.
  `IntegrationsDeleteHttpTests.Delete_keeps_the_facts_it_reported_with_their_source_name` gave way
  to `Delete_of_a_media_library_purges_its_snapshot_and_keeps_its_sync_runs_with_its_name` and
  `Delete_of_a_release_source_keeps_its_releases_with_the_source_name`.
- **The Jellyfin connection test refuses a Jellyfin older than 10.9 (MAM-8, ADR 0005).** It answers
  the new status `UnsupportedVersion` with "This is Jellyfin 10.8.13; the app reads libraries from
  Jellyfin 10.9 or later. Update Jellyfin, then test again.", and a version without a leading
  `major.minor` is refused as one it can't compare; the test still answers 200. A sync runs the same
  test first, so it never reads from an older server.
- **The Use this integration switch is read now (MAM-8).** MAM-7 kept it, but nothing read it. A
  switched-off media library can't be synced (409, and a run queued before the switch fails), while
  the library it imported last stays listed.
- **A successful library sync queues a file inspection (MAM-9).** Once a sync has replaced its
  library's snapshot, it queues an inspection of the files, or joins the one waiting; with no folder
  allowed it queues nothing, and a sync never fails because of it. A sync still never touches what
  the app read itself: a file keeps its identity, link count and inspection across syncs while its
  path stays.
- **`IFileInventory` takes the allowed roots on every call and reads media (MAM-9, ADR 0005).**
  MAM-7's contract described a file inside "the configured roots"; now the caller passes the roots
  it read from the owner's setting, and `InspectMediaAsync` reads the streams. `FileInventoryStatus`
  gained `MediaUnreadable`, `InspectorUnavailable` and `ChangedWhileReading`.
- **A Torznab feed's error text is cut before the app shows it (MAM-10, ADR 0004).** Every address in
  a feed's `<error description>` is cut to its scheme and host, and every credential-named pair
  (`passkey=`, `sid=`, …) is masked, before the text is cleaned and cut to 200 characters: Jackett's
  error 900 and Prowlarr's download error 500 quote the tracker's URL, passkey and all. A connection
  test's sentence that quoted the request (`…/1/api?t=caps&apikey=***`) now names the host only. The
  XML a feed answers is refused when it nests deeper than 32 levels, before it is read.
- **The IMDb and TMDb id rules are shared (MAM-10).** The Jellyfin adapter and the release search
  check ids with one rule (`MediaIdentifierRules`), so a search is never refused an id a library
  holds.
- **The release search contract is decided by the core (MAM-10, ADR 0005).** `ReleaseSearchQuery`
  carries its mode, its limit and only what the source takes, chosen by `ReleaseSearchPlanning` from
  the capabilities, which now say whether the source searches by text, whether its movie search takes
  text, its result limit and which categories hold movies; a hit carries its peers, and its id may be
  missing.
- **`IDownloadClient` carries only what MAM-11 implements (MAM-11, ADR 0005).** MAM-7 gave it every
  Phase A member; now it has `Capabilities` and `ListDestinationsAsync`. `SubmitAsync`,
  `ListJobsAsync` and `ListJobFilesAsync` come back with MAM-13 and MAM-14, which implement them,
  rather than stay as members qBittorrent would have to stub until then; the records they take and
  give stay in Core.
- **The qBittorrent connection test refuses a qBittorrent older than 4.4.1 (MAM-11, ADR 0005).** It
  answers `UnsupportedVersion` with "This is qBittorrent v4.3.9; the app works with qBittorrent 4.4.1
  or later. Update qBittorrent, then test again.": 4.4.0 lists a category's folder under another name
  (qBittorrent issue #15969), and older versions differ more. A version it can't compare is refused
  rather than guessed, and the test still answers 200. Listing the destinations applies the same gate.
- **A library sync stores each library's root folders (MAM-11, ADR 0008).** With each library, the
  sync now keeps the folders Jellyfin lists for it (`LibraryLocationFolders`), in Jellyfin's order and
  as Jellyfin reports them, in the same transaction as the rest of the snapshot, so a failed sync
  keeps the last good one's. They are replaced on every sync rather than matched, as nothing points at
  a folder; they go with a library the sync no longer reports, and with the integration's snapshot
  when it is removed. Only the destination check reads them: which library holds a file is still
  decided from the folders the sync has just read.
- **MAM-10's release search keeps more of each listing, and asks and stores through the code the
  stored search uses (MAM-12, ADR 0006, ADR 0011).** `POST /api/releases/search` answers exactly as
  before, but each release row it stores now also keeps its seeders and peers, its place in its
  source's answer and the ids its listing gives, with no search source
  (`ReleaseSearchHttpTests.Search_stores_each_releases_place_seeders_peers_and_listed_ids_outside_any_stored_search`).
  Asking the sources, storing in one transaction and the prune are one implementation for both
  searches (`ReleaseSources`, `ReleaseStore`), moved rather than rewritten: its prune also removes the
  stored searches more than a day old, and its log lines, worded as before, now log under the
  categories `MediaAcquisitionManager.Api.Services.Releases.ReleaseSources` and `….ReleaseStore`.
- **`ReleaseSourceNotSearchedReason` moved to the core (MAM-12).** From
  `MediaAcquisitionManager.Api.Contracts` to `MediaAcquisitionManager.Core.Enums`, with the same
  members, values and docs, since a stored column (`SearchSources.NotSearchedReason`) holds it now;
  the JSON is unchanged.
- **`IMetadataResolver` stays unimplemented (MAM-12, ADR 0005).** MAM-7 said MAM-12 would first
  implement it from the media server's data. MAM-12 matches a search and its releases against the
  library snapshot MAM-8 imported instead, which needs no call to the media server, so the contract's
  summary says so and its members are as they were.
- **`IDownloadClient` gets its submission back, and finds the client's jobs by info hash instead of
  listing them (MAM-13, ADR 0005).** MAM-11 left `SubmitAsync`, `ListJobsAsync` and
  `ListJobFilesAsync` out for MAM-13 and MAM-14. `SubmitAsync` is back, sent exactly once and never
  again by the call or a retry of it. `FindJobsAsync(settings, infoHashes)` takes the place of MAM-7's
  `ListJobsAsync`: the client's jobs that hold any of the hashes, each with every hash the client
  lists for it (`ClientJob.InfoHashes`, new). A submission is confirmed by the torrents that hold its
  hashes, an "every job" listing would read a whole client to find one torrent, and following a job
  by the id stored on it (MAM-14) asks the same question. `DownloadSubmission.ExpectedInfoHash` is now
  the hash the app worked out from the payload, never the listing's claim. `ListJobFilesAsync` stays
  MAM-14's.
- **The Search page's Download sends now (MAM-13).** MAM-12 greyed it out with "Sending a release to
  a download client comes in a later update."; it opens the Download dialog (see Added), and that
  sentence is gone.
- **The qBittorrent adapter sends one write (MAM-13, ADR 0010).** MAM-11's adapter sent nothing but
  the sign-in and GETs; a submission adds the one `torrents/add`, never sent again. MAM-11's renewal
  of a kept session qBittorrent forgot — sign in once more and send "that one read again, once" —
  still covers reads only: a submission reads `app/version` first, which renews such a session, so
  the write goes out on a session qBittorrent has just accepted, and a 403 to it is its answer. The
  destinations still send only the sign-in and GETs, and so does the job lookup.
- **The payload fetch is called (MAM-13, ADR 0004).** MAM-7 sealed a release's download link "ready
  for the server-side fetch of MAM-13", and MAM-10 built the fetch "the download step will use".
  Sending a release now calls it, once per job, after the job is written and before the client is
  asked anything, with the integration's current key put back; what it fetched goes to the download
  client and nowhere else.
- **`IDownloadClient` gets its files member back and lists a destination's jobs, and has no member
  that removes (MAM-14, ADR 0005, ADR 0014).** `ListJobFilesAsync(settings, clientJobId)`, which
  MAM-11 left out and MAM-13 left to MAM-14, is back: the job's files in the client's order, `[]`
  while the client doesn't know them, and `NotFound` for a job the client no longer has.
  `ListDestinationJobsAsync(settings, destinationKey)` is new: every job of one destination, each with
  every hash the client lists for it, for a job a client renamed past what its stored hashes find.
  `ClientJobFile.ServicePath` is now `RelativePath`, the path relative to the job's folder exactly as
  the client names it — not a service path, which nothing had read yet. No member removes, deletes,
  stops or pauses a job or its files; the one write is still `SubmitAsync`.
- **A download job has two more statuses, its progress and why it isn't moving on (MAM-14).**
  MAM-13 left `Downloading` and `Downloaded` to MAM-14; the following writes them now, and
  `DownloadJobStatus` gains `Paused` (7) and `Cancelled` (8), stored by name like the others. A job's
  JSON, in every answer that carries one, gains `progress` (0 to 1 as the client last reported it,
  `null` until it has) right after `status`, and `blockReason` (why a followed job isn't moving on,
  `null` when nothing blocks it) right after `error`:
  `{ id, releaseId, releaseTitle, sourceName, downloadClientName, destinationName,
  clientDestinationKey, status, progress, clientJobId, error, blockReason, createdUtc,
  statusChangedUtc }`.
- **A cancelled job no longer counts as already sent (MAM-14, ADR 0013).** MAM-13's decision 3
  refuses a release a job that didn't fail already sent to the client; a `Cancelled` job no longer
  counts, like a `Failed` one, as cancelling is how a person sends a release again — and while the
  torrent is still in qBittorrent, decision 4 still refuses a second copy. A `Paused` job counts.
  `DownloadsIdempotencyHttpTests.Create_of_a_release_whose_earlier_job_was_cancelled_is_201_and_sends_it`
  and `DownloadsCancelHttpTests.Cancel_lets_the_release_be_sent_again_once_qBittorrent_no_longer_has_it`
  pin it.
- **The Download dialog knows a paused and a cancelled job, and a send refreshes the Jobs page
  (MAM-14).** Sending again under the same request id answers the job already made, which may be
  paused in qBittorrent or cancelled by then: the dialog says "qBittorrent took the release, but its
  download is paused there." or "qBittorrent took the release, but its job has been cancelled since,
  so the app doesn't follow it." as a warning. Every send that answers makes the Jobs list ask again.
- **Error sentences in the dark theme are a lighter red (MAM-16).** The dark `--destructive`
  (#dc2626) reaches only 3.85:1 on the dark card, short of the 4.5:1 WCAG AA wants for text, so every
  red sentence (sign-in, settings lists and dialogs, the home page, a connection test, a failed sync)
  now takes the new `--text-danger` token: #f87171 in the dark theme, and `--destructive` as before
  in the light one. Fills, borders and the red Delete and Remove buttons keep `--destructive`. The
  selected and warning tints are tokens too (`--tint-selected`, `--tint-warning`,
  `--tint-warning-border`) with the colours they had, and `design-tokens.test.ts` fails a sheet that
  colours text with `--destructive` or writes those tints out again.
- **Every paged list shows the range on screen and numbered pages (MAM-16).** MAM-7's pager read
  "Page 2 of 5" beside Previous and Next. Under Users, Integrations, Destinations, Library, Search,
  Jobs and a job's files it now shows the range on the left ("26–50 of 812", from each list's
  `totalCount`), ‹ the page numbers › in the middle — the first and last page and the current one
  ±1, with "…" for the pages between, the current page marked `aria-current="page"` — and Per page
  on the right. The arrows are icon buttons named "Previous page" and "Next page". A list with no
  items shows no pager, and a page past the end of a list that shrank says "Page 3 of 2" rather than
  a range that ends before it starts. Below 43rem the numbers take a row of their own above the
  range and Per page, and on the narrowest phones they wrap rather than widen the page.
- **The header stays at the top of the window, with the version under the app name (MAM-16).**
  MAM-1's header scrolled away with the page, with the version beside the user menu. It is now
  sticky, in three columns: the logo with the app name and the version under it on the left (one
  home link, named "Media Acquisition Manager v0.1.0"), the search field (see the next entry) and
  the **Library** and **Jobs** links in a centre column of at most 42rem, and the user menu on the
  right. Each link carries its label as a title. Under 48rem the header shows the logo alone and the
  links as icons, under 480px the search field takes a row of its own, and a focused control or an
  anchor stops below the header rather than behind it.
- **A search starts in a field in the header, and the Search page narrows it (MAM-16, ADR 0015).**
  MAM-12's **Search** link and the page's Title field are gone. Every page has a search field in the
  header once someone is signed in, and pressing `/` anywhere else on the page jumps to it — not
  while typing in a field, holding Ctrl, Cmd or Alt, or with a dialog open. Enter opens
  `/search?q=<the line>`, and the page makes that search at once and puts its id in the address in
  place of the line, so Back goes to the page before and never asks the sources again (ADR 0011);
  React's StrictMode doesn't make it twice, and when the person searches again or goes Back before
  an answer comes, the newest line wins; Forward to that line again waits for its search or shows the
  answer that came meanwhile, and asks again only after a refusal. Only a year from 1900 to 2099 in
  brackets ending the line becomes the year: "Interstellar (2014)" searches "Interstellar" of 2014,
  while "Blade Runner 2049" and "Interstellar 2014" are titles as typed, and an id typed there is a
  title too (ADR 0015, with a section added to ADR 0012). The page says "Results for
  “Interstellar”." in bold, with "Searching…" beside it while the search is made; its form keeps
  **Year**, **IMDb id** and **TMDb id** and **Apply** makes a new search with the same title as a
  new history entry. A refusal shows the server's sentence in a banner under the form that
  **Dismiss** clears. Without a search, the page says how to start one. On the Search page the field
  shows the title of the search on screen, read from the cache without asking the server.
  `search-query.test.ts`, `HeaderSearch.test.tsx` and `SearchPage.test.tsx` pin it.
- **The user menu says who is signed in and holds the theme (MAM-16).** MAM-4's menu button showed a
  user icon and the name; it is now a pill with the user's initial, the name (hidden on phones, as
  before) and a chevron. The open menu starts with the user name over "Owner" or "Member", the
  version and the home page's readiness words ("Ready", "Ready, with warnings", "Not ready") beside a
  dot in the status colour. It asks `GET /api/health/ready` only while it is open, and leaves the
  status out until the answer arrives and after a check fails. **Profile** follows (see Added), then
  the owner's settings under a separator (a member has none, and no separator), then the **Theme**
  row and **Sign out**. It is still a disclosure that closes on Escape, a click outside and the focus
  leaving it.
- **A library card shows its versions as rows, or side by side in a table (MAM-16).** MAM-8's card
  listed every version open, with all its files, one under another. Each version is now a row: its
  edition label ("Version 2" when it has none, by its place in the card), its file summed up in
  chips and its size. The chips are the resolution class from 2160p down to 480p, read from the
  width as much as the height (1920×800 is 1080p), the video codec, the dynamic range and the
  language of every audio track ("UKR · UKR · ENG"); a file that gives none of them reads "No video
  track" or "Unknown" instead. A row opens to the file lines the card showed before, one row at a
  time; a work of one version shows it open, as a row that is no button. From two versions on, a
  **Rows** / **Table** switch under the badges shows a table instead: a column per version and a row
  each for resolution, video, HDR, bit rate, duration, audio and subtitles (a track a line), size
  and file, over the note that size and file come from Jellyfin and the rest from ffprobe. A file
  the app hasn't read, or couldn't, says so in its row and in the table's media rows, never "No
  audio tracks"; one it has no path for says so in its row, and in the table unless an earlier
  reading left its facts. A version of several files is summed up by its first, with the sizes added
  up and every file listed; one without a file says so in a row that doesn't open. The choice holds
  for every card on the Library and Search pages at once and is kept in this browser under
  `mam:versionsView`; below 48rem the switch is hidden and the versions are always rows (owner's
  choice). The search page's Details dialog lists the versions as before.
  `LibraryWorkCard.versions.test.tsx`, `use-versions-view.test.ts`, `use-narrow-screen.test.ts` and
  `media-labels.test.ts` pin it.
- **Every failed request shows one of a few error states, each with a way on (MAM-16).** A list,
  panel or dialog whose first load fails shows what failed and the server's sentence in a box in its
  place, with **Try again**, which reads "Retrying…" while it asks, and the box stays through the
  polls that ask again meanwhile instead of turning into "Loading…". A 400, 403 or 404 gets no Try
  again: an expired search, someone else's job, a member on an owner's page, a filter the server
  refuses. Before, each was one red sentence: the server's or the page's, never both. A refresh that
  fails over data on screen keeps the banner it had and gains **Retry**; a refused row or page
  action (Remove, Disable, Delete, Cancel job, Test) and a connection test's answer are banners
  above the list that **Dismiss** clears, and a test that didn't connect now reads as an error, not
  as news; one that connected, there and in the integration dialog, reads in the text colour beside
  a green icon, as green text fell short of the 4.5:1 contrast text needs. A refused save, sign-in
  or setup is a banner inside the form, above its buttons, with every field kept. Two passwords that
  differ on Setup are said under Confirm password, which is marked invalid until either is edited,
  and a size filter that isn't a size marks its box red. A refused Sync now or Inspect files is a
  red status line on its row, with an icon. Sign in, Setup, Add user, Reset password and the
  Integrations, Destinations and Download dialogs follow the same rules. The shared pieces are
  `Banner`, `ErrorState`, `Field`, `FieldErrorLine`, `RetryButton` and `useLoadRetry`, and the old
  `.settings-banner`, `.settings-error`, `.settings-dialog-error`, `.auth-error` and `.home-banner`
  lines are gone. Every page's and dialog's tests pin it.
- **Add user and Reset password say each refusal where it belongs, and the Users page says when a
  password was reset (MAM-16).** MAM-4's two dialogs showed every refusal as one red line under the
  form. Two passwords that differ are now said under **Confirm password**, marked invalid until
  either password is edited, and a taken name — the 409 — under **User name** until the name is
  edited. Every other sentence is a banner above the buttons, with every field kept: a user-name or
  password rule (both are a 400 that names its field only in English, and the dialogs copy none of
  the server's rules to place them), a user who is gone, a server that can't be reached. A reset
  that goes through closes its dialog, and a banner above the list says "The password of olena was
  reset." until **Dismiss** or the next action; a refused reset stays in its dialog with what was
  typed. `CreateUserDialog.test.tsx`, `ResetPasswordDialog.test.tsx` and `UsersPage.test.tsx` pin
  it.

### Notes for upgraders

- **Applying `AddLibraryInventory` logs EF Core warning 20410 once, and it is harmless.** On the
  first start with this change, on a fresh installation as on an existing one, the log shows
  `Microsoft.EntityFrameworkCore.Migrations[20410]`: the operation `PRAGMA foreign_keys = 0` of
  `AddLibraryInventory` cannot be executed in a transaction. Giving each file its library's foreign
  key makes SQLite rebuild the `PhysicalFiles` table, and EF Core switches foreign keys off for the
  rebuild outside the migration's transaction, so a process killed at that moment could leave the
  migration half applied. Nothing can be lost to it: no earlier build writes a row to
  `PhysicalFiles`, so the table rebuilt is empty everywhere. There is nothing to do.
- **File inspection is off until the owner allows a folder (MAM-9).** After the upgrade, open
  **Media folders** in the user menu and add the folder the app sees your media at — `/media` when
  the compose file mounts it there. The first inspection then starts with the next sync, the next
  start of the app, or **Inspect files** on the Library page; it reads every file once, which takes
  minutes on a large library, and later runs read only what changed. `AddFileInspection` rebuilds no
  table, so it logs no warning 20410.
- **Destinations start empty, and their check needs one library sync after the upgrade (MAM-11).**
  No earlier build wrote a destination. `AddDestinationDefaultAndLibraryFolders` adds a column and a
  table and rebuilds none, so it logs no warning 20410. The library folders a destination is checked
  against are stored by the next sync, so until each Jellyfin library is synced once more every
  destination you add says **Media libraries not known yet**. After the upgrade, check that the
  qBittorrent integration has its path mappings (`/downloads` → `/media` on the reference
  installation), choose **Sync now** on the Library page, then add destinations under
  **Destinations** in the user menu. A qBittorrent older than 4.4.1 now fails its connection test:
  update it first.
- **Applying `AddStoredSearches` rebuilds the `Releases` table, and logs EF Core warning 20410 once
  (MAM-12).** Giving each release a foreign key to the search source that found it makes SQLite
  rebuild `Releases`, which EF Core does in three transactions with foreign keys off between them: it
  copies the rows into `ef_temp_Releases` and commits, switches foreign keys off, drops `Releases`,
  renames the copy and commits, then switches them on again and recreates the indexes. So the log
  shows `Microsoft.EntityFrameworkCore.Migrations[20410]` for `PRAGMA foreign_keys = 0`, as for
  `AddLibraryInventory`. A process killed between the transactions leaves the migration half
  applied, and the next start can't apply it again without a repair by hand — the reason ADR 0009
  kept `PhysicalFiles` without such a key. It is accepted here because `Releases` holds only a day
  of search results, a few thousand rows at most, so the copy takes a moment: let the first start
  after the upgrade finish before you stop the container. Releases stored before the upgrade keep no
  seeders, place or ids and belong to no search; the next search a day later removes them, as no
  download job uses one yet. Searching needs no setting: the Search page works once a release source
  is switched on under Integrations, and its library side once the library is synced.
- **Applying `AddDownloadJobRequests` rebuilds the `DownloadJobs` table, and logs EF Core warning
  20410 once; it is harmless (MAM-13).** Giving each job a foreign key to the user who sent it makes
  SQLite rebuild `DownloadJobs`, which EF Core does with foreign keys off outside the migration's
  transaction, so the log shows `Microsoft.EntityFrameworkCore.Migrations[20410]` for
  `PRAGMA foreign_keys = 0`, as for `AddLibraryInventory` and `AddStoredSearches`. Nothing can be
  lost to it: no earlier build writes a download job, so the table rebuilt is empty everywhere.
  There is nothing to do.
- **Sending needs a download client and a destination, and nothing else to turn on (MAM-13).** Once
  the owner has a switched-on qBittorrent (4.4.1 or later) under Integrations and at least one of its
  destinations under Destinations — the steps of MAM-11 above — every signed-in user's **Download**
  on the Search page sends to it, offering the default destination first. Each torrent is added with
  automatic torrent management, so it saves in its category's folder even when qBittorrent's default
  for new torrents is manual; the destinations' check already shows where each category saves.
- **Applying `AddDownloadJobTracking` rebuilds no table, so it logs no warning 20410 (MAM-14).** It
  adds three nullable columns to `DownloadJobs`, the `DownloadJobFiles` table and three indexes; the
  jobs already there keep everything they had, with no progress or reason until the first pass.
- **Following needs nothing turned on, and picks up the jobs MAM-13 left (MAM-14).** The passes start
  with the app. The first ones follow every job already `Submitted`, find the id of one qBittorrent
  took without listing it, and settle a job left `Created` by a stop in the middle of its sending:
  followed if qBittorrent has it once, `NeedsLinking` if twice, and `Failed` with the reason if it
  doesn't or the app never learnt what it sent. A job `Failed` or `NeedsLinking` stays so. A
  qBittorrent that has refused the app's credentials is not signed in to again by the passes:
  correct them under Integrations, or test the connection there once qBittorrent accepts them, and
  its jobs move on with the next pass.
