import { AxiosError, AxiosHeaders } from 'axios'
import { getApiErrorMessage, isServerUnreachable, retryCanHelp } from './utils'

function responseError(status: number, data: unknown): AxiosError {
  const config = { headers: new AxiosHeaders() }
  return new AxiosError('Request failed with status code ' + status, 'ERR_BAD_RESPONSE', config, undefined, {
    status,
    statusText: '',
    headers: {},
    config,
    data,
  })
}

describe('getApiErrorMessage', () => {
  it('returns the sentence from an { error } body', () => {
    // Arrange
    const error = responseError(400, { error: 'The name is already taken.' })

    // Act
    const message = getApiErrorMessage(error, 'Could not save.')

    // Assert
    expect(message).toBe('The name is already taken.')
  })

  it('says the server could not be reached when there was no response at all', () => {
    // Arrange
    const error = new AxiosError('Network Error', 'ERR_NETWORK')

    // Act
    const message = getApiErrorMessage(error)

    // Assert
    expect(message).toBe('The server could not be reached. Check that it is running, then try again.')
  })

  it.each([
    ['an HTML error page', '<html>500 Internal Server Error</html>'],
    ['an empty body', ''],
    ['a blank error', { error: '  ' }],
    ['a non-string error', { error: 42 }],
  ])('falls back when the response carries %s', (_, data) => {
    // Arrange
    const error = responseError(500, data)

    // Act
    const message = getApiErrorMessage(error, 'Could not load.')

    // Assert
    expect(message).toBe('Could not load.')
  })

  it("says the server could not be reached when a proxy in front of it answers 502 with no body of the API's", () => {
    // Arrange — what the Vite dev proxy answers while the API is stopped.
    const error = responseError(502, '')

    // Act
    const message = getApiErrorMessage(error, 'Could not load.')

    // Assert
    expect(message).toBe('The server could not be reached. Check that it is running, then try again.')
  })

  it('returns undefined without a fallback so the caller can supply its own', () => {
    // Arrange
    const error = responseError(500, null)

    // Act
    const message = getApiErrorMessage(error)

    // Assert
    expect(message).toBeUndefined()
  })

  it('does not treat a cancelled request as unreachable', () => {
    // Arrange
    const error = new AxiosError('canceled', 'ERR_CANCELED')

    // Act
    const message = getApiErrorMessage(error, 'Could not load.')

    // Assert
    expect(message).toBe('Could not load.')
  })

  it('does not read the message of an error that did not come from the API', () => {
    // Arrange
    const error = new TypeError("Cannot read properties of undefined (reading 'map')")

    // Act
    const message = getApiErrorMessage(error, 'Could not load.')

    // Assert
    expect(message).toBe('Could not load.')
  })
})

describe('isServerUnreachable', () => {
  it('holds for a request that got no response', () => {
    // Arrange
    const error = new AxiosError('Network Error', 'ERR_NETWORK')

    // Act
    const unreachable = isServerUnreachable(error)

    // Assert
    expect(unreachable).toBe(true)
  })

  it.each([
    [502, ''],
    [503, '<html><body>Service Unavailable</body></html>'],
    [504, 'Gateway Timeout'],
  ])("holds for a %i a proxy answers without the API's JSON", (status, data) => {
    // Arrange
    const error = responseError(status, data)

    // Act
    const unreachable = isServerUnreachable(error)

    // Assert
    expect(unreachable).toBe(true)
  })

  it.each([
    ["the API's own 502 with its sentence", 502, { error: 'Jellyfin could not be reached.' }],
    ["the readiness report's 503", 503, { status: 'Unhealthy', checks: [] }],
    ['a 500 with no body', 500, ''],
    ['a 404 with no body', 404, ''],
  ])('does not hold for %s, which the server answered', (_, status, data) => {
    // Arrange
    const error = responseError(status, data)

    // Act
    const unreachable = isServerUnreachable(error)

    // Assert
    expect(unreachable).toBe(false)
  })

  it('does not hold for a cancelled request or an error that did not come from a request', () => {
    // Arrange
    const cancelled = new AxiosError('canceled', 'ERR_CANCELED')
    const crash = new TypeError('boom')

    // Act
    const results = [isServerUnreachable(cancelled), isServerUnreachable(crash)]

    // Assert
    expect(results).toEqual([false, false])
  })
})

describe('retryCanHelp', () => {
  it.each([400, 403, 404])("says no for a %i, the server's lasting answer", (status) => {
    // Arrange
    const error = responseError(status, { error: 'No.' })

    // Act
    const helps = retryCanHelp(error)

    // Assert
    expect(helps).toBe(false)
  })

  it.each([
    ['a 500', responseError(500, { error: 'Database is locked.' })],
    ['a 409', responseError(409, { error: 'Try again in a moment.' })],
    ['no response', new AxiosError('Network Error', 'ERR_NETWORK')],
    ['an error that did not come from a request', new TypeError('boom')],
  ])('says yes for %s', (_, error) => {
    // Act
    const helps = retryCanHelp(error)

    // Assert
    expect(helps).toBe(true)
  })
})
