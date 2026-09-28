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
 * that got no response at all never reached the API, and saying so is more useful than axios's
 * "Network Error". Anything else — a proxy's HTML error page, a bare status — has nothing readable
 * in it and gets `fallback`, which the caller words for its context
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
    // A cancelled request has no response either, but it is not a failure anyone should read.
    if (!error.response && error.code !== 'ERR_CANCELED') {
      return i18n.t('errors:serverUnreachable')
    }
  }
  return fallback
}
