import { api } from './api'
import type {
  AccessTokenList,
  CreateAccessTokenRequest,
  CreatedAccessToken,
  LanguageSetting,
  RecommendationSettings,
  RecommendationSettingsRequest,
} from './types'

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

  /**
   * MAM-46 — `PUT /api/profile/language`, body `{ language }`: `"en"`, `"uk"`, or null to follow the
   * browser. 200 with the language as saved; 400 `{ error }` for a language the app isn't shown in. A
   * request made with a personal access token is refused 403 `{ error }`.
   */
  saveLanguage: async (request: LanguageSetting): Promise<LanguageSetting> =>
    (await api.put<LanguageSetting>(`${BASE_URL}/language`, request)).data,

  /**
   * MAM-5 — `GET /api/profile/tokens`. 200 with the user's personal access tokens that aren't revoked,
   * expired ones included and flagged, newest first; never a secret. A request made with a token is
   * refused 403 `{ error }`: tokens are managed in the web app only.
   */
  accessTokens: async (): Promise<AccessTokenList> =>
    (await api.get<AccessTokenList>(`${BASE_URL}/tokens`)).data,

  /**
   * MAM-5 — `POST /api/profile/tokens`. 201 with the token and its secret, which no later answer
   * carries again. 400 `{ error }` for a blank name or one over 100 characters, or a missing access or
   * lifetime.
   */
  createAccessToken: async (request: CreateAccessTokenRequest): Promise<CreatedAccessToken> =>
    (await api.post<CreatedAccessToken>(`${BASE_URL}/tokens`, request)).data,

  /**
   * MAM-5 — `DELETE /api/profile/tokens/{id}`. 204; the token stops working at once. A bare 404 for a
   * token already revoked or another user's.
   */
  revokeAccessToken: async (id: string): Promise<void> => {
    await api.delete(`${BASE_URL}/tokens/${id}`)
  },
}
