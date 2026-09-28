import { isChunkLoadError } from './chunk-error'

describe('isChunkLoadError', () => {
  it.each([
    ['Chromium, transport failure', 'Failed to fetch dynamically imported module: https://x/assets/HomePage-abc.js'],
    ['Chromium, HTML served for a module', 'Failed to load module script: Expected a JavaScript module script but the server responded with a MIME type of "text/html".'],
    ['Firefox, import failure', 'error loading dynamically imported module: https://x/assets/HomePage-abc.js'],
    ['Firefox, MIME refusal', 'Loading module from "https://x/assets/a.js" was blocked because of a disallowed MIME type ("text/html").'],
    ['Safari, import failure', 'Importing a module script failed.'],
    ['Safari, load failure', 'Unable to load module'],
  ])('recognises %s', (_, message) => {
    // Arrange
    const error = new TypeError(message)

    // Act
    const result = isChunkLoadError(error)

    // Assert
    expect(result).toBe(true)
  })

  it('recognises an error named ChunkLoadError', () => {
    // Arrange
    const error = new Error('Loading chunk 7 failed.')
    error.name = 'ChunkLoadError'

    // Act
    const result = isChunkLoadError(error)

    // Assert
    expect(result).toBe(true)
  })

  it('finds the chunk failure in the cause of a wrapping error', () => {
    // Arrange
    const error = new Error('Route failed', { cause: new TypeError('Importing a module script failed.') })

    // Act
    const result = isChunkLoadError(error)

    // Assert
    expect(result).toBe(true)
  })

  it('reads the message of a plain object and of a string', () => {
    // Arrange
    const payload = { message: 'Failed to fetch dynamically imported module: /assets/x.js' }
    const text = 'Unable to load module'

    // Act
    const results = [isChunkLoadError(payload), isChunkLoadError(text)]

    // Assert
    expect(results).toEqual([true, true])
  })

  it.each([
    ['null', null],
    ['undefined', undefined],
    ['an ordinary render bug', new TypeError("Cannot read properties of undefined (reading 'map')")],
    ['an HTML parse error', new SyntaxError("Unexpected token '<'")],
    ['an error with an empty message', new Error('')],
    ['an object without a message', { status: 500 }],
  ])('ignores %s', (_, error) => {
    // Arrange
    const candidate: unknown = error

    // Act
    const result = isChunkLoadError(candidate)

    // Assert
    expect(result).toBe(false)
  })
})
