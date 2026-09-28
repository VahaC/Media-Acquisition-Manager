/**
 * Recognises the one failure mode that turns a routine release into a blank
 * page: the browser asking for a code-split chunk that no longer exists.
 *
 * Every route in `App.tsx` is a `React.lazy` import, and Vite emits each one as
 * its own fingerprinted file under `/assets/`. A release replaces that whole
 * directory. A tab that was open across the swap still runs the *old*
 * `index.html`, so the moment the user navigates it asks for a chunk filename
 * that the new build never produced. An app that updates its own container
 * makes this the normal case rather than an edge case.
 *
 * **The request does not 404.** `Program.cs` ends the pipeline with
 * `MapFallbackToFile("index.html")`, so an unmatched `/assets/*.js` is answered
 * with the SPA document: HTTP 200, `Content-Type: text/html`. The browser asked
 * for a module and got HTML, and `X-Content-Type-Options: nosniff` (always set
 * by `SecurityHeadersMiddleware`) stops it from guessing otherwise — so it
 * refuses to execute and throws. Detection therefore has to key off the *error*,
 * never off a status code: there is no failing status to read.
 *
 * Each engine words that refusal differently, and the wording is not part of any
 * spec, so this matches on several known substrings rather than one. A false
 * negative costs a blank page; a false positive costs one wasted reload. The
 * list is deliberately kept to phrases that only ever describe module loading.
 */

/**
 * Substrings that identify a failed module load, lowercased. Sources:
 *  - Chromium: "Failed to fetch dynamically imported module: <url>" (transport)
 *    and "Failed to load module script: Expected a JavaScript module script but
 *    the server responded with a MIME type of \"text/html\"…" (our case).
 *  - Firefox: "error loading dynamically imported module" and "…was blocked
 *    because of a disallowed MIME type (\"text/html\")".
 *  - Safari: "Importing a module script failed." / "Unable to load module".
 */
const CHUNK_ERROR_PATTERNS = [
  'dynamically imported module',
  'failed to load module script',
  'disallowed mime type',
  'importing a module script failed',
  'unable to load module',
] as const

/**
 * Deliberately *not* matched: a `SyntaxError: Unexpected token '<'`, which is
 * what an engine throws when it parses the fallback HTML as script. That only
 * happens without `nosniff`, which the app always sends, and the phrase is
 * common enough in ordinary application bugs that matching it would send real
 * crashes into a reload loop instead of showing them.
 */
export function isChunkLoadError(error: unknown): boolean {
  if (error == null) return false

  // Webpack-style name. Vite does not emit it, but test doubles and any future
  // bundler swap do, and checking a name is free.
  if (error instanceof Error && error.name === 'ChunkLoadError') return true

  const message = extractMessage(error).toLowerCase()
  if (message === '') return false
  return CHUNK_ERROR_PATTERNS.some((pattern) => message.includes(pattern))
}

/**
 * Vite wraps the real failure in the `payload` of its `vite:preloadError`
 * event; React hands us `error.cause` chains in other paths. Read whichever
 * carries the text.
 */
function extractMessage(error: unknown): string {
  if (typeof error === 'string') return error
  if (error instanceof Error) {
    const cause = error.cause
    // Only descend when the cause actually adds text — an Error with a
    // `cause: undefined` is the common shape and must not recurse forever.
    if (cause != null && cause !== error) {
      return `${error.message} ${extractMessage(cause)}`
    }
    return error.message
  }
  // `typeof null === 'object'`, so the guard has to be explicit even though the
  // only public caller already rejected null.
  if (typeof error === 'object' && error !== null && 'message' in error) {
    const { message } = error as { message: unknown }
    if (typeof message === 'string') return message
  }
  return ''
}
