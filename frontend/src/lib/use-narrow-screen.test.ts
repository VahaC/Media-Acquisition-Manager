import { act, renderHook } from '@testing-library/react'
import { windowIs } from '@/test/match-media'
import { useNarrowScreen } from './use-narrow-screen'

describe('useNarrowScreen', () => {
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it.each([true, false])('says whether the window is narrower than 48rem: %s', (narrow) => {
    // Arrange
    const display = windowIs(narrow)

    // Act
    const { result } = renderHook(() => useNarrowScreen())

    // Assert
    expect(result.current).toBe(narrow)
    expect(display.matchMedia).toHaveBeenCalledWith('(max-width: 48rem)')
  })

  it('follows the window as it is resized, and stops listening once unmounted', () => {
    // Arrange
    const display = windowIs(false)
    const { result, unmount } = renderHook(() => useNarrowScreen())

    // Act
    act(() => display.resize(true))
    const narrowed = result.current
    act(() => display.resize(false))
    unmount()

    // Assert
    expect(narrowed).toBe(true)
    expect(result.current).toBe(false)
    expect(display.listeners.size).toBe(0)
  })
})
