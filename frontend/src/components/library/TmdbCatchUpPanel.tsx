import { useId, useRef, useState } from 'react'
import { CircleAlert, Loader2, Upload } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Banner } from '@/components/shared/Banner'
import { useConfirm } from '@/components/shared/ConfirmDialog'
import { DisabledReason } from '@/components/shared/DisabledReason'
import { ErrorState } from '@/components/shared/ErrorState'
import { RetryButton } from '@/components/shared/RetryButton'
import { Button } from '@/components/ui/button'
import { formatNumber } from '@/lib/format'
import { useLibraryLinkCatchUp, useLibraryLinkList, usePendingLibraryLinks, useStartLibraryLinkCatchUp } from '@/lib/queries'
import type { LibraryLinkCatchUp, LibraryLinksQuery } from '@/lib/types'
import { useLoadRetry } from '@/lib/use-load-retry'
import { getApiErrorMessage, retryCanHelp } from '@/lib/utils'
import '@/styles/settings-list.css' // .settings-muted — the Loading… line and a member's sentence
import '@/styles/library.css' // .library-sources, .library-source* — the row, its lines and its button, as the suggestions' panel lays them out

/**
 * The links Jellyfin or the app refused or failed to identify, asked a link at a time for the count
 * alone (the list is the Not identified tab's, and `totalCount` is all this panel needs).
 */
const NEEDS_REVIEW: LibraryLinksQuery = { filter: 'NotWritten', page: 1, pageSize: 1 }

/**
 * MAM-34 — "Update Jellyfin" on the TMDb page, above the suggestions: the REPAIR of the links set under
 * the first rules, which only put TMDb's ids on the items, so Jellyfin still shows the titles,
 * overviews and images it took from the file names (Vasyl's decision of 2026-10-06, ADR 0026). The
 * panel says how many links are not identified yet; the button, after one question, queues them all,
 * and the background worker makes Jellyfin identify each one as its link — a film in seconds, a series
 * in minutes. While that batch runs the panel says "done of total" and the button is off; afterwards
 * the counts come back, with how many links were refused or failed and a button to the Not identified
 * tab, which lists them with their reasons.
 *
 * The question is asked every time because the click is bulk and has no undo: Jellyfin replaces each
 * item's title, overview and images, and the owner decides nothing per item here.
 *
 * Two polls keep it honest. The catch-up's own `progress` follows the batch of this panel's click (a
 * reload in the middle finds it still going). `usePendingLibraryLinks` follows the links queued from
 * the dialog or the Review tab, which carry no batch: without it a link refused after the owner moved
 * on would never reach the "refused or failed" count until a reload. A member sees the button off with
 * the owner's reason, and nothing is asked: every route behind it is the owner's.
 */
