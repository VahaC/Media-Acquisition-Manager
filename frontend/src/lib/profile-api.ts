import { api } from './api'
import type { RecommendationSettings, RecommendationSettingsRequest } from './types'

const BASE_URL = '/api/profile'

/**
 * MAM-33 — the signed-in user's own preferences, under `/api/profile` rather than `/api/auth`, which
 * is the refresh cookie's path. Every signed-in user reads and saves their own; nobody else's.
 */
export const profileApi = {
  /**
   * `GET /api/profile/recommendations`. 200 with the user's tabs, daily search limit and the searches
   * run for them in the last 24 hours; a user who never saved gets the defaults (every tab on, 60).
   */
  recommendationSettings: async (): Promise<RecommendationSettings> =>
    (await api.get<RecommendationSettings>(`${BASE_URL}/recommendations`)).data,

  /**
   * `PUT /api/profile/recommendations`. 200 with the settings as saved. 400 `{ error }` "The daily
   * search limit is a whole number from 0 to 500.", or a sentence naming a missing field. Switching on
   * a tab no other user has on queues a computation of the lists.
   */
  saveRecommendationSettings: async (request: RecommendationSettingsRequest): Promise<RecommendationSettings> =>
    (await api.put<RecommendationSettings>(`${BASE_URL}/recommendations`, request)).data,
}
