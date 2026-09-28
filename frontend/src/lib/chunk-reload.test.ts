import { recoverFromChunkError } from './chunk-reload'

const STORAGE_KEY = 'mam:chunkReload'

describe('recoverFromChunkError', () => {
  let reload: ReturnType<typeof vi.fn>

  beforeEach(() => {
    sessionStorage.clear()
    reload = vi.fn()
    // jsdom cannot navigate, and its location.reload is not configurable; swap the whole object.
    vi.stubGlobal('location', { reload })
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
    vi.useRealTimers()
  })

  it('reloads once and records the attempt when none is on record', () => {
    // Arrange
    const before = sessionStorage.getItem(STORAGE_KEY)

    // Act
    const outcome = recoverFromChunkError()

    // Assert
    expect(before).toBeNull()
    expect(outcome).toBe('reloading')
    expect(reload).toHaveBeenCalledTimes(1)
    expect(JSON.parse(sessionStorage.getItem(STORAGE_KEY)!)).toMatchObject({ count: 1 })
  })

  it('refuses a second reload inside the window, so a broken build cannot refresh forever', () => {
    // Arrange
    recoverFromChunkError()
    reload.mockClear()

    // Act
    const outcome = recoverFromChunkError()

    // Assert
    expect(outcome).toBe('exhausted')
    expect(reload).not.toHaveBeenCalled()
  })

  it('allows a fresh reload once the window has passed', () => {
    // Arrange
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-09-28T10:00:00Z'))
    recoverFromChunkError()
    reload.mockClear()
    vi.setSystemTime(new Date('2026-09-28T10:01:00.001Z'))

    // Act
    const outcome = recoverFromChunkError()

    // Assert
    expect(outcome).toBe('reloading')
    expect(reload).toHaveBeenCalledTimes(1)
  })

  it('treats an unreadable record as no attempt', () => {
    // Arrange
    sessionStorage.setItem(STORAGE_KEY, 'not json')

    // Act
    const outcome = recoverFromChunkError()

    // Assert
    expect(outcome).toBe('reloading')
    expect(reload).toHaveBeenCalledTimes(1)
  })

  it('still reloads when session storage is unavailable', () => {
    // Arrange
    const denied = () => { throw new DOMException('The operation is insecure.', 'SecurityError') }
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(denied)
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(denied)

    // Act
    const outcome = recoverFromChunkError()

    // Assert
    expect(outcome).toBe('reloading')
    expect(reload).toHaveBeenCalledTimes(1)
  })
})
