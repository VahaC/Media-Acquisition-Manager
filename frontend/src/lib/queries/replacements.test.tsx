import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { renderHook, waitFor } from '@testing-library/react'
import type { MockInstance } from 'vitest'
import {
  qk,
  useDeleteReplacementForGood,
  useReplacement,
  useReplacementOptions,
  useRestoreReplacement,
  useRetryReplacement,
  useScheduleReplacement,
} from '@/lib/queries'
import { replacementsApi } from '@/lib/replacements-api'
import { apiError, networkError } from '@/test/api-error'
import { jobOf, localClient, moviesOption } from '@/test/downloads'
import { aliens } from '@/test/library'
import {
  alienSubtitle,
  alienVideo,
  aliensWorkOf,
  REPLACEMENT_ID,
  replacementDetailsOf,
  replacementOf,
  replacementOptionsOf,
  theatricalCopy,
} from '@/test/replacements'
import { tolokaRemux } from '@/test/search'

vi.mock('@/lib/replacements-api')

// MAM-24 — the Download dialog's hooks for the owner's copies: the read is asked only when the dialog
// lets it, and a schedule asks the jobs again once it answers, and the copies again once it is refused
// over them, so the dialog never offers a list the server would refuse again. MAM-26 — the job page's
// hooks for the owner's replacement: its details only for the owner, and each action puts the job it
// answered in place and asks again for what it changed, whatever it answered, never retrying itself.

const request = {
  requestId: '0f8e1d2c-3b4a-4596-8877-665544332211',
  releaseId: tolokaRemux.id,
  downloadClientId: localClient.id,
  destinationId: moviesOption.id,
  fileIds: [theatricalCopy.file.id],
  confirmedWorkIds: [aliens.id],
}

function newClient() {
  return new QueryClient({ defaultOptions: { queries: { retry: false } } })
}

/** Renders `hook` against `client`, which the test keeps to read and spy on its cache. */
function renderWith<T>(client: QueryClient, hook: () => T) {
  return renderHook(hook, { wrapper: ({ children }) => <QueryClientProvider client={client}>{children}</QueryClientProvider> })
}

/** The query keys `client.invalidateQueries` was asked for, as the filters it was given. */
function invalidatedKeys(spy: MockInstance<QueryClient['invalidateQueries']>) {
  return spy.mock.calls.map(([filters]) => filters?.queryKey)
}

