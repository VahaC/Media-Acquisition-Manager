import {
  isBatchUnderWay,
  isLinkUnderWay,
  isSuggestionRunUnderWay,
  LIBRARY_POLL_MS,
  LINK_POLL_MS,
  linkBadgeOf,
  type LinkBadgeState,
} from '@/lib/library-links'
import type { LibraryLinkSuggestionRunStatus, LibraryLinkWriteState } from '@/lib/types'
import { batchItemOf, batchOf, confidentCalypso, confidentStripling, suggestionRunOf } from '@/test/library-links'

// MAM-34 — what the TMDb page and the Library page poll for: a preparation of the suggestions under way,
// a link that waits for Jellyfin or is being identified, and an "Accept all confident" click whose
// links still do; and the badge a link shows from its state and whether the app identified it.

describe('library-links', () => {
  it.each<[LibraryLinkSuggestionRunStatus, boolean]>([
    ['Queued', true],
    ['Running', true],
    ['Succeeded', false],
    ['Failed', false],
  ])('counts a %s preparation as under way: %s', (status, expected) => {
    // Arrange
    const run = suggestionRunOf({ status })

    // Act
    const underWay = isSuggestionRunUnderWay(run)

    // Assert
    expect(underWay).toBe(expected)
  })

  it('counts no preparation, or none yet loaded, as not under way', () => {
    // Arrange
    const runs = [null, undefined]

    // Act
    const underWay = runs.map(isSuggestionRunUnderWay)

    // Assert
    expect(underWay).toEqual([false, false])
  })

  it.each<[LibraryLinkWriteState, boolean]>([
    ['KeptInApp', false],
    ['Queued', true],
    ['Writing', true],
    ['NotWritten', false],
    ['Written', false],
    ['InLibrary', false],
    ['Lost', false],
  ])('counts a %s link as waiting for Jellyfin or being identified: %s', (state, expected) => {
    // Arrange
    const link = state

    // Act
    const underWay = isLinkUnderWay(link)

    // Assert
    expect(underWay).toBe(expected)
  })

  it('counts a link with no state, or none yet loaded, as not under way', () => {
    // Arrange
    const states = [null, undefined]

    // Act
    const underWay = states.map(isLinkUnderWay)

    // Assert
    expect(underWay).toEqual([false, false])
  })

  it.each<[LibraryLinkWriteState, boolean]>([
    ['Queued', true],
    ['Writing', true],
    ['Written', false],
    ['NotWritten', false],
    ['KeptInApp', false],
  ])('counts a click with one link %s, the other identified, as under way: %s', (writeState, expected) => {
    // Arrange
    const batch = batchOf([batchItemOf(confidentStripling.work, 'Written'), batchItemOf(confidentCalypso.work, writeState)])

    // Act
    const underWay = isBatchUnderWay(batch)

    // Assert
    expect(underWay).toBe(expected)
  })

  it('counts a click of skipped works only, or none loaded, as not under way', () => {
    // Arrange
    const skipped = batchOf([{ workId: confidentStripling.work.id, outcome: 'Skipped', reason: 'You marked that none fits.', links: [] }])

    // Act
    const underWay = [isBatchUnderWay(skipped), isBatchUnderWay(undefined)]

    // Assert
    expect(underWay).toEqual([false, false])
  })

  it.each<[LibraryLinkWriteState, boolean | undefined, LinkBadgeState]>([
    ['Written', true, 'Identified'],
    ['InLibrary', true, 'Identified'],
    ['Written', false, 'NotIdentified'],
    ['InLibrary', false, 'NotIdentified'],
    ['Written', undefined, 'Written'],
    ['InLibrary', undefined, 'InLibrary'],
    ['NotWritten', true, 'NotWritten'],
    ['NotWritten', false, 'NotWritten'],
    ['Lost', false, 'Lost'],
    ['Queued', false, 'Queued'],
    ['Writing', true, 'Writing'],
    ['KeptInApp', undefined, 'KeptInApp'],
  ])('shows a %s link, identified %s, as %s', (state, identified, expected) => {
    // Arrange
    const known = identified

    // Act
    const badge = linkBadgeOf(state, known)

    // Assert
    expect(badge).toBe(expected)
  })

  it('polls a link\'s identification every 2 s and the Library page\'s works every 5 s', () => {
    // Arrange
    const intervals = [LINK_POLL_MS, LIBRARY_POLL_MS]

    // Act
    const [link, library] = intervals

    // Assert
    expect(link).toBe(2000)
    expect(library).toBe(5000)
  })
})
