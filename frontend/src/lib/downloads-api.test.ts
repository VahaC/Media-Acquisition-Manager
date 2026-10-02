import { AxiosError, type AxiosAdapter, type AxiosResponse, type InternalAxiosRequestConfig } from 'axios'
import { api } from '@/lib/api'
import { downloadsApi } from '@/lib/downloads-api'
import { getApiErrorMessage } from '@/lib/utils'
import { jobOf, localClient, moviesOption, optionsOf } from '@/test/downloads'
import { tolokaRemux } from '@/test/search'

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

const request = {
  requestId: '0f8e1d2c-3b4a-4596-8877-665544332211',
  releaseId: tolokaRemux.id,
  downloadClientId: localClient.id,
  destinationId: moviesOption.id,
}

describe('downloadsApi', () => {
  const originalAdapter = api.defaults.adapter

  afterEach(() => {
    api.defaults.adapter = originalAdapter
  })

  it('asks for the clients and destinations of one release by its id', async () => {
    // Arrange
    const requests = respondWith(200, optionsOf(localClient))

    // Act
    const options = await downloadsApi.options(tolokaRemux.id)

    // Assert
    expect(options).toEqual(optionsOf(localClient))
    expect(requests).toHaveLength(1)
    expect(requests[0].method).toBe('get')
    expect(api.getUri(requests[0])).toBe(`/api/downloads/options?releaseId=${tolokaRemux.id}`)
  })

  it('sends a release and answers the new job (201), whatever its outcome', async () => {
    // Arrange
    const failed = jobOf({ status: 'Failed', clientJobId: null, error: '“Local qBittorrent” already has this torrent, so nothing was sent.' })
    const requests = respondWith(201, failed)

    // Act
    const job = await downloadsApi.send(request)

    // Assert
    expect(job).toEqual(failed)
    expect(requests[0].method).toBe('post')
    expect(api.getUri(requests[0])).toBe('/api/downloads')
    expect(JSON.parse(requests[0].data as string)).toEqual(request)
  })

  it('answers the existing job when the same request is answered again (200)', async () => {
    // Arrange
    const existing = jobOf({})
    respondWith(200, existing)

    // Act
    const job = await downloadsApi.send(request)

    // Assert
    expect(job).toEqual(existing)
  })

  it('rejects with the server sentence when sending is refused (409)', async () => {
    // Arrange
    respondWith(409, { error: 'This release was already sent to “Local qBittorrent”, so it isn\'t sent twice.' })

    // Act
    const failure = await downloadsApi.send(request).catch((error: unknown) => error)

    // Assert
    expect(getApiErrorMessage(failure)).toBe('This release was already sent to “Local qBittorrent”, so it isn\'t sent twice.')
  })

  it('reads one job by its id, encoded into one path segment', async () => {
    // Arrange
    const requests = respondWith(200, jobOf({}))

    // Act
    await downloadsApi.get('../settings/x')

    // Assert
    expect(requests[0].method).toBe('get')
    expect(api.getUri(requests[0])).toBe('/api/downloads/..%2Fsettings%2Fx')
  })
})
