import type { LibraryLinkBatch, LibraryLinkSuggestionRun } from './types'

/** MAM-34 — whether a preparation of the suggestions is queued or running: its line polls till it ends. */
export function isSuggestionRunUnderWay(run: LibraryLinkSuggestionRun | null | undefined): run is LibraryLinkSuggestionRun {
  return run?.status === 'Queued' || run?.status === 'Running'
}

/**
 * MAM-34 — whether a link of an "Accept all confident" click still waits for the background writer or
 * is being written: its rows poll till none does.
 */
export function isBatchUnderWay(batch: LibraryLinkBatch | undefined): boolean {
  return batch !== undefined && batch.items.some((item) =>
    item.links.some((link) => link.writeState === 'Queued' || link.writeState === 'Writing'))
}
