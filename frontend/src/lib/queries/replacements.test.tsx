import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { renderHook, waitFor } from '@testing-library/react'
import type { MockInstance } from 'vitest'
import { qk, useReplacementOptions, useScheduleReplacement } from '@/lib/queries'
import { replacementsApi } from '@/lib/replacements-api'
import { apiError, networkError } from '@/test/api-error'
import { jobOf, localClient, moviesOption } from '@/test/downloads'
import { aliens } from '@/test/library'
import { aliensWorkOf, replacementOf, replacementOptionsOf, theatricalCopy } from '@/test/replacements'
import { tolokaRemux } from '@/test/search'

vi.mock('@/lib/replacements-api')

// MAM-24 — the Download dialog's hooks for the owner's copies: the read is asked only when the dialog
// lets it, and a schedule asks the jobs again once it answers, and the copies again once it is refused
// over them, so the dialog never offers a list the server would refuse again.

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
})
