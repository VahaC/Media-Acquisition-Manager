import { AxiosError, type AxiosAdapter, type AxiosResponse, type InternalAxiosRequestConfig } from 'axios'
import { allowedRootsApi } from '@/lib/allowed-roots-api'
import { api } from '@/lib/api'

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

describe('allowedRootsApi', () => {
  const originalAdapter = api.defaults.adapter

  afterEach(() => {
    api.defaults.adapter = originalAdapter
  })

  it('reads the media folders', async () => {
    // Arrange
    const stored = {
      paths: ['/media', '/data/films'],
      replacementEnabled: true,
      libraryFolders: [
        { appPath: '/media/movies', libraryNames: ['Фільми'], ticked: true, status: 'Available' },
        { appPath: '/config/data/collections', libraryNames: ['Колекції'], ticked: false, status: 'OutsideMediaFolders' },
      ],
      unmappedLibraryFolderCount: 1,
    }
    const requests = respondWith(200, stored)

    // Act
    const roots = await allowedRootsApi.get()

    // Assert
    expect(roots).toEqual(stored)
    expect(requests[0].method).toBe('get')
    expect(api.getUri(requests[0])).toBe('/api/settings/allowed-roots')
  })

  it('replaces the media folders, the replacement switch and the ticks with what it is given, in one request (204)', async () => {
    // Arrange
    const requests = respondWith(204, '')

    // Act
    await allowedRootsApi.update({
      paths: ['/media', '/data/films'],
      replacementEnabled: true,
      replacementFolders: ['/media/movies', '/media/tv-shows'],
    })

    // Assert
    expect(requests).toHaveLength(1)
    expect(requests[0].method).toBe('put')
    expect(api.getUri(requests[0])).toBe('/api/settings/allowed-roots')
    expect(JSON.parse(requests[0].data as string)).toEqual({
      paths: ['/media', '/data/films'],
      replacementEnabled: true,
      replacementFolders: ['/media/movies', '/media/tv-shows'],
    })
  })

  it('rejects a refused list with the API sentence in the body (400)', async () => {
    // Arrange
    respondWith(400, { error: '“/media/films” is inside “/media”, which already allows it. Keep only “/media”.' })

    // Act
    const result = allowedRootsApi.update({ paths: ['/media', '/media/films'], replacementEnabled: false, replacementFolders: [] })

    // Assert
    await expect(result).rejects.toMatchObject({
      isAxiosError: true,
      response: { status: 400, data: { error: '“/media/films” is inside “/media”, which already allows it. Keep only “/media”.' } },
    })
  })
})
