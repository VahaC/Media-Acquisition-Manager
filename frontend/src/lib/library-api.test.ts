import { AxiosError, type AxiosAdapter, type AxiosResponse, type InternalAxiosRequestConfig } from 'axios'
import { api } from '@/lib/api'
import { libraryApi } from '@/lib/library-api'
import { inspectionRunOf, locations, runOf, sourceOf, sourcesOf, worksPage, aliens } from '@/test/library'

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

describe('libraryApi', () => {
  const originalAdapter = api.defaults.adapter

  afterEach(() => {
    api.defaults.adapter = originalAdapter
  })

  it('asks for one page of works with the search, filters and paging as query params', async () => {
    // Arrange
    const query = { search: 'чужі', locationId: locations.items[1].id, identifiers: 'Missing' as const, page: 2, pageSize: 50 }
    const requests = respondWith(200, worksPage(query, [aliens], 51))

    // Act
    const page = await libraryApi.works(query)

    // Assert
    expect(page.items).toEqual([aliens])
    expect(requests).toHaveLength(1)
    expect(requests[0].method).toBe('get')
    expect(api.getUri(requests[0])).toBe(
      `/api/library/works?search=%D1%87%D1%83%D0%B6%D1%96&locationId=${locations.items[1].id}&identifiers=Missing&page=2&pageSize=50`)
  })

  it('leaves the filters that are not set out of the address', async () => {
    // Arrange
    const query = { search: undefined, locationId: undefined, identifiers: undefined, page: 1, pageSize: 25 }
    const requests = respondWith(200, worksPage(query, []))

    // Act
    await libraryApi.works(query)

    // Assert
    expect(api.getUri(requests[0])).toBe('/api/library/works?page=1&pageSize=25')
  })

  it('reads the locations and the sources', async () => {
    // Arrange
    const sources = sourcesOf(sourceOf({}))
    const locationRequests = respondWith(200, locations)
    const locationList = await libraryApi.locations()
    const sourceRequests = respondWith(200, sources)

    // Act
    const sourceList = await libraryApi.sources()

    // Assert
    expect(locationList).toEqual(locations)
    expect(api.getUri(locationRequests[0])).toBe('/api/library/locations')
    expect(sourceList).toEqual(sources)
    expect(api.getUri(sourceRequests[0])).toBe('/api/library/sources')
  })

  it('cancels a sources request through the signal it is given', async () => {
    // Arrange — React Query's signal: a poll it cancels must reject rather than answer.
    const controller = new AbortController()
    const requests = respondWith(200, sourcesOf(sourceOf({})))
    await libraryApi.sources(controller.signal)
    controller.abort()

    // Act
    const cancelled = libraryApi.sources(controller.signal)

    // Assert
    await expect(cancelled).rejects.toMatchObject({ code: 'ERR_CANCELED' })
    expect(requests).toHaveLength(1)
    expect(requests[0].signal).toBe(controller.signal)
  })

  it('starts a sync of one integration and resolves with the queued run (202)', async () => {
    // Arrange
    const queued = runOf({ status: 'Queued', startedUtc: null, finishedUtc: null, itemsRead: 0, itemsTotal: null })
    const requests = respondWith(202, queued)

    // Act
    const run = await libraryApi.startSync('5d1c7a90-0000-4000-8000-000000000001')

    // Assert
    expect(run).toEqual(queued)
    expect(requests[0].method).toBe('post')
    expect(api.getUri(requests[0])).toBe('/api/library/sources/5d1c7a90-0000-4000-8000-000000000001/syncs')
  })

  it('rejects a refused start with the API sentence in the body (409)', async () => {
    // Arrange
    respondWith(409, { error: '“Home Jellyfin” is already being synced.' })

    // Act
    const result = libraryApi.startSync('5d1c7a90-0000-4000-8000-000000000001')

    // Assert
    await expect(result).rejects.toMatchObject({
      isAxiosError: true,
      response: { status: 409, data: { error: '“Home Jellyfin” is already being synced.' } },
    })
  })

  it('starts an inspection of the library files and resolves with the queued run (202)', async () => {
    // Arrange
    const queued = inspectionRunOf({ trigger: 'Requested', status: 'Queued', requestedByUserName: 'vasyl', startedUtc: null, finishedUtc: null, filesTotal: null, filesChecked: 0, filesProbed: 0 })
    const requests = respondWith(202, queued)

    // Act
    const run = await libraryApi.startInspection()

    // Assert
    expect(run).toEqual(queued)
    expect(requests).toHaveLength(1)
    expect(requests[0].method).toBe('post')
    expect(api.getUri(requests[0])).toBe('/api/library/inspections')
  })

  it('rejects an inspection without a media folder with the API sentence in the body (409)', async () => {
    // Arrange
    const sentence = 'No media folders are allowed yet. Add the folders the app may read under Media folders in the user menu, then inspect again.'
    respondWith(409, { error: sentence })

    // Act
    const result = libraryApi.startInspection()

    // Assert
    await expect(result).rejects.toMatchObject({ isAxiosError: true, response: { status: 409, data: { error: sentence } } })
  })
})
