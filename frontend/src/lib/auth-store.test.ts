import { authApi } from '@/lib/auth-api'
import { useAuthStore } from '@/lib/auth-store'
import { queryClient } from '@/lib/query-client'
import type { SignInResponse } from '@/lib/types'
import { apiError, networkError } from '@/test/api-error'
import { member, owner, resetAuthStore, sessionFor, signInAs } from '@/test/auth-session'

vi.mock('@/lib/auth-api')

/** Stands in for another tab of the same browser: the channel every tab of the app listens on. */
function openOtherTab() {
  const channel = new BroadcastChannel('mam-auth')
  const received = new Promise<unknown>((resolve) => {
    channel.onmessage = (event: MessageEvent) => resolve(event.data)
  })
  return { channel, received }
}

describe('useAuthStore', () => {
  beforeEach(() => {
    resetAuthStore()
    vi.mocked(authApi.refresh).mockReset()
  })

  afterEach(() => {
    queryClient.clear()
  })

  describe('initializeSession', () => {
    it('restores the session the refresh cookie still holds', async () => {
      // Arrange
      vi.mocked(authApi.refresh).mockResolvedValue(sessionFor(owner, 'token-1'))

      // Act
      await useAuthStore.getState().initializeSession()

      // Assert
      expect(useAuthStore.getState()).toMatchObject({ status: 'authenticated', accessToken: 'token-1', user: owner })
    })

    it('turns anonymous when the server says there is no session (401)', async () => {
      // Arrange
      vi.mocked(authApi.refresh).mockRejectedValue(apiError(401, 'Your session has ended. Sign in again.'))

      // Act
      await useAuthStore.getState().initializeSession()

      // Assert
      expect(useAuthStore.getState()).toMatchObject({ status: 'anonymous', accessToken: null, user: null })
    })

    it.each([
      ['the server cannot be reached', networkError()],
      ['the server fails (500)', apiError(500, 'The server hit an unexpected error.')],
      ['a proxy answers 502', apiError(502)],
    ])('turns unavailable when %s', async (_label, error) => {
      // Arrange
      vi.mocked(authApi.refresh).mockRejectedValue(error)

      // Act
      await useAuthStore.getState().initializeSession()

      // Assert
      expect(useAuthStore.getState()).toMatchObject({ status: 'unavailable', accessToken: null })
    })

    it('tries again when another tab refreshed the same cookie a moment ago (409)', async () => {
      // Arrange
      vi.mocked(authApi.refresh)
        .mockRejectedValueOnce(apiError(409, 'This session was refreshed a moment ago. Try again.'))
        .mockResolvedValueOnce(sessionFor(owner, 'token-1'))

      // Act
      await useAuthStore.getState().initializeSession()

      // Assert
      expect(useAuthStore.getState()).toMatchObject({ status: 'authenticated', accessToken: 'token-1' })
      expect(authApi.refresh).toHaveBeenCalledTimes(2)
    })

    it('asks the server once when two callers restore at the same time', async () => {
      // Arrange
      vi.mocked(authApi.refresh).mockResolvedValue(sessionFor(owner, 'token-1'))

      // Act
      await Promise.all([useAuthStore.getState().initializeSession(), useAuthStore.getState().initializeSession()])

      // Assert
      expect(authApi.refresh).toHaveBeenCalledOnce()
      expect(useAuthStore.getState().status).toBe('authenticated')
    })

    it('stays signed out when another tab signs out while the session is being restored', async () => {
      // Arrange
      let answer!: (session: SignInResponse) => void
      vi.mocked(authApi.refresh).mockReturnValue(new Promise<SignInResponse>((resolve) => { answer = resolve }))
      const restoring = useAuthStore.getState().initializeSession()
      await vi.waitFor(() => expect(authApi.refresh).toHaveBeenCalledOnce())
      const otherTab = new BroadcastChannel('mam-auth')

      // Act
      otherTab.postMessage('signed-out')
      await vi.waitFor(() => expect(useAuthStore.getState().status).toBe('anonymous'))
      otherTab.close()
      answer(sessionFor(owner, 'token-1'))
      await restoring

      // Assert
      expect(useAuthStore.getState()).toMatchObject({ status: 'anonymous', accessToken: null, user: null })
      expect(authApi.refresh).toHaveBeenCalledOnce()
    })

    it('does not tell the other tabs anything when a fresh tab finds no session', async () => {
      // Arrange
      vi.mocked(authApi.refresh).mockRejectedValue(apiError(401, 'Your session has ended. Sign in again.'))
      const otherTab = openOtherTab()
      const onMessage = vi.fn<(event: MessageEvent) => void>()
      otherTab.channel.addEventListener('message', onMessage)

      // Act
      await useAuthStore.getState().initializeSession()
      // A message would have arrived by the time a later one does.
      const probe = new BroadcastChannel('mam-auth')
      probe.postMessage('probe')
      await otherTab.received
      probe.close()
      otherTab.channel.close()

      // Assert
      expect(onMessage.mock.calls.map(([event]) => event.data)).toEqual(['probe'])
    })
  })

  describe('setSession', () => {
    it('drops what was cached for the previous user when a different user signs in', () => {
      // Arrange
      signInAs(member)
      queryClient.setQueryData(['settings', 'users'], 'cached for olena')

      // Act
      useAuthStore.getState().setSession(sessionFor(owner))

      // Assert
      expect(queryClient.getQueryCache().getAll()).toEqual([])
      expect(useAuthStore.getState()).toMatchObject({ status: 'authenticated', user: owner })
    })

    it('forgets a deliberate sign-out, so a later ended session remembers its page again', () => {
      // Arrange
      signInAs(owner)
      useAuthStore.getState().clear({ deliberate: true })

      // Act
      useAuthStore.getState().setSession(sessionFor(member))

      // Assert
      expect(useAuthStore.getState()).toMatchObject({ status: 'authenticated', user: member, deliberateSignOut: false })
    })

    it('keeps the cache when the same user gets a fresh token', () => {
      // Arrange
      signInAs(owner, 'token-1')
      queryClient.setQueryData(['settings', 'users'], 'cached for vasyl')

      // Act
      useAuthStore.getState().setSession(sessionFor(owner, 'token-2'))

      // Assert
      expect(queryClient.getQueryData(['settings', 'users'])).toBe('cached for vasyl')
      expect(useAuthStore.getState().accessToken).toBe('token-2')
    })
  })

  describe('clear', () => {
    it('empties the query cache, signs this tab out and tells the other tabs', async () => {
      // Arrange
      signInAs(owner)
      queryClient.setQueryData(['settings', 'users'], 'cached for vasyl')
      const otherTab = openOtherTab()

      // Act
      useAuthStore.getState().clear()
      const message = await otherTab.received
      otherTab.channel.close()

      // Assert
      expect(queryClient.getQueryCache().getAll()).toEqual([])
      expect(useAuthStore.getState()).toMatchObject({ status: 'anonymous', accessToken: null, user: null })
      expect(useAuthStore.getState().deliberateSignOut).toBe(false)
      expect(message).toBe('signed-out')
    })

    it('marks a sign-out the user asked for as deliberate', () => {
      // Arrange
      signInAs(owner)

      // Act
      useAuthStore.getState().clear({ deliberate: true })

      // Assert
      expect(useAuthStore.getState()).toMatchObject({ status: 'anonymous', accessToken: null, deliberateSignOut: true })
    })

    it('signs this tab out when another tab signs out', async () => {
      // Arrange
      signInAs(owner)
      queryClient.setQueryData(['settings', 'users'], 'cached for vasyl')
      const otherTab = new BroadcastChannel('mam-auth')

      // Act
      otherTab.postMessage('signed-out')
      await vi.waitFor(() => expect(useAuthStore.getState().status).toBe('anonymous'))
      otherTab.close()

      // Assert
      expect(useAuthStore.getState()).toMatchObject({ accessToken: null, user: null, deliberateSignOut: true })
      expect(queryClient.getQueryCache().getAll()).toEqual([])
    })
  })
})

