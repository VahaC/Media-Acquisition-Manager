import { isBatchUnderWay, isSuggestionRunUnderWay } from '@/lib/library-links'
import type { LibraryLinkSuggestionRunStatus, LibraryLinkWriteState } from '@/lib/types'
import { batchItemOf, batchOf, confidentCalypso, confidentStripling, suggestionRunOf } from '@/test/library-links'

// MAM-34 — what the TMDb page polls for: a preparation of the suggestions under way, and an "Accept
// all confident" click whose links still wait for the writer or are being written.

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
    ['Queued', true],
    ['Writing', true],
    ['Written', false],
    ['NotWritten', false],
    ['KeptInApp', false],
  ])('counts a click with one link %s, the other written, as under way: %s', (writeState, expected) => {
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
})
