import type { LibraryEpisode, LibraryFile, LibraryIdentifier, LibrarySpace, MediaIdentifierScheme, MediaWorkKind } from './library'

/**
 * MAM-20 — the four kinds of duplicate of specification §3.3, in its order: what one group says its
 * files are to each other. Worked out on every read and stored nowhere (ADR 0028).
 */
export type DuplicateClass = 'SameFile' | 'ByteIdentical' | 'SameWork' | 'Possible'

/**
 * MAM-20 — what tells a group's files or works apart; a group with any is flagged for review. MAM-37 —
 * `RunningTimes`: a same-work group whose shortest measured copy runs under three quarters of its longest;
 * `Edition` only on a possible match, as copies whose names say different editions are no copies of one work.
 */
export type DuplicateDistinctionKind = 'IdentifierConflict' | 'Edition' | 'DynamicRange' | 'RunningTimes'

/** MAM-20 — why a byte-identical group gives no estimate of the room removing its extra copies would free. */
export type RecoverableSpaceGap = 'UnlistedNames' | 'SharingFileSystem' | 'UnknownFileSystem'

/**
 * MAM-20 — a cut or presentation of a film a file's name or a library's version name says it is: a
 * possible distinction between copies of one work, never a match. A name, not the text, so the page
 * says it in the user's language (ADR 0001).
 */
export type MediaEdition =
  | 'Theatrical'
  | 'DirectorsCut'
  | 'Extended'
  | 'Unrated'
  | 'Uncut'
  | 'Remastered'
  | 'SpecialEdition'
  | 'Imax'
  | 'OpenMatte'
  | 'FinalCut'
  | 'Redux'
  | 'International'
  | 'Alternate'
  | 'Assembly'
  | 'Widescreen'
  | 'FullScreen'

/**
 * MAM-21 — what the owner decided about a possible match, by the group's key: kept as checked, or taken
 * off the list. Neither makes the works one, and neither ever authorises a removal.
 */
export type DuplicateMatchDecision = 'Confirmed' | 'Dismissed'

/** MAM-20 — one thing that tells a group's files or works apart. */
export interface DuplicateDistinction {
  kind: DuplicateDistinctionKind
  /** The registry whose ids disagree, for an identifier conflict; null otherwise. */
  scheme: MediaIdentifierScheme | null
  /** Whether the ids that disagree are the series' (two shows behind one episode's ids), not the works' own. */
  ofSeries: boolean
}

/** MAM-20 — the one file a same-file group's names lead to, as the app read it. */
export interface DuplicateIdentity {
  deviceId: number
  inode: number
  /** How many names the file system says it has; null when unknown. */
  linkCount: number | null
  /** How many of them the app found. */
  nameCount: number
}

/**
 * MAM-20 — a registry id that makes works one (ADR 0017): a work's own, or its series' with a season and
 * an episode (both null for a work's own id).
 */
export interface DuplicateIdentifier {
  scheme: MediaIdentifierScheme
  value: string
  seasonNumber: number | null
  episodeNumber: number | null
}

/** MAM-20 — a title and its year the subjects of a possible match share (ADR 0012). */
export interface DuplicateTitle {
  text: string
  year: number
}

/**
 * MAM-37 — how long a same-work group's shortest and longest copies run, in milliseconds: a copy is what one
 * version holds, its running time the sum of its files' measured durations (two parts of a film count as one).
 */
export interface DuplicateRunningTimes {
  shortestMilliseconds: number
  longestMilliseconds: number
}

/** MAM-20 — why a group is one: each class fills its own part, the rest is null or empty. */
export interface DuplicateEvidence {
  /** Same file: the file its names lead to. */
  identity: DuplicateIdentity | null
  /** Byte-identical: the SHA-256 of the bytes. */
  sha256: string | null
  /** Same work: the registry ids two works or more share, or a single work's own. */
  identifiers: DuplicateIdentifier[]
  /** Possible match: the titles and years two subjects or more share. */
  titles: DuplicateTitle[]
  /** Possible match: whether the measured durations agree; null when fewer than two are known, or for another class. */
  durationsAgree: boolean | null
  /**
   * MAM-37 — same work: the running times of the shortest and the longest measured copy, whether or not they
   * differ widely; null when fewer than two copies have every file measured, or for another class.
   */
  runningTimes: DuplicateRunningTimes | null
}