describe('useAuthStore without BroadcastChannel', () => {
  // The store picks its channel when the module loads, so these tests load a fresh copy of it in a
  // browser that has none.
  async function loadStoreWithoutBroadcastChannel() {
    vi.stubGlobal('BroadcastChannel', undefined)
    vi.resetModules()
    const [store, cache] = await Promise.all([import('@/lib/auth-store'), import('@/lib/query-client')])
    return { useAuthStore: store.useAuthStore, queryClient: cache.queryClient }
  }

  afterEach(() => {
    vi.unstubAllGlobals()
    localStorage.clear()
  })

  it('tells the other tabs through localStorage', async () => {
    // Arrange
    const { useAuthStore: store } = await loadStoreWithoutBroadcastChannel()
    store.setState({ accessToken: 'token-1', user: owner, status: 'authenticated' })

    // Act
    store.getState().clear()

    // Assert
    expect(localStorage.getItem('mam:auth:signed-out')).not.toBeNull()
    expect(store.getState().status).toBe('anonymous')
  })

  it('signs this tab out on the storage event of another tab signing out', async () => {
    // Arrange
    const { useAuthStore: store, queryClient: cache } = await loadStoreWithoutBroadcastChannel()
    store.setState({ accessToken: 'token-1', user: owner, status: 'authenticated' })
    cache.setQueryData(['settings', 'users'], 'cached for vasyl')

    // Act
    window.dispatchEvent(new StorageEvent('storage', { key: 'mam:auth:signed-out', newValue: String(Date.now()) }))

    // Assert
    expect(store.getState()).toMatchObject({ status: 'anonymous', accessToken: null, user: null, deliberateSignOut: true })
    expect(cache.getQueryCache().getAll()).toEqual([])
  })
})
