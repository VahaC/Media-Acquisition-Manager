/**
 * MAM-9 — `GET /api/settings/allowed-roots`: the folders inside the app where it may read library
 * files, in the order the owner entered them and in canonical form. None until the owner names one,
 * and then the app reads nothing.
 *
 * MAM-24 — with the replacement gates the same page saves (ADR 0033): the master switch, off by
 * default, and the library folders the app knows, each with its tick, none by default.
 */
export interface AllowedRoots {
  paths: string[]
  /** Whether the owner switched replacement on; off by default, and after an upgrade. */
  replacementEnabled: boolean
  /**
   * Every library folder the path mappings bring into the app, then every stored tick that is no
   * longer one, by app path; none until a library is synced.
   */
  libraryFolders: ReplacementFolder[]
  /**
   * How many of the libraries' folders the path mappings give no path in this app, so they can't be
   * listed or ticked.
   */
  unmappedLibraryFolderCount: number
  /**
   * MAM-26 — every quarantine holding replaced copies, one per media folder whose
   * `.mam-quarantine` holds any (ADR 0030), from the app's own records; none when nothing is there.
   */
  quarantines: QuarantineSpace[]
}

/** MAM-26 — the room one quarantine takes. */
export interface QuarantineSpace {
  /** The quarantine folder inside the app, such as `/media/.mam-quarantine`. */
  path: string
  /** The replacements whose old copies it holds. */
  replacementCount: number
  /** MAM-40 — the owner's removals of a duplicate copy it holds. */
  removalCount: number
  fileCount: number
  sizeBytes: number
}

/**
 * MAM-26 — what keeps a rename into the quarantine from working in a library folder (ADR 0033's
 * hint): another mount than its media folder's, where the quarantine is; a read-only mount; no write
 * and search permission for the app's user there or on its media folder; or the app couldn't tell.
 */
export type RenamePlaceProblem = 'OtherMount' | 'ReadOnly' | 'NotWritable' | 'CouldNotCheck'

/**
 * MAM-24 — what a tick on a library folder is worth. `Available`: the folder lies inside a media
 * folder, and a tick there counts. `OutsideMediaFolders`: it lies outside every media folder, where
 * the app may not read, so a tick has no effect until the folder is inside one. `NoLongerALibraryFolder`:
 * a stored tick whose path is no longer any library folder the app knows (the library dropped it, or
 * a mapping changed), listed only so the owner can untick it. All against the stored media folders,
 * not the ones being typed.
 */
export type ReplacementFolderStatus = 'Available' | 'OutsideMediaFolders' | 'NoLongerALibraryFolder'

/** MAM-24 — one library folder on the Media folders page's replacement list. */
export interface ReplacementFolder {
  /** The folder inside the app, in canonical form: what the page sends back to keep it ticked. */
  appPath: string
  /** Every library that lists the folder ("Фільми"); none for `NoLongerALibraryFolder`. */
  libraryNames: string[]
  /** Whether the owner ticked it for replacement. */
  ticked: boolean
  status: ReplacementFolderStatus
  /**
   * MAM-26 — whether a rename into the quarantine can work there, for every `Available` folder: none
   * means it can. Null for the other statuses, which the app doesn't check. A hint only: the rename
   * itself decides.
   */
  renameProblems: RenamePlaceProblem[] | null
  /**
   * MAM-39 — present only when renameProblems contains 'NotWritable' and the app could read the ids:
   * the folder the app's user can't write to (the ticked folder itself, or its media folder's
   * `.mam-quarantine` — the media folder itself while it has none), its owner, and the ids the app
   * runs as. Null (or missing) everywhere else.
   */
  writeAccess: WriteAccess | null
}

/**
 * MAM-39 — who owns the folder a move can't write to, and who the app runs as (PUID and PGID), so
 * the page can say exactly what to set.
 */
export interface WriteAccess {
  /** The folder inside the app the app's user can't write to. */
  path: string
  ownerUserId: number
  ownerGroupId: number
  appUserId: number
  appGroupId: number
}

/**
 * `PUT /api/settings/allowed-roots`: everything the page's one Save holds, so it stays one request
 * and one transaction (ADR 0033). The server reads a missing switch as off and missing ticks as none,
 * so the page always sends both.
 */
export interface UpdateAllowedRootsRequest {
  /** The folders as typed, blank rows included: the server's sentence names what is wrong with one. */
  paths: string[]
  replacementEnabled: boolean
  /** The app paths of every library folder the page shows ticked, in its order. */
  replacementFolders: string[]
}
