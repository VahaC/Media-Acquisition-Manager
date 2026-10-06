import { AxiosError, type AxiosAdapter, type AxiosResponse, type InternalAxiosRequestConfig } from 'axios'
import { api } from '@/lib/api'
import { libraryLinksApi } from '@/lib/library-links-api'
import { batchId, candidateOf, dialogOf, landingStripling, linkedDialogOf, linkId, progressOf, queuedItemOf } from '@/test/library-links'

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

const workId = landingStripling.id

describe('libraryLinksApi', () => {
  const originalAdapter = api.defaults.adapter

  afterEach(() => {
    api.defaults.adapter = originalAdapter
  })

  it('asks for a work\'s TMDb dialog by its id', async () => {
    // Arrange
    const requests = respondWith(200, dialogOf())

    // Act
    const dialog = await libraryLinksApi.dialog(workId)

    // Assert
    expect(dialog).toEqual(dialogOf())
    expect(requests[0].method).toBe('get')
    expect(api.getUri(requests[0])).toBe(`/api/library/works/${workId}/tmdb`)
  })

  it('puts the pick, its numbers and items as the body of the link and hands back the dialog with its item queued', async () => {
    // Arrange
    const queued = linkedDialogOf({ items: [queuedItemOf()] })
    const requests = respondWith(200, queued)
    const request = { catalogKind: 'Series' as const, catalogId: '1044', seasonNumber: '1', episodeNumber: '8', sourceItemIds: ['c0ffee'] }

    // Act
    const dialog = await libraryLinksApi.link(workId, request)

    // Assert
    expect(dialog).toEqual(queued)
    expect(dialog.items[0].writeState).toBe('Queued')
    expect(dialog.items[0].identifiedUtc).toBeNull()
    expect(requests[0].method).toBe('put')
    expect(api.getUri(requests[0])).toBe(`/api/library/works/${workId}/tmdb`)
    expect(JSON.parse(requests[0].data as string)).toEqual(request)
  })

  it('hands back the dialog a removal left', async () => {
    // Arrange
    const requests = respondWith(200, { dialog: dialogOf() })

    // Act
    const result = await libraryLinksApi.remove(workId)

    // Assert
    expect(result).toEqual({ dialog: dialogOf() })
    expect(requests[0].method).toBe('delete')
    expect(api.getUri(requests[0])).toBe(`/api/library/works/${workId}/tmdb`)
  })

  it('reads a removal of a work with no link as nothing', async () => {
    // Arrange
    respondWith(204, '')

    // Act
    const result = await libraryLinksApi.remove(workId)

    // Assert
    expect(result).toBeNull()
  })

  it('queues one item\'s identification by the link\'s id', async () => {
    // Arrange
    const requests = respondWith(200, linkedDialogOf())

    // Act
    await libraryLinksApi.writeLink(linkId)

    // Assert
    expect(requests[0].method).toBe('post')
    expect(api.getUri(requests[0])).toBe(`/api/library/tmdb/links/${linkId}/write`)
  })

  it('searches TMDb with the title and the year as query params', async () => {
    // Arrange
    const requests = respondWith(200, { items: [candidateOf()] })

    // Act
    const list = await libraryLinksApi.search(workId, { title: 'Landing Stripling', year: '1962' })

    // Assert
    expect(list.items).toEqual([candidateOf()])
    expect(api.getUri(requests[0])).toBe(`/api/library/works/${workId}/tmdb/search?title=Landing+Stripling&year=1962`)
  })

  it('leaves a blank year and a missing kind out of the address', async () => {
    // Arrange
    const requests = respondWith(200, { items: [] })

    // Act
    await libraryLinksApi.search(workId, { title: 'Landing Stripling' })
    await libraryLinksApi.lookup(workId, { text: 'tt0056164' })

    // Assert
    expect(api.getUri(requests[0])).toBe(`/api/library/works/${workId}/tmdb/search?title=Landing+Stripling`)
    expect(api.getUri(requests[1])).toBe(`/api/library/works/${workId}/tmdb/lookup?text=tt0056164`)
  })

  it('looks a bare number up with the kind it is a number of', async () => {
    // Arrange
    const requests = respondWith(200, { items: [candidateOf()] })

    // Act
    await libraryLinksApi.lookup(workId, { text: '223021', kind: 'Movie' })

    // Assert
    expect(api.getUri(requests[0])).toBe(`/api/library/works/${workId}/tmdb/lookup?text=223021&kind=Movie`)
  })

  it('reads the review queue, the confident list and the lists of links with their params', async () => {
    // Arrange
    const requests = respondWith(200, {})

    // Act
    await libraryLinksApi.queue(3)
    await libraryLinksApi.confident({ locationId: 'l1', page: 2, pageSize: 50 })
    await libraryLinksApi.links({ filter: 'Unattached', page: 1, pageSize: 25 })
    await libraryLinksApi.batch('b1')

    // Assert
    expect(requests.map((request) => api.getUri(request))).toEqual([
      '/api/library/tmdb/queue?index=3',
      '/api/library/tmdb/confident?locationId=l1&page=2&pageSize=50',
      '/api/library/tmdb/links?filter=Unattached&page=1&pageSize=25',
      '/api/library/tmdb/batches/b1',
    ])
  })

  it('posts the accepted confident suggestions under items', async () => {
    // Arrange
    const requests = respondWith(202, { batchId: 'b1', items: [] })
    const items = [{ workId, catalogKind: 'Movie' as const, catalogId: '223021' }]

    // Act
    const batch = await libraryLinksApi.acceptConfident(items)

    // Assert
    expect(batch.batchId).toBe('b1')
    expect(requests[0].method).toBe('post')
    expect(api.getUri(requests[0])).toBe('/api/library/tmdb/confident')
    expect(JSON.parse(requests[0].data as string)).toEqual({ items })
  })

  it('marks and clears "none fits" on the work', async () => {
    // Arrange
    const requests = respondWith(204, '')

    // Act
    await libraryLinksApi.markNoneFits(workId)
    await libraryLinksApi.clearNoneFits(workId)

    // Assert
    expect(requests.map((request) => [request.method, api.getUri(request)])).toEqual([
      ['put', `/api/library/works/${workId}/tmdb/none-fits`],
      ['delete', `/api/library/works/${workId}/tmdb/none-fits`],
    ])
  })

  it('removes one link by its id', async () => {
    // Arrange
    const requests = respondWith(204, '')

    // Act
    await libraryLinksApi.removeLink(linkId)

    // Assert
    expect(requests[0].method).toBe('delete')
    expect(api.getUri(requests[0])).toBe(`/api/library/tmdb/links/${linkId}`)
  })

  it('reads what Update Jellyfin would identify when no batch is under way', async () => {
    // Arrange
    const requests = respondWith(200, { toIdentify: 223, progress: null })

    // Act
    const counts = await libraryLinksApi.catchUp()

    // Assert
    expect(counts).toEqual({ toIdentify: 223, progress: null })
    expect(requests[0].method).toBe('get')
    expect(api.getUri(requests[0])).toBe('/api/library/tmdb/catch-up')
  })

  it('reads the batch under way with its total and how many are settled', async () => {
    // Arrange
    const progress = progressOf()
    const requests = respondWith(200, { toIdentify: 182, progress })

    // Act
    const counts = await libraryLinksApi.catchUp()

    // Assert
    expect(counts).toEqual({ toIdentify: 182, progress: { batchId, total: 223, done: 41 } })
    expect(api.getUri(requests[0])).toBe('/api/library/tmdb/catch-up')
  })

  it('posts Update Jellyfin with no body and hands back what it queued and its batch', async () => {
    // Arrange
    const progress = progressOf({ done: 0 })
    const requests = respondWith(202, { toIdentify: 223, progress })

    // Act
    const queued = await libraryLinksApi.startCatchUp()

    // Assert
    expect(queued).toEqual({ toIdentify: 223, progress: { batchId, total: 223, done: 0 } })
    expect(requests).toHaveLength(1)
    expect(requests[0].method).toBe('post')
    expect(api.getUri(requests[0])).toBe('/api/library/tmdb/catch-up')
    expect(requests[0].data).toBeUndefined()
  })

  it('reads no preparation before the first as nothing', async () => {
    // Arrange
    const requests = respondWith(204, '')

    // Act
    const latest = await libraryLinksApi.latestRun()

    // Assert
    expect(latest).toBeNull()
    expect(api.getUri(requests[0])).toBe('/api/library/tmdb/runs/latest')
  })

  it('rejects with the API\'s refusal', async () => {
    // Arrange
    respondWith(409, { error: 'Connect a metadata catalog under Integrations first.' })

    // Act
    const failure = libraryLinksApi.startRun()

    // Assert
    await expect(failure).rejects.toMatchObject({ response: { status: 409 } })
  })
})
