import { useSyncExternalStore } from 'react'

/** How a library card shows a work's versions: a row each, or a column each of one table. */
export type VersionsView = 'rows' | 'table'

const STORAGE_KEY = 'mam:versionsView'

/** A choice this tab made that storage refused to keep; it holds until the tab is closed. */
let unsaved: VersionsView | null = null

/** Every card on screen that shows the choice. */
const listeners = new Set<() => void>()

/** The choice as stored. Anything but `table`, a missing key included, is the default: rows. */
function readView(): VersionsView {
  if (unsaved !== null) return unsaved
  try {
    return localStorage.getItem(STORAGE_KEY) === 'table' ? 'table' : 'rows'
  } catch {
    return 'rows' /* storage unavailable: the default it is */
  }
}

function saveView(view: VersionsView) {
  try {
    localStorage.setItem(STORAGE_KEY, view)
    unsaved = null
  } catch {
    unsaved = view /* storage unavailable: the choice lasts until the tab is closed */
  }
  listeners.forEach((listener) => listener())
}

function subscribe(listener: () => void) {
  // A choice made in another tab changed the stored key; no key at all is that tab clearing this
  // site's storage, which took the choice with it. A tab never hears its own writes.
  const onStorage = (event: StorageEvent) => {
    if (event.key === null || event.key === STORAGE_KEY) listener()
  }
  listeners.add(listener)
  window.addEventListener('storage', onStorage)
  return () => {
    listeners.delete(listener)
    window.removeEventListener('storage', onStorage)
  }
}

/**
 * MAM-16 — how library cards show versions, picked on any card and remembered per browser under
 * `mam:versionsView`: rows by default, or a table that compares them.
 *
 * One choice for every card, on the Library page and under "In your library" on the Search page
 * alike, read through useSyncExternalStore rather than a state of each card (as usePageSize keeps):
 * a card that kept its own copy would stay in the other view until a reload, so the cards of one
 * page would disagree. Not a zustand store like theme-store.ts: the theme has work to do while no
 * switch is mounted (`.dark` on <html>, following the operating system), while this choice only
 * matters to the cards that read it, so storage itself is the store.
 */
export function useVersionsView(): [VersionsView, (view: VersionsView) => void] {
  return [useSyncExternalStore(subscribe, readView), saveView]
}
