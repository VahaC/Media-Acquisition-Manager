import { CircleAlert, CircleCheck, CircleX } from 'lucide-react'
import { useReadiness } from '@/lib/queries'
import type { HealthReport, HealthStatus } from '@/lib/types'
import { getApiErrorMessage } from '@/lib/utils'
import '@/styles/home.css' // .home-* — this page's layout and the service status card

/**
 * The landing page: what the app is, and whether its server is ready to work (the readiness
 * report of `GET /api/health/ready`, polled every minute).
 */
export function HomePage() {
  const { data, error, isLoading } = useReadiness()

  return (
    <div className="home-page">
      <section className="home-intro">
        <h1 className="home-title">Welcome</h1>
        <p className="home-lead">
          Media Acquisition Manager is a self-hosted place to search your media library and release
          sources side by side, and to send the release you choose to your download client.
        </p>
      </section>

      <section className="ui-card" aria-labelledby="home-status-title">
        <div className="ui-card-header">
          <h2 id="home-status-title" className="ui-card-title">Service status</h2>
          <p className="ui-card-description">
            Whether the server and its database are ready. Checked again every minute.
          </p>
        </div>
        <div className="ui-card-content">
          {/* Outside the ternary below, so a failed refresh adds a line above the last known
              status instead of replacing it. */}
          {error && data && (
            <p className="home-banner" role="alert">
              <CircleAlert className="h-3.5 w-3.5 inline" aria-hidden="true" />{' '}
              Refresh failed, showing the last known status.{' '}
              {getApiErrorMessage(error)}
            </p>
          )}
          {isLoading ? (
            <p className="home-muted">Loading…</p>
          ) : error && !data ? (
            <p className="home-error" role="alert">
              {getApiErrorMessage(error, 'Could not check the service status.')}
            </p>
          ) : data ? (
            <ReadinessSummary report={data} />
          ) : null}
        </div>
      </section>
    </div>
  )
}

// ── readiness summary ──

const STATUS_TEXT: Record<HealthStatus, string> = {
  Healthy: 'Ready',
  // ASP.NET answers 200 for Degraded: the server works, but a check wants attention.
  Degraded: 'Ready, with warnings',
  Unhealthy: 'Not ready',
}

function ReadinessSummary({ report }: { report: HealthReport }) {
  const failing = report.checks.filter((check) => check.status !== 'Healthy')

  return (
    <div className="home-status" data-status={report.status}>
      <p className="home-status-line">
        <StatusIcon status={report.status} />
        <span>{STATUS_TEXT[report.status]}</span>
      </p>
      {failing.length > 0 && (
        <ul className="home-status-checks">
          {failing.map((check) => (
            <li key={check.name}>{check.description ?? `The ${check.name} check failed.`}</li>
          ))}
        </ul>
      )}
    </div>
  )
}

function StatusIcon({ status }: { status: HealthStatus }) {
  switch (status) {
    case 'Healthy':
      return <CircleCheck className="h-4 w-4" aria-hidden="true" />
    case 'Degraded':
      return <CircleAlert className="h-4 w-4" aria-hidden="true" />
    case 'Unhealthy':
      return <CircleX className="h-4 w-4" aria-hidden="true" />
  }
}
