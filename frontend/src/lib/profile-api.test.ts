import { AxiosError, type AxiosAdapter, type AxiosResponse, type InternalAxiosRequestConfig } from 'axios'
import { api } from '@/lib/api'
import { profileApi } from '@/lib/profile-api'
import { createdToken, laptopToken } from '@/test/access-tokens'
import { allTabsOn } from '@/test/recommendations'

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

describe('profileApi', () => {
  const originalAdapter = api.defaults.adapter

  afterEach(() => {
    api.defaults.adapter = originalAdapter
  })

  it('reads the recommendation settings', async () => {
    // Arrange
    const requests = respondWith(200, allTabsOn)

    // Act
    const settings = await profileApi.recommendationSettings()

    // Assert
    expect(settings).toEqual(allTabsOn)
    expect(requests[0].method).toBe('get')
    expect(requests[0].url).toBe('/api/profile/recommendations')
  })

  it('saves the recommendation settings with a PUT of every field', async () => {
    // Arrange
    const request = { movies: true, series: false, cartoons: true, documentaries: false, dailySearchLimit: 30 }
    const requests = respondWith(200, { ...request, searchesLast24Hours: 4 })

    // Act
    const saved = await profileApi.saveRecommendationSettings(request)

    // Assert
    expect(saved).toEqual({ ...request, searchesLast24Hours: 4 })
    expect(requests[0].method).toBe('put')
    expect(requests[0].url).toBe('/api/profile/recommendations')
    expect(JSON.parse(requests[0].data as string)).toEqual(request)
  })

  it('reads the user\'s access tokens', async () => {
    // Arrange
    const requests = respondWith(200, { items: [laptopToken] })

    // Act
    const list = await profileApi.accessTokens()

    // Assert
    expect(list).toEqual({ items: [laptopToken] })
    expect(requests[0].method).toBe('get')
    expect(requests[0].url).toBe('/api/profile/tokens')
  })

  it('creates an access token with a POST of its name, access and lifetime, and returns the secret', async () => {
    // Arrange
    const request = { name: 'Claude on the NAS', access: 'ReadOnly', lifetime: 'Days90' } as const
    const requests = respondWith(201, createdToken)

    // Act
    const created = await profileApi.createAccessToken(request)

    // Assert
    expect(created).toEqual(createdToken)
    expect(requests[0].method).toBe('post')
    expect(requests[0].url).toBe('/api/profile/tokens')
    expect(JSON.parse(requests[0].data as string)).toEqual(request)
  })

  it('revokes an access token with a DELETE of its address', async () => {
    // Arrange
    const requests = respondWith(204, '')

    // Act
    await profileApi.revokeAccessToken(laptopToken.id)

    // Assert
    expect(requests[0].method).toBe('delete')
    expect(requests[0].url).toBe(`/api/profile/tokens/${laptopToken.id}`)
  })

  it('rejects with the server\'s sentence when a token is refused', async () => {
    // Arrange
    respondWith(400, { error: 'Give the token a name, such as “Claude on the laptop”.' })

    // Act
    const failure = await profileApi.createAccessToken({ name: '', access: 'ReadOnly', lifetime: 'Days30' }).catch((e: unknown) => e)

    // Assert
    expect(failure).toBeInstanceOf(AxiosError)
    expect((failure as AxiosError).response?.data).toEqual({ error: 'Give the token a name, such as “Claude on the laptop”.' })
  })
})
