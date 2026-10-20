import type { LibraryWorkLink } from './library-links'

/**
 * What a media work is. MAM-32 — a series is a work of its own, with seasons but no versions; its
 * episodes are works too, each with its versions, listed under it.
 */
export type MediaWorkKind = 'Movie' | 'Episode' | 'Series'

/**
 * A public registry whose id names one work. MAM-32 — `TmdbTv` is a show's TMDb id: TMDb numbers
 * films and shows apart, so it never matches a movie's (ADR 0017).
 */
export type MediaIdentifierScheme = 'Imdb' | 'Tmdb' | 'Tvdb' | 'TmdbTv'

/**
 * How a file's path in the service became a path in this app, through the integration's current
 * path mappings (ADR 0003). Only Identity and Mapped come with an app path.
 */
export type PathMapStatus = 'Identity' | 'Mapped' | 'NotCovered' | 'Ambiguous' | 'Invalid'

/** Where a library sync is. Queued and Running are under way; the other two are final. */
export type LibrarySyncStatus = 'Queued' | 'Running' | 'Succeeded' | 'Failed'

/** The identifiers filter of the works list: works lacking an id of their kind. */
export type LibraryIdentifierFilter = 'Missing'

/**
 * MAM-32 — the kind filter of the works list: the movies, or the series with the episodes of no
 * series.
 */
export type LibraryKindFilter = 'Movie' | 'Series'

/**
 * The order of the works list, by name. `Title` is the default and the tie-break of every other order;
 * a work lacking what an order is by (a year, a size) goes last in it.
 */
export type LibrarySort = 'Title' | 'TitleDescending' | 'Newest' | 'Oldest' | 'Largest' | 'Added'

/**
 * What kind of source claimed a fact (ADR 0008). MAM-34 — `Person` is the owner's TMDb link, set by
 * hand; `Metadata` the catalog, for the IMDb id TMDb gave for a linked work.
 */
export type ProvenanceSourceKind = 'MediaLibrary' | 'ReleaseSearch' | 'DownloadClient' | 'FileInspection' | 'Metadata' | 'Person'

/** A reliable id of a work, and the source that claimed it. */
export interface LibraryIdentifier {
  scheme: MediaIdentifierScheme
  value: string
  /**
   * The integration's name when it reported it; null when it didn't come from one. MAM-34 — for an id
   * set by hand, the name of the user who set it, for the installation owner only: null for everyone else.
   */
  sourceName: string | null
  /** MAM-34 — what kind of source claimed it: the library, a person's TMDb link, or the catalog. */
  sourceKind: ProvenanceSourceKind
  /** MAM-34 — UTC with a trailing `Z`: when a person set it; null for an id a library or the catalog reported. */
  setUtc: string | null
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

/**
 * MAM-21 — what a work's versions as rows (VersionRows) and as a table (VersionTable) read of a version:
 * the Duplicates page shows each file of a group as one, named by its file, rather than faking the
 * other fields of a version it isn't.
 */
export type ShownVersion = Pick<LibraryVersion, 'id' | 'editionLabel' | 'files'>

/** MAM-32 — where an episode sits, as the library places it. */
export interface LibraryEpisode {
  /** The series work it belongs to; null when the library names none. */
  seriesId: string | null
  /** Its season, 0 for the specials; null when the library gives none. */
  seasonNumber: number | null
  /** Its number within the season; null when the library gives none. */
  episodeNumber: number | null
}

/** MAM-32 — one season of a series, with how many of its episodes the list's search and filters keep. */
export interface LibrarySeason {
  /** The season, 0 for the specials; null for the episodes the library gives no season. */
  number: number | null
  episodeCount: number
}

/** One work of `GET /api/library/works`. Every value the library didn't report is null. */
export interface LibraryWork {
  id: string
  kind: MediaWorkKind
  title: string | null
  originalTitle: string | null
  year: number | null
  identifiers: LibraryIdentifier[]
  /**
   * The schemes of its kind it has no id of; a work needs them all to be matched reliably — IMDb and
   * TMDb for a movie, MAM-32: IMDb and TVDB for an episode, IMDb, TMDb TV and TVDB for a series.
   */
  missingIdentifiers: MediaIdentifierScheme[]
  /** None for a series: its episodes hold the versions. */
  versions: LibraryVersion[]
  /** MAM-32 — where an episode sits; null for anything else. */
  episode: LibraryEpisode | null
  /**
   * MAM-32 — a series' seasons, regular ones first, then the specials, then the episodes without a
   * season; null for anything but a series.
   */
  seasons: LibrarySeason[] | null
  /** MAM-34 — the TMDb link of the work's items; null when none of them has one. */
  link: LibraryWorkLink | null
  /**
   * MAM-35 — the work's own picture from its library, `/api/library/posters/{itemId}-{tag}.jpg`, fetched
   * with the user's token (`libraryApi.poster`); null when none of its items has one. The Library and
   * Search cards show it; an episode inside an opened season carries one too, which nothing shows.
   */
  posterUrl: string | null
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
  /** MAM-32 */
  kind?: LibraryKindFilter
  /** Left out for `Title`, the default. */
  sort?: LibrarySort
  page: number
  pageSize: number
}

/**
 * MAM-32 — the query params of `GET /api/library/works/{id}/episodes`: one season of a series, with
 * the works list's own search and filters, so a season lists the episodes its count was made of.
 */
export interface LibraryEpisodesQuery {
  /** A season number (`0` for the specials), or `None` for the episodes without one; left out for every season. */
  season?: string
  search?: string
  locationId?: string
  identifiers?: LibraryIdentifierFilter
  page: number
  pageSize: number
}

/**
 * MAM-32 — the works list's search and filters a series card passes on to its seasons, so an opened
 * season lists the episodes its count was made of.
 */
export type LibraryEpisodeFilters = Pick<LibraryEpisodesQuery, 'search' | 'locationId' | 'identifiers'>

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
  /** Distinct items read so far: MAM-32 — movies, series and episodes. */
  itemsRead: number
  /** How many items the library listed; null until the first page of each list arrived. */
  itemsTotal: number | null
  /** The movies and episodes stored as works. */
  worksCount: number | null
  /** MAM-32 — the series stored; null unless it succeeded, and for a sync from before series were read. */
  seriesCount: number | null
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
  /**
   * MAM-47 — the key of `pathProblem`'s sentence (the API's error `code`), which names its kind whatever the language
   * and the file: null when there is none. Optional until every server sends it; without it the words are compared.
   */
  pathProblemCode?: string | null
  /** MAM-9: how far the app's look at its files has got, counted when asked. */
  inspection: LibrarySourceInspection
  /** MAM-19: how much room its files take on disk, counted when asked. */
  space: LibrarySpace
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
  /** MAM-47 — the key of `problem`'s sentence, like `LibrarySource.pathProblemCode`. */
  problemCode?: string | null
}

