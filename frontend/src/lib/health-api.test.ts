import { AxiosError, type AxiosAdapter, type AxiosResponse } from 'axios'
import { api } from '@/lib/api'
import { healthApi } from '@/lib/health-api'
import type { HealthReport } from '@/lib/types'

const healthy: HealthReport = {
  status: 'Healthy',
  checks: [{ name: 'database', status: 'Healthy', description: 'The database is reachable and up to date.' }],
}

const unhealthy: HealthReport = {
  status: 'Unhealthy',
  checks: [{ name: 'database', status: 'Unhealthy', description: 'The database has 1 pending migration.' }],
}

/**
 * Answers every request with `status` and `body`. A replaced adapter also replaces axios's own
 * status check (the built-in adapters call `settle`, which applies `validateStatus`), so this does
 * the same: a status the request does not accept rejects with the AxiosError axios would raise.
 */
function respondWith(status: number, body: unknown): AxiosAdapter {
  const adapter: AxiosAdapter = async (config) => {
    const response: AxiosResponse = { status, statusText: '', headers: {}, config, data: body }
    if (config.validateStatus && !config.validateStatus(status)) {
      const code = status >= 500 ? AxiosError.ERR_BAD_RESPONSE : AxiosError.ERR_BAD_REQUEST
      throw new AxiosError(`Request failed with status code ${status}`, code, config, null, response)
    }
    return response
  }
  return vi.fn(adapter)
}

describe('healthApi.ready', () => {
  const originalAdapter = api.defaults.adapter

  afterEach(() => {
    api.defaults.adapter = originalAdapter
  })

  it('resolves with the report when every check passes (200)', async () => {
    // Arrange
    const adapter = respondWith(200, healthy)
    api.defaults.adapter = adapter

    // Act
    const report = await healthApi.ready()

    // Assert
    expect(report).toEqual(healthy)
    expect(adapter).toHaveBeenCalledWith(expect.objectContaining({ method: 'get', url: '/api/health/ready' }))
  })

  it('resolves with the report, not a rejection, when a check fails (503)', async () => {
    // Arrange
    api.defaults.adapter = respondWith(503, unhealthy)

    // Act
    const report = await healthApi.ready()

    // Assert
    expect(report).toEqual(unhealthy)
  })

  it('rejects with the API error on a 500', async () => {
    // Arrange
    api.defaults.adapter = respondWith(500, { error: 'The server hit an unexpected error.' })

    // Act
    const result = healthApi.ready()

    // Assert
    await expect(result).rejects.toMatchObject({ isAxiosError: true, response: { status: 500 } })
  })

  it('rejects when the server cannot be reached', async () => {
    // Arrange
    api.defaults.adapter = vi.fn<AxiosAdapter>(async (config) => {
      throw new AxiosError('Network Error', AxiosError.ERR_NETWORK, config)
    })

    // Act
    const result = healthApi.ready()

    // Assert
    await expect(result).rejects.toMatchObject({ isAxiosError: true, code: AxiosError.ERR_NETWORK })
  })

  it.each([
    ['an empty body (a rate limiter)', ''],
    ['a reverse proxy error page', '<html><body><h1>503 Service Unavailable</h1></body></html>'],
    ['an { error } body', { error: 'The service is temporarily unavailable.' }],
    ['a body without checks', { status: 'Unhealthy' }],
  ])('rejects a 503 whose body is not a readiness report: %s', async (_label, body) => {
    // Arrange
    api.defaults.adapter = respondWith(503, body)

    // Act
    const result = healthApi.ready()

    // Assert
    await expect(result).rejects.toMatchObject({ isAxiosError: true, response: { status: 503 } })
  })
})
