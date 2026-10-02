import { CircleAlert, CircleCheck, CircleX } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Banner } from '@/components/shared/Banner'
import { ErrorState } from '@/components/shared/ErrorState'
import { RetryButton } from '@/components/shared/RetryButton'
import { HEALTH_STATUS_TEXT } from '@/lib/health-status'
import { useReadiness } from '@/lib/queries'
import type { HealthReport, HealthStatus } from '@/lib/types'
import { useLoadRetry } from '@/lib/use-load-retry'
import { getApiErrorMessage } from '@/lib/utils'
import '@/styles/home.css' // .home-* — this page's layout and the service status card

/**
 * The landing page: what the app is, and whether its server is ready to work (the readiness
 * report of `GET /api/health/ready`, polled every minute).
 */
export function HomePage() {
  const { t } = useTranslation(['home', 'common'])
  const readiness = useReadiness()
  const { data, error, isLoading } = readiness
  // MAM-16 — the report is polled every minute, failed or not: the first failure stays on screen
  // through those polls rather than blinking to "Loading…" each time.
  const load = useLoadRetry(readiness)

  return (
    <div className="home-page">
      <section className="home-intro">
        <h1 className="home-title">{t('title')}</h1>
        <p className="home-lead">{t('lead')}</p>
      </section>

      <section className="ui-card" aria-labelledby="home-status-title">
        <div className="ui-card-header">
          <h2 id="home-status-title" className="ui-card-title">{t('status.title')}</h2>
          <p className="ui-card-description">{t('status.description')}</p>
        </div>
        <div className="ui-card-content home-status-body">
          {/* Outside the ternary below, so a failed refresh adds a line above the last known
              status instead of replacing it. */}
          {error && data && (
            <Banner action={<RetryButton onRetry={() => readiness.refetch()} />}>
              {t('status.refreshFailed')}{' '}
              {getApiErrorMessage(error)}
            </Banner>
          )}
          {load.error ? (
            <ErrorState title={t('status.loadFailed')} onRetry={load.retry} retrying={load.retrying}>
              {getApiErrorMessage(load.error)}
            </ErrorState>
          ) : isLoading ? (
            <p className="home-muted">{t('common:loading')}</p>
          ) : data ? (
            <ReadinessSummary report={data} />
          ) : null}
        </div>
      </section>
    </div>
  )
}

// ── readiness summary ──

// A check's description is the API's own English sentence and is shown as sent, like every API
// message (ADR 0001); only the fallback for a check without one is UI text.
function ReadinessSummary({ report }: { report: HealthReport }) {
  const { t } = useTranslation('home')
  const failing = report.checks.filter((check) => check.status !== 'Healthy')

  return (
    <div className="home-status" data-status={report.status}>
      <p className="home-status-line">
        <StatusIcon status={report.status} />
        <span>{t(HEALTH_STATUS_TEXT[report.status])}</span>
      </p>
      {failing.length > 0 && (
        <ul className="home-status-checks">
          {failing.map((check) => (
            <li key={check.name}>{check.description ?? t('status.checkFailed', { name: check.name })}</li>
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
