import type { LibraryIdentifier, LibraryWork, MediaWorkKind } from './library'
import type { CatalogWorkKind } from './recommendations'

// MAM-34 — a library work linked to its TMDb film or show by hand, as `/api/library/.../tmdb` describes
// it. The link is the owner's word, kept per library item (a work can hold several: the two "Чужі").
// A film's ids are written into the library's ProviderIds — ids only, nothing else; a series' and an
// episode's link stays in the app, as the server says per item (ADR 0026). Every route here is the
// installation owner's; members see a linked id only on the cards, without the name.

/**
 * What a link says its library item is: the catalog work itself (a film item ↔ a TMDb film, a series ↔
 * a TMDb show), or an episode of a TMDb show (a film item Jellyfin lists as a movie, or an episode of no
 * series). Only a film item's link to its film is written into Jellyfin: a series' is kept in the app,
 * as Jellyfin rewrites every season and episode of a series it saves, and an episode's is too.
 */
export type LibraryLinkTarget = 'Work' | 'ShowEpisode'

/**
 * Where a link stands against the library. KeptInApp: never written (a series' link, an
 * episode-to-show link, or a library that can't set ids). Queued: waiting for the background writer.
 * Writing: a write is under way. NotWritten: the write failed or wasn't confirmed, `writeError` says
 * why. Written: the library confirmed it and no sync has read it since. InLibrary: a sync read it as
 * the library's own. Lost: it was there and a later sync read the item without it.
 */
export type LibraryLinkWriteState = 'KeptInApp' | 'Queued' | 'Writing' | 'NotWritten' | 'Written' | 'InLibrary' | 'Lost'

/** The genre a library mostly holds, read from the TMDb genres of the works in it that have an id. */
export type CatalogGenreRole = 'Other' | 'Animation' | 'Documentary'

/** Whether a suggestion has the genre its work's library mostly holds; Unknown when the library has none. */
export type LinkGenreAgreement = 'Agrees' | 'Disagrees' | 'Unknown'

/** Which of a suggestion's titles came closest to the work's. */
export type LinkTitleKind = 'Title' | 'OriginalTitle' | 'AlternativeTitle'

/** What a suggestion shares with a linked neighbour of its work (a file in the same folder). */
export type LinkSiblingReason = 'SameCollection' | 'SameDirector' | 'SameShow'

/** What "Accept all confident" did with one work. */
export type LinkBatchOutcome = 'Linked' | 'Skipped'

/** Where a preparation of the suggestions is. Queued and Running are under way; the other two are final. */
export type LibraryLinkSuggestionRunStatus = 'Queued' | 'Running' | 'Succeeded' | 'Failed'

/** What queued a preparation: a finished sync, a catalog connected, or the owner's request. */
export type LibraryLinkSuggestionRunTrigger = 'LibrarySynced' | 'CatalogConnected' | 'Requested'

/**
 * The TMDb page's lists of links: those whose item is gone, those waiting for the library, those whose
 * library holds a third id, those not written, and those the library lost.
 */
export type LibraryLinkListFilter = 'Unattached' | 'Pending' | 'Conflict' | 'NotWritten' | 'Lost'

/** A work's link as its card shows it: what its items are linked to, and the worst of their states. */
export interface LibraryWorkLink {
  target: LibraryLinkTarget
  /** A film (`Movie`) or a show (`Series`): the API's kind of work, only ever these two here. */
  catalogKind: CatalogWorkKind
  /** TMDb's id, in the numbering of `catalogKind`. */
  catalogId: string
  /** TMDb's title when it was linked; null when TMDb gave none. */
  catalogTitle: string | null
  /** The season an episode-to-show link names; null otherwise. */
  seasonNumber: number | null
  /** The episode of that season; null otherwise. */
  episodeNumber: number | null
  /** The IMDb id TMDb gave, shown even where the library's own IMDb id counts instead (R8). */
  imdbId: string | null
  /** Who set it. The installation owner only: null for everyone else. */
  setByUserName: string | null
  /** UTC with a trailing `Z`: when it was set or last replaced. */
  setUtc: string
  /** The worst state over the work's items: NotWritten, then Lost, Writing, Queued, Written, InLibrary, KeptInApp. */
  writeState: LibraryLinkWriteState
  /** Why the library doesn't hold it. The installation owner only: null for everyone else. */
  writeError: string | null
  /** The library's TMDb id that is neither the link's nor one it replaces, when an item holds one: neither wins. */
  conflictingValue: string | null
}

/** Where the preparation of a work's suggestions stands. */
export interface LibraryLinkSearchState {
  /** UTC with a trailing `Z`: when TMDb was last searched for the work; null when never. */
  searchedUtc: string | null
  /** UTC with a trailing `Z`: when the owner said none of the suggestions fits; null unless they did. */
  noneFitsUtc: string | null
  /** Whether a preparation is queued or running. */
  pending: boolean
  /** Whether the work is one the preparation searches for: it lacks a TMDb id and has no link. */
  inPopulation: boolean
}

