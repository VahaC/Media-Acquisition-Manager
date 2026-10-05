import { useEffect, useRef, useState, type RefObject } from 'react'

/**
 * How far outside the window an element counts as near: one window's height above and below it, so
 * what it loads is usually there by the time a scroll brings it into view.
 */
const NEAR_MARGIN = '100% 0px'

/**
 * MAM-33 — whether the element on the returned ref has come within about one screen of the window.
 * The flag only ever turns on: an element scrolled past stays "near", so what it started loading is
 * never dropped. A browser without IntersectionObserver reports every element as near at once, which
 * loads everything, as before this hook.
 *
 * For a grid of cards that each fetch something of their own (a recommendation's poster): an
 * `<img loading="lazy">` can't defer a request made through the API client, and a `data:` URL it
 * then shows has nothing left to load.
 */
export function useNearViewport<T extends Element>(): [ref: RefObject<T | null>, near: boolean] {
  const ref = useRef<T>(null)
  const [near, setNear] = useState(() => typeof IntersectionObserver === 'undefined')

  // An observer is a subscription to the browser's layout, so an effect: it watches until the
  // element comes near, then the flag's change ends it.
  useEffect(() => {
    const element = ref.current
    if (near || element === null) return
    const observer = new IntersectionObserver(
      (entries) => {
        if (entries.some((entry) => entry.isIntersecting)) setNear(true)
      },
      { rootMargin: NEAR_MARGIN },
    )
    observer.observe(element)
    return () => observer.disconnect()
  }, [near])

  return [ref, near]
}
