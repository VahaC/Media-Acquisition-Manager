import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { isAxiosError } from 'axios'
import { authApi } from '../auth-api'
import { adoptAccountLanguage, languageGeneration, useAuthStore } from '../auth-store'
import { setupApi } from '../setup-api'
import type { ChangePasswordRequest, SetupRequest, SetupStatus, SignInRequest } from '../types'
import { qk } from './keys'

/** Whether the installation still needs its owner account (first-run setup). */
export const useSetupStatus = () =>
  useQuery({
    queryKey: qk.setup.status,
    queryFn: () => setupApi.status(),
  })

/** Creates the owner account; the owner is signed in as soon as it succeeds. */
export const useCompleteSetup = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: SetupRequest) => setupApi.complete(request),
    onSuccess: (session) => {
      // Setup closes for good once the owner exists; the cached "required" answer would otherwise
      // keep sending the sign-in page back to setup until it was refetched.
      queryClient.setQueryData<SetupStatus>(qk.setup.status, { isRequired: false })
      useAuthStore.getState().setSession(session)
    },
    onError: (error) => {
      // MAM-4 — a 409 says the owner exists after all (a setup that raced this one won, or setup
      // finished elsewhere since the page loaded). Left "required", the cache would hold the visitor
      // on this form and bounce the sign-in page back to it; answered, the setup page moves on.
      if (isAxiosError(error) && error.response?.status === 409) {
        queryClient.setQueryData<SetupStatus>(qk.setup.status, { isRequired: false })
      }
    },
  })
}

/** Signs in with a user name and password. */
export const useSignIn = () =>
  useMutation({
    mutationFn: (request: SignInRequest) => authApi.signIn(request),
    onSuccess: (session) => useAuthStore.getState().setSession(session),
  })

/**
 * MAM-16 — the signed-in user's profile: when the account was created and last signed in, and when
 * this session ends. Asked on every visit to the Profile page, as each refresh moves the session's
 * end.
 *
 * MAM-46 — and the account's language, which this tab takes from the answer (auth-store
 * `adoptAccountLanguage`): a language saved on another device reaches it when its Profile page loads,
 * so the Language card shows what the account holds, not what the session began with. Taken in the
 * query function, so only an answer fresh from the server counts, never a cached one shown again; one
 * asked before a language change here (or answered while a save is under way) is not taken.
 */
export const useProfile = () =>
  useQuery({
    queryKey: qk.auth.me,
    queryFn: async () => {
      const generationBefore = languageGeneration()
      const profile = await authApi.me()
      adoptAccountLanguage(profile.id, profile.language, generationBefore)
      return profile
    },
  })

/**
 * MAM-16 — changes the signed-in user's own password. Invalidates nothing: no field of the profile
 * changes, and this session, the one kept, ends when it did before.
 */
export const useChangePassword = () =>
  useMutation({
    mutationFn: (request: ChangePasswordRequest) => authApi.changePassword(request),
  })

/**
 * Signs out: the server revokes the session and deletes the cookie, then this tab and the others
 * forget it. The tab forgets it even when the server could not be reached — the user asked to
 * leave, and a device left showing their data because the network hiccuped is the worse outcome.
 */
export const useSignOut = () =>
  useMutation({
    mutationFn: () => authApi.signOut(),
    // MAM-4 — deliberate: the next person to sign in here starts at home, not on this user's page.
    onSettled: () => useAuthStore.getState().clear({ deliberate: true }),
  })
