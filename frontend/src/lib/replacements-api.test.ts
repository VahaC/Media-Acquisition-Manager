import { AxiosError, type AxiosAdapter, type AxiosResponse, type InternalAxiosRequestConfig } from 'axios'
import { api } from '@/lib/api'
import { replacementsApi } from '@/lib/replacements-api'
import { getApiErrorMessage } from '@/lib/utils'
import { jobOf, localClient, moviesOption } from '@/test/downloads'
import { aliens } from '@/test/library'
import {
  alienSubtitle,
  alienVideo,
  aliensWorkOf,
  optionsUnavailable,
  REPLACEMENT_ID,
  replacementDetailsOf,
  replacementOf,
  replacementOptionsOf,
  theatricalCopy,
} from '@/test/replacements'
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

  // MAM-26 — the owner's replacement after the schedule: its names in the quarantine and the actions on it.

  it('asks for one replacement\'s names and actions by its own id', async () => {
    // Arrange
    const requests = respondWith(200, replacementDetailsOf())

    // Act
    const details = await replacementsApi.get(REPLACEMENT_ID)

    // Assert
    expect(details).toEqual(replacementDetailsOf())
    expect(requests).toHaveLength(1)
    expect(requests[0].method).toBe('get')
    expect(api.getUri(requests[0])).toBe(`/api/replacements/${REPLACEMENT_ID}`)
  })

  it.each<[string, (id: string) => Promise<unknown>, string]>([
    ['Retry', (id) => replacementsApi.retry(id), 'retry'],
    ['Restore', (id) => replacementsApi.restore(id), 'restore'],
  ])('sends %s as a POST to the replacement\'s own address, with no body, and answers its job', async (_, send, action) => {
    // Arrange
    const job = jobOf({ replacement: replacementOf({ state: 'Verified' }) })
    const requests = respondWith(200, job)

    // Act
    const answer = await send(REPLACEMENT_ID)

    // Assert
    expect(answer).toEqual(job)
    expect(requests).toHaveLength(1)
    expect(requests[0].method).toBe('post')
    expect(api.getUri(requests[0])).toBe(`/api/replacements/${REPLACEMENT_ID}/${action}`)
    expect(requests[0].data).toBeUndefined()
  })

  it('sends Delete for good with exactly the names confirmed, and answers its job', async () => {
    // Arrange
    const job = jobOf({ replacement: replacementOf({ state: 'Completed' }) })
    const requests = respondWith(200, job)

    // Act
    const answer = await replacementsApi.deleteForGood(REPLACEMENT_ID, [alienVideo.id, alienSubtitle.id])

    // Assert
    expect(answer).toEqual(job)
    expect(requests[0].method).toBe('post')
    expect(api.getUri(requests[0])).toBe(`/api/replacements/${REPLACEMENT_ID}/delete-for-good`)
    expect(JSON.parse(requests[0].data as string)).toEqual({ names: [alienVideo.id, alienSubtitle.id] })
  })

  it('encodes a replacement id into one path segment', async () => {
    // Arrange
    const requests = respondWith(200, replacementDetailsOf())

    // Act
    await replacementsApi.get('../settings')

    // Assert
    expect(api.getUri(requests[0])).toBe('/api/replacements/..%2Fsettings')
  })

  it.each<[string, () => Promise<unknown>, string]>([
    ['Retry', () => replacementsApi.retry(REPLACEMENT_ID), 'This replacement isn\'t waiting for review after a move, so there\'s nothing to retry here.'],
    ['Restore', () => replacementsApi.restore(REPLACEMENT_ID), 'Replacement is switched off on the Media folders page, so nothing can be put back. Switch it on, then press Restore again.'],
    ['Delete for good', () => replacementsApi.deleteForGood(REPLACEMENT_ID, [alienVideo.id]), 'What this replacement holds in the quarantine changed since you opened it. Open Delete for good again.'],
  ])('rejects %s with the server sentence when it is refused (409)', async (_, send, sentence) => {
    // Arrange
    respondWith(409, { error: sentence })

    // Act
    const failure = await send().catch((error: unknown) => error)

    // Assert
    expect(getApiErrorMessage(failure)).toBe(sentence)
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
