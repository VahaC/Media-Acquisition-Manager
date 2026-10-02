import { useConnectionStore } from '@/lib/connection-store'

/**
 * The connection as a freshly loaded tab has it: reachable. The store lives at module level, so a
 * test that leaves the server unreachable would otherwise show the offline strip in the next one.
 */
export function resetConnectionStore() {
  useConnectionStore.setState({ unreachable: false })
}
