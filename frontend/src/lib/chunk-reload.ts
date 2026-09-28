/**
 * Decides whether a failed chunk load may be fixed by reloading the page, and
 * makes sure we only ever try that a bounded number of times.
 *
 * Reloading is the correct fix for the common case: the tab is holding a stale
 * `index.html`, and a fresh document lists the chunk filenames that actually
 * exist. It is the *wrong* fix for a genuinely broken build: if the new
 * `index.html` also references a missing chunk, reloading fixes nothing and the
 * page would refresh forever, burning CPU and hiding the failure. The attempt
 * budget below turns that into a visible error after the first try.
 *
 * (If the app can update its own container, skip the reload while that update
 * owns the page — the update flow reloads once, deliberately.)
 *
 * State lives in `sessionStorage` because it describes *this tab's* document
 * lifetime: it must survive the reload it triggers (a module-level variable
 * would not), and it must not leak to other tabs or outlive the tab
 * (`localStorage` would do both).
 */

const STORAGE_KEY = 'mam:chunkReload'

/**
 * One automatic reload is enough to pick up a fresh `index.html`. A second
 * failure means the reload did not help, so we stop and show the error instead
 * of refreshing on a loop.
 */
const MAX_ATTEMPTS = 1

/**
 * How long an attempt stays on the record. A tab left open for days can
 * legitimately meet two *different* releases; once the window has passed, the
 * next stale chunk is a new incident and earns its own reload.
 *
 * Expiry is the *only* way the record clears. Wiping it when the app mounts
 * successfully would look tidier and would reintroduce the exact loop this file
 * exists to prevent: a reload into a still-broken build mounts the shell fine,
 * clears the budget, hits the same missing chunk, and reloads again forever.
 */
const ATTEMPT_WINDOW_MS = 60_000

interface ReloadRecord {
  count: number
  firstAt: number
}

/** Why {@link recoverFromChunkError} did not reload — drives what the user sees. */
export type ChunkRecoveryOutcome =
  /** A reload was started; the document is on its way out. */
  | 'reloading'
  /** Already tried within the window and it did not help — show the error. */
  | 'exhausted'

/**
 * Reloads the page to pick up the current build, unless that would be futile.
 * Returns what it decided so the caller can render the matching message.
 */
export function recoverFromChunkError(): ChunkRecoveryOutcome {
  const record = readRecord()
  const now = Date.now()
  const withinWindow = record !== null && now - record.firstAt < ATTEMPT_WINDOW_MS
  const count = withinWindow ? record.count : 0

  if (count >= MAX_ATTEMPTS) return 'exhausted'

  writeRecord({ count: count + 1, firstAt: withinWindow ? record.firstAt : now })
  // `reload()` re-requests the document, and with no service worker in this app
  // nothing sits between the tab and the server: the answer is the running
  // build's index.html. A service worker added later must keep serving
  // navigations network-first, or this would reload into the cached stale copy.
  window.location.reload()
  return 'reloading'
}

function readRecord(): ReloadRecord | null {
  try {
    const raw = sessionStorage.getItem(STORAGE_KEY)
    if (!raw) return null
    const parsed = JSON.parse(raw) as Partial<ReloadRecord>
    if (typeof parsed.count !== 'number' || typeof parsed.firstAt !== 'number') return null
    return { count: parsed.count, firstAt: parsed.firstAt }
  } catch {
    // Unreadable or hand-edited: treat as no attempt on record. Worst case we
    // allow one more reload than intended, which is the safe direction.
    return null
  }
}

function writeRecord(record: ReloadRecord): void {
  try {
    sessionStorage.setItem(STORAGE_KEY, JSON.stringify(record))
  } catch {
    /* private mode / quota — the budget degrades to "no memory", handled above */
  }
}
