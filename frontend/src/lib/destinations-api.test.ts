import { AxiosError, type AxiosAdapter, type AxiosResponse, type InternalAxiosRequestConfig } from 'axios'
import { api } from '@/lib/api'
import { destinationsApi } from '@/lib/destinations-api'
import { destinationsPage, movies, sampleCategories, sectionsOf, tolokaSections } from '@/test/destinations'
import { localQbittorrent } from '@/test/integrations'

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

describe('destinationsApi', () => {
  const originalAdapter = api.defaults.adapter

  afterEach(() => {
    api.defaults.adapter = originalAdapter
  })

  it('asks for one page of destinations with the search and paging as query params', async () => {
    // Arrange
    const query = { search: 'фільми', page: 2, pageSize: 50 }
    const requests = respondWith(200, destinationsPage(query, [movies], 51))

    // Act
    const page = await destinationsApi.list(query)

    // Assert
    expect(page.items).toEqual([movies])
    expect(requests).toHaveLength(1)
    expect(requests[0].method).toBe('get')
    expect(api.getUri(requests[0])).toBe(
      '/api/settings/destinations?search=%D1%84%D1%96%D0%BB%D1%8C%D0%BC%D0%B8&page=2&pageSize=50')
  })

  it('leaves a search that is not set out of the address', async () => {
    // Arrange
    const query = { search: undefined, page: 1, pageSize: 25 }
    const requests = respondWith(200, destinationsPage(query, []))

    // Act
    await destinationsApi.list(query)

    // Assert
    expect(api.getUri(requests[0])).toBe('/api/settings/destinations?page=1&pageSize=25')
  })

  it('adds a destination and answers the stored one (201)', async () => {
    // Arrange
    const request = { name: 'Movies', downloadClientId: localQbittorrent.id, clientDestinationKey: 'Movies', isDefault: true }
    const requests = respondWith(201, movies)

    // Act
    const created = await destinationsApi.create(request)

    // Assert
    expect(created).toEqual(movies)
    expect(requests[0].method).toBe('post')
    expect(api.getUri(requests[0])).toBe('/api/settings/destinations')
    expect(JSON.parse(requests[0].data as string)).toEqual(request)
  })

  it('saves a destination by its id (204)', async () => {
    // Arrange
    const request = { name: 'Films', downloadClientId: localQbittorrent.id, clientDestinationKey: 'Movies', isDefault: false }
    const requests = respondWith(204, '')

    // Act
    await destinationsApi.update(movies.id, request)

    // Assert
    expect(requests[0].method).toBe('put')
    expect(api.getUri(requests[0])).toBe(`/api/settings/destinations/${movies.id}`)
    expect(JSON.parse(requests[0].data as string)).toEqual(request)
  })

  it('removes a destination by its id (204)', async () => {
    // Arrange
    const requests = respondWith(204, '')

    // Act
    await destinationsApi.remove(movies.id)

    // Assert
    expect(requests).toHaveLength(1)
    expect(requests[0].method).toBe('delete')
    expect(api.getUri(requests[0])).toBe(`/api/settings/destinations/${movies.id}`)
  })

  it('adds a destination with the tracker sections it maps, as the sections list gave them', async () => {
    // Arrange
    const request = {
      name: 'Cartoons',
      downloadClientId: localQbittorrent.id,
      clientDestinationKey: 'Cartoons',
      isDefault: false,
      sections: [{ releaseSourceId: tolokaSections.id, categoryIds: ['100044', '100125', '100195'], name: 'Мультсеріали', isSourceSpecific: true }],
    }
    const requests = respondWith(201, movies)

    // Act
    await destinationsApi.create(request)

    // Assert
    expect(JSON.parse(requests[0].data as string)).toEqual(request)
  })

  it('reads the sections of every release source, whole', async () => {
    // Arrange
    const list = sectionsOf(tolokaSections)
    const requests = respondWith(200, list)

    // Act
    const sections = await destinationsApi.sections()

    // Assert
    expect(sections).toEqual(list)
    expect(requests).toHaveLength(1)
    expect(requests[0].method).toBe('get')
    expect(api.getUri(requests[0])).toBe('/api/settings/destinations/sections')
  })

  it('asks for the categories of one download client', async () => {
    // Arrange
    const requests = respondWith(200, sampleCategories)

    // Act
    const categories = await destinationsApi.clientDestinations(localQbittorrent.id)

    // Assert
    expect(categories).toEqual(sampleCategories)
    expect(requests[0].method).toBe('get')
    expect(api.getUri(requests[0])).toBe(`/api/settings/destinations/client-destinations/${localQbittorrent.id}`)
  })

  it('rejects a save the client couldn\'t confirm with its sentence in the body (502)', async () => {
    // Arrange
    respondWith(502, { error: 'qBittorrent didn\'t answer at http://127.0.0.1:9/qbt. Check the address and that qBittorrent is running.' })

    // Act
    const result = destinationsApi.create({ name: 'Movies', downloadClientId: localQbittorrent.id, clientDestinationKey: 'Movies', isDefault: false })

    // Assert
    await expect(result).rejects.toMatchObject({
      isAxiosError: true,
      response: {
        status: 502,
        data: { error: 'qBittorrent didn\'t answer at http://127.0.0.1:9/qbt. Check the address and that qBittorrent is running.' },
      },
    })
  })

  it('rejects a destination that is gone with a bare 404', async () => {
    // Arrange
    respondWith(404, '')

    // Act
    const result = destinationsApi.remove(movies.id)

    // Assert
    await expect(result).rejects.toMatchObject({ isAxiosError: true, response: { status: 404, data: '' } })
  })
})
