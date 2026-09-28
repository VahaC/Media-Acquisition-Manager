import { useEffect, useState } from 'react'

/**
 * Debounces a typed value — used for every server-side search box, which would otherwise
 * spend a request per keystroke (often against an upstream rate limit).
 */
export function useDebounced<T>(value: T, delayMs: number): T {
  const [debounced, setDebounced] = useState(value)

  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), delayMs)
    return () => clearTimeout(timer)
  }, [value, delayMs])

  return debounced
}
