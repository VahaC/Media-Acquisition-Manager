import { QueryObserver, type QueryKey } from '@tanstack/react-query'
import { waitFor } from '@testing-library/react'
import { useConnectionStore } from '@/lib/connection-store'
import { qk } from '@/lib/queries'
import { queryClient } from '@/lib/query-client'
import { resetConnectionStore } from '@/test/connection'

/** A query on the app's own client with an observer, as a mounted page has: an active one. */
function observe(queryKey: QueryKey, queryFn: () => Promise<string>) {
  const observer = new QueryObserver(queryClient, { queryKey, queryFn, retry: false })
  const unsubscribe = observer.subscribe(() => {})
  return { observer, unsubscribe }
}

describe('useConnectionStore', () => {
  const unsubscribes: Array<() => void> = []

  afterEach(() => {
    unsubscribes.splice(0).forEach((unsubscribe) => unsubscribe())
    queryClient.clear()
    resetConnectionStore()
  })

  it('starts reachable, and follows the last request that ended', () => {
    // Arrange
    const { markUnreachable, markReachable } = useConnectionStore.getState()

    // Act
    markUnreachable()
    const afterNoAnswer = useConnectionStore.getState().unreachable
    markReachable()

    // Assert
    expect(afterNoAnswer).toBe(true)
    expect(useConnectionStore.getState().unreachable).toBe(false)
  })

  it('wakes nobody when a request finds what the store already says', () => {
    // Arrange
    const listener = vi.fn()
    const unsubscribe = useConnectionStore.subscribe(listener)
    unsubscribes.push(unsubscribe)

    // Act
    useConnectionStore.getState().markReachable()
    useConnectionStore.getState().markUnreachable()
    useConnectionStore.getState().markUnreachable()

    // Assert
    expect(listener).toHaveBeenCalledOnce()
  })

  it('asks again for what failed on screen once the server answers again, and leaves what loaded alone', async () => {
    // Arrange
    const failing = vi.fn<() => Promise<string>>()
      .mockRejectedValueOnce(new Error('The server could not be reached.'))
      .mockResolvedValue('back')
    const loaded = vi.fn<() => Promise<string>>().mockResolvedValue('fine')
    const failed = observe(['failed'], failing)
    const fine = observe(['fine'], loaded)
    unsubscribes.push(failed.unsubscribe, fine.unsubscribe)
    await waitFor(() => expect(failed.observer.getCurrentResult().status).toBe('error'))
    await waitFor(() => expect(fine.observer.getCurrentResult().status).toBe('success'))
    useConnectionStore.getState().markUnreachable()

    // Act
    useConnectionStore.getState().markReachable()

    // Assert
    await waitFor(() => expect(failed.observer.getCurrentResult().data).toBe('back'))
    expect(failing).toHaveBeenCalledTimes(2)
    expect(loaded).toHaveBeenCalledOnce()
  })

  it('asks nothing again while the server stays reachable', async () => {
    // Arrange
    const failing = vi.fn<() => Promise<string>>().mockRejectedValue(new Error('Database is locked.'))
    const failed = observe(['failed'], failing)
    unsubscribes.push(failed.unsubscribe)
    await waitFor(() => expect(failed.observer.getCurrentResult().status).toBe('error'))

    // Act
    useConnectionStore.getState().markReachable()

    // Assert
    expect(failing).toHaveBeenCalledOnce()
  })

  it('leaves the offline probe to its own tick when another request brings the news', async () => {
    // Arrange
    const asking = vi.fn<() => Promise<string>>()
      .mockRejectedValueOnce(new Error('The server could not be reached.'))
      .mockResolvedValue('back')
    const probing = vi.fn<() => Promise<string>>().mockRejectedValue(new Error('The server could not be reached.'))
    const failed = observe(['failed'], asking)
    const probe = observe(qk.health.live, probing)
    unsubscribes.push(failed.unsubscribe, probe.unsubscribe)
    await waitFor(() => expect(failed.observer.getCurrentResult().status).toBe('error'))
    await waitFor(() => expect(probe.observer.getCurrentResult().status).toBe('error'))
    useConnectionStore.getState().markUnreachable()

    // Act
    useConnectionStore.getState().markReachable()

    // Assert
    await waitFor(() => expect(failed.observer.getCurrentResult().data).toBe('back'))
    expect(probing).toHaveBeenCalledOnce()
  })
})
