import type { HealthStatus } from './types'

/**
 * The words for each readiness status, in the `home` namespace. MAM-16 — moved out of HomePage so
 * the account menu says what the Service status card says about the same report.
 */
export const HEALTH_STATUS_TEXT = {
  Healthy: 'status.ready',
  // ASP.NET answers 200 for Degraded: the server works, but a check wants attention.
  Degraded: 'status.readyWithWarnings',
  Unhealthy: 'status.notReady',
} as const satisfies Record<HealthStatus, string>
