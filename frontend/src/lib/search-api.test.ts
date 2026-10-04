import { AxiosError, type AxiosAdapter, type AxiosResponse, type InternalAxiosRequestConfig } from 'axios'
import { api } from '@/lib/api'
import { searchApi } from '@/lib/search-api'
import { aliensSearch, other, releasesPage, searchId, tolokaRemux } from '@/test/search'

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

describe('searchApi', () => {
  const originalAdapter = api.defaults.adapter

  afterEach(() => {
    api.defaults.adapter = originalAdapter
  })

  it('posts the search to /api/searches and answers the stored search', async () => {
    // Arrange
    const request = { text: 'Aliens', year: '1986', imdbId: 'tt0090605' }
    const requests = respondWith(201, aliensSearch)

    // Act
    const search = await searchApi.create(request)

    // Assert
    expect(search).toEqual(aliensSearch)
    expect(requests).toHaveLength(1)
    expect(requests[0].method).toBe('post')
    expect(api.getUri(requests[0])).toBe('/api/searches')
    expect(JSON.parse(requests[0].data as string)).toEqual(request)
  })

  it('reads one stored search by its id', async () => {
    // Arrange
    const requests = respondWith(200, aliensSearch)

    // Act
    const search = await searchApi.get(searchId)

    // Assert
    expect(search).toEqual(aliensSearch)
    expect(requests[0].method).toBe('get')
    expect(api.getUri(requests[0])).toBe(`/api/searches/${searchId}`)
  })

  it('asks for one page of the stored releases with the filters, sort and paging as query params', async () => {
    // Arrange
    const query = {
      source: other.id,
      language: 'ukr',
      quality: '1080',
      minSizeBytes: '4831838208',
      maxSizeBytes: '32212254720',
      sort: 'largest' as const,
      page: 2,
      pageSize: 50,
    }
    const requests = respondWith(200, releasesPage(query, [tolokaRemux], 51))

    // Act
    const page = await searchApi.releases(searchId, query)

    // Assert
    expect(page.items).toEqual([tolokaRemux])
    expect(requests[0].method).toBe('get')
    expect(api.getUri(requests[0])).toBe(
      `/api/searches/${searchId}/releases?source=${other.id}&language=ukr&quality=1080`
        + '&minSizeBytes=4831838208&maxSizeBytes=32212254720&sort=largest&page=2&pageSize=50')
  })

  it('asks for the releases of one kind with the kind as a query param', async () => {
    // Arrange
    const query = { kind: 'Tv' as const, page: 1, pageSize: 25 }
    const requests = respondWith(200, releasesPage(query, []))

    // Act
    await searchApi.releases(searchId, query)

    // Assert
    expect(api.getUri(requests[0])).toBe(`/api/searches/${searchId}/releases?kind=Tv&page=1&pageSize=25`)
  })

  it('leaves the filters that are not set out of the address', async () => {
    // Arrange
    const query = { source: undefined, language: undefined, quality: undefined, minSizeBytes: undefined, maxSizeBytes: undefined, sort: 'seeders' as const, page: 1, pageSize: 25 }
    const requests = respondWith(200, releasesPage(query, []))

    // Act
    await searchApi.releases(searchId, query)

    // Assert
    expect(api.getUri(requests[0])).toBe(`/api/searches/${searchId}/releases?sort=seeders&page=1&pageSize=25`)
  })

  it('encodes the id into one segment of the address, for the search and for its releases', async () => {
    // Arrange
    const id = '../library/works'
    const query = { sort: 'seeders' as const, page: 1, pageSize: 25 }
    const requests = respondWith(200, releasesPage(query, []))

    // Act
    await searchApi.get(id)
    await searchApi.releases(id, query)

    // Assert
    expect(requests.map((request) => api.getUri(request))).toEqual([
      '/api/searches/..%2Flibrary%2Fworks',
      '/api/searches/..%2Flibrary%2Fworks/releases?sort=seeders&page=1&pageSize=25',
    ])
  })

  it('rejects with the bare 404 of a search that expired or is someone else\'s', async () => {
    // Arrange
    respondWith(404, '')

    // Act
    const failure = await searchApi.get(searchId).catch((error: unknown) => error)

    // Assert
    expect(failure).toBeInstanceOf(AxiosError)
    expect((failure as AxiosError).response?.status).toBe(404)
  })
})
