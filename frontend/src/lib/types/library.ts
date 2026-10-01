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
  /** MAM-9: what the app's last look at the file found; null until it has looked. */
  inspection: LibraryFileInspection | null
}

/**
 * MAM-9 — what the app's last look at a file found. Only Inspected comes with the media facts; every
 * other status says why the streams weren't read.
 */
export type FileInspectionStatus =
  | 'Inspected'
  | 'MediaUnreadable'
  | 'NoAppPath'
  | 'OutsideAllowedRoots'
  | 'NotFound'
  | 'NotARegularFile'
  | 'Unreadable'
  | 'IdentityUnknown'
  | 'InspectorUnavailable'

/** What a track of a file carries. */
export type MediaTrackKind = 'Video' | 'Audio' | 'Subtitle'

/** A video track's dynamic range, as the stream declares it. */
export type DynamicRange = 'Sdr' | 'Hdr10' | 'Hlg' | 'DolbyVision'

/** One file's inspection on the Library page (MAM-9). */
export interface LibraryFileInspection {
  status: FileInspectionStatus
  /**
   * Why the streams weren't read, one sentence that can quote paths inside the app. The installation
   * owner only: null for everyone else, and when the status is Inspected.
   */
  problem: string | null
  /** UTC with a trailing `Z`: when the app last looked. */
  inspectedUtc: string
  /** What ffprobe read; null unless the status is Inspected. */
  media: MediaFacts | null
}

/** What ffprobe read from a file. Every value the file didn't give is null, shown as unknown. */
export interface MediaFacts {
  durationMilliseconds: number | null
  /** The overall bit rate, in bits per second. */
  bitRate: number | null
  /** In the file's own order; cover art and attachments left out. */
  tracks: MediaTrack[]
}

/** One track as ffprobe read it. A value that doesn't apply to its kind is null. */
export interface MediaTrack {
  kind: MediaTrackKind
  /** ffprobe's codec name: `hevc`, `h264`, `ac3`, `dts`, `subrip`, `hdmv_pgs_subtitle`. */
  codec: string | null
  /** ffprobe's profile name: `Main 10`, `High`, `DTS-HD MA`, `LC`. */
  profile: string | null
  /** Video only. */
  width: number | null
  height: number | null
  /** Video only; null when the stream doesn't say. */
  dynamicRange: DynamicRange | null
  /** Audio only. */
  channels: number | null
  /** Audio only: `5.1(side)`, `stereo`. */
  channelLayout: string | null
  /** As tagged, lower case (`ukr`, `eng`); null when untagged or tagged undetermined. */
  language: string | null
  isDefault: boolean
  isForced: boolean
  isHearingImpaired: boolean
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
  /** MAM-9: how far the app's look at its files has got, counted when asked. */
  inspection: LibrarySourceInspection
}

/** MAM-9 — one source's files by what the app's last look at each found. */
export interface LibrarySourceInspection {
  /** Files whose streams were read. */
  inspectedCount: number
  /** Files the last look couldn't read, whatever the reason (no path in the app included). */
  problemCount: number
  /** Files the app hasn't looked at yet. */
  notInspectedCount: number
  /** Why the first problem file (by path) wasn't read. The installation owner only: null for everyone else. */
  problem: string | null
}

/** `GET /api/library/sources`, ordered by name. */
export interface LibrarySourceList {
  items: LibrarySource[]
  /** MAM-9: one for the whole library, as a run reads the files of every source. */
  inspection: FileInspectionOverview
}

/** Where an inspection of the library's files is. Queued and Running are under way; the other two are final. */
export type FileInspectionRunStatus = 'Queued' | 'Running' | 'Succeeded' | 'Failed'

/** What queued an inspection: the owner's Inspect files, a finished sync, or the app's start. */
export type FileInspectionTrigger = 'Requested' | 'LibrarySynced' | 'AppStarted'

/** MAM-9 — where the inspection of the library's files stands. */
export interface FileInspectionOverview {
  /** Whether the owner has allowed any media folder; without one the app reads no file. */
  hasAllowedRoots: boolean
  /** The run under way: the running one, else the queued one; null when none is. */
  activeRun: FileInspectionRun | null
  /** The latest run that succeeded or failed; null before the first. */
  lastFinishedRun: FileInspectionRun | null
  /** Whether a run waits to start: the active one, or one queued behind the running one. */
  hasQueuedRun: boolean
}

/** One inspection of the library's files, from the request to its outcome (MAM-9). */
export interface FileInspectionRun {
  id: string
  trigger: FileInspectionTrigger
  status: FileInspectionRunStatus
  /** Who asked for it. The installation owner only, and null when a sync or the app's start queued it. */
  requestedByUserName: string | null
  /** UTC with a trailing `Z`: when it was queued. */
  createdUtc: string
  startedUtc: string | null
  finishedUtc: string | null
  /** How many files it looks at; null until it has listed them. */
  filesTotal: number | null
  /** How many it has looked at so far. */
  filesChecked: number
  /** How many ffprobe read in this run; the rest came from the cache or couldn't be read. */
  filesProbed: number
  /** Why it failed. The installation owner only: null for everyone else. */
  error: string | null
}
