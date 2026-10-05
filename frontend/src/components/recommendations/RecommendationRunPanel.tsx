import { useId, useState } from 'react'
import { CircleAlert, CircleCheck, Loader2, RefreshCw } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Banner } from '@/components/shared/Banner'
import { DisabledReason } from '@/components/shared/DisabledReason'
import { ErrorState } from '@/components/shared/ErrorState'
import { RetryButton } from '@/components/shared/RetryButton'
import { Button } from '@/components/ui/button'
import { useAuthStore } from '@/lib/auth-store'
import { formatDateTime, formatNumber, formatTimeAgo } from '@/lib/format'
import { useLatestRecommendationRun, useStartRecommendationRun } from '@/lib/queries'
import { isRunUnderWay } from '@/lib/recommendations'
import type { RecommendationRun, RecommendationRunStage } from '@/lib/types'
import { useLoadRetry } from '@/lib/use-load-retry'
import { getApiErrorMessage } from '@/lib/utils'
import '@/styles/settings-list.css' // .settings-muted — the Loading… line
import '@/styles/library.css' // .library-sources, .library-source* — the row, its status line and its button, as the Library's sources lay them out
import '@/styles/jobs.css' // .jobs-progress* — the bar under a running computation's line, a download's bar on the Jobs page

/** Date and time without the seconds, as the Library's sync panel shows a run. */
const INSTANT_FORMAT: Intl.DateTimeFormatOptions = { dateStyle: 'medium', timeStyle: 'short' }

interface RecommendationRunPanelProps {
  /**
   * Whether a metadata catalog is connected, from the list; undefined until it has loaded. Without
   * one the owner's Recompute now is disabled with the reason the API would refuse it with.
   */
  catalogConnected: boolean | undefined
}

/**
 * MAM-33 — how the lists were last computed, polled while a computation is under way, laid out like a
 * source of the Library's sync panel. Everyone sees when the lists were computed; the owner alone reads
 * why a computation failed (the sentence can name the catalog's address) and has Recompute now, which
 * everyone else doesn't get at all: they have nothing to start. A refused start says why under the line.
 *
 * A first computation reads every library work from the catalog and takes minutes, so while one runs
 * the line says what it is doing, how far through that it is and when it started, with a bar under it
 * when the stage counts: "Computing" alone read as stuck.
 */
export function RecommendationRunPanel({ catalogConnected }: RecommendationRunPanelProps) {
  const { t } = useTranslation(['recommendations', 'common'])
  const isOwner = useAuthStore((state) => state.user?.isOwner === true)
  const titleId = useId()
  const latest = useLatestRecommendationRun()
  const { data, error, isLoading, dataUpdatedAt } = latest
  const load = useLoadRetry(latest)
  const start = useStartRecommendationRun()
  const [startError, setStartError] = useState<string | null>(null)

  const recompute = async () => {
    setStartError(null)
    try {
      await start.mutateAsync()
    } catch (e: unknown) {
      // Mostly a 409 (the catalog was removed meanwhile) or a 503 (too many waiting).
      setStartError(getApiErrorMessage(e, t('run.startFailed')))
    }
  }

  const reason = catalogConnected === false ? t('run.noCatalog') : undefined
  const label = start.isPending ? t('run.starting') : t('run.recompute')

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
              {/* How long ago the run started is measured from the poll's answer, so it moves on
                  with each poll rather than from a clock of its own. */}
              <RunLine run={data} now={dataUpdatedAt} />
              {data?.status === 'Running' && <StageProgress run={data} />}
              {startError && (
                // `alert`, not the run line's `status`: the refusal answers the click, read out at once.
                <p role="alert" className="library-source-status" data-status="Failed">
                  <CircleAlert className="h-3.5 w-3.5" aria-hidden="true" />
                  <span>{startError}</span>
                </p>
              )}
            </div>
            {isOwner && (
              <div className="library-source-actions">
                <DisabledReason reason={reason}>
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    title={label}
                    disabled={reason !== undefined || start.isPending}
                    onClick={() => void recompute()}
                  >
                    {start.isPending || isRunUnderWay(data)
                      ? <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />
                      : <RefreshCw className="h-3.5 w-3.5" aria-hidden="true" />}
                    <span className="label-text">{label}</span>
                  </Button>
                </DisabledReason>
              </div>
            )}
          </div>
        ) : null}
      </div>
    </section>
  )
}