/**
 * MAM-20 — the room removing all but one copy of a group's file would free (ADR 0018): `sizeBytes` when
 * it can be told, else `gap` says why not — never a guess.
 */
export interface DuplicateRecoverableSpace {
  sizeBytes: number | null
  gap: RecoverableSpaceGap | null
}

/** MAM-20 — a library work holding a group's file, through one of its versions. */
export interface LibraryDuplicateWork {
  id: string
  kind: MediaWorkKind
  /** The title its Library card shows; null when it has none. */
  title: string | null
  /** The year given with that title; null when none. */
  year: number | null
  /** An episode's place; null for a film. */
  episode: LibraryEpisode | null
  /** The title of that place's series; null when there is none. */
  seriesTitle: string | null
  /** The version the file is a file of. */
  versionId: string
  /** That version's name, as the library gave it; null when none. */
  editionLabel: string | null
  /** The integration that reported that version. */
  sourceName: string | null
  /** The work's ids as every reader counts them (MAM-34 links applied), with where each came from. */
  identifiers: LibraryIdentifier[]
}

/** MAM-20 — one file name of a group, with what the app read of it and the works holding it. */
export interface LibraryDuplicateFile {
  /** The name as the Library page shows a file; no library for a name only the folder scan found. */
  file: LibraryFile
  /** Its file's size as the app's last look read it; null when unknown. */
  readSizeBytes: number | null
  /** Its file's device, from the app's last look; null when unknown. */
  deviceId: number | null
  /** Its file's inode, from the app's last look; null when unknown. */
  inode: number | null
  /** How many names its file has; null when unknown. */
  linkCount: number | null
  /** Its bytes' SHA-256, when the app hashed them as they are now; null otherwise. */
  sha256: string | null
  /** The editions its file name and its versions' names say it is. */
  editions: MediaEdition[]
  /** The library works holding it, in title order; none for a name no library reported. */
  works: LibraryDuplicateWork[]
}

/**
 * MAM-21 — the owner's decision about a possible match. `decidedByUserName` is the owner's to read (MAM-8's
 * rule for who did what): null for everyone else.
 */
export interface DuplicateDecision {
  decision: DuplicateMatchDecision
  /** UTC with a trailing `Z`. */
  decidedUtc: string
  decidedByUserName: string | null
}

/** MAM-21 — a Confirm or Dismiss of a possible match: the group's key, and the decision the body carries. */
export interface DuplicateDecisionRequest {
  key: string
  decision: DuplicateMatchDecision
}

/**
 * MAM-21 — a decision or its undo as the page makes it: the group's key, and whether the group leaves the
 * list on screen with it (a dismissed match, or an undone dismissal on the Dismissed tab).
 */
export interface DuplicateDecisionChange {
  key: string
  leavesList: boolean
}

/** MAM-20 — one group of `GET /api/library/duplicates`. */
export interface LibraryDuplicateGroup {
  /** What names the group on every read while its members stay: 32 lowercase hex characters. */
  key: string
  class: DuplicateClass
  /** Whether the owner must look before anything is done with it; a possible match stays true whatever was decided. */
  needsReview: boolean
  distinctions: DuplicateDistinction[]
  evidence: DuplicateEvidence
  /** By service path, then id. */
  files: LibraryDuplicateFile[]
  /** The room its files take, each file once (ADR 0018). */
  space: LibrarySpace
  /**
   * The room removing all but one copy would free: 0 for one file's names, an estimate or why there is
   * none for byte-identical files; null for copies of a work and possible matches, which are never redundant.
   */
  recoverable: DuplicateRecoverableSpace | null
  /** MAM-21 — the owner's decision about a possible match; null when undecided, and for every other class. */
  decision: DuplicateDecision | null
}

/** One page of `GET /api/library/duplicates`. */
export interface LibraryDuplicateList {
  items: LibraryDuplicateGroup[]
  /** Every group of the class and decision asked for, across all pages. */
  totalCount: number
  page: number
  pageSize: number
}

/**
 * The query params of `GET /api/library/duplicates`: a class, a decision and a page, all applied by the
 * server. Without `decision` the list leaves the dismissed groups out; an empty filter is left out
 * rather than sent empty.
 */
export interface LibraryDuplicatesQuery {
  class?: DuplicateClass
  /** MAM-21 */
  decision?: DuplicateMatchDecision
  page: number
  pageSize: number
}
