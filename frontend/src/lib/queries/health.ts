import { useQuery } from '@tanstack/react-query'
import { healthApi } from '../health-api'
import { qk } from './keys'

/** The server's readiness report, re-checked every minute while the page is open. */
export const useReadiness = () =>
  useQuery({
    queryKey: qk.health.ready,
    queryFn: () => healthApi.ready(),
    refetchInterval: 60_000,
  })
