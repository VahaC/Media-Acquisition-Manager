import { refreshSession } from '@/lib/api'
import { authApi } from '@/lib/auth-api'
import {
  adoptAccountLanguage,
  holdLanguage,
  languageGeneration,
  languageSaveSettled,
  languageSaveStarted,
  useAuthStore,
} from '@/lib/auth-store'
import { i18n } from '@/lib/i18n'
import { qk } from '@/lib/queries/keys'
import { queryClient } from '@/lib/query-client'
import type { CurrentUserProfile, SignInResponse } from '@/lib/types'
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

describe('useAuthStore and the account language', () => {
  // jsdom's browser prefers en-US, then en: an account without a choice runs the UI in en-US.
  beforeEach(() => {
    resetAuthStore()
  })

  afterEach(async () => {
    queryClient.clear()
    await i18n.changeLanguage('en-US')
  })

  it('runs the UI in the language the account chose once its session is installed', async () => {
    // Act
    useAuthStore.getState().setSession(sessionFor({ ...owner, language: 'uk' }))

    // Assert
    await vi.waitFor(() => expect(i18n.language).toBe('uk'))
    expect(document.documentElement.lang).toBe('uk')
  })

  it('runs the UI in the browser\'s language for an account that follows the browser', async () => {
    // Arrange
    useAuthStore.getState().setSession(sessionFor({ ...owner, language: 'uk' }))
    await vi.waitFor(() => expect(i18n.language).toBe('uk'))

    // Act
    useAuthStore.getState().setSession(sessionFor(member))

    // Assert
    await vi.waitFor(() => expect(i18n.language).toBe('en-US'))
  })

  it('keeps the browser\'s regional English for an account that chose English', async () => {
    // Act
    useAuthStore.getState().setSession(sessionFor({ ...owner, language: 'en' }))

    // Assert
    await vi.waitFor(() => expect(useAuthStore.getState().status).toBe('authenticated'))
    expect(i18n.language).toBe('en-US')
  })

  it('gives the page back to the browser\'s language on sign-out', async () => {
    // Arrange
    useAuthStore.getState().setSession(sessionFor({ ...owner, language: 'uk' }))
    await vi.waitFor(() => expect(i18n.language).toBe('uk'))

    // Act
    useAuthStore.getState().clear({ deliberate: true })

    // Assert
    await vi.waitFor(() => expect(i18n.language).toBe('en-US'))
  })

  it('puts a saved language on the user and the cached profile, runs the UI in it and tells the other tabs', async () => {
    // Arrange
    signInAs(owner)
    const profile: CurrentUserProfile = {
      ...owner,
      createdUtc: '2026-09-28T09:15:00Z',
      lastSignInUtc: null,
      sessionExpiresUtc: '2026-10-31T18:42:00Z',
    }
    queryClient.setQueryData(qk.auth.me, profile)
    const otherTab = openOtherTab()

    // Act
    await useAuthStore.getState().setLanguage('uk')
    const message = await otherTab.received
    otherTab.channel.close()

    // Assert
    expect(useAuthStore.getState().user).toEqual({ ...owner, language: 'uk' })
    expect(queryClient.getQueryData(qk.auth.me)).toEqual({ ...profile, language: 'uk' })
    expect(i18n.language).toBe('uk')
    expect(message).toEqual({ type: 'language', userId: owner.id, language: 'uk' })
  })

  it('follows a language another tab saved for the same user', async () => {
    // Arrange
    signInAs(owner)
    const otherTab = new BroadcastChannel('mam-auth')

    // Act
    otherTab.postMessage({ type: 'language', userId: owner.id, language: 'uk' })
    await vi.waitFor(() => expect(i18n.language).toBe('uk'))
    otherTab.close()

    // Assert
    expect(useAuthStore.getState().user).toEqual({ ...owner, language: 'uk' })
    expect(useAuthStore.getState().status).toBe('authenticated')
  })

  it('ignores a language another tab saved for a different user', async () => {
    // Arrange
    signInAs(member)
    const otherTab = new BroadcastChannel('mam-auth')
    const changed = vi.fn()
    i18n.on('languageChanged', changed)

    // Act
    otherTab.postMessage({ type: 'language', userId: owner.id, language: 'uk' })
    // A later message for this tab's user has arrived only after the first one was handled.
    otherTab.postMessage({ type: 'language', userId: member.id, language: 'en' })
    await vi.waitFor(() => expect(useAuthStore.getState().user?.language).toBe('en'))
    otherTab.close()
    i18n.off('languageChanged', changed)

    // Assert
    expect(changed).not.toHaveBeenCalled()
    expect(i18n.language).toBe('en-US')
  })
})

