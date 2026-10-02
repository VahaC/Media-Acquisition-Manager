import { hashKey } from '@tanstack/react-query'
import { create } from 'zustand'
import { qk } from './queries/keys'
import { queryClient } from './query-client'

/** The offline strip's probe (useServerProbe), by the hash TanStack files it under. */
const PROBE_HASH = hashKey(qk.health.live)

interface ConnectionState {
  /** The last request that ended got no answer from the server (isServerUnreachable). */
  unreachable: boolean
  markUnreachable: () => void
  /** The server answered, whatever it said. */
  markReachable: () => void
}

/**
 * MAM-16 — whether the server can be reached, as the last request that ended found it. lib/api.ts
 * sets it for every request, queries, actions and the session refresh alike, as they all go through
 * that one client; the offline strip under the header reads it.
 *
 * Each setter writes only a change: every request that ends calls one of them, and an unchanged
 * value written again would still wake every component that reads the store.
 */
export const useConnectionStore = create<ConnectionState>((set, get) => ({
  unreachable: false,

  markUnreachable: () => {
    if (!get().unreachable) set({ unreachable: true })
  },

  markReachable: () => {
    if (!get().unreachable) return
    set({ unreachable: false })
    // A list or panel that failed while the server was away would otherwise keep its error until
    // someone pressed Try again or Retry: the server is back, so what is on screen asks again. A
    // query already on its way is left to finish, and the probe is left out: the strip it serves
    // goes now, so asking it once more would only ask for what this answer already said.
    //
    // A known loop: a GET that a proxy answers with a bodiless 504 because the endpoint is slow, not
    // because the API is down, raises the strip; the probe's answer clears it and asks that GET
    // again here, about every 5 s while the endpoint stays slow. A proxy timeout longer than the
    // slowest endpoint keeps it from happening.
    void queryClient.refetchQueries(
      {
        type: 'active',
        predicate: (query) => query.queryHash !== PROBE_HASH
          && query.state.status === 'error' && query.state.fetchStatus === 'idle',
      },
      { cancelRefetch: false },
    )
  },
}))
