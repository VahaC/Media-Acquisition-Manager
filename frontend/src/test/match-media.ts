// A window.matchMedia a test controls, in place of the setup's stub that never matches. It answers
// one question, whether the window is narrower than 48rem (useNarrowScreen, and the stylesheets'
// phone rules), and a resize tells every listener, as a phone turning would. The test's
// vi.restoreAllMocks() puts the setup's stub back.

/** A window `narrow` or wider than 48rem, which the test can resize. */
export function windowIs(narrow: boolean) {
  const listeners = new Set<() => void>()
  const state = { narrow }
  const matchMedia = vi.spyOn(window, 'matchMedia').mockImplementation((query: string) => ({
    get matches() {
      return state.narrow && query === '(max-width: 48rem)'
    },
    media: query,
    onchange: null,
    addEventListener: (_type: string, listener: () => void) => listeners.add(listener),
    removeEventListener: (_type: string, listener: () => void) => listeners.delete(listener),
    addListener() {},
    removeListener() {},
    dispatchEvent: () => false,
  }) as unknown as MediaQueryList)
  return {
    matchMedia,
    listeners,
    resize(nowNarrow: boolean) {
      state.narrow = nowNarrow
      listeners.forEach((listener) => listener())
    },
  }
}
