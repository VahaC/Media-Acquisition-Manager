import { AxiosError, type AxiosAdapter, type AxiosResponse, type InternalAxiosRequestConfig } from 'axios'
import { api } from '@/lib/api'
import { replacementsApi } from '@/lib/replacements-api'
import { getApiErrorMessage } from '@/lib/utils'
import { jobOf, localClient, moviesOption } from '@/test/downloads'
import { aliens } from '@/test/library'
import { aliensWorkOf, optionsUnavailable, replacementOf, replacementOptionsOf, theatricalCopy } from '@/test/replacements'
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
  fileIds: [theatricalCopy.file.id],
  confirmedWorkIds: [aliens.id],
}

describe('replacementsApi', () => {
  const originalAdapter = api.defaults.adapter

  afterEach(() => {
    api.defaults.adapter = originalAdapter
  })

  it('asks what a release may replace through a destination, without the held marks', async () => {
    // Arrange
    const requests = respondWith(200, replacementOptionsOf([aliensWorkOf()]))

    // Act
    const options = await replacementsApi.options(tolokaRemux.id, moviesOption.id, false)

    // Assert
    expect(options).toEqual(replacementOptionsOf([aliensWorkOf()]))
    expect(requests).toHaveLength(1)
    expect(requests[0].method).toBe('get')
    expect(api.getUri(requests[0])).toBe(
      `/api/replacements/options?releaseId=${tolokaRemux.id}&destinationId=${moviesOption.id}&holdings=false`)
  })

  it('asks for the held marks too once Replace is picked', async () => {
    // Arrange
    const requests = respondWith(200, optionsUnavailable('SwitchedOff'))

    // Act
    await replacementsApi.options(tolokaRemux.id, moviesOption.id, true)

    // Assert
    expect(api.getUri(requests[0])).toBe(
      `/api/replacements/options?releaseId=${tolokaRemux.id}&destinationId=${moviesOption.id}&holdings=true`)
  })

  it('rejects with the server sentence when the read is refused (403)', async () => {
    // Arrange
    respondWith(403, { error: 'Only the installation owner can do this.' })

    // Act
    const failure = await replacementsApi.options(tolokaRemux.id, moviesOption.id, false).catch((error: unknown) => error)

    // Assert
    expect(getApiErrorMessage(failure)).toBe('Only the installation owner can do this.')
  })

  it('schedules a replacement with the ticked copies and the confirmed works, and answers the new job (201)', async () => {
    // Arrange
    const job = jobOf({ replacement: replacementOf() })
    const requests = respondWith(201, job)

    // Act
    const answer = await replacementsApi.schedule(request)

    // Assert
    expect(answer).toEqual(job)
    expect(requests).toHaveLength(1)
    expect(requests[0].method).toBe('post')
    expect(api.getUri(requests[0])).toBe('/api/replacements')
    expect(JSON.parse(requests[0].data as string)).toEqual(request)
  })

  it('answers the existing job when the same request is answered again (200)', async () => {
    // Arrange
    const existing = jobOf({ replacement: replacementOf({ state: 'Submitted' }) })
    respondWith(200, existing)

    // Act
    const job = await replacementsApi.schedule(request)

    // Assert
    expect(job).toEqual(existing)
  })

  it('rejects with the server sentence when a ticked copy can\'t be replaced any more (409)', async () => {
    // Arrange
    respondWith(409, { error: '“Alien (1979).mkv” changed since the app inspected it, so it can\'t be replaced. Inspect the library again, or pick Keep both.' })

    // Act
    const failure = await replacementsApi.schedule(request).catch((error: unknown) => error)

    // Assert
    expect(getApiErrorMessage(failure)).toBe(
      '“Alien (1979).mkv” changed since the app inspected it, so it can\'t be replaced. Inspect the library again, or pick Keep both.')
  })
})
