import { act, renderHook } from '@testing-library/react'
import { useVersionsView } from './use-versions-view'

/** What another tab's write (or its clearing the site's storage, with `key` null) looks like from this one. */
function anotherTabStores(key: string | null, value: string | null) {
  act(() => {
    window.dispatchEvent(new StorageEvent('storage', { key, newValue: value }))
  })
}

describe('useVersionsView', () => {
  beforeEach(() => {
    localStorage.clear()
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it.each([
    [null, 'rows'],
    ['rows', 'rows'],
    ['table', 'table'],
    ['grid', 'rows'],
  ])('reads %s stored as %s', (stored, view) => {
    // Arrange
    if (stored !== null) localStorage.setItem('mam:versionsView', stored)

    // Act
    const { result } = renderHook(() => useVersionsView())

    // Assert
    expect(result.current[0]).toBe(view)
  })

  it('saves a pick under mam:versionsView and shows it to every card on screen', () => {
    // Arrange
    const first = renderHook(() => useVersionsView()).result
    const second = renderHook(() => useVersionsView()).result

    // Act
    act(() => first.current[1]('table'))

    // Assert
    expect(localStorage.getItem('mam:versionsView')).toBe('table')
    expect(first.current[0]).toBe('table')
    expect(second.current[0]).toBe('table')
  })

  it('follows a pick made in another tab, and goes back to rows when that tab clears the storage', () => {
    // Arrange
    const { result } = renderHook(() => useVersionsView())

    // Act
    localStorage.setItem('mam:versionsView', 'table')
    anotherTabStores('mam:versionsView', 'table')
    const afterPick = result.current[0]
    localStorage.clear()
    anotherTabStores(null, null)

    // Assert
    expect(afterPick).toBe('table')
    expect(result.current[0]).toBe('rows')
  })

  it('keeps a pick for this tab when storage refuses to read and to write it', async () => {
    // Arrange — a fresh copy of the hook: the pick it keeps in memory would carry into later tests.
    vi.resetModules()
    const { useVersionsView: freshUseVersionsView } = await import('./use-versions-view')
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new DOMException('The operation is insecure.', 'SecurityError')
    })
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('The operation is insecure.', 'SecurityError')
    })
    const first = renderHook(() => freshUseVersionsView()).result
    const second = renderHook(() => freshUseVersionsView()).result
    const opened = first.current[0]

    // Act
    act(() => first.current[1]('table'))

    // Assert
    expect(opened).toBe('rows')
    expect(first.current[0]).toBe('table')
    expect(second.current[0]).toBe('table')
    expect(vi.mocked(Storage.prototype.setItem)).toHaveBeenCalledWith('mam:versionsView', 'table')
  })
})
