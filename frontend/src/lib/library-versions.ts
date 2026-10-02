import type { LibraryVersion } from './types'

/**
 * MAM-8 — versions by edition label (unlabelled first), then by their first file's path, so the
 * two copies of one movie keep their places from one sync to the next.
 *
 * MAM-12 — moved out of LibraryWorkCard: the search page's Details dialog lists a work's versions
 * too, and in the same order as its card.
 */
export function compareVersions(a: LibraryVersion, b: LibraryVersion): number {
  return (a.editionLabel ?? '').localeCompare(b.editionLabel ?? '')
    || (a.files[0]?.servicePath ?? '').localeCompare(b.files[0]?.servicePath ?? '')
}

/**
 * A path's last segment and the folder holding it, split at the last `/` or `\` (a Windows path).
 * A path without a folder part is all name.
 *
 * MAM-16 — moved out of FileLines, so the versions table names a file exactly as its expanded row
 * does.
 */
export function splitPath(path: string): { name: string; folder: string | null } {
  const cut = Math.max(path.lastIndexOf('/'), path.lastIndexOf('\\'))
  if (cut < 0 || cut === path.length - 1) return { name: path, folder: null }
  // A file right under the root keeps the root as its folder rather than an empty line.
  return { name: path.slice(cut + 1), folder: cut === 0 ? path.slice(0, 1) : path.slice(0, cut) }
}

/**
 * MAM-16 — the bytes a version takes on disk: its files' sizes added up (Jellyfin stacks a movie in
 * parts as one version of several files). Null when a size is missing, which leaves the total
 * unknown rather than too small, or when the version has no file.
 */
export function versionSize(version: LibraryVersion): number | null {
  if (version.files.length === 0) return null
  let total = 0
  for (const file of version.files) {
    if (file.sizeBytes === null) return null
    total += file.sizeBytes
  }
  return total
}
