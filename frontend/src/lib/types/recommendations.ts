import type { IntegrationCallStatus } from './integrations'

/**
 * MAM-33 — the four lists the Recommendations page offers, in the order it shows them (Vasyl's
 * decision 2). A work goes to one of them by its genres: Documentary first, then Animation, else
 * Movies for a film and Series for a show.
 */
export type RecommendationTab = 'Movies' | 'Series' | 'Cartoons' | 'Documentaries'

/** MAM-33 — what a catalog work is: a film or a show. The catalog numbers the two apart. */
export type CatalogWorkKind = 'Movie' | 'Series'

/**
 * MAM-33 — why a work is recommended. Each kept reason is a chip on its card; the toolbar filters by
 * one.
 */
export type RecommendationReasonKind =
  | 'Collection'
  | 'Director'
  | 'Creator'
  | 'Recommended'
  | 'MissingSeasons'
  | 'TopRated'
  | 'NewAtSource'

/** MAM-33 — where a computation of the lists is. Queued and Running are under way; the other two are final. */
export type RecommendationRunStatus = 'Queued' | 'Running' | 'Succeeded' | 'Failed'

/** MAM-33 — what queued a computation. */
export type RecommendationRunTrigger = 'LibrarySynced' | 'Requested' | 'TabEnabled' | 'CatalogConnected' | 'FeedRead'

/**
 * MAM-33 — what a running computation is doing, in the order it does it: reading the library, looking
 * up the catalog ids of works that have none, reading the library's films and shows in the catalog,
 * then their collections and their directors and creators, finding top-rated works for the tabs with
 * few seeds, and saving the lists. A first computation reads every work from the catalog and takes a
 * few minutes; a later one reads cached rows.
 */
export type RecommendationRunStage =
  | 'ReadingLibrary'
  | 'FindingLibraryWorks'
  | 'ReadingWorks'
  | 'ReadingCollections'
  | 'ReadingPeople'
  | 'Discovering'
  | 'Saving'

/**
 * MAM-33 — the order of a tab's works, sorted by the server. Score, its default, puts the strongest
 * reasons first; Rating and Votes the highest first; Newest and Oldest go by the release date; Title
 * from A to Z; Releases puts the works the release sources had most releases of first.
 */
export type RecommendationSort = 'Score' | 'Rating' | 'Votes' | 'Newest' | 'Oldest' | 'Title' | 'Releases'

/** MAM-33 — one reason a work is recommended, with the facts its chip shows. */
export interface RecommendationReason {
  kind: RecommendationReasonKind
  /** The collection (Collection), the director (Director), the creator (Creator) or the release source (NewAtSource); null otherwise. */
  name: string | null
  /**
   * Collection: the released parts the library holds; Director / Creator: their works in the library;
   * Recommended: how many of the library's catalog works recommend it (a film the library holds twice
   * counts once). Null for the other kinds.
   */
  held: number | null
  /** Collection: the released parts of the collection. Null for the other kinds. */
  total: number | null
  /** Recommended: the display titles of the library works that recommend it, at most 5; empty otherwise. */
  titles: string[]
  /** MissingSeasons: the aired season numbers the library has no episode of, ascending; empty otherwise. */
  seasons: number[]
  /** NewAtSource: when the release source published it (UTC, trailing `Z`); null otherwise. */
  dateUtc: string | null
}

/** MAM-33 — what the last check of the release sources found for a work, shared by every user. */
export interface RecommendationAvailability {
  /** When the sources were last asked (UTC, trailing `Z`). */
  checkedUtc: string
  /** Whether any release matched the work. */
  found: boolean
  /** How many releases matched; 0 when none did. */
  releaseCount: number
  /** The best resolution class a matching release claims (2160 for 2160p); null when none claims one. */
  bestResolution: number | null
  /** Whether a matching release claims a Ukrainian audio track. */
  hasUkrainianAudio: boolean
  /** The most seeders of a matching release; null when no source reports seeders. */
  maxSeeders: number | null
}

/** MAM-33 — one recommended work the library doesn't have yet (or a held series with seasons missing). */
export interface Recommendation {
  /** The catalog work's id: the key of a card. */
  id: string
  kind: CatalogWorkKind
  /** The catalog's id of the work ("603"); with `kind`, what "Not interested" is kept by. */
  catalogId: string
  /** The catalog's title in the UI's catalog language (Ukrainian). */
  title: string
  /** The title in its original language; null when the catalog has none. */
  originalTitle: string | null
  /** The release year; null when the catalog has no date. */
  year: number | null
  /** The catalog's genre names. */
  genres: string[]
  /** The catalog's average rating out of 10; null when nobody rated it. */
  rating: number | null
  /** How many votes the rating is from. */
  voteCount: number
  /** `/api/recommendations/posters/<file>`, fetched with the user's token; null when the catalog has no poster. */
  posterUrl: string | null
  /** Why it is recommended, strongest first. */
  reasons: RecommendationReason[]
  /** What the release sources had for it; null until a check of them. */
  availability: RecommendationAvailability | null
  /** Whether a download job someone follows matches it. */
  downloading: boolean
  /** What the Search page searches for: the title, then the year in brackets ("Alien (1979)"). */
  searchText: string
  /** A film's TMDb id, which the Search page narrows by; null for a show. */
  searchTmdbId: string | null
}