/**
 * The latest computation in one line, `role="status"` so a screen reader reads its change out — from
 * "Computing · Reading your library" to "Computed …" — without moving the focus, as the Library's sync
 * line does with its counts. `now` is when the run was last asked for, what "started 2 minutes ago"
 * is measured from.
 */
function RunLine({ run, now }: { run: RecommendationRun | null; now: number }) {
  const { t } = useTranslation(['recommendations', 'common'])

  const text = (() => {
    if (run === null) return t('run.never')
    const time = formatDateTime(run.finishedUtc ?? run.createdUtc, INSTANT_FORMAT)
    switch (run.status) {
      case 'Queued':
        return t('run.queued')
      case 'Running': {
        // "Computing · Reading your films and shows in the catalog: 240 of 815 · started 2 minutes ago";
        // a stage without counts, or none yet, says less.
        const stage = stageOf(run)
        const stageText = stage && t(`run.stages.${stage.name}`)
        return [
          t('run.running'),
          stage && (stage.counts === null
            ? stageText
            : t('run.stageCounts', { stage: stageText, counts: t('run.counts', countsText(stage.counts)) })),
          run.startedUtc !== null && t('run.started', { ago: formatTimeAgo(run.startedUtc, now) }),
        ].filter(Boolean).join(t('common:separator'))
      }
      case 'Succeeded':
        return t('run.succeeded', { time })
      case 'Failed':
        // Null for anyone but the owner: the sentence can name the catalog's address.
        return run.error === null ? t('run.failedHidden') : t('run.failed', { time, error: run.error })
    }
  })()

  return (
    <p role="status" className="library-source-status" data-status={run?.status}>
      {run !== null && <RunIcon run={run} />}
      <span>{text}</span>
    </p>
  )
}

/**
 * How far through its stage a running computation is: a bar, with the share done beside it, under the
 * line that says it in words. The look of a download's bar on the Jobs page; named after the stage, and
 * read out as "240 of 815" rather than a percentage. Nothing for a stage without counts, or none yet.
 */
function StageProgress({ run }: { run: RecommendationRun }) {
  const { t } = useTranslation('recommendations')
  const stage = stageOf(run)
  if (stage === null || stage.counts === null || stage.counts.total <= 0) return null

  const { total } = stage.counts
  const done = Math.min(total, Math.max(0, stage.counts.done))
  // Cut down, never rounded up, as on the Jobs page: 814 of 815 must not read 100%.
  const percent = formatNumber(Math.floor((done / total) * 100) / 100, { style: 'percent' })

  return (
    <span className="jobs-progress">
      {/* A native bar, so jobs.css draws it; the explicit values repeat value and max for the
          progressbar role, and the text reads the counts out rather than a percentage. */}
      <progress
        className="jobs-progress-bar"
        value={done}
        max={total}
        aria-label={t(`run.stages.${stage.name}`)}
        aria-valuemin={0}
        aria-valuenow={done}
        aria-valuemax={total}
        aria-valuetext={t('run.counts', countsText(stage.counts))}
      />
      {/* The bar's text says the counts already. */}
      <span className="jobs-progress-value" aria-hidden="true">{percent}</span>
    </span>
  )
}

interface StageCounts {
  done: number
  total: number
}

/**
 * What a running computation is doing, with its counts when the stage has them; null while it is
 * queued, once it has ended and before its first stage, whatever the answer holds.
 */
function stageOf(run: RecommendationRun): { name: RecommendationRunStage; counts: StageCounts | null } | null {
  if (run.status !== 'Running' || run.stage === null) return null
  const counts = run.stageDone !== null && run.stageTotal !== null ? { done: run.stageDone, total: run.stageTotal } : null
  return { name: run.stage, counts }
}

/** The counts as the locale's numbers, for "{{done}} of {{total}}": "1,240 of 3,815". */
function countsText({ done, total }: StageCounts) {
  return { done: formatNumber(done), total: formatNumber(total) }
}

function RunIcon({ run }: { run: RecommendationRun }) {
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
