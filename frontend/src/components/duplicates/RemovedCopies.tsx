import { useState } from 'react'
import { TriangleAlert } from 'lucide-react'
import { Trans, useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import { RemovedCopyCard } from '@/components/duplicates/RemovedCopyCard'
import { Banner } from '@/components/shared/Banner'
import { ErrorState } from '@/components/shared/ErrorState'
import { ListPager } from '@/components/shared/ListPager'
import { RetryButton } from '@/components/shared/RetryButton'
import { formatFileSize } from '@/lib/format'
import { splitPath } from '@/lib/library-versions'
import { useCopyRemovals } from '@/lib/queries'
import { useLoadRetry } from '@/lib/use-load-retry'
import { PAGE_SIZES } from '@/lib/use-page-size'
import { getApiErrorMessage, retryCanHelp } from '@/lib/utils'
import '@/styles/settings-list.css' // .settings-muted, .settings-empty — the loading line and the empty state
import '@/styles/library.css' // .library-source-warning, .library-link — the switched-off line and its link
import '@/styles/duplicates.css' // .duplicates-groups — the column of cards

interface RemovedCopiesProps {
  /** The Duplicates page's page size, one preference per device for every tab. */
  pageSize: number
  onPageSize: (size: number) => void
}

/**
 * What the owner's last Restore or Delete for good came to: the removal it was about, its file's name, and the
 * sentence — what it did, or why it failed.
 */
interface Outcome {
  tone: 'ok' | 'error'
  removalId: string
  name: string
  text: string
}

/**
 * Puts the focus on an element as it appears, or as it starts holding an outcome: the card whose button had
 * it is gone, and the focus would otherwise fall to the page's body. At module level, so the ref is the same
 * function on every render and React calls it only on that change, never again on a re-render.
 */
function focusOnMount(node: HTMLElement | null) {
  node?.focus()
}

/**
 * MAM-40 — the Duplicates page's Removed copies tab, the owner's alone: the copies he removed that are still
 * in the quarantine, newest first, paged by the server, with the room all of them take there; each a card
 * with Restore and Delete for good (RemovedCopyCard). What an action did shows above the list, which no
 * longer has the removal once it is back or deleted; a refusal stays on its card while the list has it, and
 * shows above the list too once the list, asked for again, no longer does. Either takes the focus from the
 * card it came from — the empty state does, when the list has nothing left. Replaced copies stay on their
 * jobs' pages.
 *
 * While replacement is switched off the server refuses Restore and Delete for good: each card holds them
 * with the reason, and the tab says so once, with the way to the Media folders page, as the Download dialog
 * does.
 *
 * The tab starts at page 1 each time it opens, and moves to the last page there is when an action leaves
 * fewer removals than the page on screen needs.
 */
export function RemovedCopies({ pageSize, onPageSize }: RemovedCopiesProps) {
  const { t } = useTranslation(['duplicates', 'common'])
  const [page, setPage] = useState(1)
  const [outcome, setOutcome] = useState<Outcome | null>(null)
  const removals = useCopyRemovals({ page, pageSize })
  const { data, error, isLoading, isPlaceholderData } = removals
  const load = useLoadRetry(removals)
  const pages = data ? Math.max(1, Math.ceil(data.totalCount / pageSize)) : 1
  // Only on this page's own answer, not the one kept on screen while it loads.
  if (data && !isPlaceholderData && page > pages) setPage(pages)
  // A failure stays on its card while the list has the card; above the list once it hasn't.
  const failedOnCard = outcome?.tone === 'error' && data !== undefined && data.items.some((removal) => removal.id === outcome.removalId)
  // The switched-off line only where it holds something: a card the switch alone keeps from acting.
  const heldBySwitch = data?.switchedOff === true && data.items.some((removal) => removal.canRestore || removal.canDeleteForGood)

  // The outcome belongs to the page it was made on, as the Duplicates page's short rows do: on another page a
  // failure whose card isn't there would come up as a banner and take the focus from the pager.
  const goToPage = (next: number) => {
    setPage(next)
    setOutcome(null)
  }

  const changePageSize = (next: number) => {
    onPageSize(next)
    goToPage(1)
  }

  return (
    <>
      {outcome && !failedOnCard && (
        <Banner ref={focusOnMount} tabIndex={-1} tone={outcome.tone} onDismiss={() => setOutcome(null)}>
          {outcome.tone === 'ok' ? outcome.text : t('removed.failedOf', { name: outcome.name, error: outcome.text })}
        </Banner>
      )}
      {/* Outside the ternary below, so a failed refresh adds a line above the cards instead of replacing them. */}
      {error && data && !isPlaceholderData && (
        <Banner action={<RetryButton onRetry={() => removals.refetch()} />}>
          {t('removed.refreshFailed')}{' '}
          {getApiErrorMessage(error)}
        </Banner>
      )}

      {load.error ? (
        <ErrorState
          title={t('removed.loadFailed')}
          onRetry={retryCanHelp(load.error) ? load.retry : undefined}
          retrying={load.retrying}
        >
          {getApiErrorMessage(load.error)}
        </ErrorState>
      ) : isLoading ? (
        <p className="settings-muted">{t('common:loading')}</p>
      ) : data && data.totalCount === 0 ? (
        <div className="settings-empty">
          {/* Takes the focus only when an action of the owner's emptied the list, after its banner did. */}
          <p ref={outcome !== null ? focusOnMount : undefined} tabIndex={-1}>{t('empty.Removed')}</p>
        </div>
      ) : data ? (
        <>
          <p>{t('removed.room', { size: formatFileSize(data.quarantinedBytes) })}</p>
          {heldBySwitch && (
            <p className="library-source-warning">
              <TriangleAlert className="h-3.5 w-3.5" aria-hidden="true" />
              <span>
                {/* The tag is <page>, not <link>: Trans reads <link> as HTML's empty element and drops the words in it. */}
                <Trans
                  t={t}
                  i18nKey="removed.switchedOffLinked"
                  components={{ page: <Link className="library-link" to="/settings/media-folders" /> }}
                />
              </span>
            </p>
          )}
          <ul className="duplicates-groups">
            {data.items.map((removal) => (
              <li key={removal.id}>
                <RemovedCopyCard
                  removal={removal}
                  switchedOff={data.switchedOff}
                  error={failedOnCard && outcome.removalId === removal.id ? outcome.text : null}
                  onStart={() => setOutcome(null)}
                  onDone={(text) => setOutcome({ tone: 'ok', removalId: removal.id, name: splitPath(removal.originalPath).name, text })}
                  onFailed={(text) => setOutcome({ tone: 'error', removalId: removal.id, name: splitPath(removal.originalPath).name, text })}
                />
              </li>
            ))}
          </ul>
          <ListPager
            page={page}
            pages={pages}
            pageSize={pageSize}
            pageSizes={PAGE_SIZES}
            totalCount={data.totalCount}
            onPage={goToPage}
            onPageSize={changePageSize}
          />
        </>
      ) : null}
    </>
  )
}
