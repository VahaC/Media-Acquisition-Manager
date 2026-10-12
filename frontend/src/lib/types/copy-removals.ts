import type { DuplicateClass } from './duplicates'
import type { ReplacementName } from './replacements'

// MAM-40 — removing one copy of a duplicate group into the quarantine from the Duplicates page, through
// the owner-only `/api/replacements/removals`: the same master switch, ticked folders, quarantine and
// Restore / Delete for good as a replacement's old copies (ADR 0030–0033). The server works out every
// verdict and every reason; the page shows them and sends the file the owner picked with the identity it
// showed, never a path.

/**
 * MAM-40 — whether one file of a same-work or exact-copies group can be removed now, as the list read it:
 * the server checks it again when Remove is pressed. `reason` is a sentence to read when it can't.
 */
export interface CopyRemovalVerdict {
  allowed: boolean
  /** Why it can't be removed now: the server's sentence; null when it can. */
  reason: string | null
}

/**
 * MAM-40 — where a removal is: Moving (into the quarantine), Quarantined, NeedsReview (something of it
 * stayed or went wrong: `reason` says what), Restoring, Restored (back where it was), Deleting, Deleted
 * (gone for good), or NotMoved (its video's rename failed, so nothing went into the quarantine). The last
 * three have ended, and the list leaves them out.
 */
export type CopyRemovalState =
  | 'Moving'
  | 'Quarantined'
  | 'NeedsReview'
  | 'Restoring'
  | 'Restored'
  | 'Deleting'
  | 'Deleted'
  | 'NotMoved'

/** The classes a copy is removed from: no other class has a copy to spare. */
export type CopyRemovalGroupClass = Extract<DuplicateClass, 'SameWork' | 'ByteIdentical'>

/**
 * MAM-40 — `POST /api/replacements/removals`: the group and the file the owner picked, with the identity
 * the page showed for it (the app's last look), so the server refuses a file that changed since.
 */
export interface RemoveCopyRequest {
  groupKey: string
  /** The file's `LibraryDuplicateFile.file.id`. */
  fileId: string
  deviceId: number | null
  inode: number | null
  sizeBytes: number | null
}

/** MAM-40 — one copy removed into the quarantine, with its names and what the owner may do with it. */
export interface CopyRemoval {
  id: string
  state: CopyRemovalState
  /** Why it needs review, or why it didn't move: the app's own sentence; null otherwise. */
  reason: string | null
  /** The group's heading when it was removed: "The Gods Must Be Crazy (1980)", "Clarkson's Farm · S02E03 · Title". */
  title: string
  groupClass: CopyRemovalGroupClass
  /** The video's path in the app: where Restore puts it back. */
  originalPath: string
  /** The video's path in the quarantine. */
  quarantinePath: string
  /** The room its names in the quarantine take. */
  sizeBytes: number
  /** The room Delete for good frees: nothing for a name with another name (a hardlink). */
  freesBytes: number
  requestedByUserName: string
  /** UTC with a trailing `Z`. */
  createdUtc: string
  /** UTC with a trailing `Z`. */
  stateChangedUtc: string
  canRestore: boolean
  canDeleteForGood: boolean
  /** The video, then its same-name companions, each with where it is now: a replacement's names' shape. */
  names: ReplacementName[]
}

/** MAM-40 — one page of `GET /api/replacements/removals`: the removals still open, newest first. */
export interface CopyRemovalListResponse {
  items: CopyRemoval[]
  /** Every open removal, across all pages. */
  totalCount: number
  page: number
  pageSize: number
  /** The room every open removal's names in the quarantine take together. */
  quarantinedBytes: number
  /**
   * The replacement master switch is off on the Media folders page: the server refuses Restore and Delete for
   * good (409) until it is on again, whatever each removal's `canRestore` and `canDeleteForGood` say.
   */
  switchedOff: boolean
}

/** The query params of `GET /api/replacements/removals`: the page, applied by the server. */
export interface CopyRemovalsQuery {
  page: number
  pageSize: number
}
