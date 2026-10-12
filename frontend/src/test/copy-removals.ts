import type { CopyRemoval, CopyRemovalListResponse, CopyRemovalsQuery, ReplacementName } from '@/lib/types'
import { backedUpAliens } from '@/test/duplicates'

// MAM-40 — what GET /api/replacements/removals answers once the owner removed the Theatrical Cut's copy on
// the backup disk (test/duplicates.ts's exact copies): its video and its English subtitle in the quarantine
// of /media.

/** The removal's own id, which its Restore and Delete for good ask by. */
export const REMOVAL_ID = 'c0e40000-0000-4000-8000-000000000001'

/** Where `relative` lands in the quarantine of /media for REMOVAL_ID. */
const quarantined = (relative: string) => `/media/.mam-quarantine/${REMOVAL_ID}/${relative}`

/** The copy's video, as the Duplicates page listed it. */
export const removedVideoPath = backedUpAliens.file.appPath!

/** One name of the removal; `changes` sets what a test is about. */
export function removedNameOf(changes: Partial<ReplacementName> = {}): ReplacementName {
  return {
    id: 'c0e40000-0000-4000-8000-000000000101',
    kind: 'Video',
    originalPath: removedVideoPath,
    quarantinePath: quarantined('backup/Aliens (1986) [Theatrical Cut] BDRip 1080p H.265.mkv'),
    sizeBytes: 14_845_708_473,
    otherNames: false,
    state: 'Quarantined',
    problem: null,
    ...changes,
  }
}

/** Its English subtitle, moved with it: 60 KiB. */
export const removedSubtitle = removedNameOf({
  id: 'c0e40000-0000-4000-8000-000000000102',
  kind: 'Companion',
  originalPath: '/media/backup/Aliens (1986) [Theatrical Cut] BDRip 1080p H.265.en.srt',
  quarantinePath: quarantined('backup/Aliens (1986) [Theatrical Cut] BDRip 1080p H.265.en.srt'),
  sizeBytes: 61_440,
})

/** The removal as the Removed copies tab lists it: Quarantined, Restore and Delete for good offered. */
export function removalOf(changes: Partial<CopyRemoval> = {}): CopyRemoval {
  const video = removedNameOf()
  return {
    id: REMOVAL_ID,
    state: 'Quarantined',
    reason: null,
    title: 'Чужі (1986)',
    groupClass: 'ByteIdentical',
    originalPath: video.originalPath,
    quarantinePath: video.quarantinePath,
    sizeBytes: video.sizeBytes + removedSubtitle.sizeBytes,
    freesBytes: video.sizeBytes + removedSubtitle.sizeBytes,
    requestedByUserName: 'vasyl',
    createdUtc: '2026-10-12T08:30:00Z',
    stateChangedUtc: '2026-10-12T08:30:02Z',
    canRestore: true,
    canDeleteForGood: true,
    names: [video, removedSubtitle],
    ...changes,
  }
}

/**
 * The page the server answers `query` with, out of `total` removals taking `quarantinedBytes`, with replacement
 * switched on.
 */
export function removalsPage(
  query: CopyRemovalsQuery,
  items: CopyRemoval[],
  total = items.length,
  quarantinedBytes = items.reduce((sum, item) => sum + item.sizeBytes, 0),
): CopyRemovalListResponse {
  return { items, totalCount: total, page: query.page, pageSize: query.pageSize, quarantinedBytes, switchedOff: false }
}