/** One library item of a work in its TMDb dialog, with its link and where the link stands. */
export interface LibraryLinkItem {
  /** The item's link; null when it has none. */
  linkId: string | null
  /** The library's id of the item. */
  sourceItemId: string
  /** The library integration it is in; null when no longer known. */
  sourceName: string | null
  /** The item's title as the library gives it. */
  itemTitle: string | null
  /** What the library says the item is. */
  itemKind: MediaWorkKind
  /**
   * Whether a link to the work itself would be written into the library: false for a series item, whose
   * link stays in the app, for an episode, and for a library switched off or that can't set ids.
   */
  canWrite: boolean
  /** Why not, as the server's sentence, which the dialog shows under the item; null when it can. */
  cannotWriteReason: string | null
  /** Null without a link, like every link field below. */
  target: LibraryLinkTarget | null
  catalogKind: CatalogWorkKind | null
  catalogId: string | null
  catalogTitle: string | null
  seasonNumber: number | null
  episodeNumber: number | null
  imdbId: string | null
  writeState: LibraryLinkWriteState | null
  /** Why the library doesn't hold it (the dialog is the owner's). */
  writeError: string | null
  /** UTC with a trailing `Z`: when the state last changed. */
  writeStateUtc: string | null
  /** UTC with a trailing `Z`: when the link was set or last replaced. */
  setUtc: string | null
  setByUserName: string | null
  /** The ids the last sync read for the item: the library's own. */
  libraryIdentifiers: LibraryIdentifier[]
  /** The library's TMDb id that is neither the link's nor one it replaces; null when there is none. */
  conflictingValue: string | null
}

/** How close a suggestion's titles come to the work's. */
export interface TitleEvidence {
  /** 0 to 1. */
  similarity: number
  /** Whether one of its titles is one of the work's, as titles are matched. */
  exact: boolean
  matchedKind: LinkTitleKind
  matchedTitle: string
}

/** The work's year against the suggestion's. */
export interface YearEvidence {
  libraryYear: number | null
  catalogYear: number | null
  /** How many years apart; null when either isn't known. */
  difference: number | null
}

/** The work's measured duration against the suggestion's runtime, in minutes. */
export interface RuntimeEvidence {
  /** The work's version nearest the suggestion's runtime; null when nothing was measured. */
  libraryMinutes: number | null
  catalogMinutes: number | null
  /** Null when either isn't known. */
  agrees: boolean | null
}

/** The suggestion's genres against what the work's library mostly holds. */
export interface GenreEvidence {
  /** Null when the library has no genre of its own. */
  libraryRole: CatalogGenreRole | null
  agreement: LinkGenreAgreement
}

/** The linked neighbour a suggestion looks related to. */
export interface SiblingEvidence {
  reason: LinkSiblingReason
  siblingTitle: string
}

/** What speaks for and against a suggestion; every piece always there but the neighbour. */
export interface LinkEvidence {
  title: TitleEvidence
  year: YearEvidence
  runtime: RuntimeEvidence
  /** Whether it is the kind of work the library work is (a film for a movie, a show for a series). */
  kindAgrees: boolean
  genre: GenreEvidence
  sibling: SiblingEvidence | null
}

/** A TMDb film or show suggested for a work, or found by a search or a pasted id. */
export interface LinkCandidate {
  /** A film (`Movie`) or a show (`Series`): the API's kind of work, only ever these two here. */
  catalogKind: CatalogWorkKind
  catalogId: string
  /** Its title in Ukrainian, or TMDb's fallback. */
  title: string
  originalTitle: string | null
  year: number | null
  overview: string | null
  /** Its runtime (a show's episodes'). */
  runtimeMinutes: number | null
  /** Where the app serves its poster (`/api/recommendations/posters/{file}`); null when it has none. */
  posterUrl: string | null
  imdbId: string | null
  /** Whether it is the work's one confident suggestion. */
  isConfident: boolean
  evidence: LinkEvidence
  /** For a show suggested to a film or an episode of no series: the season to prefill; null otherwise. */
  seasonNumber: number | null
  /** The episode to prefill; null otherwise. */
  episodeNumber: number | null
}

/** `GET /api/library/works/{id}/tmdb`: a work's TMDb dialog. */
export interface LibraryLinkDialog {
  /** The work as the Library page shows it, its link included. */
  work: LibraryWork
  /** Each library item the work holds, with its link. */
  items: LibraryLinkItem[]
  /** Whether a metadata catalog is connected: without one nothing can be linked. */
  catalogConnected: boolean
  /** The prepared suggestions, best first, at most five, the confident one always among them. */
  suggestions: LinkCandidate[]
  search: LibraryLinkSearchState
  /**
   * The title the free search starts from: the work's title cleaned the way the server cleans it for
   * the suggestions' own searches ("Landing Stripling" of "120. Landing Stripling"), as TMDb finds
   * nothing by the raw one; null when nothing of a title is left.
   */
  searchTitle: string | null
  /** The year the free search starts from: the one that title names in brackets (a span's first), else the work's. */
  searchYear: number | null
}