describe('the account language against answers older than a change', () => {
  // MAM-46 — an answer the server worked out before a language change in this tab (a save, another tab's
  // save followed) must not switch the tab back.
  beforeEach(() => {
    resetAuthStore()
    vi.mocked(authApi.refresh).mockReset()
  })

  afterEach(async () => {
    queryClient.clear()
    await i18n.changeLanguage('en-US')
  })

  it('takes the language a fresh profile answered when nothing changed since it was asked for', async () => {
    // Arrange
    signInAs({ ...owner, language: 'en' })
    const before = languageGeneration()

    // Act
    adoptAccountLanguage(owner.id, 'uk', before)

    // Assert
    await vi.waitFor(() => expect(i18n.language).toBe('uk'))
    expect(useAuthStore.getState().user?.language).toBe('uk')
  })

  it('takes no language a profile answered when the language changed after it was asked for', async () => {
    // Arrange
    signInAs({ ...owner, language: 'en' })
    const before = languageGeneration()
    await useAuthStore.getState().setLanguage('uk')

    // Act
    adoptAccountLanguage(owner.id, 'en', before)

    // Assert
    expect(useAuthStore.getState().user?.language).toBe('uk')
    expect(i18n.language).toBe('uk')
  })

  it('takes no language a profile answered while a save is under way, nor one asked for before it ended', () => {
    // Arrange
    signInAs({ ...owner, language: 'en' })
    languageSaveStarted()
    const askedDuringTheSave = languageGeneration()

    // Act
    adoptAccountLanguage(owner.id, 'uk', askedDuringTheSave)
    languageSaveSettled()
    adoptAccountLanguage(owner.id, 'uk', askedDuringTheSave)

    // Assert
    expect(useAuthStore.getState().user?.language).toBe('en')
    expect(i18n.language).toBe('en-US')
  })

  it('takes no language a profile of another user answered', () => {
    // Arrange
    signInAs({ ...member, language: 'en' })

    // Act
    adoptAccountLanguage(owner.id, 'uk', languageGeneration())

    // Assert
    expect(useAuthStore.getState().user?.language).toBe('en')
    expect(i18n.language).toBe('en-US')
  })

  it('keeps a language saved here over a refresh that was on its way meanwhile', async () => {
    // Arrange
    signInAs({ ...owner, language: 'en' }, 'token-1')
    let answer!: (session: SignInResponse) => void
    vi.mocked(authApi.refresh).mockReturnValue(new Promise<SignInResponse>((resolve) => { answer = resolve }))
    const refreshed = refreshSession('token-1')

    // Act
    await useAuthStore.getState().setLanguage('uk')
    // The server read the user before the save reached it.
    answer(sessionFor({ ...owner, language: 'en' }, 'token-2'))
    await refreshed

    // Assert
    expect(useAuthStore.getState()).toMatchObject({ accessToken: 'token-2', user: { ...owner, language: 'uk' } })
    expect(i18n.language).toBe('uk')
  })

  it('installs the language another tab saved while this tab was restoring its session', async () => {
    // Arrange
    let answer!: (session: SignInResponse) => void
    vi.mocked(authApi.refresh).mockReturnValue(new Promise<SignInResponse>((resolve) => { answer = resolve }))
    const restored = useAuthStore.getState().initializeSession()
    const otherTab = new BroadcastChannel('mam-auth')

    // Act
    otherTab.postMessage({ type: 'language', userId: owner.id, language: 'uk' })
    // Time for the message to arrive. With no user yet, the tab only notes it: nothing visible to wait on.
    await new Promise((resolve) => setTimeout(resolve, 50))
    otherTab.close()
    answer(sessionFor({ ...owner, language: 'en' }, 'token-1'))
    await restored

    // Assert
    expect(useAuthStore.getState()).toMatchObject({ status: 'authenticated', user: { ...owner, language: 'uk' } })
    expect(i18n.language).toBe('uk')
  })

  it('installs a refresh\'s language as it came when nothing changed meanwhile', async () => {
    // Arrange
    signInAs({ ...owner, language: 'en' }, 'token-1')
    vi.mocked(authApi.refresh).mockResolvedValue(sessionFor({ ...owner, language: 'uk' }, 'token-2'))

    // Act
    await refreshSession('token-1')

    // Assert
    expect(useAuthStore.getState().user?.language).toBe('uk')
    await vi.waitFor(() => expect(i18n.language).toBe('uk'))
  })
})

