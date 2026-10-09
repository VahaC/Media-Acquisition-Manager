import type { AllowedRoots, ReplacementFolder } from '@/lib/types'

// MAM-24 — what GET /api/settings/allowed-roots answers: the media folders, the replacement switch and
// the library folders with their ticks. The folders are the Jellyfin sample's (VendorSamples
// virtual-folders.json) with `/media` allowed: four inside it, the collections outside.

/** The stored settings: `paths`, replacement off and no library folder unless `changes` says otherwise. */
export function allowedRootsOf(paths: string[], changes: Partial<Omit<AllowedRoots, 'paths'>> = {}): AllowedRoots {
  return { paths, replacementEnabled: false, libraryFolders: [], unmappedLibraryFolderCount: 0, ...changes }
}

/** A library folder inside the media folders, unticked unless `changes` says otherwise. */
export function libraryFolderOf(appPath: string, libraryNames: string[], changes: Partial<ReplacementFolder> = {}): ReplacementFolder {
  return { appPath, libraryNames, ticked: false, status: 'Available', ...changes }
}

export const moviesFolder = libraryFolderOf('/media/movies', ['Фільми'])
export const tvShowsFolder = libraryFolderOf('/media/tv-shows', ['Серіали'])
/** Listed by two libraries, as one folder may be. */
export const cartoonsFolder = libraryFolderOf('/media/cartoons', ['Мульфільми', 'Мультсеріали'])
/** Jellyfin's own collections, outside `/media`: a tick there would have no effect. */
export const collectionsFolder = libraryFolderOf('/config/data/collections', ['Колекції'], { status: 'OutsideMediaFolders' })