/**
 * MAM-19 — how much room a set of file names takes on disk, each file counted once by its device and
 * inode however many names lead to it, so two hardlinks of one file take its size once (ADR 0018).
 * Only what the app's own look at each file read counts, never the size a library reported.
 */
export interface LibrarySpace {
  /** The sizes of the files the names lead to, each file once. */
  sizeBytes: number
  /** How many files that is. */
  fileCount: number
  /** How many of the names lead to them: more than `fileCount` when a file has several names. */
  nameCount: number
  /** The names the app knows no file behind yet: not looked at, or the look found nothing or no identity. */
  unmeasuredCount: number
}

/** `GET /api/library/sources`, ordered by name. */
export interface LibrarySourceList {
  items: LibrarySource[]
  /** MAM-9: one for the whole library, as a run reads the files of every source. */
  inspection: FileInspectionOverview
  /** MAM-18: the Media folders row, beside the libraries, as the app's own source of file names. */
  scan: FolderScanOverview
}

/** Where an inspection of the library's files is. Queued and Running are under way; the other two are final. */
export type FileInspectionRunStatus = 'Queued' | 'Running' | 'Succeeded' | 'Failed'

/**
 * What queued an inspection: the owner's Inspect files, a finished sync, the app's start, or — MAM-18
 * — a scan of the media folders that stored the names it found.
 */
export type FileInspectionTrigger = 'Requested' | 'LibrarySynced' | 'AppStarted' | 'FolderScanned'

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
  /**
   * MAM-19: how much room every stored file name takes on disk, each file once — a name a library
   * reported and a name a scan stored that lead to one file count it once.
   */
  space: LibrarySpace
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
  /** MAM-19: its last step, the hashing of the files a byte-identical copy could hide among. */
  hashing: FileHashing
  /** Why it failed. The installation owner only: null for everyone else. */
  error: string | null
}

/**
 * MAM-19 — the last step of a file inspection (ADR 0022): a file is hashed only when a file of
 * another identity has its size and its media facts don't tell the two apart.
 */
export interface FileHashing {
  /** How many files need a hash, each counted once however many names lead to it; null until the step has listed them. */
  candidates: number | null
  /** How many of them it has dealt with so far. */
  checked: number
  /** How many of them it read in full in this run; the others came from the cache or couldn't be hashed. */
  read: number
  /** How many of them it couldn't hash; never a failure of the run. */
  problemCount: number
  /** Why the first of them wasn't hashed; it quotes paths. The installation owner only: null for everyone else. */
  problem: string | null
}

