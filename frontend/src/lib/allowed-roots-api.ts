import { api } from './api'
import type { AllowedRoots } from './types'

const BASE_URL = '/api/settings/allowed-roots'

/**
 * MAM-9 — the media folders: the folders inside the app where it may read library files. Owner-only:
 * both calls answer 403 `{ error }` for anyone else, the read included — the paths describe the
 * owner's mounts.
 */
export const allowedRootsApi = {
  /** `GET /api/settings/allowed-roots`. 200 with every folder, in the order the owner entered them. */
  get: async (): Promise<AllowedRoots> =>
    (await api.get<AllowedRoots>(BASE_URL)).data,

  /**
   * `PUT /api/settings/allowed-roots` with the whole list, which replaces the stored one. 204; 400
   * `{ error }` names the first rule a path breaks (blank, not a full path, listed twice, inside
   * another one…). Saving doesn't start an inspection.
   */
  update: async (paths: string[]): Promise<void> => {
    await api.put(BASE_URL, { paths })
  },
}
