import { AxiosError, type AxiosAdapter, type AxiosResponse, type InternalAxiosRequestConfig } from 'axios'
import { api } from '@/lib/api'
import { useAuthStore } from '@/lib/auth-store'
import { useConnectionStore } from '@/lib/connection-store'
import { i18n } from '@/lib/i18n'
import { queryClient } from '@/lib/query-client'
import { owner, sessionFor, signInAs } from '@/test/auth-session'
import { resetConnectionStore } from '@/test/connection'

interface Reply {
  status: number
  data?: unknown
}

/**
 * A fake server behind the real client: `reply` answers every request, the refresh included, so the
 * interceptors run exactly as in the browser. A replaced adapter also replaces axios's status check,
 * so this applies `validateStatus` itself and rejects the way axios would.
 */
function serve(reply: (config: InternalAxiosRequestConfig) => Reply | Promise<Reply>) {
  const adapter = vi.fn<AxiosAdapter>(async (config) => {
    const { status, data } = await reply(config)
    const response: AxiosResponse = { status, statusText: '', headers: {}, config, data }
    if (config.validateStatus && !config.validateStatus(status)) {
      const code = status >= 500 ? AxiosError.ERR_BAD_RESPONSE : AxiosError.ERR_BAD_REQUEST
      throw new AxiosError(`Request failed with status code ${status}`, code, config, null, response)
    }
    return response
  })
  api.defaults.adapter = adapter
  return adapter
}

const REFRESH_URL = '/api/auth/refresh'
const signInToContinue: Reply = { status: 401, data: { error: 'Sign in to continue.' } }
const sessionEnded: Reply = { status: 401, data: { error: 'Your session has ended. Sign in again.' } }
const refreshedMomentsAgo: Reply = { status: 409, data: { error: 'This session was refreshed a moment ago. Try again.' } }

const bearerOf = (config: InternalAxiosRequestConfig) => config.headers.get('Authorization')

/** The Authorization header of every request the fake server received at `url`, in order. */
function bearersSentTo(adapter: ReturnType<typeof serve>, url: string) {
  return adapter.mock.calls.filter(([config]) => config.url === url).map(([config]) => bearerOf(config))
}

/** Answers with `token-2` to a refresh, and to other requests with 200 only for `token-2`. */
function serverThatRefreshesTo(refresh: () => Reply | Promise<Reply> = () => ({ status: 200, data: sessionFor(owner, 'token-2') })) {
  return serve((config) => {
    if (config.url === REFRESH_URL) return refresh()
    return bearerOf(config) === 'Bearer token-2' ? { status: 200, data: `answer from ${config.url}` } : signInToContinue
  })
}

