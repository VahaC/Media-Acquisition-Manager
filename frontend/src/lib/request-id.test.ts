import { newRequestId } from './request-id'

const UUID_V4 = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/

describe('newRequestId', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('makes a lower-case version 4 UUID', () => {
    // Arrange
    const count = 50

    // Act
    const ids = Array.from({ length: count }, () => newRequestId())

    // Assert
    for (const id of ids) expect(id).toMatch(UUID_V4)
  })

  it('makes a different id every time', () => {
    // Arrange
    const count = 50

    // Act
    const ids = new Set(Array.from({ length: count }, () => newRequestId()))

    // Assert
    expect(ids.size).toBe(count)
  })

  it('sets the version and variant bits over the random bytes it draws', () => {
    // Arrange
    vi.stubGlobal('crypto', { getRandomValues: <T extends ArrayBufferView>(array: T) => array })

    // Act
    const id = newRequestId()

    // Assert
    expect(id).toBe('00000000-0000-4000-8000-000000000000')
  })

  it('works where the browser leaves randomUUID out, as it does on a plain http LAN address', () => {
    // Arrange
    const getRandomValues = crypto.getRandomValues.bind(crypto)
    vi.stubGlobal('crypto', { getRandomValues })

    // Act
    const id = newRequestId()

    // Assert
    expect(id).toMatch(UUID_V4)
  })
})
