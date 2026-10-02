import { onlineManager, QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { AxiosError, type AxiosAdapter, type AxiosResponse, type InternalAxiosRequestConfig } from 'axios'
import { OfflineStrip } from '@/components/OfflineStrip'
import { api } from '@/lib/api'
import { useAuthStore } from '@/lib/auth-store'
import { useConnectionStore } from '@/lib/connection-store'
import { queryClient } from '@/lib/query-client'
import { owner, resetAuthStore, signInAs } from '@/test/auth-session'
import { resetConnectionStore } from '@/test/connection'

const SENTENCE = 'The server could not be reached. Check that it is running, then try again.'
const HEALTH_URL = '/api/health'

/**
 * A fake server behind the real client, so the interceptor that tells the strip runs as in the
 * browser: no response while it is down (`up` false), a liveness report once it is up. With `hold`
 * set, the next request waits until the test calls `release`.
 */
function fakeServer() {
  const server = {
    up: false,
    hold: false,
    release: () => {},
    /** How many times the strip's probe has asked. */
    probes: () => adapter.mock.calls.filter(([config]) => config.url === HEALTH_URL).length,
  }
  const adapter = vi.fn<AxiosAdapter>(async (config: InternalAxiosRequestConfig) => {
    if (server.hold) {
      server.hold = false
      await new Promise<void>((resolve) => { server.release = resolve })
    }
    if (!server.up) throw new AxiosError('Network Error', AxiosError.ERR_NETWORK, config)
    const response: AxiosResponse = { status: 200, statusText: '', headers: {}, config, data: { status: 'Healthy', checks: [] } }
    return response
  })
  api.defaults.adapter = adapter
  return server
}

function renderStrip() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <OfflineStrip />
    </QueryClientProvider>,
  )
}

/** A page's request that gets no answer, the way the strip learns of it. */
async function requestThatFails() {
  await act(async () => {
    await api.get('/api/things').catch(() => {})
  })
}

describe('OfflineStrip', () => {
  const originalAdapter = api.defaults.adapter

  beforeEach(() => {
    signInAs(owner)
  })

  afterEach(() => {
    vi.useRealTimers()
    onlineManager.setOnline(true)
    api.defaults.adapter = originalAdapter
    queryClient.clear()
    resetConnectionStore()
    resetAuthStore()
  })

  it('shows nothing while the server answers', () => {
    // Arrange
    fakeServer()

    // Act
    renderStrip()

    // Assert
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('says the server cannot be reached, that it keeps asking, and offers Retry now after a request gets no response', async () => {
    // Arrange
    fakeServer()
    renderStrip()

    // Act
    await requestThatFails()

    // Assert
    const strip = await screen.findByRole('alert')
    expect(strip).toHaveTextContent(`${SENTENCE} Trying again every few seconds.`)
    expect(screen.getByRole('button', { name: 'Retry now' })).toBeEnabled()
  })

  it('asks the health endpoint at once and every 5 seconds while the server cannot be reached', async () => {
    // Arrange
    vi.useFakeTimers({ shouldAdvanceTime: true })
    const server = fakeServer()
    renderStrip()
    await requestThatFails()
    await vi.waitFor(() => expect(server.probes()).toBe(1))

    // Act
    await act(async () => {
      await vi.advanceTimersByTimeAsync(10_000)
    })

    // Assert
    expect(server.probes()).toBe(3)
    expect(screen.getByRole('alert')).toBeInTheDocument()
  })

  it('goes on the first answer, and stops asking', async () => {
    // Arrange
    vi.useFakeTimers({ shouldAdvanceTime: true })
    const server = fakeServer()
    renderStrip()
    await requestThatFails()
    await vi.waitFor(() => expect(server.probes()).toBe(1))
    server.up = true

    // Act
    await act(async () => {
      await vi.advanceTimersByTimeAsync(5_000)
    })

    // Assert
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    await act(async () => {
      await vi.advanceTimersByTimeAsync(15_000)
    })
    expect(server.probes()).toBe(2)
  })

  it('goes when any other request is answered too', async () => {
    // Arrange
    const server = fakeServer()
    renderStrip()
    await requestThatFails()
    await screen.findByRole('alert')
    server.up = true

    // Act
    await act(async () => {
      await api.get('/api/things')
    })

    // Assert
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('asks at once from Retry now, reading Retrying… until the answer arrives', async () => {
    // Arrange
    const user = userEvent.setup()
    const server = fakeServer()
    renderStrip()
    await requestThatFails()
    await screen.findByRole('alert')
    await vi.waitFor(() => expect(server.probes()).toBe(1))
    server.hold = true

    // Act
    await user.click(screen.getByRole('button', { name: 'Retry now' }))

    // Assert
    expect(await screen.findByRole('button', { name: 'Retrying…' })).toBeDisabled()
    expect(server.probes()).toBe(2)
    server.up = true
    act(() => server.release())
    await vi.waitFor(() => expect(screen.queryByRole('alert')).not.toBeInTheDocument())
  })

  it('still asks while the browser says it is offline, so Retry now never waits on nothing', async () => {
    // Arrange
    const user = userEvent.setup()
    const server = fakeServer()
    renderStrip()
    await requestThatFails()
    await vi.waitFor(() => expect(server.probes()).toBe(1))
    act(() => onlineManager.setOnline(false))

    // Act
    await user.click(screen.getByRole('button', { name: 'Retry now' }))

    // Assert
    await vi.waitFor(() => expect(server.probes()).toBe(2))
    expect(await screen.findByRole('button', { name: 'Retry now' })).toBeEnabled()
  })

  it.each([
    ['restores the session', 'initializing'],
    ['says the session could not be restored', 'unavailable'],
  ] as const)('stays out of the way while the session gate %s, which says the same itself', async (_, status) => {
    // Arrange
    const server = fakeServer()
    useAuthStore.setState({ accessToken: null, user: null, status })
    renderStrip()

    // Act
    await requestThatFails()

    // Assert
    expect(useConnectionStore.getState().unreachable).toBe(true)
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(server.probes()).toBe(0)
  })

  it('shows on the sign-in page too, once the session is known to be gone', async () => {
    // Arrange
    fakeServer()
    useAuthStore.setState({ accessToken: null, user: null, status: 'anonymous' })
    renderStrip()

    // Act
    await requestThatFails()

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent(SENTENCE)
  })
})
