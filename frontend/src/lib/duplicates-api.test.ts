import { AxiosError, type AxiosAdapter, type AxiosResponse, type InternalAxiosRequestConfig } from 'axios'
import { api } from '@/lib/api'
import { duplicatesApi } from '@/lib/duplicates-api'
import { getApiErrorMessage } from '@/lib/utils'
import { duplicatesPage, possibleGroupOf, sameFileGroup } from '@/test/duplicates'

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

const key = possibleGroupOf().key

describe('duplicatesApi', () => {
  const originalAdapter = api.defaults.adapter

  afterEach(() => {
    api.defaults.adapter = originalAdapter
  })

  it('asks for a page of one class with its paging as query params', async () => {
    // Arrange
    const query = { class: 'SameFile' as const, page: 2, pageSize: 10 }
    const requests = respondWith(200, duplicatesPage(query, [sameFileGroup], 11))

    // Act
    const list = await duplicatesApi.list(query)

    // Assert
    expect(list).toEqual(duplicatesPage(query, [sameFileGroup], 11))
    expect(requests[0].method).toBe('get')
    expect(api.getUri(requests[0])).toBe('/api/library/duplicates?class=SameFile&page=2&pageSize=10')
  })

  it('asks for the dismissed possible matches by the decision\'s name', async () => {
    // Arrange
    const query = { decision: 'Dismissed' as const, page: 1, pageSize: 25 }
    const requests = respondWith(200, duplicatesPage(query, []))

    // Act
    await duplicatesApi.list(query)

    // Assert
    expect(api.getUri(requests[0])).toBe('/api/library/duplicates?decision=Dismissed&page=1&pageSize=25')
  })

  it('leaves out a class and a decision the page doesn\'t filter by', async () => {
    // Arrange
    const requests = respondWith(200, duplicatesPage({ page: 1, pageSize: 25 }, []))

    // Act
    await duplicatesApi.list({ page: 1, pageSize: 25 })

    // Assert
    expect(api.getUri(requests[0])).toBe('/api/library/duplicates?page=1&pageSize=25')
  })

  it('rejects with the server\'s sentence for a class that isn\'t one', async () => {
    // Arrange
    respondWith(400, { error: '“Hardlink” is not a duplicate class. Use SameFile, ByteIdentical, SameWork or Possible.' })

    // Act
    const failure = await duplicatesApi.list({ page: 1, pageSize: 25 }).catch((error: unknown) => error)

    // Assert
    expect(getApiErrorMessage(failure)).toBe('“Hardlink” is not a duplicate class. Use SameFile, ByteIdentical, SameWork or Possible.')
  })

  it.each(['Confirmed', 'Dismissed'] as const)('puts %s as the body of the group\'s decision', async (decision) => {
    // Arrange
    const requests = respondWith(204, '')

    // Act
    await duplicatesApi.decide(key, decision)

    // Assert
    expect(requests[0].method).toBe('put')
    expect(api.getUri(requests[0])).toBe(`/api/library/duplicates/${key}/decision`)
    expect(JSON.parse(requests[0].data as string)).toEqual({ decision })
  })

  it('rejects a decision a member sends with the owner sentence', async () => {
    // Arrange
    respondWith(403, { error: 'Only the installation owner can do this.' })

    // Act
    const failure = await duplicatesApi.decide(key, 'Confirmed').catch((error: unknown) => error)

    // Assert
    expect(getApiErrorMessage(failure)).toBe('Only the installation owner can do this.')
  })

  it('deletes the group\'s decision on Undo', async () => {
    // Arrange
    const requests = respondWith(204, '')

    // Act
    await duplicatesApi.undo(key)

    // Assert
    expect(requests[0].method).toBe('delete')
    expect(api.getUri(requests[0])).toBe(`/api/library/duplicates/${key}/decision`)
  })

  it('rejects an Undo of a key no group has now with a bare 404', async () => {
    // Arrange
    respondWith(404, '')

    // Act
    const failure = await duplicatesApi.undo(key).catch((error: unknown) => error)

    // Assert
    expect(failure).toBeInstanceOf(AxiosError)
    expect((failure as AxiosError).response?.status).toBe(404)
  })
})
