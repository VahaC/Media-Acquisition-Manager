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
 * then Submitted, Downloading (or Paused) and Downloaded —, then (MAM-25) its new files are checked:
 * Inspecting while the app reads them, nothing touched, and Verified once every ticked copy is paired
 * with a checked new file of its work. MAM-26 — then the old copies move: Quarantining while the app
 * renames them into the quarantine, and Quarantined once every ticked copy's video is there, where it
 * rests until the owner decides (ADR 0030): Restoring, then Restored (both copies back in the
 * library), or Deleting, then Completed. It stops at NeedsReview, Failed (a new file that isn't
 * readable media) or Cancelled, with the reason.
 */
export type ReplacementJobState =
  | 'Scheduled'
  | 'Submitted'
  | 'Downloading'
  | 'Paused'
  | 'Downloaded'
  | 'Inspecting'
  | 'Verified'
  | 'Quarantining'
  | 'Quarantined'
  | 'Restoring'
  | 'Restored'
  | 'Deleting'
  | 'Completed'
  | 'NeedsReview'
  | 'Failed'
  | 'Cancelled'

/** MAM-24 — the replacement a download job carries, as the Jobs page and the Download dialog show it. */
export interface DownloadJobReplacement {
  /** MAM-26 — the replacement's own id (not its download job's): what its details and the owner's actions ask by. */
  id: string
  state: ReplacementJobState
  /** The old copies it would replace. */
  copyCount: number
  /** Why it stopped; null unless it did. */
  reason: string | null
  /** Replacement is switched off now: nothing moves while it is off (ADR 0033). */
  switchedOff: boolean
  /**
   * Its copies whose folder is no longer ticked for replacement, which won't move — MAM-26: of the
   * copies still to move, any of which stops the whole move for review unless ticked again.
   */
  copiesOutsideTickedFolders: number
  /** MAM-26 — its ticked copies whose video is in the quarantine. */
  quarantinedCopyCount: number
  /** MAM-26 — the companion files that stayed in their folder while their copy moved: the job's page says why. */
  stayedFileCount: number
  /** MAM-26 — the mover stopped it for review, so the owner may press Retry; never after a check of the new files (MAM-27's). */
  canRetry: boolean
}

/** MAM-26 — a name the move plans: the ticked copy's video, or one of its same-name companions (`Alien.srt`). */
export type ReplacementNameKind = 'Video' | 'Companion'

/**
 * MAM-26 — where one name of a replacement is: Planned (in its folder, not moved yet), Moving,
 * Quarantined, Stayed (it couldn't or mustn't move: `problem` says why), Restoring, Restored (back
 * where it was), Deleting, Deleted (gone for good), or Lost (found neither where it was nor in the
 * quarantine after the app stopped mid-way).
 */
export type ReplacementNameState =
  | 'Planned'
  | 'Moving'
  | 'Quarantined'
  | 'Stayed'
  | 'Restoring'
  | 'Restored'
  | 'Deleting'
  | 'Deleted'
  | 'Lost'

/** MAM-26 — one name a replacement moves into the quarantine, and where it is now. */
export interface ReplacementName {
  id: string
  kind: ReplacementNameKind
  /** Where it was, in the app's terms: where Restore puts it back. */
  originalPath: string
  /** Where it goes in the quarantine: `<media folder>/.mam-quarantine/<replacement id>/<relative path>`. */
  quarantinePath: string
  sizeBytes: number
  /** The file has more names than this one (a hardlink): deleting it for good frees no space. */
  otherNames: boolean
  state: ReplacementNameState
  /** Why it stayed, wasn't put back or deleted, or was lost: the app's own sentence; null otherwise. */
  problem: string | null
}

/**
 * MAM-26 — `GET /api/replacements/{id}`: one of the owner's replacements, with every name its move
 * planned by position and what the owner may do with it. The server works out each `can…` and the
 * room; the page shows them.
 */
export interface ReplacementDetails {
  id: string
  downloadJobId: string
  state: ReplacementJobState
  /** Why it stopped; null unless it did. */
  reason: string | null
  /** Replacement is switched off now: nothing is moved, put back or deleted while it is off (ADR 0033). */
  switchedOff: boolean
  /** The mover stopped it for review: Retry sends it back to be checked and moved. */
  canRetry: boolean
  /** Something of it is in the quarantine, and it is Quarantined or in review: Restore puts it back. */
  canRestore: boolean
  /** It is Quarantined: Delete for good deletes what it holds in the quarantine. */
  canDeleteForGood: boolean
  /** The room its names in the quarantine take. */
  quarantinedBytes: number
  /** The room Delete for good would free: a name with another name (a hardlink) frees nothing. */
  freesBytes: number
  names: ReplacementName[]
}

/**
 * MAM-26 — the body of `POST /api/replacements/{id}/delete-for-good`: the ids of the names the owner
 * confirmed, exactly as the confirmation listed them. The server refuses a list that isn't what the
 * replacement holds in the quarantine now.
 */
export interface DeleteQuarantinedRequest {
  names: string[]
}
