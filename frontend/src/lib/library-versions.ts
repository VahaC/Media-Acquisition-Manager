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