/** What a free search or a pasted id found: the first page of a search, or the one an id names. */
export interface LinkCandidateList {
  items: LinkCandidate[]
}

/** `DELETE /api/library/works/{id}/tmdb`: the dialog as the removal left it. */
export interface LibraryLinkRemoveResult {
  dialog: LibraryLinkDialog
}

/**
 * The body of `PUT /api/library/works/{id}/tmdb`. The numbers travel as text, as the API reads every
 * field of it as text and answers a malformed one with its own sentence.
 */
export interface LibraryLinkRequest {
  catalogKind: CatalogWorkKind
  catalogId: string
  /** For a show linked to a film or an episode of no series: 0 to 999; null for none. */
  seasonNumber: string | null
  /** 1 to 9999; never without a season. */
  episodeNumber: string | null
  /** The items to link; null for every item of the work. */
  sourceItemIds: string[] | null
}

/** The query params of `GET /api/library/works/{id}/tmdb/search`; a blank year is left out. */
export interface LibraryLinkSearchQuery {
  title: string
  year?: string
}

/** The query params of `GET /api/library/works/{id}/tmdb/lookup`; `kind` only for a bare number. */
export interface LibraryLinkLookupQuery {
  text: string
  kind?: CatalogWorkKind
}

/** One preparation of the suggestions for the works without a TMDb id. */
export interface LibraryLinkSuggestionRun {
  id: string
  status: LibraryLinkSuggestionRunStatus
  trigger: LibraryLinkSuggestionRunTrigger
  /** Who asked for it; null when the app queued it. */
  requestedByUserName: string | null
  /** UTC with a trailing `Z`. */
  createdUtc: string
  startedUtc: string | null
  finishedUtc: string | null
  /** How many works it searches; null until counted. */
  worksTotal: number | null
  worksDone: number
  /** Why it failed; null unless it did. */
  error: string | null
}

/** `GET /api/library/tmdb/queue?index=`: the review queue and the work at an index. */
export interface LibraryLinkQueue {
  /** The works without a TMDb id, without a link, not marked "none fits". */
  totalCount: number
  index: number
  /** Null when the queue is empty or the index past its end. */
  current: LibraryLinkDialog | null
}

/** A work and its confident suggestion. */
export interface ConfidentSuggestion {
  work: LibraryWork
  candidate: LinkCandidate
}

/** One library's numbers on the TMDb page. */
export interface LibraryLinkLocationCount {
  /** Null for the works in no known library. */
  locationId: string | null
  locationName: string | null
  /** Its works without a TMDb id and without a link. */
  populationCount: number
  /** How many of them have a confident suggestion. */
  confidentCount: number
}

/** `GET /api/library/tmdb/confident`: one page of the works with a confident suggestion. */
export interface ConfidentSuggestionList {
  items: ConfidentSuggestion[]
  totalCount: number
  page: number
  pageSize: number
  locations: LibraryLinkLocationCount[]
}

/** The query params of `GET /api/library/tmdb/confident`; an empty library is left out. */
export interface ConfidentSuggestionsQuery {
  locationId?: string
  page: number
  pageSize: number
}

/** One accepted confident suggestion of "Accept all confident". */
export interface LibraryLinkAcceptItem {
  workId: string
  catalogKind: CatalogWorkKind
  catalogId: string
}

/** One item's link as the TMDb page's lists show it. */
export interface LibraryLinkRow {
  linkId: string
  /** The work its item sits on; null while the library reports no such item. */
  workId: string | null
  workTitle: string | null
  sourceName: string
  sourceItemId: string
  /** The item's title when it was linked, kept for an item the library no longer reports. */
  itemTitle: string | null
  target: LibraryLinkTarget
  catalogKind: CatalogWorkKind
  catalogId: string
  catalogTitle: string | null
  seasonNumber: number | null
  episodeNumber: number | null
  writeState: LibraryLinkWriteState
  writeError: string | null
  /** UTC with a trailing `Z`. */
  setUtc: string
  setByUserName: string
  conflictingValue: string | null
}

/** One work of an "Accept all confident" click. */
export interface LibraryLinkBatchItem {
  workId: string
  outcome: LinkBatchOutcome
  /** Why it was skipped, as a sentence; null when it was linked. */
  reason: string | null
  /** Its links, one per library item, with their states as they are now. */
  links: LibraryLinkRow[]
}

/** What "Accept all confident" did, polled again by its id while the writes are under way. */
export interface LibraryLinkBatch {
  batchId: string
  items: LibraryLinkBatchItem[]
}

/** `GET /api/library/tmdb/links`: one page of the links of a filter. */
export interface LibraryLinkList {
  items: LibraryLinkRow[]
  totalCount: number
  page: number
  pageSize: number
}

/** The query params of `GET /api/library/tmdb/links`. */
export interface LibraryLinksQuery {
  filter: LibraryLinkListFilter
  page: number
  pageSize: number
}
