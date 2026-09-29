import { act, renderHook } from '@testing-library/react'
import { DEFAULT_PAGE_SIZE, usePageSize } from './use-page-size'

/** The key whose storage refuses every read and write, as a private window or blocked site data does. */
const UNAVAILABLE_KEY = 'mam:unavailable:pageSize'

describe('usePageSize', () => {
  beforeEach(() => {
    localStorage.clear()
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('reads a stored size, ignores a foreign value and survives unavailable storage', () => {
    // Arrange
    localStorage.setItem('mam:stored:pageSize', '50')
    localStorage.setItem('mam:foreign:pageSize', '7')
    // Stored, but refused when read: the default proves the refusal was reached and caught.
    localStorage.setItem(UNAVAILABLE_KEY, '50')
    const getItem = Storage.prototype.getItem
    const setItem = Storage.prototype.setItem
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(function (this: Storage, key: string) {
      if (key === UNAVAILABLE_KEY) throw new DOMException('The operation is insecure.', 'SecurityError')
      return getItem.call(this, key)
    })
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(function (this: Storage, key: string, value: string) {
      if (key === UNAVAILABLE_KEY) throw new DOMException('The operation is insecure.', 'SecurityError')
      setItem.call(this, key, value)
    })

    // Act
    const stored = renderHook(() => usePageSize('mam:stored:pageSize')).result
    const foreign = renderHook(() => usePageSize('mam:foreign:pageSize')).result
    const unavailable = renderHook(() => usePageSize(UNAVAILABLE_KEY)).result
    const openedUnavailable = unavailable.current[0]
    act(() => unavailable.current[1](100))

    // Assert
    expect(stored.current[0]).toBe(50)
    expect(foreign.current[0]).toBe(DEFAULT_PAGE_SIZE)
    expect(openedUnavailable).toBe(DEFAULT_PAGE_SIZE)
    // The write failed, and the choice still holds until the page is closed.
    expect(unavailable.current[0]).toBe(100)
    expect(vi.mocked(Storage.prototype.setItem)).toHaveBeenCalledWith(UNAVAILABLE_KEY, '100')
    expect(getItem.call(localStorage, UNAVAILABLE_KEY)).toBe('50')
  })

  it('saves a picked size under its own list\'s key only', () => {
    // Arrange
    localStorage.setItem('mam:users:pageSize', '10')
    const { result } = renderHook(() => usePageSize('mam:integrations:pageSize'))

    // Act
    act(() => result.current[1](50))

    // Assert
    expect(result.current[0]).toBe(50)
    expect(localStorage.getItem('mam:integrations:pageSize')).toBe('50')
    expect(localStorage.getItem('mam:users:pageSize')).toBe('10')
  })
})