/** MAM-33 — a work "Not interested" is kept by, for this user alone: its kind and the catalog's id. */
export interface RecommendationDismissal {
  kind: CatalogWorkKind
  catalogId: string
}

/** MAM-33 — a reason kind and how many of the tab's works have it. */
export interface RecommendationReasonFacet {
  kind: RecommendationReasonKind
  count: number
}

/** MAM-33 — a catalog genre and how many of the tab's works have it. */
export interface RecommendationGenreFacet {
  /** The catalog's genre id, which the list filters by. */
  id: number
  name: string
  count: number
}

/** MAM-33 — a decade (1990 for the 1990s) and how many of the tab's works came out in it. */
export interface RecommendationDecadeFacet {
  decade: number
  count: number
}

/** MAM-33 — films or shows, and how many of the tab's works are one. */
export interface RecommendationKindFacet {
  kind: CatalogWorkKind
  count: number
}

/** MAM-33 — what the toolbar's selects offer, with the count of each value. */
export interface RecommendationFacets {
  reasons: RecommendationReasonFacet[]
  genres: RecommendationGenreFacet[]
  decades: RecommendationDecadeFacet[]
  kinds: RecommendationKindFacet[]
}

/** MAM-33 — one computation of the lists. */
export interface RecommendationRun {
  id: string
  status: RecommendationRunStatus
  trigger: RecommendationRunTrigger
  /** Who pressed Recompute now; to the installation owner only, null for everyone else and for an automatic run. */
  requestedByUserName: string | null
  /** UTC, trailing `Z`; null while queued. */
  startedUtc: string | null
  /** UTC, trailing `Z`; null while under way. */
  finishedUtc: string | null
  /** How many library works the run started from; null until it succeeded. */
  seedCount: number | null
  /** How many works it recommends; null until it succeeded. */
  candidateCount: number | null
  /** Why it failed, to the installation owner only: the sentence can name the catalog's address. */
  error: string | null
  /** When it was queued (UTC, trailing `Z`). */
  createdUtc: string
  /** What it is doing while it runs; null while queued, once it has ended, and before its first stage. */
  stage: RecommendationRunStage | null
  /** How many items of the stage it has done; null when the stage has no counts or none is running. */
  stageDone: number | null
  /** How many items the stage has; null when the stage has no counts or none is running. */
  stageTotal: number | null
}

/** MAM-33 — one page of one tab, with what its filters offer. */
export interface RecommendationList {
  items: Recommendation[]
  /** How many works the tab's filters keep, over every page. */
  totalCount: number
  page: number
  pageSize: number
  facets: RecommendationFacets
  /** Whether a metadata catalog (TMDb) is connected; without one nothing is computed. */
  catalogConnected: boolean
  /** The latest computation; null before the first. */
  lastRun: RecommendationRun | null
  /** MAM-31 — the switched-on release sources whose last call for the lists failed, by name. */
  failedSources: RecommendationSourceFailure[]
}

/**
 * MAM-31 — a release source whose last call for the lists failed — the availability check's search or
 * the newest releases' read —, said beside the cards whose counts it couldn't refresh, as the Search
 * page says a failed source.
 */
export interface RecommendationSourceFailure {
  /** The source's name now. */
  name: string
  /** How the call went: never `Succeeded`. */
  status: Exclude<IntegrationCallStatus, 'Succeeded'>
  /** The source's sentence with what to do, every secret masked; the owner's alone, null for anyone else. */
  error: string | null
  failedUtc: string
}

/**
 * MAM-33 — `GET /api/recommendations`: one tab, filtered and paged by the server. An unset filter is
 * left out of the request.
 */
export interface RecommendationsQuery {
  tab: RecommendationTab
  reason?: RecommendationReasonKind
  /** `true` keeps only the works a release source carries. */
  available?: boolean
  genreId?: number
  /** The first year of a decade: 1990 for the 1990s. */
  decade?: number
  /** Films or shows: offered in Cartoons and Documentaries, which hold both. */
  kind?: CatalogWorkKind
  /** The order of the works; left out for Score, the server's default. */
  sort?: RecommendationSort
  page: number
  pageSize: number
}

/** MAM-33 — the signed-in user's own recommendation settings (Vasyl's decision 5). */
export interface RecommendationSettings {
  /** Whether the Movies tab is on. */
  movies: boolean
  /** Whether the Series tab is on. */
  series: boolean
  /** Whether the Cartoons tab is on. */
  cartoons: boolean
  /** Whether the Documentaries tab is on. */
  documentaries: boolean
  /** How many release-source searches the app may run for this user's lists in 24 hours; 0 runs none. */
  dailySearchLimit: number
  /** How many it ran in the last 24 hours. */
  searchesLast24Hours: number
}

/** MAM-33 — `PUT /api/profile/recommendations`: every field is required. */
export interface RecommendationSettingsRequest {
  movies: boolean
  series: boolean
  cartoons: boolean
  documentaries: boolean
  /** A whole number from 0 to 500. */
  dailySearchLimit: number
}
