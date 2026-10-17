import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { languageSaveSettled, languageSaveStarted, useAuthStore } from '../auth-store'
import { profileApi } from '../profile-api'
import type { CreateAccessTokenRequest, RecommendationSettings, RecommendationSettingsRequest, UiLanguage } from '../types'
import { qk } from './keys'

/**
 * MAM-33 — the signed-in user's recommendation tabs and daily search limit. The header reads it to
 * hide Recommendations when every tab is off, the page to pick its tabs, the Profile card to edit it.
 * `enabled` is false before anyone is signed in (the header renders then too).
 */
export const useRecommendationSettings = (enabled = true) =>
  useQuery({
    queryKey: qk.profile.recommendations,
    queryFn: () => profileApi.recommendationSettings(),
    enabled,
  })

/**
 * MAM-33 — saves the recommendation settings; the answer replaces the cached ones, so the header and
 * the page follow at once. Switching on a tab nobody else has on queues a computation, so the latest
 * run is asked for again: its polling starts if one is under way.
 */
export const useSaveRecommendationSettings = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: RecommendationSettingsRequest) => profileApi.saveRecommendationSettings(request),
    onSuccess: (settings) => {
      queryClient.setQueryData<RecommendationSettings>(qk.profile.recommendations, settings)
      void queryClient.invalidateQueries({ queryKey: qk.recommendations.latestRun })
    },
  })
}

/**
 * MAM-46 — saves the language the signed-in user chose (null: the browser's). Once the server has it,
 * this tab and the user's other tabs here run in it (auth-store `setLanguage`), which mounts the
 * signed-in pages afresh when the UI's language changes. One scope: choices made in quick succession
 * go out one after another, so the last one chosen is the one that stays. While one is under way, an
 * account `GET /api/auth/me` answered with is not taken (auth-store `adoptAccountLanguage`): it may be
 * from before the save.
 */
export const useSaveLanguage = () =>
  useMutation({
    mutationFn: (language: UiLanguage | null) => profileApi.saveLanguage({ language }),
    scope: { id: 'profile-language' },
    onMutate: () => languageSaveStarted(),
    onSuccess: (saved) => useAuthStore.getState().setLanguage(saved.language),
    onSettled: () => languageSaveSettled(),
  })

/** MAM-5 — the signed-in user's personal access tokens, for the Profile page's Access tokens card. */
export const useAccessTokens = () =>
  useQuery({
    queryKey: qk.profile.accessTokens,
    queryFn: () => profileApi.accessTokens(),
  })

/**
 * MAM-5 — creates a token. The answer carries the secret, so it is never written into the query cache:
 * the list is asked for again instead, and `gcTime: 0` drops the mutation (and the secret in its
 * result) from the mutation cache as soon as the form that sent it unmounts, which it does to show the
 * secret. The list's refetch is not waited for, so the secret shows at once. The owner's own count on
 * the Users page changes too.
 */
export const useCreateAccessToken = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: CreateAccessTokenRequest) => profileApi.createAccessToken(request),
    gcTime: 0,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: qk.profile.accessTokens })
      void queryClient.invalidateQueries({ queryKey: qk.settings.users.all })
    },
  })
}

/**
 * MAM-5 — revokes one of the user's tokens. It refetches the list before it resolves, so the Revoke
 * button settles onto a list without the row rather than onto the old one for a moment.
 */
export const useRevokeAccessToken = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => profileApi.revokeAccessToken(id),
    onSuccess: () => Promise.all([
      queryClient.invalidateQueries({ queryKey: qk.profile.accessTokens }),
      queryClient.invalidateQueries({ queryKey: qk.settings.users.all }),
    ]),
  })
}
