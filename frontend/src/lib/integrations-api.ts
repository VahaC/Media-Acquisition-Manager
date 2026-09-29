import { api } from './api'
import type {
  ConnectionTestResult,
  CreateIntegrationRequest,
  IntegrationAdapterTypeList,
  IntegrationList,
  IntegrationSummary,
  IntegrationsQuery,
  TestIntegrationRequest,
  UpdateIntegrationRequest,
} from './types'

const BASE_URL = '/api/settings/integrations'

/**
 * Owner-only: every call answers 403 `{ error }` for anyone else, the reads included — the list
 * names every service, address and account on the owner's network. No response ever carries a
 * secret, only `hasPassword` / `hasApiKey`.
 */
export const integrationsApi = {
  /**
   * `GET /api/settings/integrations?search=&kind=&page=&pageSize=`. One page, searched
   * (case-insensitive part of the name), filtered by kind and ordered by name on the server. 400
   * `{ error }` for a page below 1, a page size outside 1–100 or a kind that doesn't exist.
   */
  list: async (query: IntegrationsQuery): Promise<IntegrationList> =>
    (await api.get<IntegrationList>(BASE_URL, { params: query })).data,

  /**
   * `GET /api/settings/integrations/adapter-types`. 200 with every type of service this build can
   * connect to and the credentials each takes. Fixed by the build, so it is never paged.
   */
  adapterTypes: async (): Promise<IntegrationAdapterTypeList> =>
    (await api.get<IntegrationAdapterTypeList>(`${BASE_URL}/adapter-types`)).data,

  /**
   * `POST /api/settings/integrations`. 201 with the new integration. 400 `{ error }` names the rule
   * that failed (type, name, address, credentials or a path mapping); 409 when the name is taken,
   * whatever its letter case.
   */
  create: async (request: CreateIntegrationRequest): Promise<IntegrationSummary> =>
    (await api.post<IntegrationSummary>(BASE_URL, request)).data,

  /**
   * `PUT /api/settings/integrations/{id}`. 204; 400 `{ error }` names the broken rule; a bare 404
   * when the integration is gone; 409 `{ error }` when the name is taken or this build no longer
   * connects to its type.
   */
  update: async (id: string, request: UpdateIntegrationRequest): Promise<void> => {
    await api.put(`${BASE_URL}/${id}`, request)
  },

  /**
   * `DELETE /api/settings/integrations/{id}`. 204; the stored credentials and path mappings go with
   * it, and nothing changes in the service itself. A bare 404 when it is already gone.
   */
  remove: async (id: string): Promise<void> => {
    await api.delete(`${BASE_URL}/${id}`)
  },

  /**
   * `POST /api/settings/integrations/{id}/test`, with the stored settings. 200 `{ status, message }`
   * whatever the service answered; a bare 404 when the integration is gone; 409 `{ error }` when
   * this build can't connect to its type or a stored secret can't be read.
   */
  test: async (id: string): Promise<ConnectionTestResult> =>
    (await api.post<ConnectionTestResult>(`${BASE_URL}/${id}/test`)).data,

  /**
   * `POST /api/settings/integrations/test`, with an unsaved draft; nothing is stored. 200
   * `{ status, message }` whatever the service answered; 400 `{ error }` names the rule the draft
   * breaks; 404 `{ error }` when the integration being edited is gone; 409 `{ error }` as for `test`.
   */
  testDraft: async (request: TestIntegrationRequest): Promise<ConnectionTestResult> =>
    (await api.post<ConnectionTestResult>(`${BASE_URL}/test`, request)).data,
}