describe('api', () => {
  const originalAdapter = api.defaults.adapter

  beforeEach(() => {
    signInAs(owner, 'token-1')
  })

  afterEach(() => {
    api.defaults.adapter = originalAdapter
    Reflect.deleteProperty(navigator, 'locks')
    queryClient.clear()
    resetConnectionStore()
  })

  it('sends the access token as a bearer header', async () => {
    // Arrange
    const adapter = serve(() => ({ status: 200 }))

    // Act
    await api.get('/api/things')

    // Assert
    expect(bearersSentTo(adapter, '/api/things')).toEqual(['Bearer token-1'])
  })

  it('sends the UI locale as Accept-Language on every request', async () => {
    // Arrange
    const adapter = serve(() => ({ status: 200 }))

    // Act
    await api.get('/api/things')
    await i18n.changeLanguage('uk-UA')
    try {
      await api.put('/api/other-things', {})
    } finally {
      await i18n.changeLanguage('en-US')
    }

    // Assert
    expect(adapter.mock.calls.map(([config]) => [config.url, config.headers.get('Accept-Language')])).toEqual([
      ['/api/things', 'en-US'],
      ['/api/other-things', 'uk-UA'],
    ])
  })

  it('refreshes once and retries once with the new token when a request is refused with 401', async () => {
    // Arrange
    const adapter = serverThatRefreshesTo()

    // Act
    const response = await api.get('/api/things')

    // Assert
    expect(response.data).toBe('answer from /api/things')
    expect(bearersSentTo(adapter, REFRESH_URL)).toHaveLength(1)
    expect(bearersSentTo(adapter, '/api/things')).toEqual(['Bearer token-1', 'Bearer token-2'])
    expect(useAuthStore.getState()).toMatchObject({ accessToken: 'token-2', status: 'authenticated' })
  })

  it('shares one refresh between requests refused while it is under way', async () => {
    // Arrange
    let answerRefresh!: () => void
    const refreshAnswered = new Promise<void>((resolve) => { answerRefresh = resolve })
    const adapter = serverThatRefreshesTo(async () => {
      await refreshAnswered
      return { status: 200, data: sessionFor(owner, 'token-2') }
    })
    const urls = ['/api/things', '/api/other-things', '/api/more-things']

    // Act
    const responses = Promise.all(urls.map((url) => api.get(url)))
    await vi.waitFor(() => expect(bearersSentTo(adapter, REFRESH_URL)).toHaveLength(1))
    await vi.waitFor(() => expect(urls.flatMap((url) => bearersSentTo(adapter, url))).toHaveLength(3))
    answerRefresh()
    const answers = await responses

    // Assert
    expect(answers.map((response) => response.data)).toEqual(urls.map((url) => `answer from ${url}`))
    expect(bearersSentTo(adapter, REFRESH_URL)).toHaveLength(1)
    expect(urls.map((url) => bearersSentTo(adapter, url))).toEqual(urls.map(() => ['Bearer token-1', 'Bearer token-2']))
  })

  it('uses a token another request installed meanwhile instead of refreshing again', async () => {
    // Arrange
    const adapter = serve((config) => {
      if (bearerOf(config) === 'Bearer token-2') return { status: 200, data: 'answer' }
      // Another request finished its refresh while this one was on the way with the old token.
      useAuthStore.setState({ accessToken: 'token-2' })
      return signInToContinue
    })

    // Act
    const response = await api.get('/api/things')

    // Assert
    expect(response.data).toBe('answer')
    expect(bearersSentTo(adapter, REFRESH_URL)).toEqual([])
    expect(bearersSentTo(adapter, '/api/things')).toEqual(['Bearer token-1', 'Bearer token-2'])
  })

  it('retries a refresh answered with 409, when another tab has just rotated the cookie', async () => {
    // Arrange
    let refreshes = 0
    const adapter = serverThatRefreshesTo(() => {
      refreshes += 1
      return refreshes < 3 ? refreshedMomentsAgo : { status: 200, data: sessionFor(owner, 'token-2') }
    })

    // Act
    const response = await api.get('/api/things')

    // Assert
    expect(response.data).toBe('answer from /api/things')
    expect(bearersSentTo(adapter, REFRESH_URL)).toHaveLength(3)
    expect(useAuthStore.getState().accessToken).toBe('token-2')
  })

  it('gives the caller the 409 and keeps the session when the refresh keeps conflicting', async () => {
    // Arrange
    const adapter = serverThatRefreshesTo(() => refreshedMomentsAgo)

    // Act
    const result = api.get('/api/things')

    // Assert
    await expect(result).rejects.toMatchObject({ response: { status: 409 } })
    // The first attempt and one retry after each of the 0, 100 and 250 ms pauses.
    expect(bearersSentTo(adapter, REFRESH_URL)).toHaveLength(4)
    expect(useAuthStore.getState()).toMatchObject({ accessToken: 'token-1', status: 'authenticated' })
  })

  it('signs out, empties the cache and rejects when the refresh answers 401', async () => {
    // Arrange
    const adapter = serverThatRefreshesTo(() => sessionEnded)
    queryClient.setQueryData(['settings', 'users'], 'cached for the signed-in user')

    // Act
    const result = api.get('/api/things')

    // Assert
    await expect(result).rejects.toMatchObject({ response: sessionEnded })
    expect(useAuthStore.getState()).toMatchObject({ accessToken: null, user: null, status: 'anonymous' })
    expect(queryClient.getQueryCache().getAll()).toEqual([])
    expect(bearersSentTo(adapter, '/api/things')).toHaveLength(1)
  })

  it('keeps the session when the server cannot be reached for the refresh', async () => {
    // Arrange
    serve((config) => {
      if (config.url === REFRESH_URL) throw new AxiosError('Network Error', AxiosError.ERR_NETWORK, config)
      return signInToContinue
    })

    // Act
    const result = api.get('/api/things')

    // Assert
    await expect(result).rejects.toMatchObject({ code: AxiosError.ERR_NETWORK })
    expect(useAuthStore.getState()).toMatchObject({ accessToken: 'token-1', status: 'authenticated' })
    // MAM-16 — the refresh is a request like any other: the offline strip shows for it too.
    expect(useConnectionStore.getState().unreachable).toBe(true)
  })

  it('hands a second 401 to the caller instead of refreshing again', async () => {
    // Arrange
    const adapter = serve((config) =>
      config.url === REFRESH_URL ? { status: 200, data: sessionFor(owner, 'token-2') } : signInToContinue)

    // Act
    const result = api.get('/api/things')

    // Assert
    await expect(result).rejects.toMatchObject({ response: signInToContinue })
    expect(bearersSentTo(adapter, REFRESH_URL)).toHaveLength(1)
    expect(bearersSentTo(adapter, '/api/things')).toEqual(['Bearer token-1', 'Bearer token-2'])
  })

  it.each([
    '/api/auth/sign-in',
    '/api/auth/refresh',
    '/api/auth/sign-out',
    '/api/setup',
  ])('never refreshes after a 401 from %s', async (url) => {
    // Arrange
    const adapter = serve(() => ({ status: 401, data: { error: 'The user name or password is incorrect.' } }))

    // Act
    const result = api.post(url)

    // Assert
    await expect(result).rejects.toMatchObject({ response: { status: 401 } })
    expect(adapter).toHaveBeenCalledOnce()
    expect(useAuthStore.getState()).toMatchObject({ accessToken: 'token-1', status: 'authenticated' })
  })

  // MAM-16 — the Profile page's calls live under /api/auth/ too, but their 401 is an expired access
  // token like anyone else's: left out, the page would read "Sign in to continue." after 15 minutes.
  it.each([
    ['get', '/api/auth/me'],
    ['post', '/api/auth/password'],
  ] as const)('refreshes once and retries once after a 401 from %s %s', async (method, url) => {
    // Arrange
    const adapter = serverThatRefreshesTo()

    // Act
    const response = await api[method](url)

    // Assert
    expect(response.data).toBe(`answer from ${url}`)
    expect(bearersSentTo(adapter, REFRESH_URL)).toHaveLength(1)
    expect(bearersSentTo(adapter, url)).toEqual(['Bearer token-1', 'Bearer token-2'])
  })

  it.each([
    ['this tab', () => useAuthStore.getState().clear({ deliberate: true })],
    ['another tab', () => {
      const otherTab = new BroadcastChannel('mam-auth')
      otherTab.postMessage('signed-out')
      otherTab.close()
    }],
  ])('installs nothing and retries nothing when %s signs out while the refresh is under way', async (_label, signOut) => {
    // Arrange
    let answerRefresh!: () => void
    const refreshAnswered = new Promise<void>((resolve) => { answerRefresh = resolve })
    const adapter = serverThatRefreshesTo(async () => {
      await refreshAnswered
      return { status: 200, data: sessionFor(owner, 'token-2') }
    })
    const result = api.get('/api/things')
    await vi.waitFor(() => expect(bearersSentTo(adapter, REFRESH_URL)).toHaveLength(1))

    // Act
    signOut()
    await vi.waitFor(() => expect(useAuthStore.getState().status).toBe('anonymous'))
    answerRefresh()

    // Assert
    await expect(result).rejects.toThrow('Signed out while the session was being refreshed.')
    expect(useAuthStore.getState()).toMatchObject({ accessToken: null, user: null, status: 'anonymous' })
    expect(bearersSentTo(adapter, '/api/things')).toEqual(['Bearer token-1'])
  })

  it('refreshes inside the cross-tab Web Lock where the browser has one', async () => {
    // Arrange
    let insideLock = false
    const request = vi.fn(async (_name: string, task: () => Promise<unknown>) => {
      insideLock = true
      try {
        return await task()
      } finally {
        insideLock = false
      }
    })
    Object.defineProperty(navigator, 'locks', { value: { request }, configurable: true })
    const refreshedInsideLock: boolean[] = []
    const adapter = serverThatRefreshesTo(() => {
      refreshedInsideLock.push(insideLock)
      return { status: 200, data: sessionFor(owner, 'token-2') }
    })

    // Act
    await api.get('/api/things')

    // Assert
    expect(request).toHaveBeenCalledExactlyOnceWith('mam-auth-refresh', expect.any(Function))
    // Taking the lock and then refreshing after it was released would leave the tabs racing.
    expect(refreshedInsideLock).toEqual([true])
    expect(bearersSentTo(adapter, REFRESH_URL)).toHaveLength(1)
  })
})

