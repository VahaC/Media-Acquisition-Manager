import { localQbittorrent, localTransmission, retiredClient } from '@/test/integrations'
import { sharedKindOf } from './download-clients'

// MAM-31 — the kind the Destinations page and the destination dialog take their words from before a
// client is picked.

describe('download clients', () => {
  it.each([
    ['every client saves to categories', [localQbittorrent, { ...localQbittorrent, id: 'other' }], 'Category'],
    ['every client saves to folders', [localTransmission], 'Folder'],
  ])('names the kind they share when %s', (_, clients, kind) => {
    // Arrange

    // Act
    const shared = sharedKindOf(clients)

    // Assert
    expect(shared).toBe(kind)
  })

  it.each([
    ['the clients differ', [localQbittorrent, localTransmission]],
    ['there is no client', []],
    ['a client\'s type isn\'t registered', [retiredClient]],
  ])('knows no kind when %s', (_, clients) => {
    // Arrange

    // Act
    const shared = sharedKindOf(clients)

    // Assert
    expect(shared).toBeNull()
  })
})
