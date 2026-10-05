import { AxiosError, type AxiosAdapter, type AxiosResponse, type InternalAxiosRequestConfig } from 'axios'
import { api } from '@/lib/api'
import { recommendationsApi } from '@/lib/recommendations-api'
import { firstQueryOf, guardians, listOf, runOf } from '@/test/recommendations'

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

describe('recommendationsApi', () => {
  const originalAdapter = api.defaults.adapter

  afterEach(() => {
    api.defaults.adapter = originalAdapter
  })

  it('asks for one page of a tab with every filter and the order as query params', async () => {
    // Arrange
    const query = { tab: 'Documentaries' as const, reason: 'TopRated' as const, available: true, genreId: 99, decade: 1990, kind: 'Series' as const, sort: 'Rating' as const, page: 2, pageSize: 50 }
    const requests = respondWith(200, listOf(query, [guardians]))

    // Act
    const list = await recommendationsApi.list(query)

    // Assert
    expect(list.items).toEqual([guardians])
    expect(requests[0].method).toBe('get')
    expect(api.getUri(requests[0])).toBe(
      '/api/recommendations?tab=Documentaries&reason=TopRated&available=true&genreId=99&decade=1990&kind=Series&sort=Rating&page=2&pageSize=50')
  })

  it('leaves the filters that are not set out of the address', async () => {
    // Arrange
    const query = firstQueryOf('Movies')
    const requests = respondWith(200, listOf(query, []))

    // Act
    await recommendationsApi.list(query)

    // Assert
    expect(api.getUri(requests[0])).toBe('/api/recommendations?tab=Movies&page=1&pageSize=25')
  })

  it('reads the latest computation', async () => {
    // Arrange
    const run = runOf()
    const requests = respondWith(200, run)

    // Act
    const latest = await recommendationsApi.latestRun()

    // Assert
    expect(latest).toEqual(run)
    expect(requests[0].method).toBe('get')
    expect(requests[0].url).toBe('/api/recommendations/runs/latest')
  })

  it('reads a 204 for the latest computation as none yet', async () => {
    // Arrange
    respondWith(204, '')

    // Act
    const latest = await recommendationsApi.latestRun()

    // Assert
    expect(latest).toBeNull()
  })

  it('starts a computation with a POST and returns the queued run', async () => {
    // Arrange
    const queued = runOf({ status: 'Queued', trigger: 'Requested', startedUtc: null, finishedUtc: null })
    const requests = respondWith(202, queued)

    // Act
    const run = await recommendationsApi.startRun()

    // Assert
    expect(run).toEqual(queued)
    expect(requests[0].method).toBe('post')
    expect(requests[0].url).toBe('/api/recommendations/runs')
  })

  it('marks a work "Not interested" with a PUT of its kind and catalog id, and undoes it with a DELETE', async () => {
    // Arrange
    const requests = respondWith(204, '')

    // Act
    await recommendationsApi.dismiss('Series', '73586')
    await recommendationsApi.undismiss('Series', '73586')

    // Assert
    expect(requests.map((request) => [request.method, request.url])).toEqual([
      ['put', '/api/recommendations/dismissals/Series/73586'],
      ['delete', '/api/recommendations/dismissals/Series/73586'],
    ])
  })

  it('fetches a poster through the API client as a blob', async () => {
    // Arrange
    const poster = new Blob(['jpeg bytes'], { type: 'image/jpeg' })
    const requests = respondWith(200, poster)

    // Act
    const blob = await recommendationsApi.poster(guardians.posterUrl!)

    // Assert
    expect(blob).toBe(poster)
    expect(requests[0].url).toBe('/api/recommendations/posters/r2J02Z2OpNTctfOSN1Ydgii51I3.jpg')
    expect(requests[0].responseType).toBe('blob')
  })

  it.each([
    ['another API path', '/api/settings/integrations'],
    ['another site', 'https://image.tmdb.org/t/p/w342/poster.jpg'],
    ['a way out of the posters', '/api/recommendations/posters/../runs'],
  ])('refuses %s as a poster URL without sending the token anywhere', async (_, url) => {
    // Arrange
    const requests = respondWith(200, new Blob())

    // Act
    const attempt = recommendationsApi.poster(url)

    // Assert
    await expect(attempt).rejects.toThrow('Not a poster URL')
    expect(requests).toEqual([])
  })
})
