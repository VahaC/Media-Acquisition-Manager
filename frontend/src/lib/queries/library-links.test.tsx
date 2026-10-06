import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { renderHook, waitFor } from '@testing-library/react'
import type { MockInstance } from 'vitest'
import { libraryLinksApi } from '@/lib/library-links-api'
import { qk, useLibraryLinkCatchUp, useLibraryLinkDialog, useLibraryLinkList, usePendingLibraryLinks } from '@/lib/queries'
import type { LibraryLinkDialog, LibraryLinksQuery } from '@/lib/types'
import { catchUpOf, linkedDialogOf, linksPage, linkRowOf, progressOf, queuedItemOf, landingStripling } from '@/test/library-links'

vi.mock('@/lib/library-links-api')

// MAM-34 — the hooks that follow an identification Jellyfin runs in the background: they poll while
// something is under way, and the answer that brings its end asks the works and the lists of links
// again, so a card, the Not identified tab and the Update Jellyfin counts never stay as they were.

const PENDING: LibraryLinksQuery = { filter: 'Pending', page: 1, pageSize: 1 }
const NOT_WRITTEN: LibraryLinksQuery = { filter: 'NotWritten', page: 1, pageSize: 1 }

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

describe('library link hooks', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    vi.useFakeTimers({ shouldAdvanceTime: true })
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  describe('useLibraryLinkDialog', () => {
    it('asks again every 2 s while an item is queued and stops once it is identified', async () => {
      // Arrange
      vi.mocked(libraryLinksApi.dialog)
        .mockResolvedValueOnce(linkedDialogOf({ items: [queuedItemOf()] }))
        .mockResolvedValueOnce(linkedDialogOf({ items: [queuedItemOf({ writeState: 'Writing' })] }))
        .mockResolvedValue(linkedDialogOf())
      const client = newClient()
      const { result } = renderWith(client, () => useLibraryLinkDialog(landingStripling.id))
      await waitFor(() => expect(result.current.data?.items[0].writeState).toBe('Queued'))

      // Act
      await vi.advanceTimersByTimeAsync(2_000)
      await waitFor(() => expect(result.current.data?.items[0].writeState).toBe('Writing'))
      await vi.advanceTimersByTimeAsync(2_000)
      await waitFor(() => expect(result.current.data?.items[0].writeState).toBe('Written'))
      await vi.advanceTimersByTimeAsync(10_000)

      // Assert
      expect(libraryLinksApi.dialog).toHaveBeenCalledTimes(3)
    })

    it('asks for the works and the other link queries again when the identification ends, but not for its own dialog', async () => {
      // Arrange
      vi.mocked(libraryLinksApi.dialog)
        .mockResolvedValueOnce(linkedDialogOf({ items: [queuedItemOf()] }))
        .mockResolvedValue(linkedDialogOf())
      const client = newClient()
      const { result } = renderWith(client, () => useLibraryLinkDialog(landingStripling.id))
      await waitFor(() => expect(result.current.data?.items[0].writeState).toBe('Queued'))
      const spy = vi.spyOn(client, 'invalidateQueries')

      // Act
      await vi.advanceTimersByTimeAsync(2_000)
      await waitFor(() => expect(result.current.data?.items[0].writeState).toBe('Written'))

      // Assert
      expect(invalidatedKeys(spy)).toContainEqual(qk.library.worksAll)
      const others = spy.mock.calls.map(([filters]) => filters).find((filters) => filters?.queryKey === qk.library.links.all)
      const ownDialog = { queryKey: qk.library.links.work(landingStripling.id) } as never
      const anotherDialog = { queryKey: qk.library.links.work('another') } as never
      expect(others?.predicate?.(ownDialog)).toBe(false)
      expect(others?.predicate?.(anotherDialog)).toBe(true)
    })

    it('leaves the works alone when only the suggestions finished being prepared', async () => {
      // Arrange
      const preparing: LibraryLinkDialog = linkedDialogOf({ items: [], search: { searchedUtc: null, noneFitsUtc: null, pending: true, inPopulation: true } })
      vi.mocked(libraryLinksApi.dialog)
        .mockResolvedValueOnce(preparing)
        .mockResolvedValue({ ...preparing, search: { ...preparing.search, pending: false } })
      const client = newClient()
      const { result } = renderWith(client, () => useLibraryLinkDialog(landingStripling.id))
      await waitFor(() => expect(result.current.data?.search.pending).toBe(true))
      const spy = vi.spyOn(client, 'invalidateQueries')

      // Act
      await vi.advanceTimersByTimeAsync(2_000)
      await waitFor(() => expect(result.current.data?.search.pending).toBe(false))

      // Assert
      expect(spy).not.toHaveBeenCalled()
    })
  })

  describe('useLibraryLinkList', () => {
    it('asks nothing while it is disabled, as for a member', async () => {
      // Arrange
      const client = newClient()

      // Act
      renderWith(client, () => useLibraryLinkList(NOT_WRITTEN, false))
      await vi.advanceTimersByTimeAsync(5_000)

      // Assert
      expect(libraryLinksApi.links).not.toHaveBeenCalled()
    })

    it('reads the page of its filter while it is enabled', async () => {
      // Arrange
      vi.mocked(libraryLinksApi.links).mockImplementation(async (query) => linksPage(query, [linkRowOf({ writeState: 'NotWritten' })]))
      const client = newClient()

      // Act
      const { result } = renderWith(client, () => useLibraryLinkList(NOT_WRITTEN))
      await waitFor(() => expect(result.current.data?.totalCount).toBe(1))

      // Assert
      expect(libraryLinksApi.links).toHaveBeenCalledWith(NOT_WRITTEN)
    })
  })

  describe('useLibraryLinkCatchUp', () => {
    it('asks nothing while it is disabled, as for a member', async () => {
      // Arrange
      const client = newClient()

      // Act
      renderWith(client, () => useLibraryLinkCatchUp(false))
      await vi.advanceTimersByTimeAsync(5_000)

      // Assert
      expect(libraryLinksApi.catchUp).not.toHaveBeenCalled()
    })

    it('does not poll while no batch is under way', async () => {
      // Arrange
      vi.mocked(libraryLinksApi.catchUp).mockResolvedValue(catchUpOf())
      const client = newClient()
      const { result } = renderWith(client, () => useLibraryLinkCatchUp(true))
      await waitFor(() => expect(result.current.data).toEqual(catchUpOf()))

      // Act
      await vi.advanceTimersByTimeAsync(10_000)

      // Assert
      expect(libraryLinksApi.catchUp).toHaveBeenCalledTimes(1)
    })

    it('follows the batch every 2 s and, when it ends, asks for the works and the lists of links again', async () => {
      // Arrange
      vi.mocked(libraryLinksApi.catchUp)
        .mockResolvedValueOnce(catchUpOf({ toIdentify: 182, progress: progressOf() }))
        .mockResolvedValueOnce(catchUpOf({ toIdentify: 60, progress: progressOf({ done: 120 }) }))
        .mockResolvedValue(catchUpOf({ toIdentify: 4, progress: null }))
      vi.mocked(libraryLinksApi.links).mockImplementation(async (query) => linksPage(query, []))
      const client = newClient()
      const { result } = renderWith(client, () => useLibraryLinkCatchUp(true))
      const list = renderWith(client, () => useLibraryLinkList(NOT_WRITTEN))
      await waitFor(() => expect(result.current.data?.progress?.done).toBe(41))
      await waitFor(() => expect(list.result.current.data).toBeDefined())
      const spy = vi.spyOn(client, 'invalidateQueries')

      // Act
      await vi.advanceTimersByTimeAsync(2_000)
      await waitFor(() => expect(result.current.data?.progress?.done).toBe(120))
      const midway = invalidatedKeys(spy).length
      await vi.advanceTimersByTimeAsync(2_000)
      await waitFor(() => expect(result.current.data?.progress).toBeNull())
      await waitFor(() => expect(libraryLinksApi.links).toHaveBeenCalledTimes(2))
      await vi.advanceTimersByTimeAsync(10_000)

      // Assert
      expect(midway).toBe(0)
      expect(invalidatedKeys(spy)).toContainEqual(qk.library.worksAll)
      expect(libraryLinksApi.catchUp).toHaveBeenCalledTimes(3)
    })
  })

  describe('usePendingLibraryLinks', () => {
    it('polls while links wait and, when the last one settles, refreshes the counts, the other lists and the works once', async () => {
      // Arrange
      let pending = 1
      vi.mocked(libraryLinksApi.links).mockImplementation(async (query) => linksPage(query, [], query.filter === 'Pending' ? pending : 0))
      vi.mocked(libraryLinksApi.catchUp).mockResolvedValue(catchUpOf())
      const client = newClient()
      const { result } = renderWith(client, () => usePendingLibraryLinks(true))
      renderWith(client, () => useLibraryLinkList(NOT_WRITTEN))
      renderWith(client, () => useLibraryLinkCatchUp(true))
      await waitFor(() => expect(result.current.data?.totalCount).toBe(1))
      const spy = vi.spyOn(client, 'invalidateQueries')
      const pendingAsks = () => vi.mocked(libraryLinksApi.links).mock.calls.filter(([query]) => query.filter === 'Pending').length
      const notWrittenAsks = () => vi.mocked(libraryLinksApi.links).mock.calls.filter(([query]) => query.filter === 'NotWritten').length
      const askedBefore = notWrittenAsks()

      // Act
      pending = 0
      await vi.advanceTimersByTimeAsync(2_000)
      await waitFor(() => expect(result.current.data?.totalCount).toBe(0))
      await waitFor(() => expect(notWrittenAsks()).toBe(askedBefore + 1))
      await vi.advanceTimersByTimeAsync(10_000)

      // Assert
      expect(invalidatedKeys(spy)).toContainEqual(qk.library.links.catchUp)
      expect(invalidatedKeys(spy)).toContainEqual(qk.library.worksAll)
      // The Pending list is asked for twice only: the first load and the poll that found it empty. Invalidating
      // the query whose answer is being returned would cancel it and ask again for ever.
      expect(pendingAsks()).toBe(2)
      expect(libraryLinksApi.catchUp).toHaveBeenCalledTimes(2)
    })

    it('does not poll while nothing waits', async () => {
      // Arrange
      vi.mocked(libraryLinksApi.links).mockImplementation(async (query) => linksPage(query, [], 0))
      const client = newClient()
      const { result } = renderWith(client, () => usePendingLibraryLinks(true))
      await waitFor(() => expect(result.current.data?.totalCount).toBe(0))

      // Act
      await vi.advanceTimersByTimeAsync(10_000)

      // Assert
      expect(libraryLinksApi.links).toHaveBeenCalledTimes(1)
      expect(libraryLinksApi.links).toHaveBeenCalledWith(PENDING)
    })

    it('asks nothing while it is disabled, as for a member', async () => {
      // Arrange
      const client = newClient()

      // Act
      renderWith(client, () => usePendingLibraryLinks(false))
      await vi.advanceTimersByTimeAsync(5_000)

      // Assert
      expect(libraryLinksApi.links).not.toHaveBeenCalled()
    })
  })
})
