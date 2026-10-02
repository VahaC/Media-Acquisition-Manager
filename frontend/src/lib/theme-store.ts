import { create } from 'zustand'

/** The theme picked in this browser: always light, always dark, or whichever the operating system is in. */
export type ThemeMode = 'light' | 'dark' | 'system'

/**
 * The key index.html's inline script reads before the first paint, so a reload starts in the theme
 * picked here with no flash of the other one. That script stays as it is (its hash is pinned in the
 * CSP); the store reads and writes the key the same way instead.
 */
const STORAGE_KEY = 'mam:theme'

const darkScheme = window.matchMedia('(prefers-color-scheme: dark)')

interface ThemeState {
  mode: ThemeMode
  /** Applies `mode` to this tab and remembers it in this browser. */
  setMode: (mode: ThemeMode) => void
}

/**
 * MAM-16 — the theme of this tab. One store rather than state in each switch: the switches mount only
 * while the account menu (or the Profile page) is open, and System has to follow the operating
 * system on every page, menu open or not.
 */
export const useThemeStore = create<ThemeState>((set) => ({
  mode: readMode(),

  setMode: (mode) => {
    saveMode(mode)
    applyMode(mode)
    set({ mode })
  },
}))

/** A stored value, read the way index.html's script reads it: anything but light or dark is System. */
function modeOf(value: string | null): ThemeMode {
  return value === 'light' || value === 'dark' ? value : 'system'
}

function readMode(): ThemeMode {
  try {
    return modeOf(localStorage.getItem(STORAGE_KEY))
  } catch {
    return 'system' /* storage unavailable: the page was left light, so System is applied below */
  }
}

// System removes the key rather than storing a value of its own: a missing key is what index.html's
// script reads as System.
function saveMode(mode: ThemeMode) {
  try {
    if (mode === 'system') localStorage.removeItem(STORAGE_KEY)
    else localStorage.setItem(STORAGE_KEY, mode)
  } catch {
    /* storage unavailable: the choice lasts until the tab is closed */
  }
}

/** `.dark` on <html> switches every colour token (index.css). */
function applyMode(mode: ThemeMode) {
  const dark = mode === 'dark' || (mode === 'system' && darkScheme.matches)
  document.documentElement.classList.toggle('dark', dark)
}

// The page starts in the theme index.html's script applied, which is this mode in every case but
// one: when storage cannot be read, the script gives up and leaves the page light whatever the
// operating system uses, while the switch says System. Applying the mode once puts the page in step.
applyMode(useThemeStore.getState().mode)

// index.html's script reads the operating system once, before the first paint. While the theme is
// System, a change there (a schedule switching to dark at sunset) reaches the open app too.
darkScheme.addEventListener('change', () => {
  const { mode } = useThemeStore.getState()
  if (mode === 'system') applyMode(mode)
})

// A choice made in another tab of this browser: it changed the stored key, which every other tab
// hears about (a tab never hears its own writes), so all of them show the same theme. No key at all
// is another tab clearing this site's storage, which removed the theme's key with the rest.
window.addEventListener('storage', (event) => {
  if (event.key !== null && event.key !== STORAGE_KEY) return
  const mode = modeOf(event.newValue)
  applyMode(mode)
  useThemeStore.setState({ mode })
})
