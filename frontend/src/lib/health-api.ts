import { isAxiosError } from 'axios'
import { api } from './api'
import type { HealthReport } from './types'

export const healthApi = {
  /**
   * MAM-16 — `GET /api/health`, liveness: 200 while the process is up, with no checks run. The
   * offline strip asks it while the server can't be reached; whether it answers is all it learns
   * (lib/api.ts records that for every request), so a report that says something failed is still
   * the server answering.
   */
  live: async (): Promise<HealthReport> => (await api.get<HealthReport>('/api/health')).data,

  /**
   * `GET /api/health/ready`. 200 when every readiness check passes, 503 when one fails — and the
   * 503 carries the same report, naming the failing check. That 503 is returned rather than thrown,
   * so the page can say *what* is not ready instead of a bare "request failed".
   * Anything else still rejects: a 500, no answer at all, and a 503 whose body is not a report.
   * The last one is not the health endpoint talking — a rate limiter's empty 503, a reverse
   * proxy's "Service Unavailable" page, an `{ error }` body — and handing it to the page as a
   * report would crash it on `report.checks`.
   */
  ready: async (): Promise<HealthReport> => {
    try {
      return (await api.get<HealthReport>('/api/health/ready')).data
    } catch (error: unknown) {
      if (isAxiosError(error) && error.response?.status === 503 && isHealthReport(error.response.data)) {
        return error.response.data
      }
      throw error
    }
  },
}

/** The shape check that tells the readiness report apart from any other 503 body. */
function isHealthReport(body: unknown): body is HealthReport {
  if (typeof body !== 'object' || body === null) return false
  const candidate = body as Partial<Record<keyof HealthReport, unknown>>
  return typeof candidate.status === 'string' && Array.isArray(candidate.checks)
}
