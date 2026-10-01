/** What a media work is. Movies only in Phase A. */
export type MediaWorkKind = 'Movie'

/** A public registry whose id names one work. */
export type MediaIdentifierScheme = 'Imdb' | 'Tmdb'

/**
 * How a file's path in the service became a path in this app, through the integration's current
 * path mappings (ADR 0003). Only Identity and Mapped come with an app path.
 */
export type PathMapStatus = 'Identity' | 'Mapped' | 'NotCovered' | 'Ambiguous' | 'Invalid'

/** Where a library sync is. Queued and Running are under way; the other two are final. */
export type LibrarySyncStatus = 'Queued' | 'Running' | 'Succeeded' | 'Failed'

/** The identifiers filter of the works list: works lacking an IMDb or a TMDb id. */
export type LibraryIdentifierFilter = 'Missing'

/** A reliable id of a work, and the integration that reported it. */
export interface LibraryIdentifier {
  scheme: MediaIdentifierScheme
  value: string
  /** The integration's name when it reported it; null when it didn't come from one. */
  sourceName: string | null
}

/** One file of a version, with the path this app reaches it at when the mappings give one. */
export interface LibraryFile {
  id: string
  /** Verbatim, as the service reported it. */
  servicePath: string
  /** Null unless `pathStatus` is Identity or Mapped. */
  appPath: string | null
  pathStatus: PathMapStatus
  /**
   * The mapper's sentence when there is no app path, to the installation owner only: null when there
   * is an app path, and always null for everyone else (MAM-8).
   */
  pathProblem: string | null
  /** Null when the service didn't report a size. */
  sizeBytes: number | null
  /** The service's library (a Jellyfin library such as "Фільми") holding it; null when unknown. */
  locationName: string | null
}

/** One version of a work: a cut, an edition or another copy, with its files. */
export interface LibraryVersion {
  id: string
  sourceName: string | null
  sourceItemId: string | null
  /** The service's own label for the version, when it offers a choice of versions. */
  editionLabel: string | null
  /** UTC with a trailing `Z`: the sync that last saw it. */
  observedUtc: string
  files: LibraryFile[]
}

/** One work of `GET /api/library/works`. Every value the library didn't report is null. */
export interface LibraryWork {
  id: string
  kind: MediaWorkKind
  title: string | null
  originalTitle: string | null
  year: number | null
  identifiers: LibraryIdentifier[]
  /** The schemes it has no id of; a work needs both to be matched reliably. */
  missingIdentifiers: MediaIdentifierScheme[]
  versions: LibraryVersion[]
}

/** One page of `GET /api/library/works`. */
export interface LibraryWorkList {
  items: LibraryWork[]
  /** Every work matching the filters, across all pages. */
  totalCount: number
  page: number
  pageSize: number
}

/**
 * The query params of `GET /api/library/works`. Search, filters and paging all happen on the
 * server; an empty search or filter is left out rather than sent empty.
 */
export interface LibraryWorksQuery {
  search?: string
  locationId?: string
  identifiers?: LibraryIdentifierFilter
  page: number
  pageSize: number
}

/** A library of a media library integration (a Jellyfin library) that holds at least one file. */
export interface LibraryLocation {
  id: string
  name: string
  sourceName: string | null
}

/** `GET /api/library/locations`, ordered by name, then source name. */
export interface LibraryLocationList {
  items: LibraryLocation[]
}

/** One sync of a media library, from the request to its outcome. */
export interface LibrarySyncRun {
  id: string
  /** Null once the integration is removed; `integrationName` keeps its name. */
  integrationId: string | null
  integrationName: string
  status: LibrarySyncStatus
  /** Who started it. The installation owner only: null for everyone else (MAM-8). */
  requestedByUserName: string | null
  /** UTC with a trailing `Z`: when it was requested. */
  createdUtc: string
  startedUtc: string | null
  finishedUtc: string | null
  /** Distinct movies read so far. */
  itemsRead: number
  /** How many movies the library listed; null until the first page arrived. */
  itemsTotal: number | null
  worksCount: number | null
  versionsCount: number | null
  filesCount: number | null
  /** Why it failed. The installation owner only: null for everyone else (MAM-8). */
  error: string | null
}

/** One media library integration on the Library page, with its sync state. */
export interface LibrarySource {
  integrationId: string
  name: string
  /** Null when this version of the app no longer registers the integration's type. */
  adapterDisplayName: string | null
  isEnabled: boolean
  /** Whether this version of the app can read a library from its type. */
  canReadLibrary: boolean
  latestRun: LibrarySyncRun | null
  lastSucceededRun: LibrarySyncRun | null
  /** Its stored files the current path mappings give no app path, counted when asked. */
  filesWithoutAppPathCount: number
  /** The first of those files' mapping problems. The installation owner only: null for everyone else. */
  pathProblem: string | null
}

/** `GET /api/library/sources`, ordered by name. */
export interface LibrarySourceList {
  items: LibrarySource[]
}
