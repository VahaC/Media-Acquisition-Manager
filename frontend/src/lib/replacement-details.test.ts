import { jobCopyOf, newFileOf, replacementDetailsOf } from '@/test/replacements'
import { anyCopyDroppable, copyFacts, dropLeavesOthers, newFilePlace } from './replacement-details'

// MAM-27 — how the job's page words what the check found, and what it may still offer to drop.

describe('newFilePlace', () => {
  it.each<[string, Parameters<typeof newFileOf>[0], string | null]>([
    ['a film', {}, null],
    ['an episode', { seasonNumber: 1, episodeNumber: 4 }, 'Season 1 · Episode 4'],
    ['a file of several episodes, by their range', { seasonNumber: 1, episodeNumber: 1, episodeNumberEnd: 2 }, 'Season 1 · Episodes 1–2'],
    ['a range that ends where it starts, as one episode', { seasonNumber: 1, episodeNumber: 3, episodeNumberEnd: 3 }, 'Season 1 · Episode 3'],
    ['the specials\' range', { seasonNumber: 0, episodeNumber: 1, episodeNumberEnd: 2 }, 'Specials · Episodes 1–2'],
    ['a range the library gave no season', { seasonNumber: null, episodeNumber: 5, episodeNumberEnd: 6 }, 'Episodes 5–6'],
  ])('names where the library placed %s', (_, changes, place) => {
    // Arrange
    const file = newFileOf(changes)

    // Act
    const named = newFilePlace(file)

    // Assert
    expect(named).toBe(place)
  })
})

describe('copyFacts', () => {
  it('names a copy of several episodes by their range, after its size and running time', () => {
    // Arrange
    const copy = jobCopyOf({ sizeBytes: 3_221_225_472, durationMilliseconds: 3_000_000, seasonNumber: 2, episodeNumber: 5, episodeNumberEnd: 6 })

    // Act
    const facts = copyFacts(copy)

    // Assert
    expect(facts).toBe('3 GiB · 50:00 · Season 2 · Episodes 5–6')
  })
})

describe('what the owner can still drop', () => {
  const second = { id: 'e1a50000-0000-4000-8000-000000000202', position: 1 }

  it.each<[string, Parameters<typeof jobCopyOf>[0][], boolean]>([
    ['a film\'s one copy, the last, which is never dropped', [{ canDrop: false }], false],
    ['two copies the server lets go', [{ canDrop: true }, { ...second, canDrop: true }], true],
  ])('offers Drop or not for %s', (_, copies, offered) => {
    // Arrange
    const details = replacementDetailsOf({ copies: copies.map((copy) => jobCopyOf(copy)) })

    // Act
    const droppable = anyCopyDroppable(details)

    // Assert
    expect(droppable).toBe(offered)
  })

  it.each<[string, Parameters<typeof jobCopyOf>[0][], number, boolean]>([
    ['one copy only a drop settles beside one that a new check can pair', [{ onlyDropSettles: true }, second], 1, true],
    ['a film\'s one copy only a drop settles', [{ onlyDropSettles: true }], 1, false],
    ['every copy left one only a drop settles, beside one dropped', [{ onlyDropSettles: true }, { ...second, onlyDropSettles: true }, { id: 'e1a50000-0000-4000-8000-000000000203', position: 2, droppedUtc: '2026-10-10T14:00:00Z', status: 'Dropped' }], 2, false],
  ])('says whether dropping leaves another copy to retry, for %s', (_, copies, dropOnlyCopyCount, leaves) => {
    // Arrange
    const details = replacementDetailsOf({ copies: copies.map((copy) => jobCopyOf(copy)), dropOnlyCopyCount })

    // Act
    const others = dropLeavesOthers(details)

    // Assert
    expect(others).toBe(leaves)
  })
})
