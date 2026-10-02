import { useQuery } from '@tanstack/react-query'
import { healthApi } from '../health-api'
import { qk } from './keys'

/** MAM-16 — how often the offline strip asks whether the server is back. */
const PROBE_INTERVAL_MS = 5_000

/** The server's readiness report, re-checked every minute while the page is open. */
export const useReadiness = () =>
  useQuery({
    queryKey: qk.health.ready,
    queryFn: () => healthApi.ready(),
    refetchInterval: 60_000,
  })

/**
 * MAM-16 — while `enabled` (the server can't be reached), asks the liveness endpoint at once and
 * every 5 s after, until an answer hides the offline strip. lib/api.ts notes the answer; nothing
 * here reads the report.
 *
 * Asked even while the browser says it is offline (`networkMode: 'always'`): TanStack would hold
 * the probe back, and the strip's Retry now would wait on it with no answer; asked, it fails at once
 * and the strip stays. Never retried at once, as the next tick is the retry.
 */
export const useServerProbe = (enabled: boolean) =>
  useQuery({
    queryKey: qk.health.live,
    queryFn: () => healthApi.live(),
    enabled,
    refetchInterval: enabled ? PROBE_INTERVAL_MS : false,
    retry: false,
    networkMode: 'always',
  })
