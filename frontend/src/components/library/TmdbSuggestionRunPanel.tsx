import { useId, useState } from 'react'
import { CircleAlert, CircleCheck, Loader2, RefreshCw } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Banner } from '@/components/shared/Banner'
import { DisabledReason } from '@/components/shared/DisabledReason'
import { ErrorState } from '@/components/shared/ErrorState'
import { RetryButton } from '@/components/shared/RetryButton'
import { Button } from '@/components/ui/button'
import { formatDateTime, formatNumber } from '@/lib/format'
import { isSuggestionRunUnderWay } from '@/lib/library-links'
import { useLatestLinkSuggestionRun, useStartLinkSuggestionRun } from '@/lib/queries'
import type { LibraryLinkSuggestionRun } from '@/lib/types'
import { useLoadRetry } from '@/lib/use-load-retry'
import { getApiErrorMessage } from '@/lib/utils'
import '@/styles/settings-list.css' // .settings-muted — the Loading… line
import '@/styles/library.css' // .library-sources, .library-source* — the row, its status line and its button, as the Library's sources lay them out

/** Date and time without the seconds, as the Library's sync panel shows a run. */
const INSTANT_FORMAT: Intl.DateTimeFormatOptions = { dateStyle: 'medium', timeStyle: 'short' }

/**
 * MAM-34 — how the TMDb suggestions were last prepared, on the TMDb page above its tabs, laid out like
 * the Recommendations page's run panel: one status line, polled while a preparation is under way,
 * with Prepare suggestions — Prepare again once there was one —, which searches TMDb anew for every
 * work without an id, disabled with the reason while a preparation is under way. The page is the
 * owner's, and so is the sentence of why a preparation failed (it can name the catalog's address). A
 * refused start says why under the line.
 */
export function TmdbSuggestionRunPanel() {
  const { t } = useTranslation(['libraryLinks', 'common'])
  const titleId = useId()
  const latest = useLatestLinkSuggestionRun()
  const { data, error, isLoading } = latest
  const load = useLoadRetry(latest)
  const start = useStartLinkSuggestionRun()
  const [startError, setStartError] = useState<string | null>(null)

  const prepare = async () => {
    setStartError(null)
    try {
      await start.mutateAsync()
    } catch (e: unknown) {
      // Mostly a 409: no catalog is connected.
      setStartError(getApiErrorMessage(e, t('run.startFailed')))
    }
  }

  const label = start.isPending ? t('run.starting') : data === null ? t('run.prepareFirst') : t('run.prepare')
  // Off while a preparation is queued or running, with the reason, as Sync now is while a sync is.
  const reason = isSuggestionRunUnderWay(data) ? t('run.underWay') : undefined

  return (
    <section className="ui-card" aria-labelledby={titleId}>
      <div className="ui-card-header">
        <h2 id={titleId} className="ui-card-title">{t('run.title')}</h2>
        <p className="ui-card-description">{t('run.description')}</p>
      </div>
      <div className="ui-card-content library-sources">
        {/* Outside the ternary below, so a failed poll adds a line above the last known status. */}
        {error && data !== undefined && (
          <Banner action={<RetryButton onRetry={() => latest.refetch()} />}>
            {t('run.refreshFailed')}{' '}
            {getApiErrorMessage(error)}
          </Banner>
        )}
        {load.error ? (
          <ErrorState title={t('run.loadFailed')} onRetry={load.retry} retrying={load.retrying}>
            {getApiErrorMessage(load.error)}
          </ErrorState>
        ) : isLoading ? (
          <p className="settings-muted">{t('common:loading')}</p>
        ) : data !== undefined ? (
          <div className="library-source">
            <div className="library-source-body">
              <RunLine run={data} />
              {startError && (
                // `alert`, not the run line's `status`: the refusal answers the click, read out at once.
                <p role="alert" className="library-source-status" data-status="Failed">
                  <CircleAlert className="h-3.5 w-3.5" aria-hidden="true" />
                  <span>{startError}</span>
                </p>
              )}
            </div>
            <div className="library-source-actions">
              <DisabledReason reason={reason}>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  title={label}
                  disabled={reason !== undefined || start.isPending}
                  onClick={() => void prepare()}
                >
                  {start.isPending || reason !== undefined
                    ? <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />
                    : <RefreshCw className="h-3.5 w-3.5" aria-hidden="true" />}
                  <span className="label-text">{label}</span>
                </Button>
              </DisabledReason>
            </div>
          </div>
        ) : null}
      </div>
    </section>
  )
}

/**
 * The latest preparation in one line, `role="status"` so a screen reader reads its change out — from
 * "Preparing the suggestions: 240 of 815 works…" to "Suggestions prepared …" — without moving the focus.
 */
function RunLine({ run }: { run: LibraryLinkSuggestionRun | null }) {
  const { t } = useTranslation('libraryLinks')

  const text = (() => {
    if (run === null) return t('run.never')
    const time = formatDateTime(run.finishedUtc ?? run.createdUtc, INSTANT_FORMAT)
    switch (run.status) {
      case 'Queued':
        return t('run.queued')
      case 'Running':
        // The works are counted once the run has read them.
        return run.worksTotal === null
          ? t('run.running')
          : t('run.runningCounts', { count: run.worksTotal, done: formatNumber(run.worksDone), total: formatNumber(run.worksTotal) })
      case 'Succeeded':
        return t('run.succeeded', { time })
      case 'Failed':
        return run.error === null ? t('run.failedHidden', { time }) : t('run.failed', { time, error: run.error })
    }
  })()

  return (
    <p role="status" className="library-source-status" data-status={run?.status}>
      {run !== null && <RunIcon run={run} />}
      <span>{text}</span>
    </p>
  )
}

function RunIcon({ run }: { run: LibraryLinkSuggestionRun }) {
  switch (run.status) {
    case 'Queued':
    case 'Running':
      return <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />
    case 'Succeeded':
      return <CircleCheck className="h-3.5 w-3.5" aria-hidden="true" />
    case 'Failed':
      return <CircleAlert className="h-3.5 w-3.5" aria-hidden="true" />
  }
}
