import { useState } from 'react'

/** The part of a `useQuery` result the hook reads; pass the whole result. */
interface LoadQuery<TError> {
  data: unknown
  error: TError | null
  /** When the query last failed (ms since the epoch); 0 for a query that never did. */
  errorUpdatedAt: number
  isPlaceholderData: boolean
  refetch: () => Promise<unknown>
}

/** A failure kept on screen, and when its query reported it. */
interface KeptError<TError> {
  error: TError
  at: number
}

interface LoadRetry<TError> {
  /**
   * The failed first load to show in place of the content (an ErrorState); null while the query
   * has data of its own. A failed refresh keeps its data, so it is the page's banner over that data
   * that tells of it, from the query's own `error`.
   */
  error: TError | null
  /**
   * True only while a retry started by `retry` for the failure on screen is pending: Try again
   * reads "Retrying…".
   */
  retrying: boolean
  /** Asks again; for Try again. */
  retry: () => void
}

/**
 * MAM-16 — keeps a query's failed first load on screen while it asks again, and tells Try again
 * apart from every other refetch.
 *
 * Each time a query that has no data fetches, TanStack resets it to pending with no error. On the
 * query alone, a page swaps its ErrorState for "Loading…" the moment Try again is pressed, so the
 * button never reads "Retrying…", and a query polled while in error (a job's files every 2 s, the
 * readiness report every minute) or refetched on reconnect blinks the same way. Here the last
 * failure stays until the query has data of its own: an answer clears it, and a new failure
 * replaces it.
 *
 * That only holds if the page checks `load.error` before `isLoading`, which stays true through the
 * retry: `load.error ? <ErrorState onRetry={load.retry} retrying={load.retrying}> : isLoading ? …`.
 * The banner over a failed refresh stays on `query.error && query.data`.
 *
 * `retrying` follows the click alone. Taken from `isFetching`, it would also read "Retrying…" on
 * every poll, on a button nobody pressed.
 */
export function useLoadRetry<TError>(query: LoadQuery<TError>): LoadRetry<TError> {
  const [kept, setKept] = useState<KeptError<TError> | null>(null)
  // The failure a pending Try again was pressed for, by its errorUpdatedAt: a page that moves on
  // mid-retry (another job's files) and fails there does not inherit "Retrying…".
  const [retryingFor, setRetryingFor] = useState<number | null>(null)

  // Kept during render rather than in an effect, so the render that starts the refetch already has
  // it, and "Loading…" never shows for a frame in between.
  if (query.error !== null && kept?.error !== query.error) {
    setKept({ error: query.error, at: query.errorUpdatedAt })
  }

  // Another page's rows standing in (keepPreviousData) are not this query's data: the failure of
  // the page asked for stays on screen while it is asked again.
  const hasData = query.data !== undefined && !query.isPlaceholderData
  // The reset to pending leaves errorUpdatedAt as it was, and a query that never failed reports 0:
  // a kept failure belongs to the query that reported it, not to the next one the page asks for
  // (another job's files) while that one loads for the first time.
  const keptError = kept !== null && kept.at === query.errorUpdatedAt ? kept.error : null
  const error = hasData ? null : query.error ?? keptError

  const retry = async () => {
    const at = query.errorUpdatedAt
    setRetryingFor(at)
    try {
      // refetch resolves once the query holds the answer, so the render that drops "Retrying…"
      // already shows the data or the new failure.
      await query.refetch()
    } finally {
      // Only its own click: a retry pressed since, for another failure, is still pending.
      setRetryingFor((current) => (current === at ? null : current))
    }
  }

  return { error, retrying: retryingFor === query.errorUpdatedAt, retry: () => void retry() }
}
