import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { profileApi } from '../profile-api'
import type { RecommendationSettings, RecommendationSettingsRequest } from '../types'
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
