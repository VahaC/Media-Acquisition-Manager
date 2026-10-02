import { AxiosError, type AxiosAdapter, type AxiosResponse, type InternalAxiosRequestConfig } from 'axios'
import { api } from '@/lib/api'
import { downloadsApi } from '@/lib/downloads-api'
import { getApiErrorMessage } from '@/lib/utils'
import { downloadedJob, downloadingJob, fileOf, filesPage, jobOf, jobsPage, localClient, moviesOption, optionsOf } from '@/test/downloads'
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

  it('asks for one page of the caller\'s jobs with the status filter and the paging as query parameters', async () => {
    // Arrange
    const page = jobsPage({ page: 2, pageSize: 10 }, [downloadingJob], 11)
    const requests = respondWith(200, page)

    // Act
    const list = await downloadsApi.list({ status: 'Downloading', page: 2, pageSize: 10 })

    // Assert
    expect(list).toEqual(page)
    expect(requests[0].method).toBe('get')
    expect(api.getUri(requests[0])).toBe('/api/downloads?status=Downloading&page=2&pageSize=10')
  })

  it('leaves the status out of the list\'s query when every job is asked for', async () => {
    // Arrange
    const requests = respondWith(200, jobsPage({ page: 1, pageSize: 25 }, []))

    // Act
    await downloadsApi.list({ status: undefined, page: 1, pageSize: 25 })

    // Assert
    expect(api.getUri(requests[0])).toBe('/api/downloads?page=1&pageSize=25')
  })

  it('rejects with the server sentence when the list\'s status isn\'t one (400)', async () => {
    // Arrange
    respondWith(400, { error: '“Gone” is not a download status. Use Created, Submitted, Downloading, Paused, Downloaded, Failed, NeedsLinking or Cancelled.' })

    // Act
    const failure = await downloadsApi.list({ page: 1, pageSize: 25 }).catch((error: unknown) => error)

    // Assert
    expect(getApiErrorMessage(failure)).toBe(
      '“Gone” is not a download status. Use Created, Submitted, Downloading, Paused, Downloaded, Failed, NeedsLinking or Cancelled.')
  })

  it('asks for one page of a job\'s files, the id encoded into one path segment', async () => {
    // Arrange
    const page = filesPage({ page: 1, pageSize: 50 }, [fileOf({})])
    const requests = respondWith(200, page)

    // Act
    const files = await downloadsApi.files('../settings/x', { page: 1, pageSize: 50 })

    // Assert
    expect(files).toEqual(page)
    expect(requests[0].method).toBe('get')
    expect(api.getUri(requests[0])).toBe('/api/downloads/..%2Fsettings%2Fx/files?page=1&pageSize=50')
  })

  it('cancels a job with a POST and no body, the id encoded into one path segment, and answers the cancelled job', async () => {
    // Arrange
    const cancelled = jobOf({ status: 'Cancelled' })
    const requests = respondWith(200, cancelled)

    // Act
    const job = await downloadsApi.cancel('../settings/x')

    // Assert
    expect(job).toEqual(cancelled)
    expect(requests).toHaveLength(1)
    expect(requests[0].method).toBe('post')
    expect(api.getUri(requests[0])).toBe('/api/downloads/..%2Fsettings%2Fx/cancel')
    expect(requests[0].data).toBeUndefined()
  })

  it('rejects with the server sentence when the job has already ended (409)', async () => {
    // Arrange
    respondWith(409, { error: 'This download is already downloaded, so there\'s nothing to stop following.' })

    // Act
    const failure = await downloadsApi.cancel(downloadedJob.id).catch((error: unknown) => error)

    // Assert
    expect(getApiErrorMessage(failure)).toBe('This download is already downloaded, so there\'s nothing to stop following.')
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
