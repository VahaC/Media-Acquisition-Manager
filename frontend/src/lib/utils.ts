import { isAxiosError } from 'axios'
import { clsx, type ClassValue } from 'clsx'
import { i18n } from './i18n'

/**
 * Joins class names, dropping falsy ones. Plain clsx: there is no Tailwind, so there are no
 * conflicting utilities for tailwind-merge to resolve.
 */
export function cn(...inputs: ClassValue[]): string {
  return clsx(inputs)
}

/**
 * The sentence to show for a failed request.
 *
 * The API answers every failure that carries a message with `{ "error": "<sentence>" }`, so that
 * sentence wins. It stays the API's English sentence in every UI language (ADR 0001). A request
 * that never reached the API (isServerUnreachable) says so, which is more useful than axios's
 * "Network Error" or a proxy's "Bad Gateway". Anything else — an HTML error page, a bare status —
 * has nothing readable in it and gets `fallback`, which the caller words for its context
 * (`getApiErrorMessage(error) ?? t('loadFailed')`).
 */
export function getApiErrorMessage(error: unknown): string | undefined
export function getApiErrorMessage(error: unknown, fallback: string): string
export function getApiErrorMessage(error: unknown, fallback?: string): string | undefined {
  if (isAxiosError(error)) {
    const body: unknown = error.response?.data
    if (typeof body === 'object' && body !== null && 'error' in body
      && typeof body.error === 'string' && body.error.trim() !== '') {
      return body.error
    }
  }
  if (isServerUnreachable(error)) return i18n.t('errors:serverUnreachable')
  return fallback
}

/** The statuses a proxy in front of the API answers with when it can't reach it. */
const PROXY_FAILURE_STATUSES: ReadonlySet<number> = new Set([502, 503, 504])

/**
 * MAM-16 — whether a request failed without the API answering it: no response at all (the server
 * stopped, the network gone), or a 502, 503 or 504 that carries no JSON body. The API words each of
 * its own 502s and 503s in an `{ error }` body, and the readiness report's 503 is JSON too; the Vite
 * dev proxy and a reverse proxy in front of a stopped API answer with an empty or an HTML body, and
 * that is the server being unreachable as much as no answer is. The error lines and the offline
 * strip (lib/connection-store.ts) share this one test, so they never disagree.
 *
 * A cancelled request has no response either, but it is not a failure anyone should read.
 */
export function isServerUnreachable(error: unknown): boolean {
  if (!isAxiosError(error) || error.code === 'ERR_CANCELED') return false
  const { response } = error
  if (!response) return true
  return PROXY_FAILURE_STATUSES.has(response.status) && (typeof response.data !== 'object' || response.data === null)
}

/** The answers a second try of the same request gets again. */
const LASTING_STATUSES: ReadonlySet<number> = new Set([400, 403, 404])

/**
 * MAM-16 — whether asking again could bring another answer, for a Try again button. A 400 (a filter
 * or a page the server refuses), a 403 (not the owner) and a 404 (expired, someone else's, never
 * there) are the server's lasting answer to that request: a button there would only promise what a
 * second try can't give.
 */
export function retryCanHelp(error: unknown): boolean {
  const status = isAxiosError(error) ? error.response?.status : undefined
  return status === undefined || !LASTING_STATUSES.has(status)
}
