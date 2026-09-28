import axios, { isAxiosError, type InternalAxiosRequestConfig } from 'axios'
// MAM-4 — api.ts, auth-api.ts and auth-store.ts import each other: the client reads the token from
// the store, the store refreshes through the client. That is safe because none of them touches
// another's exports while it loads, only later inside functions.
import { authApi } from './auth-api'
import { signOutGeneration, useAuthStore } from './auth-store'

/**
 * The one HTTP client every `<domain>-api.ts` module goes through. Same origin in every setup:
 * the API serves the SPA in production and Vite proxies `/api` in development.
 */
export const api = axios.create({ baseURL: '/' })

/**
 * MAM-4 — the name of the Web Lock every tab of this origin takes around a refresh. The refresh
 * token rotates on every use, so two tabs refreshing with the same cookie at once would make the
 * server answer the slower one with 409; queued behind the lock, the second tab sends the new cookie.
 */
const REFRESH_LOCK_NAME = 'mam-auth-refresh'

/**
 * MAM-4 — the pauses before retrying a refresh answered with 409. That 409 means another tab (or a
 * browser without Web Locks) rotated this cookie a moment ago and its answer, carrying the new
 * cookie, is still on the way. Three short pauses cover a slow answer; a longer wait would only
 * delay the page the user is looking at.
 */
const CONFLICT_RETRY_DELAYS_MS = [0, 100, 250] as const

/**
 * Sign-in, refresh, sign-out and setup answer 401 for their own reasons (a wrong password, an ended
 * session). Refreshing on those would loop, or turn a refused password into "session ended".
 */
const SESSION_REQUEST = /^\/?api\/(?:auth\/|setup(?:[/?#]|$))/

/** A request config that remembers it has already been retried after a refresh. */
type RetriableRequestConfig = InternalAxiosRequestConfig & { mamRetriedAfterRefresh?: boolean }

api.interceptors.request.use((config) => {
  const { accessToken } = useAuthStore.getState()
  if (accessToken) config.headers.set('Authorization', `Bearer ${accessToken}`)
  return config
})

// A 401 on an ordinary request means the access token expired (or its session ended): refresh
// once, then send the request once more with the new token. A second 401 goes to the caller.
api.interceptors.response.use(undefined, async (error: unknown) => {
  if (!isAxiosError(error) || error.response?.status !== 401 || !error.config) throw error
  const config: RetriableRequestConfig = error.config
  if (config.mamRetriedAfterRefresh || SESSION_REQUEST.test(config.url ?? '')) throw error

  await refreshSession(tokenSentWith(config))
  config.mamRetriedAfterRefresh = true
  // The request interceptor puts the new token on it.
  return api.request(config)
})

let refreshInFlight: Promise<string> | null = null

/**
 * Gets a fresh access token and installs it in the auth store; resolves with it.
 *
 * Single-flight: every caller in this tab shares the refresh already under way, and across tabs the
 * refresh runs under the `mam-auth-refresh` Web Lock. Inside the lock, a token newer than
 * `tokenSent` (the one the failed request carried) means another request refreshed meanwhile, and
 * that token is used instead of refreshing again. A 409 is retried after 0, 100 and 250 ms.
 *
 * A 401 means the session has ended for the whole browser (the server has deleted the cookie every
 * tab shares), so a signed-in tab clears it and tells the other tabs. Anything else — the server
 * unreachable, a 5xx, a 409 that outlasted the retries — rejects and leaves the session alone: the
 * cookie is still good, and signing the user out because the server restarted would throw away the
 * page they were on. An answer that arrives after this tab signed out rejects too, uninstalled.
 */
export function refreshSession(tokenSent: string | null): Promise<string> {
  refreshInFlight ??= withRefreshLock(() => refreshUnlessRefreshed(tokenSent)).finally(() => {
    refreshInFlight = null
  })
  return refreshInFlight
}

async function withRefreshLock<T>(task: () => Promise<T>): Promise<T> {
  // Web Locks exist only in a secure context (HTTPS or localhost). A plain-HTTP LAN install has
  // none; there the tabs can collide, and the 409 retries below settle the collision.
  const locks: LockManager | undefined = navigator.locks
  return locks ? await locks.request(REFRESH_LOCK_NAME, task) : await task()
}

async function refreshUnlessRefreshed(tokenSent: string | null): Promise<string> {
  const { accessToken } = useAuthStore.getState()
  if (accessToken !== null && accessToken !== tokenSent) return accessToken

  const signOutsBefore = signOutGeneration()
  try {
    const session = await refreshWithConflictRetries()
    // MAM-4 — the user signed out, here or in another tab, while this refresh was on the way.
    // Installing its answer would sign them straight back in, and the request waiting on it would
    // go out again under a session they have just left; rejecting drops both.
    if (signOutGeneration() !== signOutsBefore) throw new Error('Signed out while the session was being refreshed.')
    useAuthStore.getState().setSession(session)
    return session.accessToken
  } catch (error: unknown) {
    if (isAxiosError(error) && error.response?.status === 401) {
      const { status, clear } = useAuthStore.getState()
      // A tab that had a session announces its end to the others; a tab that was only asking
      // whether it has one (a fresh load) just learns that it hasn't.
      if (status === 'authenticated') clear()
      else useAuthStore.setState({ status: 'anonymous' })
    }
    throw error
  }
}

async function refreshWithConflictRetries() {
  for (const delay of CONFLICT_RETRY_DELAYS_MS) {
    try {
      return await authApi.refresh()
    } catch (error: unknown) {
      if (!isAxiosError(error) || error.response?.status !== 409) throw error
      await new Promise((resolve) => setTimeout(resolve, delay))
    }
  }
  return authApi.refresh()
}

/** The access token a request went out with, read back from its Authorization header. */
function tokenSentWith(config: InternalAxiosRequestConfig): string | null {
  const header = config.headers.get('Authorization')
  return typeof header === 'string' && header.startsWith('Bearer ') ? header.slice('Bearer '.length) : null
}
