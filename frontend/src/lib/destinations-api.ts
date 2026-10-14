import { api } from './api'
import type {
  ClientDestinationList,
  CreateDestinationRequest,
  Destination,
  DestinationList,
  DestinationSectionList,
  DestinationsQuery,
  UpdateDestinationRequest,
} from './types'

const BASE_URL = '/api/settings/destinations'

/**
 * MAM-11 — the download destinations: each one a category of a download client, never a folder of
 * the app's own (ADR 0010) — MAM-30: or a folder of a client that saves to folders, as it sees it. Owner-only: every call answers 403 `{ error }` for anyone else, the reads
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
   * download client integration, or — MAM-32 — a section's release source no longer exists; 409
   * `{ error }` when the name is taken for that client, the client is switched off or can't be used,
   * or it has no such category; 502 `{ error }` with the client's own sentence when it couldn't be
   * asked.
   */
  create: async (request: CreateDestinationRequest): Promise<Destination> =>
    (await api.post<Destination>(BASE_URL, request)).data,

  /**
   * `PUT /api/settings/destinations/{id}`. 204; a bare 404 when the destination is gone; otherwise
   * the failures of `create`. Only a new client or category asks the client again: a new name or the
   * default alone doesn't. MAM-32 — `sections` replaces the destination's own; left out keeps them.
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
   *
   * MAM-30 — for a client that saves to folders: the media library folders it can reach through its
   * path mappings and its own download folder, each confirmed by the client (one it can't see is listed
   * as FolderNotSeen, never hidden), plus `unplaced`, the libraries it offers no folder for.
   * `?destinationId=` (the Edit dialog's, on its own client) has the client asked about that
   * destination's stored folder too, so a mapping edit doesn't show it as missing (design D4); 400
   * `{ error }` for one that isn't an id.
   */
  clientDestinations: async (downloadClientId: string, destinationId?: string): Promise<ClientDestinationList> =>
    (await api.get<ClientDestinationList>(`${BASE_URL}/client-destinations/${downloadClientId}`, {
      params: { destinationId },
    })).data,

  /**
   * MAM-32 — `GET /api/settings/destinations/sections`: every release source's sections, each with
   * the destination it is mapped to. Every enabled source is asked live; 200 whatever they said — a
   * source that couldn't be asked carries its sentence in `error`, with the sections already mapped
   * of it. Whole, never paged.
   */
  sections: async (): Promise<DestinationSectionList> =>
    (await api.get<DestinationSectionList>(`${BASE_URL}/sections`)).data,
}
