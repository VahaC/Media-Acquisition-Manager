import { useAuthStore } from '@/lib/auth-store'
import type { CurrentUser, SignInResponse } from '@/lib/types'

/** The installation owner. */
export const owner: CurrentUser = { id: '0b6f2c1e-4a1d-4f7a-9c1e-000000000001', userName: 'vasyl', isOwner: true }

/** A user who is not the owner. */
export const member: CurrentUser = { id: '0b6f2c1e-4a1d-4f7a-9c1e-000000000002', userName: 'olena', isOwner: false }

/** What a sign-in, setup or refresh answers for `user`. */
export function sessionFor(user: CurrentUser, accessToken = `token-of-${user.userName}`): SignInResponse {
  return { accessToken, accessTokenExpiresUtc: '2026-09-28T12:15:00Z', user }
}

/** Signs `user` in without asking any server. */
export function signInAs(user: CurrentUser, accessToken = `token-of-${user.userName}`) {
  useAuthStore.setState({ accessToken, user, status: 'authenticated', deliberateSignOut: false })
}

/** The store as a freshly loaded tab has it: nothing asked yet. */
export function resetAuthStore() {
  useAuthStore.setState({ accessToken: null, user: null, status: 'initializing', deliberateSignOut: false })
}
