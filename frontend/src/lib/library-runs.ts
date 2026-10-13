import type { FolderScanOverview, LibrarySyncRun } from './types'

/**
 * Whether a sync is still under way. The sources list polls while one is, and a source with one
 * can't be synced again until it ends (the API answers 409).
 */
export function isSyncUnderWay(run: LibrarySyncRun | null | undefined): run is LibrarySyncRun {
  return run?.status === 'Queued' || run?.status === 'Running'
}

/**
 * MAM-42 — the files in no library the app counts now: the names the scans stored that no library
 * reports, by the counts of their last look, which leave out the ones the owner ignored. The bar's chip
 * and the Media folders row both read it, so they can't disagree.
 */
export function unreportedCountOf(scan: FolderScanOverview): number {
  const { inspectedCount, problemCount, notInspectedCount } = scan.files
  return inspectedCount + problemCount + notInspectedCount
}
