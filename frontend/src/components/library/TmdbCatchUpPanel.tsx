import { useId, useState } from 'react'
import { CircleAlert, Loader2, Upload } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Banner } from '@/components/shared/Banner'
import { DisabledReason } from '@/components/shared/DisabledReason'
import { ErrorState } from '@/components/shared/ErrorState'
import { RetryButton } from '@/components/shared/RetryButton'
import { Button } from '@/components/ui/button'
import { formatNumber } from '@/lib/format'
import { useLibraryLinkCatchUp, usePendingLibraryLinks, useStartLibraryLinkCatchUp } from '@/lib/queries'
import type { LibraryLinkCatchUp } from '@/lib/types'
import { useLoadRetry } from '@/lib/use-load-retry'
import { getApiErrorMessage, retryCanHelp } from '@/lib/utils'
import '@/styles/settings-list.css' // .settings-muted — the Loading… line and a member's sentence
import '@/styles/library.css' // .library-sources, .library-source* — the row, its lines and its button, as the suggestions' panel lays them out

/**
 * MAM-34 — "Update Jellyfin" on the TMDb page, above the suggestions (Vasyl's decision of 2026-10-06).
 * The links set before the app wrote series' and episodes' links into Jellyfin, and asked Jellyfin to
 * look up an item's missing metadata after a write, reach Jellyfin only through this one click: series'
 * and episodes' links then kept in the app, and films' written without the lookup. The panel says how
 * many of each there are; the button queues them all, and the background writer writes them one at a
 * time — nothing changes where the ids are in place — and asks Jellyfin for each one's lookup. The
 * click asks nothing first: a series' link is written like a film's (ADR 0026).
 *
 * The writes are followed through the Pending list (`usePendingLibraryLinks`), which is read as the
 * page opens too, so a reload in the middle of them still shows how many are left. While any link
 * waits or is being written — "Link N works" of the Confident tab included —, and when both counts
 * are 0, the button is off with the reason. A member sees it off with the owner's reason, and nothing
 * is asked: every route behind it is the owner's.
 */
export function TmdbCatchUpPanel({ isOwner }: { isOwner: boolean }) {
  const { t } = useTranslation(['libraryLinks', 'common'])
  const titleId = useId()
  const counts = useLibraryLinkCatchUp(isOwner)
  const pending = usePendingLibraryLinks(isOwner)
  const load = useLoadRetry(counts)
  const start = useStartLibraryLinkCatchUp()
  const [startError, setStartError] = useState<string | null>(null)
  const { data } = counts

  const left = pending.data?.totalCount ?? 0
  const reason = !isOwner
    ? t('catchUp.notOwner')
    : left > 0
      ? t('catchUp.underWay')
      : data !== undefined && data.toWrite === 0 && data.toLookUp === 0 ? t('catchUp.nothingToDo') : undefined
  const label = start.isPending ? t('catchUp.starting') : t('catchUp.update')

  const update = async () => {
    setStartError(null)
    try {
      await start.mutateAsync()
    } catch (e: unknown) {
      setStartError(getApiErrorMessage(e, t('catchUp.startFailed')))
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
          // Off until the counts are in: a click must name what it sends.
          disabled={reason !== undefined || data === undefined || start.isPending}
          onClick={() => void update()}
        >
          {start.isPending || left > 0
            ? <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />
            : <Upload className="h-3.5 w-3.5" aria-hidden="true" />}
          <span className="label-text">{label}</span>
        </Button>
      </DisabledReason>
    </div>
  )

  // A failed poll of either keeps the last known lines under a banner; only the counts' own first
  // load, failed, takes their place.
  const refreshError = data !== undefined ? counts.error ?? pending.error : null

  return (
    <section className="ui-card" aria-labelledby={titleId}>
      <div className="ui-card-header">
        <h2 id={titleId} className="ui-card-title">{t('catchUp.title')}</h2>
        <p className="ui-card-description">{t('catchUp.description')}</p>
      </div>
      <div className="ui-card-content library-sources">
        {refreshError && (
          <Banner action={<RetryButton onRetry={() => Promise.all([counts.refetch(), pending.refetch()])} />}>
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
              <CatchUpLines counts={data} left={left} />
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
 * The two counts, or how many links are left to write while any are, `role="status"` so a screen
 * reader reads the change out — from "Writing into Jellyfin: 37 links left…" back to the counts —
 * without moving the focus.
 */
function CatchUpLines({ counts, left }: { counts: LibraryLinkCatchUp; left: number }) {
  const { t } = useTranslation('libraryLinks')

  return (
    <div role="status" className="library-source-body">
      {left > 0 ? (
        <p className="library-source-status">
          <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />
          <span>{t('catchUp.pending', { count: left, value: formatNumber(left) })}</span>
        </p>
      ) : (
        <>
          <p className="library-source-status">{t('catchUp.toWrite', { value: formatNumber(counts.toWrite) })}</p>
          <p className="library-source-status">{t('catchUp.toLookUp', { value: formatNumber(counts.toLookUp) })}</p>
        </>
      )}
    </div>
  )
}
