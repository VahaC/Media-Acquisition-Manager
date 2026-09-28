/** ASP.NET Core's health status, serialized as its name. */
export type HealthStatus = 'Healthy' | 'Degraded' | 'Unhealthy'

/** One readiness check, e.g. `database`. */
export interface HealthCheckResult {
  name: string
  status: HealthStatus
  /** A readable sentence — for a failing check it says what is wrong. */
  description: string | null
}

/** The body of `GET /api/health` and `GET /api/health/ready`. */
export interface HealthReport {
  status: HealthStatus
  checks: HealthCheckResult[]
}
