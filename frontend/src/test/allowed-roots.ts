import type { AllowedRoots, QuarantineSpace, ReplacementFolder } from '@/lib/types'

// MAM-24 — what GET /api/settings/allowed-roots answers: the media folders, the replacement switch and
// the library folders with their ticks. The folders are the Jellyfin sample's (VendorSamples
// virtual-folders.json) with `/media` allowed: four inside it, the collections outside. MAM-26 — with
// whether a rename into the quarantine can work in each folder, and the quarantines' room.

/**
 * The stored settings: `paths`, replacement off, no library folder and nothing in any quarantine
 * unless `changes` says otherwise.
 */
export function allowedRootsOf(paths: string[], changes: Partial<Omit<AllowedRoots, 'paths'>> = {}): AllowedRoots {
  return { paths, replacementEnabled: false, libraryFolders: [], unmappedLibraryFolderCount: 0, quarantines: [], ...changes }
}

/**
 * A library folder inside the media folders, unticked unless `changes` says otherwise. MAM-26 — as the
 * server answers, a folder inside the media folders is checked for a rename (none in the way unless
 * `changes` names some), and any other isn't (null).
 */
export function libraryFolderOf(appPath: string, libraryNames: string[], changes: Partial<ReplacementFolder> = {}): ReplacementFolder {
  const status = changes.status ?? 'Available'
  return { appPath, libraryNames, ticked: false, status, renameProblems: status === 'Available' ? [] : null, ...changes }
}

/** MAM-26 — the quarantine of `/media`, holding old copies of `replacementCount` replacements. */
export function quarantineOf(changes: Partial<QuarantineSpace> = {}): QuarantineSpace {
  return { path: '/media/.mam-quarantine', replacementCount: 2, fileCount: 3, sizeBytes: 45_097_156_608, ...changes }
}

export const moviesFolder = libraryFolderOf('/media/movies', ['Фільми'])
export const tvShowsFolder = libraryFolderOf('/media/tv-shows', ['Серіали'])
/** Listed by two libraries, as one folder may be. */
export const cartoonsFolder = libraryFolderOf('/media/cartoons', ['Мульфільми', 'Мультсеріали'])
/** Jellyfin's own collections, outside `/media`: a tick there would have no effect. */
export const collectionsFolder = libraryFolderOf('/config/data/collections', ['Колекції'], { status: 'OutsideMediaFolders' })
