import type { LibrarySyncRun } from './types'

/**
 * Whether a sync is still under way. The sources list polls while one is, and a source with one
 * can't be synced again until it ends (the API answers 409).
 */
export function isSyncUnderWay(run: LibrarySyncRun | null | undefined): run is LibrarySyncRun {
  return run?.status === 'Queued' || run?.status === 'Running'
}
