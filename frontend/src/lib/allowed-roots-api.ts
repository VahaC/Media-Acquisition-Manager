import { api } from './api'
import type { AllowedRoots, UpdateAllowedRootsRequest } from './types'

const BASE_URL = '/api/settings/allowed-roots'

/**
 * MAM-9 — the media folders: the folders inside the app where it may read library files. Owner-only:
 * both calls answer 403 `{ error }` for anyone else, the read included — the paths describe the
 * owner's mounts. MAM-24 — the replacement switch and the library folders ticked for it travel with
 * them (ADR 0033).
 */
export const allowedRootsApi = {
  /**
   * `GET /api/settings/allowed-roots`. 200 with every folder, in the order the owner entered them,
   * the replacement switch, and the library folders the app knows with their ticks.
   */
  get: async (): Promise<AllowedRoots> =>
    (await api.get<AllowedRoots>(BASE_URL)).data,

  /**
   * `PUT /api/settings/allowed-roots` with the whole list, the switch and every tick, which replace
   * the stored ones in one transaction. 204; 400 `{ error }` names the first rule a path breaks
   * (blank, not a full path, listed twice, inside another one…) or the tick that isn't a library
   * folder the app knows. Saving doesn't start an inspection.
   */
  update: async (request: UpdateAllowedRootsRequest): Promise<void> => {
    await api.put(BASE_URL, request)
  },
}
