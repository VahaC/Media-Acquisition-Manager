import type { MediaEdition } from './duplicates'
import type { DynamicRange, LibraryFile, MediaIdentifierScheme, MediaWorkKind } from './library'
import type { ReleaseMatchEvidence } from './search'

// MAM-24 — scheduling the replacement of specific old copies from the Download dialog, through the
// owner-only `/api/replacements` (ADR 0030–0034). The server works out every reason and every mark;
// the dialog shows them and sends the ids the owner ticked, never a path or an identity.

/**
 * Why nothing of the release can be replaced, checked in this order: replacement is switched off on
 * the Media folders page; the release is a magnet link, whose files the app can't check before
 * sending; the destination can't be used for a replacement (`destinationProblem` says why); the
 * release matches no film or held episode of the owner's.
 */
export type ReplacementUnavailableReason = 'SwitchedOff' | 'Magnet' | 'DestinationNotUsable' | 'NoMatchedWork'

/**
 * Why no copy of a matched work can be replaced: it matched by title alone, its ids conflict, the
 * owner holds none of the episodes the release claims, or the release's title names no season.
 */
export type ReplacementWorkUnavailableReason = 'TitleOnly' | 'ConflictingIds' | 'NoClaimedEpisodes' | 'NoClaimedSeason'

/**
 * Why one copy can't be ticked, the first that applies in the server's order: no path in the app,
 * its library folder isn't ticked, it sits outside the episodes the release claims, another
 * replacement holds it, the app hasn't inspected it, the same file is also a copy of another
 * matched work that can't be replaced (by its title alone, or with conflicting ids), it isn't there
 * any more, it can't be read now, or it changed since the app inspected it.
 */
export type ReplacementCopyUnavailableReason =
  | 'FolderNotTicked'
  | 'NoAppPath'
  | 'NotInspected'
  | 'ChangedSinceInspection'
  | 'Missing'
  | 'Unreadable'
  | 'OutsideTheClaim'
  | 'AlreadyScheduled'
  | 'SharedWithUnavailableWork'

/**
 * How one download client's list of torrents was read for the held marks: every torrent placed,
 * some of them not (`unplacedTorrents`), or not read at all (`error` says why).
 */
export type ReplacementClientCheckStatus = 'Read' | 'Partial' | 'NotRead'

/**
 * A torrent that lists a copy (ADR 0031). `secondName`: the torrent's file is another name of the
 * copy's data (a hardlink), so moving the copy frees no space; otherwise the torrent holds the copy
 * itself, and the owner removes that torrent, without its files, before the copy can move.
 */
export interface ReplacementHold {
  clientName: string
  /** The client's id of the torrent (qBittorrent's hash): two torrents of one name are two holds. */
  torrentId: string
  torrentName: string
  secondName: boolean
}

/** One file of a matched work (a film's, or an episode's) the owner may tick. */
export interface ReplacementCopy {
  file: LibraryFile
  versionId: string
  /** The library's own label for the copy's version; null when it offers none. */
  editionLabel: string | null
  /** MAM-37 — the edition words its name and its version's label hold. */
  editions: MediaEdition[]
  /** The dynamic ranges ffprobe read from its video; none when unknown. */
  dynamicRanges: DynamicRange[]
  /** The file has more names than this one (a hardlink): moving it frees no space. */
  otherNames: boolean
  /** Why it can't be ticked; null when it can. */
  unavailable: ReplacementCopyUnavailableReason | null
  /** The torrents that list it; never a reason it can't be ticked. */
  holds: ReplacementHold[]
}

/** One held episode a TV release claims, with its versions' files. */
export interface ReplacementEpisode {
  workId: string
  seasonNumber: number | null
  episodeNumber: number | null
  title: string | null
  copies: ReplacementCopy[]
}

/** One library work the release matched: a film with its copies, or a series with its claimed episodes. */
export interface ReplacementWork {
  workId: string
  kind: MediaWorkKind
  title: string | null
  year: number | null
  evidence: ReleaseMatchEvidence
  conflictingSchemes: MediaIdentifierScheme[]
  /** Matched by title and year without a conflict: a copy of it is replaced only with the owner's tick. */
  needsConfirmation: boolean
  /** The tick's own words, "This release is Alien (1979)": the server's, stored with the job as said. */
  confirmationText: string
  /** The libraries holding it, in the server's order. */
  libraryNames: string[]
  /** Why none of its copies can be replaced; null when they may be. */
  unavailable: ReplacementWorkUnavailableReason | null
  /** A film's: every file of every version, in the Library's order. None for a series. */
  copies: ReplacementCopy[]
  /** A series': the held episodes the release claims, by season, then number. None for a film. */
  episodes: ReplacementEpisode[]
}

/** How one download client answered the read for the held marks. */
export interface ReplacementClientCheck {
  clientName: string
  status: ReplacementClientCheckStatus
  /** Why it wasn't read, every secret masked; null unless `NotRead`. */
  error: string | null
  /** The torrents whose files couldn't be placed in the app's folders (`Partial`). */
  unplacedTorrents: number
}

/**
 * `GET /api/replacements/options?releaseId=&destinationId=&holdings=`: what of the release the owner
 * may replace, and why not where not.
 */
export interface ReplacementOptions {
  /** Why nothing can be replaced; `works` is empty while it is set. */
  unavailable: ReplacementUnavailableReason | null
  /** The server's sentence for `DestinationNotUsable`; null otherwise. */
  destinationProblem: string | null
  /** MAM-37 — the edition words the release's title holds. */
  releaseEditions: MediaEdition[]
  works: ReplacementWork[]
  /** One per download client when the torrents were read (`holdings`); empty otherwise. */
  clients: ReplacementClientCheck[]
}

/**
 * The body of `POST /api/replacements`: a download, as `POST /api/downloads` takes it, with the
 * copies the owner ticked and the title-and-year matches he confirmed.
 */
export interface ScheduleReplacementRequest {
  requestId: string
  releaseId: string
  downloadClientId: string
  destinationId: string
  fileIds: string[]
  confirmedWorkIds: string[]
}

/**
 * Where a scheduled replacement stands. It follows its download — Scheduled while it is being sent,
 * then Submitted, Downloading (or Paused) and Downloaded, where this version leaves it waiting —
 * and stops at NeedsReview, Failed or Cancelled, with the reason.
 */
export type ReplacementJobState =
  | 'Scheduled'
  | 'Submitted'
  | 'Downloading'
  | 'Paused'
  | 'Downloaded'
  | 'NeedsReview'
  | 'Failed'
  | 'Cancelled'

/** MAM-24 — the replacement a download job carries, as the Jobs page and the Download dialog show it. */
export interface DownloadJobReplacement {
  state: ReplacementJobState
  /** The old copies it would replace. */
  copyCount: number
  /** Why it stopped; null unless it did. */
  reason: string | null
  /** Replacement is switched off now: nothing moves while it is off (ADR 0033). */
  switchedOff: boolean
  /** Its copies whose folder is no longer ticked for replacement, which won't move. */
  copiesOutsideTickedFolders: number
}
