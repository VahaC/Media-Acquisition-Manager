import { isAxiosError } from 'axios'
import { create } from 'zustand'
import { refreshSession } from './api'
import { queryClient } from './query-client'
import type { CurrentUser, SignInResponse } from './types'

/**
 * - `initializing`: nothing asked yet; the refresh cookie may still hold a session.
 * - `authenticated`: an access token and its user are here.
 * - `anonymous`: no session; the sign-in page is the way in.
 * - `unavailable`: the server could not be asked, so whether a session exists is unknown.
 */
export type AuthStatus = 'initializing' | 'authenticated' | 'anonymous' | 'unavailable'

interface AuthState {
  accessToken: string | null
  user: CurrentUser | null
  status: AuthStatus
  /**
   * MAM-4 — the user signed out on purpose, in this tab or another one, rather than the session
   * ending by itself. The next person to sign in on this device then starts at home instead of at
   * the page the last one was on, which was theirs and may be owner-only. A new session resets it.
   */
  deliberateSignOut: boolean
  /** Installs what a sign-in, setup or refresh answered. */
  setSession: (session: SignInResponse) => void
  /**
   * Signs this tab out and tells the other tabs to do the same. `deliberate` when the user asked to
   * sign out; left out when the server ended the session.
   */
  clear: (options?: { deliberate?: boolean }) => void
  /** Asks the server whether the refresh cookie still holds a session. */
  initializeSession: () => Promise<void>
}

// MAM-4 — how a sign-out reaches the other tabs of this browser. They share the refresh cookie, so
// once one tab has signed out the others' sessions are gone too; without a message they would keep
// showing data until their access token expired, up to 15 minutes later.
const CHANNEL_NAME = 'mam-auth'
const SIGNED_OUT_MESSAGE = 'signed-out'
/** The storage-event fallback for browsers without BroadcastChannel. */
const SIGNED_OUT_STORAGE_KEY = 'mam:auth:signed-out'

const channel = typeof BroadcastChannel === 'function' ? new BroadcastChannel(CHANNEL_NAME) : null

/**
 * MAM-4 — how many times this tab has been signed out, by itself or by another tab. A refresh reads
 * it before it asks the server; if it has changed by the time the answer arrives, the user left
 * meanwhile, and installing that answer would sign them straight back in.
 */
let signOuts = 0

/** The sign-out count a refresh compares before installing its answer (see `signOuts`). */
export function signOutGeneration(): number {
  return signOuts
}

/**
 * The session in this tab. Deliberately not persisted: the access token lives in memory only, and
 * the refresh token is an HttpOnly cookie no script can read, so a reload starts at `initializing`
 * and asks the server again.
 */
export const useAuthStore = create<AuthState>((set, get) => ({
  accessToken: null,
  user: null,
  status: 'initializing',
  deliberateSignOut: false,

  setSession: (session) => {
    const previous = get().user
    // A different account in the same tab: nothing cached for the last one may be shown to this one.
    if (previous && previous.id !== session.user.id) queryClient.removeQueries()
    set({ accessToken: session.accessToken, user: session.user, status: 'authenticated', deliberateSignOut: false })
  },

  clear: (options) => {
    signOutThisTab(options?.deliberate === true)
    announceSignOut()
  },

  initializeSession: async () => {
    try {
      await refreshSession(null)
    } catch (error: unknown) {
      // A 401 has already made this tab anonymous. So has a sign-out that arrived meanwhile (MAM-4 —
      // the refresh then rejects without installing its answer); calling that "unavailable" would
      // put a signed-out tab back to asking the server. Anything else leaves the question open.
      const sessionEnded = isAxiosError(error) && error.response?.status === 401
      const { status } = get()
      if (!sessionEnded && status !== 'authenticated' && status !== 'anonymous') set({ status: 'unavailable' })
    }
  },
}))

/** Forgets the session and everything cached under it, in this tab only. */
function signOutThisTab(deliberate: boolean) {
  signOuts += 1
  queryClient.clear()
  useAuthStore.setState({ accessToken: null, user: null, status: 'anonymous', deliberateSignOut: deliberate })
}

function announceSignOut() {
  if (channel) {
    channel.postMessage(SIGNED_OUT_MESSAGE)
    return
  }
  try {
    // A new value every time: the storage event fires only when the value changes.
    localStorage.setItem(SIGNED_OUT_STORAGE_KEY, String(Date.now()))
  } catch {
    /* storage unavailable: the other tabs find out at their next refresh */
  }
}

// A tab never receives its own message or storage event, so a received one is always another tab.
// MAM-4 — counted as a deliberate sign-out: the message usually means the person at this device
// chose Sign out in that tab. The same message also comes from a tab whose refresh found the
// session ended, and then this tab starts the next sign-in at home too.
if (channel) {
  channel.onmessage = (event: MessageEvent) => {
    if (event.data === SIGNED_OUT_MESSAGE) signOutThisTab(true)
  }
} else {
  window.addEventListener('storage', (event) => {
    if (event.key === SIGNED_OUT_STORAGE_KEY && event.newValue !== null) signOutThisTab(true)
  })
}