export function TmdbCatchUpPanel({ isOwner, onShowNotIdentified }: { isOwner: boolean; onShowNotIdentified?: () => void }) {
  const { t } = useTranslation(['libraryLinks', 'common'])
  const titleId = useId()
  const confirm = useConfirm()
  const counts = useLibraryLinkCatchUp(isOwner)
  const review = useLibraryLinkList(NEEDS_REVIEW, isOwner)
  const pending = usePendingLibraryLinks(isOwner)
  const load = useLoadRetry(counts)
  const start = useStartLibraryLinkCatchUp()
  const [startError, setStartError] = useState<string | null>(null)
  // A double click asks once.
  const inFlight = useRef(false)
  const { data } = counts

  const underWay = data !== undefined && data.progress !== null
  const needsReview = review.data?.totalCount ?? 0
  const reason = !isOwner
    ? t('catchUp.notOwner')
    : underWay
      ? t('catchUp.underWay')
      : data !== undefined && data.toIdentify === 0 ? t('catchUp.nothingToDo') : undefined
  const label = start.isPending ? t('catchUp.starting') : t('catchUp.update')

  const update = async () => {
    if (data === undefined || inFlight.current) return
    inFlight.current = true
    setStartError(null)
    try {
      const confirmed = await confirm({
        title: t('catchUp.confirmTitle'),
        message: t('catchUp.confirmMessage', { count: data.toIdentify, value: formatNumber(data.toIdentify) }),
        confirmLabel: t('catchUp.update'),
        destructive: true,
      })
      if (!confirmed) return
      await start.mutateAsync()
    } catch (e: unknown) {
      setStartError(getApiErrorMessage(e, t('catchUp.startFailed')))
    } finally {
      inFlight.current = false
    }
  }

  const button = (
    <div className="library-source-actions">
      <DisabledReason reason={reason}>
        <Button
          type="button"
          variant="outline"
          size="sm"
          title={label}
          // Off until the counts are in: the question must name how many items it asks about.
          disabled={reason !== undefined || data === undefined || start.isPending}
          onClick={() => void update()}
        >
          {start.isPending || underWay
            ? <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />
            : <Upload className="h-3.5 w-3.5" aria-hidden="true" />}
          <span className="label-text">{label}</span>
        </Button>
      </DisabledReason>
    </div>
  )

  // A failed poll of any of the three keeps the last known lines under a banner; only the counts' own
  // first load, failed, takes their place.
  const refreshError = data !== undefined ? counts.error ?? review.error ?? pending.error : null

  return (
    <section className="ui-card" aria-labelledby={titleId}>
      <div className="ui-card-header">
        <h2 id={titleId} className="ui-card-title">{t('catchUp.title')}</h2>
        <p className="ui-card-description">{t('catchUp.description')}</p>
      </div>
      <div className="ui-card-content library-sources">
        {refreshError && (
          <Banner action={<RetryButton onRetry={() => Promise.all([counts.refetch(), review.refetch(), pending.refetch()])} />}>
            {t('catchUp.refreshFailed')}{' '}
            {getApiErrorMessage(refreshError)}
          </Banner>
        )}
        {!isOwner ? (
          <div className="library-source">
            {/* On screen as well as on the button's hover: a phone shows no tooltip. */}
            <p className="settings-muted">{t('catchUp.notOwner')}</p>
            {button}
          </div>
        ) : load.error ? (
          <ErrorState title={t('catchUp.loadFailed')} onRetry={retryCanHelp(load.error) ? load.retry : undefined} retrying={load.retrying}>
            {getApiErrorMessage(load.error)}
          </ErrorState>
        ) : counts.isLoading ? (
          <p className="settings-muted">{t('common:loading')}</p>
        ) : data !== undefined ? (
          <div className="library-source">
            <div className="library-source-body">
              <CatchUpLines counts={data} needsReview={needsReview} />
              {/* A sibling of the status lines, not inside them: a control inside a live region is read
                  out again every time a poll changes the region. Not in `.label-text`, so its words
                  stay on a phone, where the button beside it shows its icon only. */}
              {!underWay && needsReview > 0 && onShowNotIdentified !== undefined && (
                <div className="library-source-actions">
                  <Button type="button" variant="outline" size="sm" onClick={onShowNotIdentified}>
                    {t('catchUp.showNotIdentified')}
                  </Button>
                </div>
              )}
              {startError && (
                // `alert`, not the lines' `status`: the refusal answers the click, read out at once.
                <p role="alert" className="library-source-status" data-status="Failed">
                  <CircleAlert className="h-3.5 w-3.5" aria-hidden="true" />
                  <span>{startError}</span>
                </p>
              )}
            </div>
            {button}
          </div>
        ) : null}
      </div>
    </section>
  )
}

/**
 * While a batch runs, how far it is — "Identifying in Jellyfin: 41 of 223 links…" — and why the count can
 * stall; otherwise how many links are not identified yet, and how many were refused or failed. The
 * `role="status"` makes a screen reader read the change out, from the progress back to the counts,
 * without moving the focus; it holds text lines only.
 */
function CatchUpLines({ counts, needsReview }: { counts: LibraryLinkCatchUp; needsReview: number }) {
  const { t } = useTranslation('libraryLinks')
  const { progress } = counts

  return (
    <div role="status" className="library-source-body">
      {progress !== null ? (
        <>
          <p className="library-source-status">
            <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />
            <span>{t('catchUp.progress', { count: progress.total, done: formatNumber(progress.done), total: formatNumber(progress.total) })}</span>
          </p>
          <p className="settings-muted">{t('catchUp.progressNote')}</p>
        </>
      ) : (
        <>
          <p className="library-source-status">{t('catchUp.toIdentify', { value: formatNumber(counts.toIdentify) })}</p>
          {needsReview > 0 && (
            <p className="library-source-warning">
              <CircleAlert className="h-3.5 w-3.5" aria-hidden="true" />
              <span>{t('catchUp.needsReview', { count: needsReview, value: formatNumber(needsReview) })}</span>
            </p>
          )}
        </>
      )}
    </div>
  )
}
