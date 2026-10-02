import { useThemeStore } from '@/lib/theme-store'

// The store asks for the operating system's colour scheme when it loads, so the stand-in is installed
// before the imports run: an OS in light mode whose answer a test can change, as its settings would.
const os = vi.hoisted(() => {
  const listeners = new Set<() => void>()
  const query = {
    matches: false,
    media: '(prefers-color-scheme: dark)',
    addEventListener: (_type: string, listener: () => void) => {
      listeners.add(listener)
    },
    removeEventListener: (_type: string, listener: () => void) => {
      listeners.delete(listener)
    },
  }
  window.matchMedia = () => query as unknown as MediaQueryList
  return {
    /** The operating system switches to dark (or back to light). */
    switchTo(dark: boolean) {
      query.matches = dark
      listeners.forEach((listener) => listener())
    },
  }
})

const isDark = () => document.documentElement.classList.contains('dark')

/** What another tab's choice looks like from this one. */
function anotherTabStores(value: string | null) {
  window.dispatchEvent(new StorageEvent('storage', { key: 'mam:theme', newValue: value }))
}

describe('useThemeStore', () => {
  beforeEach(() => {
    os.switchTo(false)
    useThemeStore.setState({ mode: 'system' })
    document.documentElement.classList.remove('dark')
    localStorage.clear()
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('stores Light and Dark, and removes the key for System, setting the class on <html> for each', () => {
    // Arrange
    os.switchTo(true)
    const { setMode } = useThemeStore.getState()

    // Act
    setMode('light')
    const light = { stored: localStorage.getItem('mam:theme'), dark: isDark() }
    setMode('dark')
    const dark = { stored: localStorage.getItem('mam:theme'), dark: isDark() }
    setMode('system')
    const system = { stored: localStorage.getItem('mam:theme'), dark: isDark() }

    // Assert
    expect(light).toEqual({ stored: 'light', dark: false })
    expect(dark).toEqual({ stored: 'dark', dark: true })
    // A dark operating system, followed.
    expect(system).toEqual({ stored: null, dark: true })
    expect(useThemeStore.getState().mode).toBe('system')
  })

  it('follows the operating system while the theme is System', () => {
    // Arrange
    useThemeStore.getState().setMode('system')

    // Act
    os.switchTo(true)
    const afterDark = isDark()
    os.switchTo(false)

    // Assert
    expect(afterDark).toBe(true)
    expect(isDark()).toBe(false)
  })

  it('ignores the operating system while the theme is Light or Dark', () => {
    // Arrange
    const { setMode } = useThemeStore.getState()
    setMode('light')

    // Act
    os.switchTo(true)
    const lightOnDarkOs = isDark()
    setMode('dark')
    os.switchTo(false)

    // Assert
    expect(lightOnDarkOs).toBe(false)
    expect(isDark()).toBe(true)
  })

  it('takes the theme another tab picks, System included', () => {
    // Arrange
    os.switchTo(true)
    useThemeStore.getState().setMode('light')

    // Act
    anotherTabStores('dark')
    const afterDark = { mode: useThemeStore.getState().mode, dark: isDark() }
    anotherTabStores('light')
    const afterLight = { mode: useThemeStore.getState().mode, dark: isDark() }
    anotherTabStores(null)

    // Assert
    expect(afterDark).toEqual({ mode: 'dark', dark: true })
    expect(afterLight).toEqual({ mode: 'light', dark: false })
    expect(useThemeStore.getState().mode).toBe('system')
    expect(isDark()).toBe(true)
  })

  it('leaves the theme alone when another tab stores something else', () => {
    // Arrange
    useThemeStore.getState().setMode('dark')

    // Act
    window.dispatchEvent(new StorageEvent('storage', { key: 'mam:users:pageSize', newValue: '50' }))

    // Assert
    expect(useThemeStore.getState().mode).toBe('dark')
    expect(isDark()).toBe(true)
  })

  it("goes back to System when another tab clears this site's storage", () => {
    // Arrange
    os.switchTo(true)
    useThemeStore.getState().setMode('light')

    // Act
    window.dispatchEvent(new StorageEvent('storage', { key: null, newValue: null }))

    // Assert
    expect(useThemeStore.getState().mode).toBe('system')
    expect(isDark()).toBe(true)
  })

  it('still switches the theme of this tab when storage refuses the choice', () => {
    // Arrange
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('The quota has been exceeded.', 'QuotaExceededError')
    })

    // Act
    useThemeStore.getState().setMode('dark')

    // Assert
    expect(useThemeStore.getState().mode).toBe('dark')
    expect(isDark()).toBe(true)
    expect(localStorage.getItem('mam:theme')).toBeNull()
  })
})

describe('useThemeStore when it loads', () => {
  // The stored choice is read once, when the module loads, so each case loads a fresh copy.
  async function loadWith(stored: string | null) {
    if (stored !== null) localStorage.setItem('mam:theme', stored)
    vi.resetModules()
    return (await import('@/lib/theme-store')).useThemeStore.getState().mode
  }

  beforeEach(() => {
    os.switchTo(false)
  })

  afterEach(() => {
    document.documentElement.classList.remove('dark')
    localStorage.clear()
    vi.restoreAllMocks()
  })

  it.each([
    ['light', 'light'],
    ['dark', 'dark'],
    [null, 'system'],
    ['sepia', 'system'],
  ])('reads a stored %s as %s, the way the pre-paint script in index.html does', async (stored, mode) => {
    // Arrange
    const storedValue = stored

    // Act
    const loaded = await loadWith(storedValue)

    // Assert
    expect(loaded).toBe(mode)
  })

  it('starts at System when storage cannot be read', async () => {
    // Arrange
    localStorage.setItem('mam:theme', 'dark')
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new DOMException('The operation is insecure.', 'SecurityError')
    })

    // Act
    const loaded = await loadWith(null)

    // Assert
    expect(loaded).toBe('system')
  })

  it('applies System itself when storage cannot be read, where the pre-paint script left the page light', async () => {
    // Arrange
    os.switchTo(true)
    // What index.html's script does on a dark operating system when reading storage throws.
    document.documentElement.classList.remove('dark')
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new DOMException('The operation is insecure.', 'SecurityError')
    })

    // Act
    const loaded = await loadWith(null)

    // Assert
    expect(loaded).toBe('system')
    expect(isDark()).toBe(true)
  })
})
