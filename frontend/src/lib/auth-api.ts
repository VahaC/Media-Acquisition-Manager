import { api } from './api'
import type { ChangePasswordRequest, CurrentUserProfile, SignInRequest, SignInResponse } from './types'

export const authApi = {
  /**
   * `POST /api/auth/sign-in`. 200 with an access token and the user; the refresh token arrives as
   * the HttpOnly `mam_refresh` cookie. Every refusal — unknown user, wrong password, disabled
   * account, a blank field — is the same 401 `{ error }`, so the page shows that one sentence and
   * learns nothing about which part was wrong. 429 when this address has tried too often in the
   * last minute.
   */
  signIn: async (request: SignInRequest): Promise<SignInResponse> =>
    (await api.post<SignInResponse>('/api/auth/sign-in', request)).data,

  /**
   * `POST /api/auth/refresh`, authenticated by the cookie alone. 200 rotates the cookie and returns
   * a fresh access token. 401: the session has ended (signed out, revoked, expired, the user
   * disabled or deleted) and the server has deleted the cookie. 409: another tab rotated this very
   * cookie a moment ago; the browser holds the new cookie by the time a retry goes out.
   *
   * Components never call this: `refreshSession` in api.ts does, single-flight across tabs.
   */
  refresh: async (): Promise<SignInResponse> =>
    (await api.post<SignInResponse>('/api/auth/refresh')).data,

  /**
   * `POST /api/auth/sign-out`. 204: the cookie's session is revoked (if there was one) and the
   * cookie deleted. Needs no access token, so an expired one can't keep anyone signed in.
   */
  signOut: async (): Promise<void> => {
    await api.post('/api/auth/sign-out')
  },

  /**
   * `GET /api/auth/me`. 200 with the signed-in user, when the account was created and last signed
   * in, and when this session ends; 401 without a live session. MAM-16 — the Profile page's data.
   */
  me: async (): Promise<CurrentUserProfile> => (await api.get<CurrentUserProfile>('/api/auth/me')).data,

  /**
   * MAM-16 — `POST /api/auth/password`, the signed-in user's own password. 204: changed, every other
   * session of the user has ended and this one stays (ADR 0016). 400 `{ error }`: a blank current
   * password, a new one that breaks the rule, or a wrong current password — never a 401, which
   * would read as an expired token. 429 after ten attempts in a minute by this user.
   */
  changePassword: async (request: ChangePasswordRequest): Promise<void> => {
    await api.post('/api/auth/password', request)
  },
}
