import { AxiosError, type AxiosAdapter, type AxiosResponse, type InternalAxiosRequestConfig } from 'axios'
import { api } from '@/lib/api'
import { copyRemovalsApi } from '@/lib/copy-removals-api'
import { getApiErrorMessage } from '@/lib/utils'
import { REMOVAL_ID, removalOf, removalsPage } from '@/test/copy-removals'
import { backedUpAliens, byteIdenticalGroupOf } from '@/test/duplicates'

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

/** The backup disk's copy of the Theatrical Cut, with the identity the Duplicates page showed. */
const request = {
  groupKey: byteIdenticalGroupOf().key,
  fileId: backedUpAliens.file.id,
  deviceId: 2065,
  inode: 77,
  sizeBytes: 14_845_708_473,
}

describe('copyRemovalsApi', () => {
  const originalAdapter = api.defaults.adapter

  afterEach(() => {
    api.defaults.adapter = originalAdapter
  })

  it('asks for a page of the removals with its paging as query params', async () => {
    // Arrange
    const query = { page: 2, pageSize: 10 }
    const requests = respondWith(200, removalsPage(query, [removalOf()], 11))

    // Act
    const list = await copyRemovalsApi.list(query)

    // Assert
    expect(list).toEqual(removalsPage(query, [removalOf()], 11))
    expect(requests[0].method).toBe('get')
    expect(api.getUri(requests[0])).toBe('/api/replacements/removals?page=2&pageSize=10')
  })

  it('rejects a read a member sends with the owner sentence', async () => {
    // Arrange
    respondWith(403, { error: 'Only the installation owner can do this.' })

    // Act
    const failure = await copyRemovalsApi.list({ page: 1, pageSize: 25 }).catch((error: unknown) => error)

    // Assert
    expect(getApiErrorMessage(failure)).toBe('Only the installation owner can do this.')
  })

  it('posts the group, the file and the identity the page showed, and answers with the removal', async () => {
    // Arrange
    const requests = respondWith(200, removalOf())

    // Act
    const removal = await copyRemovalsApi.remove(request)

    // Assert
    expect(removal).toEqual(removalOf())
    expect(requests[0].method).toBe('post')
    expect(api.getUri(requests[0])).toBe('/api/replacements/removals')
    expect(JSON.parse(requests[0].data as string)).toEqual(request)
  })

  it('sends an identity the app never read as nulls', async () => {
    // Arrange
    const requests = respondWith(200, removalOf())

    // Act
    await copyRemovalsApi.remove({ ...request, deviceId: null, inode: null, sizeBytes: null })

    // Assert
    expect(JSON.parse(requests[0].data as string)).toEqual({ ...request, deviceId: null, inode: null, sizeBytes: null })
  })

  it('rejects a removal a check refuses with the server\'s sentence', async () => {
    // Arrange
    respondWith(409, { error: 'Still seeded by torrent “Aliens.1986.BDRip” — remove it in qBittorrent without its files, then press Remove again.' })

    // Act
    const failure = await copyRemovalsApi.remove(request).catch((error: unknown) => error)

    // Assert
    expect(getApiErrorMessage(failure)).toBe('Still seeded by torrent “Aliens.1986.BDRip” — remove it in qBittorrent without its files, then press Remove again.')
  })

  it('rejects a removal from a group gone meanwhile with a bare 404', async () => {
    // Arrange
    respondWith(404, '')

    // Act
    const failure = await copyRemovalsApi.remove(request).catch((error: unknown) => error)

    // Assert
    expect(failure).toBeInstanceOf(AxiosError)
    expect((failure as AxiosError).response?.status).toBe(404)
  })

  it('posts Restore to the removal\'s own address, with no body', async () => {
    // Arrange
    const requests = respondWith(200, removalOf({ state: 'Restored', canRestore: false, canDeleteForGood: false }))

    // Act
    const removal = await copyRemovalsApi.restore(REMOVAL_ID)

    // Assert
    expect(removal.state).toBe('Restored')
    expect(requests[0].method).toBe('post')
    expect(api.getUri(requests[0])).toBe(`/api/replacements/removals/${REMOVAL_ID}/restore`)
    expect(requests[0].data).toBeUndefined()
  })

  it('posts Delete for good to the removal\'s own address, with no body: the whole removal', async () => {
    // Arrange
    const requests = respondWith(200, removalOf({ state: 'Deleted', canRestore: false, canDeleteForGood: false }))

    // Act
    const removal = await copyRemovalsApi.deleteForGood(REMOVAL_ID)

    // Assert
    expect(removal.state).toBe('Deleted')
    expect(requests[0].method).toBe('post')
    expect(api.getUri(requests[0])).toBe(`/api/replacements/removals/${REMOVAL_ID}/delete-for-good`)
    expect(requests[0].data).toBeUndefined()
  })

  it('encodes a removal\'s id into one path segment', async () => {
    // Arrange
    const requests = respondWith(200, removalOf())

    // Act
    await copyRemovalsApi.restore('a/b')

    // Assert
    expect(api.getUri(requests[0])).toBe('/api/replacements/removals/a%2Fb/restore')
  })

  it('rejects a Restore the server refuses with its sentence', async () => {
    // Arrange
    respondWith(409, { error: 'Replacement is switched off on the Media folders page: nothing is put back while it is off.' })

    // Act
    const failure = await copyRemovalsApi.restore(REMOVAL_ID).catch((error: unknown) => error)

    // Assert
    expect(getApiErrorMessage(failure)).toBe('Replacement is switched off on the Media folders page: nothing is put back while it is off.')
  })
})