describe('api, on whether the server can be reached', () => {
  const originalAdapter = api.defaults.adapter

  beforeEach(() => {
    signInAs(owner, 'token-1')
  })

  afterEach(() => {
    api.defaults.adapter = originalAdapter
    queryClient.clear()
    resetConnectionStore()
  })

  it('marks the server unreachable when a request gets no response', async () => {
    // Arrange
    serve((config) => {
      throw new AxiosError('Network Error', AxiosError.ERR_NETWORK, config)
    })

    // Act
    const result = api.get('/api/things')

    // Assert
    await expect(result).rejects.toMatchObject({ code: AxiosError.ERR_NETWORK })
    expect(useConnectionStore.getState().unreachable).toBe(true)
  })

  it('marks the server unreachable when a proxy in front of it answers 502 with an empty body', async () => {
    // Arrange — the Vite dev proxy, and a reverse proxy, while the API is stopped.
    serve(() => ({ status: 502, data: '' }))

    // Act
    const result = api.get('/api/things')

    // Assert
    await expect(result).rejects.toMatchObject({ response: { status: 502 } })
    expect(useConnectionStore.getState().unreachable).toBe(true)
  })

  it.each([
    ['a 200', { status: 200, data: 'answer' }],
    ['a refusal', { status: 404, data: '' }],
    ["the API's own 502 with its sentence", { status: 502, data: { error: 'Jellyfin could not be reached.' } }],
  ])('marks it reachable again on the next answer, %s included', async (_, reply) => {
    // Arrange
    useConnectionStore.setState({ unreachable: true })
    serve(() => reply)

    // Act
    await api.get('/api/things').catch(() => {})

    // Assert
    expect(useConnectionStore.getState().unreachable).toBe(false)
  })

  it('leaves the state alone for a cancelled request, which nobody waits for', async () => {
    // Arrange
    serve((config) => {
      throw new AxiosError('canceled', AxiosError.ERR_CANCELED, config)
    })

    // Act
    const result = api.get('/api/things')

    // Assert
    await expect(result).rejects.toMatchObject({ code: AxiosError.ERR_CANCELED })
    expect(useConnectionStore.getState().unreachable).toBe(false)
  })
})
