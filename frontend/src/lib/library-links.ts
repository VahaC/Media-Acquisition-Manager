import type { LibraryLinkBatch, LibraryLinkSuggestionRun, LibraryLinkWriteState } from './types'

/**
 * MAM-34 — how often an open dialog, the TMDb page's counts and a followed batch are asked for again
 * while an identification is under way or a preparation of the suggestions runs. Jellyfin's Identify
 * takes seconds for a film and minutes for a series (the live check of 2026-10-06 timed 1.8 s, 21.3 s
 * for an episode through the Open Movie Database, and 159.3 s for a series), so the 2 s of a sync's
 * progress line rather than the 10 s of other live views. Here rather than in the query hooks so
 * `queries/library.ts` can share it without importing the link hooks.
 */
export const LINK_POLL_MS = 2_000

/**
 * MAM-34 — how often the Library page's works are asked for again while a card's link waits for
 * Jellyfin or is being identified. The works payload is heavy (versions and files), the house cadence
 * for lists is 30 s and for live state 10 s, and 2 s is for cheap counters only: 5 s is the fastest a
 * card should ask for it and still follow a film's seconds-long identification.
 */
export const LIBRARY_POLL_MS = 5_000

/** MAM-34 — whether a preparation of the suggestions is queued or running: its line polls till it ends. */
export function isSuggestionRunUnderWay(run: LibraryLinkSuggestionRun | null | undefined): run is LibraryLinkSuggestionRun {
  return run?.status === 'Queued' || run?.status === 'Running'
}

/**
 * MAM-34 — whether a link waits for the background worker or Jellyfin is identifying its item: what
 * the dialog, the batches and the panel poll until it ends. A link with no state yet isn't.
 */
export function isLinkUnderWay(state: LibraryLinkWriteState | null | undefined): boolean {
  return state === 'Queued' || state === 'Writing'
}

/**
 * MAM-34 — whether a link of an "Accept all confident" click still waits for the background worker or
 * is being identified: its rows poll till none does.
 */
export function isBatchUnderWay(batch: LibraryLinkBatch | undefined): boolean {
  return batch !== undefined && batch.items.some((item) =>
    item.links.some((link) => isLinkUnderWay(link.writeState)))
}

/**
 * What a link's badge names: a wire state, or one of the two the app tells apart. `Written` and
 * `InLibrary` mean Jellyfin holds the link's ids, which is not the same as the app having identified
 * the item: a link set under the first rules put the ids only, so the title, overview and images are
 * still what Jellyfin took from the file name.
 */
export type LinkBadgeState = LibraryLinkWriteState | 'Identified' | 'NotIdentified'

/**
 * MAM-34 — the badge a link shows, from its wire state and whether the app identified it
 * (`identifiedUtc !== null` on an item or a row, `identified` on a Library card). `Written` or
 * `InLibrary` with `identified` true is "Identified in Jellyfin", with it false "Not identified yet", so
 * a card never says a neutral "In Jellyfin" for a link the app never identified. Every other state, or
 * a caller with no such fact (`identified` undefined), shows the state itself.
 */
export function linkBadgeOf(state: LibraryLinkWriteState, identified: boolean | undefined): LinkBadgeState {
  if (state !== 'Written' && state !== 'InLibrary') return state
  if (identified === undefined) return state
  return identified ? 'Identified' : 'NotIdentified'
}
