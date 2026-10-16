import { AxiosError, type AxiosAdapter, type AxiosResponse, type InternalAxiosRequestConfig } from 'axios'
import { api } from '@/lib/api'
import type { UserList } from '@/lib/types'
import { usersApi } from '@/lib/users-api'

/**
 * Answers every request with `status` and `body`, and keeps the requests it was sent. A replaced
 * adapter also replaces axios's own status check, so a status the request doesn't accept rejects
 * with the AxiosError axios would raise.
 */
function respondWith(status: number, body: unknown) {
  const requests: InternalAxiosRequestConfig[] = []
  const adapter: AxiosAdapter = async (config) => {
    requests.push(config)
    const response: AxiosResponse = { status, statusText: '', headers: {}, config, data: body }
    if (config.validateStatus && !config.validateStatus(status)) {
      const code = status >= 500 ? AxiosError.ERR_BAD_RESPONSE : AxiosError.ERR_BAD_REQUEST
      throw new AxiosError(`Request failed with status code ${status}`, code, config, null, response)
    }
    return response
  }
  api.defaults.adapter = adapter
  return requests
}

const olenaId = '0b6f2c1e-4a1d-4f7a-9c1e-000000000002'

describe('usersApi', () => {
  const originalAdapter = api.defaults.adapter

  afterEach(() => {
    api.defaults.adapter = originalAdapter
  })

  it('reads a page of users with each one\'s count of active access tokens', async () => {
    // Arrange
    const page: UserList = {
      items: [{
        id: olenaId,
        userName: 'olena',
        isOwner: false,
        isDisabled: false,
        createdUtc: '2026-09-10T08:00:00Z',
        lastSignInUtc: null,
        activeTokenCount: 2,
      }],
      totalCount: 1,
      page: 1,
      pageSize: 25,
    }
    const requests = respondWith(200, page)

    // Act
    const list = await usersApi.list({ search: 'ol', page: 1, pageSize: 25 })

    // Assert
    expect(list).toEqual(page)
    expect(requests[0].method).toBe('get')
    expect(requests[0].url).toBe('/api/settings/users')
    expect(requests[0].params).toEqual({ search: 'ol', page: 1, pageSize: 25 })
  })

  it('revokes every access token of a user with a DELETE under the user\'s address', async () => {
    // Arrange
    const requests = respondWith(204, '')

    // Act
    await usersApi.revokeTokens(olenaId)

    // Assert
    expect(requests).toHaveLength(1)
    expect(requests[0].method).toBe('delete')
    expect(requests[0].url).toBe(`/api/settings/users/${olenaId}/tokens`)
  })

  it('rejects with a bare 404 when the user is gone', async () => {
    // Arrange
    respondWith(404, '')

    // Act
    const failure = await usersApi.revokeTokens(olenaId).catch((e: unknown) => e)

    // Assert
    expect(failure).toBeInstanceOf(AxiosError)
    expect((failure as AxiosError).response?.status).toBe(404)
  })
})
