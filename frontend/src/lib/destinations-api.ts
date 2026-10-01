import { api } from './api'
import type {
  ClientDestinationList,
  CreateDestinationRequest,
  Destination,
  DestinationList,
  DestinationsQuery,
  UpdateDestinationRequest,
} from './types'

const BASE_URL = '/api/settings/destinations'

/**
 * MAM-11 — the download destinations: each one a category of a download client, never a folder of
 * the app's own (ADR 0010). Owner-only: every call answers 403 `{ error }` for anyone else, the reads
 * included — a row quotes the client's folders and the owner's path mappings.
 */
export const destinationsApi = {
  /**
   * `GET /api/settings/destinations?search=&page=&pageSize=`. One page, searched (case-insensitive
   * part of the name) and ordered by the server, each row with its live check: every client on the
   * page is asked once. 400 `{ error }` for a page below 1 or a page size outside 1–100.
   */
  list: async (query: DestinationsQuery): Promise<DestinationList> =>
    (await api.get<DestinationList>(BASE_URL, { params: query })).data,

  /**
   * `POST /api/settings/destinations`. 201 with the new destination, once the client has confirmed
   * the category. 400 `{ error }` names the rule that failed; 404 `{ error }` when the client is not a
   * download client integration; 409 `{ error }` when the name is taken for that client, the client
   * is switched off or can't be used, or it has no such category; 502 `{ error }` with the client's
   * own sentence when it couldn't be asked.
   */
  create: async (request: CreateDestinationRequest): Promise<Destination> =>
    (await api.post<Destination>(BASE_URL, request)).data,

  /**
   * `PUT /api/settings/destinations/{id}`. 204; a bare 404 when the destination is gone; otherwise
   * the failures of `create`. Only a new client or category asks the client again: a new name or the
   * default alone doesn't.
   */
  update: async (id: string, request: UpdateDestinationRequest): Promise<void> => {
    await api.put(`${BASE_URL}/${id}`, request)
  },

  /**
   * `DELETE /api/settings/destinations/{id}`. 204; nothing changes in the client, its category
   * included. Removing the default leaves none until the owner picks another. A bare 404 when it is
   * already gone.
   */
  remove: async (id: string): Promise<void> => {
    await api.delete(`${BASE_URL}/${id}`)
  },

  /**
   * `GET /api/settings/destinations/client-destinations/{downloadClientId}`: the categories the
   * client offers right now, each with its check. 200 whatever the client said — `error` carries the
   * sentence when it couldn't be asked. A bare 404 when the id is not a download client integration.
   */
  clientDestinations: async (downloadClientId: string): Promise<ClientDestinationList> =>
    (await api.get<ClientDestinationList>(`${BASE_URL}/client-destinations/${downloadClientId}`)).data,
}
