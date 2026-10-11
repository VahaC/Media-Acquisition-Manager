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

/**
 * MAM-27 — who stopped a replacement waiting for review: Download when the app can't follow its
 * download (nothing to check again: only Keep both or Cancel job end it), Check when the check of its
 * new files stopped, Move when the mover stopped right before moving the old copies. Worked out by the
 * server from the replacement's own times, never stored.
 */
export type ReplacementStop = 'Download' | 'Check' | 'Move'

/**
 * MAM-27 — where a running check of the new files is: reading them on the disk (Files), or waiting for
 * Jellyfin to identify them once it was asked to look (Library), each until its own limit.
 */
export type ReplacementCheckStage = 'Files' | 'Library'

/**
 * MAM-27 — where one ticked old copy stands, the first that applies in the server's order: dropped by
 * the owner; where its video's name is once the move took it up (InQuarantine, Deleted, Restored,
 * Lost, or Moving while it is being moved, put back or deleted); Stays when the replacement ended
 * without moving it; Stopped when it stopped the replacement (`problem` says why); Paired with a
 * checked new file; NotPaired once the check is over without a pair; otherwise Waiting for the check.
 */
export type ReplacementCopyStatus =
  | 'Dropped'
  | 'Waiting'
  | 'Paired'
  | 'Stopped'
  | 'NotPaired'
  | 'Moving'
  | 'InQuarantine'
  | 'Restored'
  | 'Deleted'
  | 'Lost'
  | 'Stays'

/**
 * MAM-27 — how far the check of one new file got, the first that applies in the server's order: it
 * stopped the replacement (`problem` says why); the client hasn't downloaded it all; it isn't read on
 * the disk yet, or is (Checked) while Jellyfin isn't asked to look yet; Jellyfin was asked and not
 * read yet, lists no item for it, or hasn't finished reading it; it is an extra, simply added; Jellyfin
 * gave no TMDb or IMDb id that names a work yet, or no season and episode number for an episode; or it
 * is identified.
 */
export type ReplacementNewFileProgress =
  | 'Stopped'
  | 'Downloading'
  | 'NotChecked'
  | 'Checked'
  | 'AskedToLook'
  | 'NotListed'
  | 'NotRead'
  | 'Extra'
  | 'NoId'
  | 'NotNumbered'
  | 'Identified'

/**
 * MAM-25 — what the library made of a new file, by its word alone (ADR 0017): a film, an episode, or
 * anything else (a plain video, an extra), which is never taken for a work.
 */
export type LibraryFileKind = 'Movie' | 'Episode' | 'Other'

/** MAM-24 — the replacement a download job carries, as the Jobs page and the Download dialog show it. */
export interface DownloadJobReplacement {
  /** MAM-26 — the replacement's own id (not its download job's): what its details and the owner's actions ask by. */
  id: string
  state: ReplacementJobState
  /** The old copies it would replace — MAM-27: without the ones the owner dropped from it. */
  copyCount: number
  /** Why it stopped; null unless it did. */
  reason: string | null
  /** Replacement is switched off now: nothing moves while it is off (ADR 0033). */
  switchedOff: boolean
  /**
   * Its copies whose folder is no longer ticked for replacement, which won't move — MAM-26: of the
   * copies still to move, any of which stops the whole move for review unless ticked again. MAM-27 —
   * a dropped copy never counts: it doesn't move with this replacement.
   */
  copiesOutsideTickedFolders: number
  /** MAM-26 — its ticked copies whose video is in the quarantine. */
  quarantinedCopyCount: number
  /** MAM-26 — the companion files that stayed in their folder while their copy moved: the job's page says why. */
  stayedFileCount: number
  /**
   * MAM-26 — the mover stopped it for review, so the owner may press Retry. MAM-27 — or the check of
   * its new files stopped, unless a copy only a drop (or Keep both) settles is still in it; never when
   * the app can't follow its download.
   */
  canRetry: boolean
  /** MAM-27 — the copies the owner dropped from it: each stays where it is. */
  droppedCopyCount: number
  /** MAM-27 — its copies, not dropped, that only dropping them or Keep both settles: a new check can't pair them. */
  dropOnlyCopyCount: number
  /**
   * MAM-27 — the owner may press Keep both: none of its old copies has started to move and it hasn't
   * ended, so cancelling the replacement alone leaves both copies and the download as they are.
   */
  canCancel: boolean
  /** MAM-27 — who stopped it; null unless it waits for review. */
  stop: ReplacementStop | null
  /** MAM-27 — where the check of its new files is; null unless it is Inspecting. */
  checkStage: ReplacementCheckStage | null
  /** MAM-27 — until when the running check waits for its stage (for Jellyfin, an hour); null unless it is Inspecting. */
  checkWaitsUntilUtc: string | null
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
 * MAM-27 — one registry id of a new file's library item (`ofSeries`: its series' rather than the
 * episode's own, ADR 0017), or of a work the replacement expects (`setByOwner`: the owner's own TMDb
 * link, ADR 0026). The other flag is always false.
 */
export interface ReplacementJobIdentifier {
  scheme: MediaIdentifierScheme
  value: string
  ofSeries: boolean
  setByOwner: boolean
}

/**
 * MAM-27 — one old copy the owner ticked, by position, with what the check found of it, its pair and
 * where it stands. Raw facts plus the server's status: the page never works out a pair itself.
 */
