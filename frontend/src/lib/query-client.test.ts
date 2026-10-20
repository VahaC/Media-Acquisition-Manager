import { createElement, type ReactNode } from 'react'
import { QueryClientProvider, QueryObserver, type QueryKey } from '@tanstack/react-query'
import { act, renderHook, waitFor } from '@testing-library/react'
import { authApi } from '@/lib/auth-api'
import { useAuthStore } from '@/lib/auth-store'
import { i18n } from '@/lib/i18n'
import { useProfile } from '@/lib/queries'
import { queryClient } from '@/lib/query-client'
import type { CurrentUserProfile, UiLanguage } from '@/lib/types'
import { member, resetAuthStore, signInAs } from '@/test/auth-session'

vi.mock('@/lib/auth-api')

// MAM-47 — the server words its sentences in the language each request asks for (Accept-Language, the UI
// locale), so a cached answer is in the language the UI ran in when it was fetched.

/**
 * A query on the app's own client with an observer, as a mounted page has: an active one. Fresh for good,
 * like the integrations', the library's and the recommendations' lists, which a page mounting afresh
 * doesn't ask for again: only the language change does.
 */
function observe(queryKey: QueryKey, queryFn: () => Promise<string>) {
  const observer = new QueryObserver(queryClient, { queryKey, queryFn, retry: false, staleTime: Infinity })
  const unsubscribe = observer.subscribe(() => {})
  return { observer, unsubscribe }
}

/** The server's sentence in the language the request asks for, as lib/api.ts sends it: the UI locale. */
const sentenceInUiLanguage = async () =>
  i18n.language === 'uk' ? 'Jellyfin не відповів.' : 'Jellyfin did not answer.'

/** The member's profile as the server answers it, with the language their account holds. */
const profileOf = (language: UiLanguage | null): CurrentUserProfile => ({
  ...member,
  language,
  createdUtc: '2026-09-29T07:30:00Z',
  lastSignInUtc: null,
  sessionExpiresUtc: '2026-11-01T07:30:00Z',
})

/** The app's providers around a hook: its one query client, as App.tsx mounts it. */
const appProviders = ({ children }: { children: ReactNode }) =>
  createElement(QueryClientProvider, { client: queryClient }, children)

describe('queryClient', () => {
  const unsubscribes: Array<() => void> = []

  afterEach(async () => {
    unsubscribes.splice(0).forEach((unsubscribe) => unsubscribe())
    queryClient.clear()
    resetAuthStore()
    // The app's one i18n instance: a test that switched it to Ukrainian must not leave it there.
    await i18n.changeLanguage('en-US')
  })

  it('refetches the cached answers on screen when the UI language changes, so the server\'s sentences follow it', async () => {
    // Arrange
    const queryFn = vi.fn(sentenceInUiLanguage)
    const { observer, unsubscribe } = observe(['sources'], queryFn)
    unsubscribes.push(unsubscribe)
    await waitFor(() => expect(observer.getCurrentResult().data).toBe('Jellyfin did not answer.'))

    // Act
    await i18n.changeLanguage('uk')

    // Assert
    await waitFor(() => expect(observer.getCurrentResult().data).toBe('Jellyfin не відповів.'))
    expect(queryFn).toHaveBeenCalledTimes(2)
  })

  it('marks a cached answer nothing shows stale without fetching it, so it is asked for again when shown', async () => {
    // Arrange
    const queryFn = vi.fn(sentenceInUiLanguage)
    await queryClient.prefetchQuery({ queryKey: ['jobs'], queryFn, staleTime: Infinity })

    // Act
    await i18n.changeLanguage('uk')

    // Assert
    expect(queryClient.getQueryState(['jobs'])?.isInvalidated).toBe(true)
    expect(queryClient.getQueryData(['jobs'])).toBe('Jellyfin did not answer.')
    expect(queryFn).toHaveBeenCalledTimes(1)
  })

  // The profile's answer is the one answer that can change the language again (auth-store
  // adoptAccountLanguage): asked afresh by the switch, it brings back the language already running, so
  // the switch ends after one round instead of refetching in a loop.
  it('asks once more for what is on screen when the account\'s language is switched, and no more', async () => {
    // Arrange
    let accountLanguage: UiLanguage = 'en'
    const profileAskedIn: string[] = []
    vi.mocked(authApi.me).mockImplementation(async () => {
      profileAskedIn.push(i18n.language)
      return profileOf(accountLanguage)
    })
    const sentence = vi.fn(sentenceInUiLanguage)
    signInAs({ ...member, language: 'en' })
    const { result, unmount } = renderHook(() => useProfile(), { wrapper: appProviders })
    unsubscribes.push(unmount)
    const { observer, unsubscribe } = observe(['sources'], sentence)
    unsubscribes.push(unsubscribe)
    await waitFor(() => expect(result.current.data?.language).toBe('en'))
    await waitFor(() => expect(observer.getCurrentResult().data).toBe('Jellyfin did not answer.'))

    // Act — what the Profile's Language card does once the server has saved the choice.
    accountLanguage = 'uk'
    await act(() => useAuthStore.getState().setLanguage('uk'))

    // Assert
    await waitFor(() => expect(observer.getCurrentResult().data).toBe('Jellyfin не відповів.'))
    await waitFor(() => expect(queryClient.isFetching()).toBe(0))
    await waitFor(() => expect(result.current.data?.language).toBe('uk'))
    expect(profileAskedIn).toEqual(['en-US', 'uk'])
    expect(sentence).toHaveBeenCalledTimes(2)
    expect(i18n.language).toBe('uk')
  })
})