describe('the account language held while a page must not mount afresh', () => {
  beforeEach(() => {
    resetAuthStore()
  })

  afterEach(async () => {
    queryClient.clear()
    await i18n.changeLanguage('en-US')
  })

  it('puts a language another tab saved on the user at once and runs the UI in it once released', async () => {
    // Arrange
    signInAs(owner)
    const release = holdLanguage()
    const otherTab = new BroadcastChannel('mam-auth')

    // Act
    otherTab.postMessage({ type: 'language', userId: owner.id, language: 'uk' })
    await vi.waitFor(() => expect(useAuthStore.getState().user?.language).toBe('uk'))
    otherTab.close()
    const languageWhileHeld = i18n.language
    release()

    // Assert
    expect(languageWhileHeld).toBe('en-US')
    await vi.waitFor(() => expect(i18n.language).toBe('uk'))
  })

  it('holds a refresh\'s language too, and lets go only once the last hold is released', async () => {
    // Arrange
    signInAs(owner, 'token-1')
    const releaseFirst = holdLanguage()
    const releaseSecond = holdLanguage()

    // Act
    useAuthStore.getState().setSession(sessionFor({ ...owner, language: 'uk' }, 'token-2'))
    releaseFirst()
    releaseFirst()
    const languageWhileStillHeld = i18n.language
    releaseSecond()

    // Assert
    expect(languageWhileStillHeld).toBe('en-US')
    await vi.waitFor(() => expect(i18n.language).toBe('uk'))
  })

  it('still gives the page back to the browser\'s language on sign-out', async () => {
    // Arrange
    useAuthStore.getState().setSession(sessionFor({ ...owner, language: 'uk' }))
    await vi.waitFor(() => expect(i18n.language).toBe('uk'))
    const release = holdLanguage()

    // Act
    useAuthStore.getState().clear({ deliberate: true })

    // Assert
    await vi.waitFor(() => expect(i18n.language).toBe('en-US'))
    release()
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

  afterEach(async () => {
    vi.unstubAllGlobals()
    localStorage.clear()
    await i18n.changeLanguage('en-US')
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

  it('tells the other tabs a saved language through localStorage', async () => {
    // Arrange
    const { useAuthStore: store } = await loadStoreWithoutBroadcastChannel()
    store.setState({ accessToken: 'token-1', user: owner, status: 'authenticated' })

    // Act
    await store.getState().setLanguage('uk')

    // Assert
    expect(JSON.parse(localStorage.getItem('mam:auth:language') ?? 'null'))
      .toMatchObject({ type: 'language', userId: owner.id, language: 'uk' })
    expect(store.getState().user?.language).toBe('uk')
  })

  it('follows a language on the storage event of another tab saving one', async () => {
    // Arrange
    const { useAuthStore: store } = await loadStoreWithoutBroadcastChannel()
    store.setState({ accessToken: 'token-1', user: owner, status: 'authenticated' })
    const saved = JSON.stringify({ type: 'language', userId: owner.id, language: 'uk', at: Date.now() })

    // Act
    window.dispatchEvent(new StorageEvent('storage', { key: 'mam:auth:language', newValue: saved }))

    // Assert
    await vi.waitFor(() => expect(store.getState().user?.language).toBe('uk'))
    expect(store.getState().status).toBe('authenticated')
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
