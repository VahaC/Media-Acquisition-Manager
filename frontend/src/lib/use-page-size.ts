import { useState } from 'react'

/** The page lengths a server-paged list offers. */
export const PAGE_SIZES: readonly number[] = [10, 25, 50, 100]
export const DEFAULT_PAGE_SIZE = 25

/**
 * The page size of one list, remembered per device under `storageKey` (`mam:<list>:pageSize`): a
 * phone and a desktop want different page lengths, and it is a preference, not part of the URL.
 *
 * MAM-7 — moved out of the MAM-4 Users page so every settings list keeps its size the same way.
 * A stored value that isn't one of `PAGE_SIZES` is ignored, and the list works without storage.
 */
export function usePageSize(storageKey: string): [number, (size: number) => void] {
  const [pageSize, setPageSize] = useState(() => readPageSize(storageKey))

  const changePageSize = (size: number) => {
    setPageSize(size)
    savePageSize(storageKey, size)
  }

  return [pageSize, changePageSize]
}

function readPageSize(storageKey: string): number {
  try {
    const stored = Number(localStorage.getItem(storageKey))
    if (PAGE_SIZES.includes(stored)) return stored
  } catch {
    /* storage unavailable: the default it is */
  }
  return DEFAULT_PAGE_SIZE
}

function savePageSize(storageKey: string, size: number) {
  try {
    localStorage.setItem(storageKey, String(size))
  } catch {
    /* storage unavailable: the choice lasts until the page is closed */
  }
}