describe('replacement hooks', () => {
  beforeEach(() => {
    vi.resetAllMocks()
  })

  describe('useReplacementOptions', () => {
    it('asks nothing while the dialog doesn\'t let it, and asks once it does', async () => {
      // Arrange
      vi.mocked(replacementsApi.options).mockResolvedValue(replacementOptionsOf([aliensWorkOf()]))
      const client = newClient()
      const { result, rerender } = renderHook(
        ({ enabled }: { enabled: boolean }) => useReplacementOptions(tolokaRemux.id, moviesOption.id, false, enabled),
        {
          initialProps: { enabled: false },
          wrapper: ({ children }) => <QueryClientProvider client={client}>{children}</QueryClientProvider>,
        },
      )
      const askedWhileHeld = vi.mocked(replacementsApi.options).mock.calls.length

      // Act
      rerender({ enabled: true })

      // Assert
      await waitFor(() => expect(result.current.data).toEqual(replacementOptionsOf([aliensWorkOf()])))
      expect(askedWhileHeld).toBe(0)
      expect(vi.mocked(replacementsApi.options).mock.calls).toEqual([[tolokaRemux.id, moviesOption.id, false]])
    })

    it('asks nothing without a destination', async () => {
      // Arrange
      const client = newClient()

      // Act
      const { result } = renderWith(client, () => useReplacementOptions(tolokaRemux.id, '', false, true))

      // Assert
      expect(result.current.fetchStatus).toBe('idle')
      expect(replacementsApi.options).not.toHaveBeenCalled()
    })

    it('keeps the last answer on screen, as a stand-in, while the held marks are asked for', async () => {
      // Arrange
      vi.mocked(replacementsApi.options)
        .mockResolvedValueOnce(replacementOptionsOf([aliensWorkOf()]))
        .mockReturnValueOnce(new Promise(() => {}))
      const client = newClient()
      const { result, rerender } = renderHook(
        ({ holdings }: { holdings: boolean }) => useReplacementOptions(tolokaRemux.id, moviesOption.id, holdings, true),
        {
          initialProps: { holdings: false },
          wrapper: ({ children }) => <QueryClientProvider client={client}>{children}</QueryClientProvider>,
        },
      )
      await waitFor(() => expect(result.current.data).toBeDefined())

      // Act
      rerender({ holdings: true })

      // Assert
      await waitFor(() => expect(replacementsApi.options).toHaveBeenCalledTimes(2))
      expect(result.current.isPlaceholderData).toBe(true)
      expect(result.current.data).toEqual(replacementOptionsOf([aliensWorkOf()]))
    })
  })

  describe('useScheduleReplacement', () => {
    it('asks for every list of jobs again once a schedule answers, and nothing else', async () => {
      // Arrange
      vi.mocked(replacementsApi.schedule).mockResolvedValue(jobOf({ replacement: replacementOf() }))
      const client = newClient()
      const invalidate = vi.spyOn(client, 'invalidateQueries')
      const { result } = renderWith(client, () => useScheduleReplacement())

      // Act
      await result.current.mutateAsync(request)

      // Assert
      expect(replacementsApi.schedule).toHaveBeenCalledExactlyOnceWith(request)
      expect(invalidatedKeys(invalidate)).toEqual([qk.downloads.jobs.lists])
    })

    it.each([400, 409])('asks for the release\'s copies through the destination again, with and without the held marks, after a %i', async (status) => {
      // Arrange
      vi.mocked(replacementsApi.schedule).mockRejectedValue(apiError(status, 'One of the ticked copies isn\'t a copy of the work this release matched.'))
      const client = newClient()
      const invalidate = vi.spyOn(client, 'invalidateQueries')
      const { result } = renderWith(client, () => useScheduleReplacement())

      // Act
      await result.current.mutateAsync(request).catch(() => undefined)

      // Assert
      expect(invalidatedKeys(invalidate)).toEqual([
        qk.replacements.options(tolokaRemux.id, moviesOption.id, false),
        qk.replacements.options(tolokaRemux.id, moviesOption.id, true),
      ])
    })

    it('asks nothing again when the schedule fails for another reason', async () => {
      // Arrange
      vi.mocked(replacementsApi.schedule).mockRejectedValueOnce(networkError()).mockRejectedValueOnce(apiError(404))
      const client = newClient()
      const invalidate = vi.spyOn(client, 'invalidateQueries')
      const { result } = renderWith(client, () => useScheduleReplacement())

      // Act
      await result.current.mutateAsync(request).catch(() => undefined)
      await result.current.mutateAsync(request).catch(() => undefined)

      // Assert
      expect(replacementsApi.schedule).toHaveBeenCalledTimes(2)
      expect(invalidate).not.toHaveBeenCalled()
    })
  })

  describe('useReplacement', () => {
    it('asks nothing for anyone but the owner, and asks the owner\'s by its id', async () => {
      // Arrange
      vi.mocked(replacementsApi.get).mockResolvedValue(replacementDetailsOf())
      const client = newClient()
      const { result, rerender } = renderHook(
        ({ enabled }: { enabled: boolean }) => useReplacement(REPLACEMENT_ID, enabled),
        {
          initialProps: { enabled: false },
          wrapper: ({ children }) => <QueryClientProvider client={client}>{children}</QueryClientProvider>,
        },
      )
      const askedWhileHeld = vi.mocked(replacementsApi.get).mock.calls.length

      // Act
      rerender({ enabled: true })

      // Assert
      await waitFor(() => expect(result.current.data).toEqual(replacementDetailsOf()))
      expect(askedWhileHeld).toBe(0)
      expect(vi.mocked(replacementsApi.get).mock.calls).toEqual([[REPLACEMENT_ID]])
    })

    it('asks only once when the read fails: a 404 won\'t change on a second try', async () => {
      // Arrange
      vi.mocked(replacementsApi.get).mockRejectedValue(apiError(404))
      const client = new QueryClient()

      // Act
      const { result } = renderWith(client, () => useReplacement(REPLACEMENT_ID, true))

      // Assert
      await waitFor(() => expect(result.current.isError).toBe(true))
      expect(replacementsApi.get).toHaveBeenCalledOnce()
    })
  })

  describe('the owner\'s actions', () => {
    const answered = jobOf({ replacement: replacementOf({ state: 'Verified' }) })

    it('puts the job Retry answered in place, and asks again for the jobs and the replacement, not the quarantine room', async () => {
      // Arrange
      vi.mocked(replacementsApi.retry).mockResolvedValue(answered)
      const client = newClient()
      const invalidate = vi.spyOn(client, 'invalidateQueries')
      const { result } = renderWith(client, () => useRetryReplacement())

      // Act
      await result.current.mutateAsync(REPLACEMENT_ID)

      // Assert
      expect(replacementsApi.retry).toHaveBeenCalledExactlyOnceWith(REPLACEMENT_ID)
      expect(client.getQueryData(qk.downloads.jobs.detail(answered.id))).toEqual(answered)
      expect(invalidatedKeys(invalidate)).toEqual([qk.downloads.jobs.all, qk.replacements.job(REPLACEMENT_ID)])
    })

    it.each<[string, (client: QueryClient) => () => Promise<unknown>, () => void]>([
      ['Restore', (client) => {
        const { result } = renderWith(client, () => useRestoreReplacement())
        return () => result.current.mutateAsync(REPLACEMENT_ID)
      }, () => vi.mocked(replacementsApi.restore).mockResolvedValue(answered)],
      ['Delete for good', (client) => {
        const { result } = renderWith(client, () => useDeleteReplacementForGood())
        return () => result.current.mutateAsync({ id: REPLACEMENT_ID, names: [alienVideo.id, alienSubtitle.id] })
      }, () => vi.mocked(replacementsApi.deleteForGood).mockResolvedValue(answered)],
    ])('puts the job %s answered in place, and asks again for the jobs, the replacement and the quarantine room', async (_, hook, answer) => {
      // Arrange
      answer()
      const client = newClient()
      const invalidate = vi.spyOn(client, 'invalidateQueries')
      const send = hook(client)

      // Act
      await send()

      // Assert
      expect(client.getQueryData(qk.downloads.jobs.detail(answered.id))).toEqual(answered)
      expect(invalidatedKeys(invalidate)).toEqual([
        qk.downloads.jobs.all,
        qk.replacements.job(REPLACEMENT_ID),
        qk.settings.allowedRoots,
      ])
    })

    it('sends Delete for good the names it is given', async () => {
      // Arrange
      vi.mocked(replacementsApi.deleteForGood).mockResolvedValue(answered)
      const { result } = renderWith(newClient(), () => useDeleteReplacementForGood())

      // Act
      await result.current.mutateAsync({ id: REPLACEMENT_ID, names: [alienVideo.id] })

      // Assert
      expect(replacementsApi.deleteForGood).toHaveBeenCalledExactlyOnceWith(REPLACEMENT_ID, [alienVideo.id])
    })

    it.each<[string, (client: QueryClient) => () => Promise<unknown>, () => unknown, (readonly unknown[])[]]>([
      ['Retry', (client) => {
        const { result } = renderWith(client, () => useRetryReplacement())
        return () => result.current.mutateAsync(REPLACEMENT_ID)
      }, () => vi.mocked(replacementsApi.retry).mockRejectedValue(apiError(409, 'Refused.')),
      [qk.downloads.jobs.all, qk.replacements.job(REPLACEMENT_ID)]],
      ['Restore', (client) => {
        const { result } = renderWith(client, () => useRestoreReplacement())
        return () => result.current.mutateAsync(REPLACEMENT_ID)
      }, () => vi.mocked(replacementsApi.restore).mockRejectedValue(apiError(409, 'Refused.')),
      [qk.downloads.jobs.all, qk.replacements.job(REPLACEMENT_ID), qk.settings.allowedRoots]],
      ['Delete for good', (client) => {
        const { result } = renderWith(client, () => useDeleteReplacementForGood())
        return () => result.current.mutateAsync({ id: REPLACEMENT_ID, names: [alienVideo.id] })
      }, () => vi.mocked(replacementsApi.deleteForGood).mockRejectedValue(apiError(409, 'Refused.')),
      [qk.downloads.jobs.all, qk.replacements.job(REPLACEMENT_ID), qk.settings.allowedRoots]],
    ])('asks again for what %s may have changed when it is refused, and never sends it twice', async (_, hook, refuse, keys) => {
      // Arrange
      refuse()
      const client = newClient()
      const invalidate = vi.spyOn(client, 'invalidateQueries')
      const send = hook(client)

      // Act
      const failure = await send().catch((error: unknown) => error)

      // Assert
      expect(failure).toBeInstanceOf(Error)
      expect(invalidatedKeys(invalidate)).toEqual(keys)
      const calls = [replacementsApi.retry, replacementsApi.restore, replacementsApi.deleteForGood]
        .map((call) => vi.mocked(call).mock.calls.length)
      expect(calls.reduce((sum, count) => sum + count, 0)).toBe(1)
    })
  })
})