export interface ReplacementJobCopy {
  id: string
  /** Its 0-based place among the replacement's copies, in the dialog's order. */
  position: number
  /** Its path inside the app when it was scheduled. */
  appPath: string
  /** Its size on the disk then. */
  sizeBytes: number
  /** The work the release matched: the film, or the episode's series (one of `works`). */
  workId: string
  /** An episode's place as its own library item gave it; null for a film. */
  seasonNumber: number | null
  episodeNumber: number | null
  /** The last episode a copy of several covers; null when it covers one, and for a film. */
  episodeNumberEnd: number | null
  /** Its version's edition label then; null when the library gave none. */
  editionLabel: string | null
  /** Its measured running time; null when it was never read, which leaves the new file's uncompared. */
  durationMilliseconds: number | null
  /** The new file (one of `newFiles`) it is paired with; null while none is. */
  pairedNewFileId: string | null
  /** Why it stopped the replacement, the app's own sentence; null when it didn't. Kept as history once dropped. */
  problem: string | null
  /** Only dropping it, or keeping both, settles its problem: a new check can't pair it, so Retry isn't offered. */
  onlyDropSettles: boolean
  /** When the owner dropped it: it stays where it is and never moves with this replacement. Null while it is in it. */
  droppedUtc: string | null
  status: ReplacementCopyStatus
  /**
   * The owner may drop it now: the replacement stopped for review after its check or before its move,
   * the copy isn't dropped, none of its names left its folder, and it isn't the last copy left.
   */
  canDrop: boolean
}

/**
 * MAM-27 — one new video file the replacement's download brought, by position, with what each check
 * found of it (ADR 0035): the client's word, the disk's, and the library's item and ids.
 */
export interface ReplacementJobNewFile {
  id: string
  /** Its 0-based place among the download's files, in the client's own order. */
  position: number
  /** Its path relative to the download's folder, as the client names it. */
  relativePath: string
  /** Its path inside the app; null when the client's path mappings give none. */
  appPath: string | null
  /** Check 1 — its size and how far it is downloaded (0 to 1), as the client said; null when it didn't. */
  clientSizeBytes: number | null
  clientProgress: number | null
  /** Check 2 — what the disk and the media inspector said of it; each null until read, or when not given. */
  sizeBytes: number | null
  durationMilliseconds: number | null
  /** The first video stream's codec, in the media inspector's names (`hevc`). */
  videoCodec: string | null
  width: number | null
  height: number | null
  /** When it was last read on the disk; null before. */
  checkedUtc: string | null
  /** Check 4 — what the library made of it; null while it lists no item for it. */
  kind: LibraryFileKind | null
  /** Check 3 — an episode's place as the library gave it. */
  seasonNumber: number | null
  episodeNumber: number | null
  episodeNumberEnd: number | null
  /** When the library was last read for it; null before it was asked to look. */
  readUtc: string | null
  /** The work (one of `works`) the library's ids identified it as; null while none is. */
  workId: string | null
  /** Why it stopped the replacement, the app's own sentence; null when it didn't. */
  problem: string | null
  progress: ReplacementNewFileProgress
  /** The ids the library gave its item when last read. */
  identifiers: ReplacementJobIdentifier[]
}

/**
 * MAM-27 — one work the replacement expects a new file to be, as the Download dialog showed it when the
 * owner scheduled: what the new files' ids are compared with.
 */
export interface ReplacementJobWork {
  workId: string
  kind: MediaWorkKind
  title: string | null
  year: number | null
  /** The owner confirmed a match by title and year; false for a match by an id, which needs none. */
  confirmedByOwner: boolean
  /** Its registry ids then: a series' are its show's. */
  identifiers: ReplacementJobIdentifier[]
}

/**
 * MAM-26 — `GET /api/replacements/{id}`: one of the owner's replacements, with every name its move
 * planned by position and what the owner may do with it. The server works out each `can…` and the
 * room; the page shows them. MAM-27 — with its ticked copies, its new files and the works it expects,
 * how far the check got, and who stopped it.
 */
export interface ReplacementDetails {
  id: string
  downloadJobId: string
  state: ReplacementJobState
  /** Why it stopped; null unless it did. */
  reason: string | null
  /** Replacement is switched off now: nothing is moved, put back or deleted while it is off (ADR 0033). */
  switchedOff: boolean
  /**
   * The mover stopped it for review: Retry sends it back to be checked and moved. MAM-27 — or the check
   * of its new files stopped and no copy only a drop settles is left: Retry starts the check again.
   */
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
  /** MAM-27 — Keep both is offered: none of its old copies has started to move, and it hasn't ended. */
  canCancel: boolean
  /** MAM-27 — who stopped it; null unless it waits for review. */
  stop: ReplacementStop | null
  /** MAM-27 — where the check of its new files is; null unless it is Inspecting. */
  checkStage: ReplacementCheckStage | null
  /** MAM-27 — when the last check of its new files started; null before one did. */
  checkStartedUtc: string | null
  /** MAM-27 — when that check asked Jellyfin to look at the new files; null before it did. */
  libraryAskedUtc: string | null
  /** MAM-27 — until when the running check waits for its stage; null unless it is Inspecting. */
  checkWaitsUntilUtc: string | null
  /** MAM-27 — its copies, not dropped, that only dropping them or Keep both settles. */
  dropOnlyCopyCount: number
  /** MAM-27 — every copy the owner ticked, dropped ones included, by position. */
  copies: ReplacementJobCopy[]
  /** MAM-27 — the new video files of its download the last check listed, by position; none before one did. */
  newFiles: ReplacementJobNewFile[]
  /** MAM-27 — the works it expects the new files to be. */
  works: ReplacementJobWork[]
}

/**
 * MAM-26 — the body of `POST /api/replacements/{id}/delete-for-good`: the ids of the names the owner
 * confirmed, exactly as the confirmation listed them. The server refuses a list that isn't what the
 * replacement holds in the quarantine now.
 */
export interface DeleteQuarantinedRequest {
  names: string[]
}
