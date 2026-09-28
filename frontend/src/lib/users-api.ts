import { api } from './api'
import type { CreateUserRequest, UserList, UserSummary, UsersQuery } from './types'

const BASE_URL = '/api/settings/users'

/** Owner-only: every call answers 403 `{ error }` for anyone else, the reads included. */
export const usersApi = {
  /**
   * `GET /api/settings/users?search=&page=&pageSize=`. One page, searched (case-insensitive part of
   * the user name) and ordered by the server. 400 `{ error }` for a page below 1 or a page size
   * outside 1–100.
   */
  list: async (query: UsersQuery): Promise<UserList> =>
    (await api.get<UserList>(BASE_URL, { params: query })).data,

  /**
   * `POST /api/settings/users`. 201 with the new user. 400 `{ error }` names the user-name or
   * password rule that failed; 409 when the name is taken, whatever its letter case.
   */
  create: async (request: CreateUserRequest): Promise<UserSummary> =>
    (await api.post<UserSummary>(BASE_URL, request)).data,

  /**
   * `POST /api/settings/users/{id}/disable`. 200 with the user; their sessions end at once, not when
   * their access token expires. 409 for the installation owner; a bare 404 when the user is gone.
   */
  disable: async (id: string): Promise<UserSummary> =>
    (await api.post<UserSummary>(`${BASE_URL}/${id}/disable`)).data,

  /** `POST /api/settings/users/{id}/enable`. 200 with the user; a bare 404 when the user is gone. */
  enable: async (id: string): Promise<UserSummary> =>
    (await api.post<UserSummary>(`${BASE_URL}/${id}/enable`)).data,

  /**
   * `POST /api/settings/users/{id}/password`. 204; every session of that user ends except the
   * caller's own, so an owner resetting their own password stays signed in here. 400 `{ error }`
   * names the password rule that failed; a bare 404 when the user is gone.
   */
  resetPassword: async (id: string, password: string): Promise<void> => {
    await api.post(`${BASE_URL}/${id}/password`, { password })
  },

  /**
   * `DELETE /api/settings/users/{id}`. 204; the user's sessions go with them. 409 for the
   * installation owner; a bare 404 when the user is already gone.
   */
  remove: async (id: string): Promise<void> => {
    await api.delete(`${BASE_URL}/${id}`)
  },
}
