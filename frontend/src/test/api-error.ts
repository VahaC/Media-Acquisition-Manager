import { AxiosError, AxiosHeaders } from 'axios'

/**
 * An axios failure the way the API sends one: a status plus, when `error` is given, the
 * `{ error }` body with its sentence. Without it the body is empty, like a bare `NotFound()`.
 */
export function apiError(status: number, error?: string): AxiosError {
  const config = { headers: new AxiosHeaders() }
  const code = status >= 500 ? AxiosError.ERR_BAD_RESPONSE : AxiosError.ERR_BAD_REQUEST
  return new AxiosError(`Request failed with status code ${status}`, code, config, undefined, {
    status,
    statusText: '',
    headers: {},
    config,
    data: error === undefined ? '' : { error },
  })
}

/** A request that never reached the server. */
export function networkError(): AxiosError {
  return new AxiosError('Network Error', AxiosError.ERR_NETWORK)
}
