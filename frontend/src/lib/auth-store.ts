import { isAxiosError } from 'axios'
import { create } from 'zustand'
import { refreshSession } from './api'
import { applyAccountLanguage, isUiLanguage } from './i18n'
import { qk } from './queries/keys'
import { queryClient } from './query-client'
import type { CurrentUser, CurrentUserProfile, SignInResponse, UiLanguage } from './types'

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
  /** Installs what a sign-in, setup or refresh answered, and runs the UI in the user's language. */
  setSession: (session: SignInResponse) => void
  /**
   * MAM-46 — the language the Profile has just saved: this tab runs in it at once, and the other tabs
   * of this browser, signed in as the same user, follow.
   */
  setLanguage: (language: UiLanguage | null) => Promise<void>
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
/**
 * MAM-46 — a language saved in one tab reaches the others on the same channel (or under this storage
 * key): `{ type: 'language', userId, language }`. The user id keeps a tab that has meanwhile signed in
 * as someone else from taking it.
 */
const LANGUAGE_MESSAGE_TYPE = 'language'
const LANGUAGE_STORAGE_KEY = 'mam:auth:language'

interface LanguageMessage {
  type: typeof LANGUAGE_MESSAGE_TYPE
  userId: string
  language: UiLanguage | null
}

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
 * MAM-46 — how many times the account's language has changed in this tab, or may be about to: a
 * session installed, a language followed (this tab's save, another tab's, the Profile's fresh account)
 * and a save starting. An answer the server worked out before a change can be older than it; one asked
 * before a change and answered after it is not taken (see `adoptAccountLanguage`, `sessionWithLanguageSince`).
 */
let languageChanges = 0

/** MAM-46 — the language Profile saves under way in this tab; while one is, nobody can say which language stays. */
let languageSavesInFlight = 0

/**
 * MAM-46 — the last language this tab followed and when (its `languageChanges` count), even while the
 * tab had no user yet: a refresh under way meanwhile may answer with the language from before it.
 */
let lastFollowed: { userId: string; language: UiLanguage | null; generation: number } | null = null

/**
 * MAM-46 — while something on screen must not be mounted afresh (a new token's secret, which the server
 * never shows again), a language followed from elsewhere waits: the signed-in pages mount afresh when the
 * UI's language changes (ProtectedRoute). See `holdLanguage`.
 */
let languageHolds = 0

/** The language-change count an answer compares before it is taken (see `languageChanges`). */
export function languageGeneration(): number {
  return languageChanges
}

/**
 * MAM-46 — a Profile save of the language starts (`useSaveLanguage`): an account the server answered
 * with before it is no longer the latest word, and none is taken until the save has ended.
 */
export function languageSaveStarted() {
  languageChanges += 1
  languageSavesInFlight += 1
}

/** MAM-46 — a save `languageSaveStarted` counted has ended, saved or refused. */
export function languageSaveSettled() {
  languageChanges += 1
  languageSavesInFlight = Math.max(0, languageSavesInFlight - 1)
}

/**
 * MAM-46 — takes the language `GET /api/auth/me` answered for `userId`, asked when the count was
 * `generationBefore`: a change saved on another device then reaches this tab the moment its Profile page
 * loads, rather than at its next refresh, and the Language card never shows a choice the account no
 * longer has. Not taken for another user, nor when the language has changed (or a save has started)
 * since the question went out, nor while a save is under way: the answer may be from before it.
 */
export function adoptAccountLanguage(userId: string, language: UiLanguage | null, generationBefore: number) {
  if (languageChanges !== generationBefore || languageSavesInFlight > 0) return
  const { user } = useAuthStore.getState()
  if (user?.id !== userId || user.language === language) return
  void followLanguage(userId, language)
}

/**
 * MAM-46 — `session` as a refresh asked when the count was `generationBefore` should install it: with the
 * language this tab followed meanwhile for the same user, if it did. The server may have read the user
 * before that language was saved, and installing its answer would switch the tab back until the next
 * refresh.
 */
export function sessionWithLanguageSince(generationBefore: number, session: SignInResponse): SignInResponse {
  if (!lastFollowed || lastFollowed.generation <= generationBefore || lastFollowed.userId !== session.user.id) return session
  return { ...session, user: { ...session.user, language: lastFollowed.language } }
}

/**
 * MAM-46 — keeps the UI in its language until the returned function is called: a language followed
 * meanwhile (another tab's save, a refresh, the Profile's fresh account) goes on the user at once and
 * runs the UI once the last hold is released. Taken while a new token's secret is on screen.
 */
export function holdLanguage(): () => void {
  languageHolds += 1
  let released = false
  return () => {
    if (released) return
    released = true
    languageHolds -= 1
    if (languageHolds === 0) void applyAccountLanguage(useAuthStore.getState().user?.language ?? null)
  }
}

/** Runs the UI in the account's language unless a hold keeps it (`holdLanguage` applies it on release). */
async function runInAccountLanguage(language: UiLanguage | null) {
  if (languageHolds > 0) return
  await applyAccountLanguage(language)
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
    languageChanges += 1
    set({ accessToken: session.accessToken, user: session.user, status: 'authenticated', deliberateSignOut: false })
    // MAM-46 — the account's language, which arrives with the session: until then (the sign-in page,
    // "Restoring your session…") the browser's decides.
    void runInAccountLanguage(session.user.language)
  },

  setLanguage: async (language) => {
    const { user } = get()
    if (!user) return
    await followLanguage(user.id, language)
    announceLanguage({ type: LANGUAGE_MESSAGE_TYPE, userId: user.id, language })
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
  // MAM-46 — the next person at this device signs in on a page in the browser's language, not in the
  // one the last user chose.
  void applyAccountLanguage(null)
}

/**
 * MAM-46 — puts a saved language on the signed-in user `userId`, here and in the Profile's cached
 * account, and runs the UI in it. Nothing happens for another user: the tab signed in as someone else
 * since the language was saved. A tab still restoring its session only notes it, for the refresh under
 * way to install (`sessionWithLanguageSince`).
 */
async function followLanguage(userId: string, language: UiLanguage | null) {
  languageChanges += 1
  lastFollowed = { userId, language, generation: languageChanges }
  const { user } = useAuthStore.getState()
  if (user?.id !== userId) return
  useAuthStore.setState({ user: { ...user, language } })
  queryClient.setQueryData<CurrentUserProfile>(qk.auth.me, (profile) => profile && { ...profile, language })
  await runInAccountLanguage(language)
}

function isLanguageMessage(data: unknown): data is LanguageMessage {
  if (typeof data !== 'object' || data === null) return false
  const message = data as Partial<LanguageMessage>
  return message.type === LANGUAGE_MESSAGE_TYPE
    && typeof message.userId === 'string'
    && (message.language === null || isUiLanguage(message.language))
}

function announceLanguage(message: LanguageMessage) {
  if (channel) {
    channel.postMessage(message)
    return
  }
  try {
    // A time in it, so saving the same language twice still changes the value and fires the event.
    localStorage.setItem(LANGUAGE_STORAGE_KEY, JSON.stringify({ ...message, at: Date.now() }))
  } catch {
    /* storage unavailable: the other tabs take the language at their next refresh */
  }
}

/** A language message read back from the storage fallback; null for anything else. */
function languageMessageOf(value: string | null): LanguageMessage | null {
  if (value === null) return null
  try {
    const data: unknown = JSON.parse(value)
    return isLanguageMessage(data) ? data : null
  } catch {
    return null
  }
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
// MAM-46 — a language message is another tab's Profile saving one: this tab follows it.
if (channel) {
  channel.onmessage = (event: MessageEvent) => {
    if (event.data === SIGNED_OUT_MESSAGE) signOutThisTab(true)
    else if (isLanguageMessage(event.data)) void followLanguage(event.data.userId, event.data.language)
  }
} else {
  window.addEventListener('storage', (event) => {
    if (event.key === SIGNED_OUT_STORAGE_KEY && event.newValue !== null) signOutThisTab(true)
    if (event.key === LANGUAGE_STORAGE_KEY) {
      const message = languageMessageOf(event.newValue)
      if (message) void followLanguage(message.userId, message.language)
    }
  })
}
