import { useSyncExternalStore } from 'react'

/**
 * Narrower than this, the app's lists become stacked cards and the header keeps only its icons. The
 * stylesheets' `@media (max-width: 48rem)` rules use the same width, so markup chosen here and styles
 * chosen there change together.
 */
const NARROW_SCREEN = '(max-width: 48rem)'

function subscribe(listener: () => void) {
  const query = window.matchMedia(NARROW_SCREEN)
  query.addEventListener('change', listener)
  return () => query.removeEventListener('change', listener)
}

const isNarrow = () => window.matchMedia(NARROW_SCREEN).matches

/**
 * MAM-16 — whether the window is narrower than 48rem, kept up to date as it is resized or a phone is
 * turned. For what a component renders there, not how it looks: that is a stylesheet's @media rule.
 */
export function useNarrowScreen(): boolean {
  return useSyncExternalStore(subscribe, isNarrow)
}
