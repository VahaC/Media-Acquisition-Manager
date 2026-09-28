import { AxiosError, AxiosHeaders } from 'axios'
import { getApiErrorMessage } from './utils'

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
    ['an HTML error page', '<html>502 Bad Gateway</html>'],
    ['an empty body', ''],
    ['a blank error', { error: '  ' }],
    ['a non-string error', { error: 42 }],
  ])('falls back when the response carries %s', (_, data) => {
    // Arrange
    const error = responseError(502, data)

    // Act
    const message = getApiErrorMessage(error, 'Could not load.')

    // Assert
    expect(message).toBe('Could not load.')
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
