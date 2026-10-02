import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { AxiosError, AxiosHeaders } from 'axios'
import { MemoryRouter } from 'react-router-dom'
import { healthApi } from '@/lib/health-api'
import type { HealthReport } from '@/lib/types'
import { HomePage } from '@/pages/HomePage'

vi.mock('@/lib/health-api')

const healthy: HealthReport = {
  status: 'Healthy',
  checks: [{ name: 'database', status: 'Healthy', description: 'The database is reachable and up to date.' }],
}

const unhealthy: HealthReport = {
  status: 'Unhealthy',
  checks: [{ name: 'database', status: 'Unhealthy', description: 'The database has 1 pending migration.' }],
}

/** An axios failure the way the API sends one: a status plus an `{ error }` body. */
function apiError(status: number, error: string): AxiosError {
  const config = { headers: new AxiosHeaders() }
  return new AxiosError('Request failed', 'ERR_BAD_RESPONSE', config, undefined, {
    status,
    statusText: '',
    headers: {},
    config,
    data: { error },
  })
}

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <HomePage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
  return { client }
}

describe('HomePage', () => {
  beforeEach(() => {
    vi.mocked(healthApi.ready).mockReset()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('says it is loading until the first report arrives', () => {
    // Arrange
    vi.mocked(healthApi.ready).mockReturnValue(new Promise<HealthReport>(() => {}))

    // Act
    renderPage()

    // Assert
    expect(screen.getByText('Loading…')).toBeInTheDocument()
  })

  it('shows Ready when every check passes', async () => {
    // Arrange
    vi.mocked(healthApi.ready).mockResolvedValue(healthy)

    // Act
    renderPage()

    // Assert
    expect(await screen.findByText('Ready')).toBeInTheDocument()
    expect(screen.queryByText('The database is reachable and up to date.')).not.toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('shows Not ready with the failing check description', async () => {
    // Arrange
    vi.mocked(healthApi.ready).mockResolvedValue(unhealthy)

    // Act
    renderPage()

    // Assert
    expect(await screen.findByText('Not ready')).toBeInTheDocument()
    expect(screen.getByText('The database has 1 pending migration.')).toBeInTheDocument()
  })

  it('shows Ready, with warnings and the check description when a check is degraded', async () => {
    // Arrange
    vi.mocked(healthApi.ready).mockResolvedValue({
      status: 'Degraded',
      checks: [{ name: 'database', status: 'Degraded', description: 'The database is slow to answer.' }],
    })

    // Act
    renderPage()

    // Assert
    expect(await screen.findByText('Ready, with warnings')).toBeInTheDocument()
    expect(screen.getByText('The database is slow to answer.')).toBeInTheDocument()
  })

  it('names the failing check when it carries no description', async () => {
    // Arrange
    vi.mocked(healthApi.ready).mockResolvedValue({
      status: 'Unhealthy',
      checks: [{ name: 'database', status: 'Unhealthy', description: null }],
    })

    // Act
    renderPage()

    // Assert
    expect(await screen.findByText('Not ready')).toBeInTheDocument()
    expect(screen.getByText('The database check failed.')).toBeInTheDocument()
  })

  it('checks again after a minute and shows the new status', async () => {
    // Arrange
    vi.useFakeTimers({ shouldAdvanceTime: true })
    vi.mocked(healthApi.ready).mockResolvedValueOnce(healthy).mockResolvedValueOnce(unhealthy)
    renderPage()
    await screen.findByText('Ready')

    // Act
    await vi.advanceTimersByTimeAsync(60_000)

    // Assert
    expect(await screen.findByText('Not ready')).toBeInTheDocument()
    expect(screen.getByText('The database has 1 pending migration.')).toBeInTheDocument()
    expect(screen.queryByText('Ready')).not.toBeInTheDocument()
    expect(healthApi.ready).toHaveBeenCalledTimes(2)
  })

  it('says what failed and the API sentence, with Try again, when the first load fails', async () => {
    // Arrange
    vi.mocked(healthApi.ready).mockRejectedValue(
      apiError(500, 'The server hit an unexpected error. Quote trace id abc-123 when reporting it.'),
    )

    // Act
    renderPage()

    // Assert
    const state = await screen.findByRole('alert')
    expect(state).toHaveTextContent('Could not check the service status.')
    expect(state).toHaveTextContent('The server hit an unexpected error. Quote trace id abc-123 when reporting it.')
    expect(screen.getByRole('button', { name: 'Try again' })).toBeEnabled()
    expect(screen.queryByText('Ready')).not.toBeInTheDocument()
  })

  it('checks again on Try again and shows the status', async () => {
    // Arrange
    vi.mocked(healthApi.ready).mockRejectedValueOnce(new AxiosError('Network Error', 'ERR_NETWORK'))
    vi.mocked(healthApi.ready).mockResolvedValueOnce(healthy)
    renderPage()
    const user = userEvent.setup()
    await screen.findByRole('alert')

    // Act
    await user.click(screen.getByRole('button', { name: 'Try again' }))

    // Assert
    expect(await screen.findByText('Ready')).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('keeps the failed first load on screen through the minute\'s check, without reading Retrying…', async () => {
    // Arrange
    vi.useFakeTimers({ shouldAdvanceTime: true })
    vi.mocked(healthApi.ready)
      .mockRejectedValueOnce(new AxiosError('Network Error', 'ERR_NETWORK'))
      .mockReturnValueOnce(new Promise<HealthReport>(() => {}))
    renderPage()
    await screen.findByRole('alert')

    // Act
    await vi.advanceTimersByTimeAsync(60_000)

    // Assert
    expect(healthApi.ready).toHaveBeenCalledTimes(2)
    expect(screen.getByRole('alert')).toHaveTextContent('Could not check the service status.')
    expect(screen.getByRole('button', { name: 'Try again' })).toBeEnabled()
    expect(screen.queryByText('Loading…')).not.toBeInTheDocument()
  })

  it('keeps the last known status on screen under a banner when a refresh fails', async () => {
    // Arrange
    vi.mocked(healthApi.ready).mockResolvedValueOnce(healthy)
    const { client } = renderPage()
    await screen.findByText('Ready')
    vi.mocked(healthApi.ready).mockRejectedValueOnce(new AxiosError('Network Error', 'ERR_NETWORK'))

    // Act
    await client.refetchQueries()

    // Assert
    const banner = await screen.findByRole('alert')
    expect(banner).toHaveTextContent('Refresh failed, showing the last known status.')
    expect(banner).toHaveTextContent('The server could not be reached.')
    expect(screen.getByText('Ready')).toBeInTheDocument()
  })

  it('asks again from Retry on the banner, and drops the banner once the status is back', async () => {
    // Arrange
    vi.mocked(healthApi.ready).mockResolvedValueOnce(healthy)
    const { client } = renderPage()
    const user = userEvent.setup()
    await screen.findByText('Ready')
    vi.mocked(healthApi.ready).mockRejectedValueOnce(new AxiosError('Network Error', 'ERR_NETWORK'))
    await client.refetchQueries()
    await screen.findByRole('alert')
    vi.mocked(healthApi.ready).mockResolvedValueOnce(unhealthy)

    // Act
    await user.click(screen.getByRole('button', { name: 'Retry' }))

    // Assert
    expect(await screen.findByText('Not ready')).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(healthApi.ready).toHaveBeenCalledTimes(3)
  })
})