/** MAM-18 — where a scan of the media folders is. Queued and Running are under way; the other two are final. */
export type FolderScanStatus = 'Queued' | 'Running' | 'Succeeded' | 'Failed'

/**
 * MAM-18 — where the scan of the media folders for the files no library reported stands: the Library
 * page's Media folders row.
 */
export interface FolderScanOverview {
  /** The scan under way: the running one, else the queued one; null when none is. */
  activeRun: FolderScanRun | null
  /** The latest scan that succeeded or failed; null before the first. */
  lastFinishedRun: FolderScanRun | null
  /** Whether a scan waits to start: the active one, or one queued behind the running one. */
  hasQueuedRun: boolean
  /**
   * The names the scans stored, by what the app's last look at each found — only those no library
   * reports under the current path mappings, so a sync that starts reporting one takes it off at once.
   */
  files: LibrarySourceInspection
  /**
   * MAM-19: how much room the files behind those same names take on disk, each file once — MAM-42: the
   * ignored ones included, as they still take it.
   */
  space: LibrarySpace
  /** MAM-42: how many of those names the owner told the app to stop counting; `files` leaves them out. */
  ignoredCount: number
  /** MAM-44: how many of those names, not ignored, the library takes for a film's extras; `files` leaves them out. */
  extrasCount: number
}

/**
 * MAM-42 — the tabs of the Files in no library dialog besides its first (no view: every file the app
 * counts): the counted files the app couldn't read, and the ignored ones. MAM-44 — and the files the
 * library takes for a film's extras, which warn of nothing.
 */
export type UnreportedFileView = 'Problems' | 'Extras' | 'Ignored'

/**
 * MAM-42 — where a file in no library sits among the libraries' folders: under none of them, under one
 * (which then didn't list it), or the app knows no library folder to compare with. MAM-43 — under one, in a
 * folder whose name makes the library take its videos for extras, or named as an extra.
 */
export type UnreportedFilePlaceKind = 'NoLibraryFolder' | 'LibraryFolder' | 'FoldersUnknown' | 'ExtrasFolder' | 'ExtraName'

export interface UnreportedFilePlace {
  kind: UnreportedFilePlaceKind
  /** The library whose folder holds it ("Фільми"); null for NoLibraryFolder and FoldersUnknown. */
  libraryName: string | null
  /**
   * MAM-43 — what makes it an extra: the folder's name as on the disk ("Shorts") for ExtrasFolder, the rule its
   * name matches ("-trailer", "sample") for ExtraName; null otherwise.
   */
  extraName?: string | null
}

/** MAM-42 — one file the scan found that no library reports now. */
export interface UnreportedFile {
  /** The name's id, which Ignore and Stop ignoring take. */
  id: string
  appPath: string
  /** Its size as the app's last look read it; null until read. */
  sizeBytes: number | null
  firstSeenUtc: string
  /** When the owner told the app to stop counting it; null while it counts. */
  ignoredUtc: string | null
  place: UnreportedFilePlace
  /** What the app's last look found, `problem` the owner's only; null until it has looked. */
  inspection: LibraryFileInspection | null
}

/** MAM-42 — the query string of the Files in no library list. */
export interface UnreportedFilesQuery {
  /** Absent: every file the app counts. */
  view?: UnreportedFileView
  page: number
  pageSize: number
}

/** MAM-42 — one page of the Files in no library list, with what each of its tabs holds. */
export interface UnreportedFileList {
  items: UnreportedFile[]
  totalCount: number
  page: number
  pageSize: number
  /** The files in no library the app counts: the first tab's. */
  countedCount: number
  /** Those of them the app couldn't read: the Problems tab's. */
  problemCount: number
  ignoredCount: number
  /** MAM-44 — the files the library takes for a film's extras, not ignored: the Extras tab's. */
  extrasCount: number
}

/** MAM-18 — one scan of the media folders, from the request to its outcome. */
export interface FolderScanRun {
  id: string
  status: FolderScanStatus
  /** Who asked for it. The installation owner only: null for everyone else. */
  requestedByUserName: string | null
  /** UTC with a trailing `Z`: when it was queued. */
  createdUtc: string
  startedUtc: string | null
  finishedUtc: string | null
  /** The media files the walk has reached so far, each once however many links lead to it; the total once it succeeded. */
  filesFound: number
  /** How many of them no library reported; null until the scan stored what it found. */
  filesUnreported: number | null
  /** The folders, links and names the walk didn't go into or through. */
  problemCount: number
  /** The first of those problems' sentences; it quotes paths. The installation owner only: null for everyone else. */
  problem: string | null
  /** Why it failed. The installation owner only: null for everyone else. */
  error: string | null
}
