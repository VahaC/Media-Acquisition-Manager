import { api } from './api'
import type { SetupRequest, SetupStatus, SignInResponse } from './types'

export const setupApi = {
  /** `GET /api/setup`, anonymous. 200 `{ isRequired }` — true until the installation owner exists. */
  status: async (): Promise<SetupStatus> => (await api.get<SetupStatus>('/api/setup')).data,

  /**
   * `POST /api/setup`, anonymous: creates the installation owner and signs them in at once (200,
   * the same body and cookie as a sign-in). 400 `{ error }` names the user-name or password rule
   * that failed. 409 once an owner exists — including when a second setup raced this one and won.
   * 429 under the sign-in rate limit.
   */
  complete: async (request: SetupRequest): Promise<SignInResponse> =>
    (await api.post<SignInResponse>('/api/setup', request)).data,
}
