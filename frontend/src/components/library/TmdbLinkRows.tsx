import { useState } from 'react'
import { CircleAlert, Link2, Trash2, TriangleAlert } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { LinkStateBadge } from '@/components/library/LinkStateBadge'
import { TmdbLinkDialog } from '@/components/library/TmdbLinkDialog'
import { Banner } from '@/components/shared/Banner'
import { useConfirm } from '@/components/shared/ConfirmDialog'
import { ErrorState } from '@/components/shared/ErrorState'
import { ListPager } from '@/components/shared/ListPager'
import { RetryButton } from '@/components/shared/RetryButton'
import { Button } from '@/components/ui/button'
import { formatDateTime } from '@/lib/format'
import { i18n } from '@/lib/i18n'
import { useLibraryLinkList, useRemoveLibraryLinkById } from '@/lib/queries'
import type { LibraryLinkRow } from '@/lib/types'
import { useLoadRetry } from '@/lib/use-load-retry'
import { PAGE_SIZES, usePageSize } from '@/lib/use-page-size'
import { getApiErrorMessage, retryCanHelp } from '@/lib/utils'
import '@/styles/settings-list.css' // .settings-muted, .settings-empty, .settings-lead — the lines above and instead of the rows
import '@/styles/library.css' // .library-works, .library-work-meta, .library-badge-warning, .library-source-status — the cards' column, facts and lines
import '@/styles/library-links.css' // .tmdb-link-row*, .tmdb-link-state — a link's card and its badges

/** Per device: a phone and a desktop want different page lengths. */
const PAGE_SIZE_STORAGE_KEY = 'mam:library-tmdb:linksPageSize'

const INSTANT_FORMAT: Intl.DateTimeFormatOptions = { dateStyle: 'medium', timeStyle: 'short' }

/** The lists of links the TMDb page has a tab for. */
type RowsFilter = 'Conflict' | 'Unattached'

/**
 * MAM-34 — one of the TMDb page's lists of links, a page at a time: the Conflicts tab (the items whose
 * Jellyfin holds another TMDb id than the one linked, so neither counts — "TMDb…" opens the work's
 * dialog to keep one of the two), or the Unattached tab (the links whose item the library no longer
 * reports — Remove forgets one, after a destructive confirmation; nothing is changed in Jellyfin, as
 * the item is gone). Each card says what the item was, what it is linked to, by whom and when, and
 * where the link stands against Jellyfin.
 */
export function TmdbLinkRows({ filter }: { filter: RowsFilter }) {
  const { t } = useTranslation(['libraryLinks', 'common'])
  const confirm = useConfirm()
  const [pageSize, setPageSize] = usePageSize(PAGE_SIZE_STORAGE_KEY)
  const [page, setPage] = useState(1)
  const list = useLibraryLinkList({ filter, page, pageSize })
  const { data, error, isLoading, isPlaceholderData } = list
  const load = useLoadRetry(list)
  const remove = useRemoveLibraryLinkById()
  const [removeErrors, setRemoveErrors] = useState<Record<string, string>>({})
  // The parent mounts the dialog only while it is open, so every opening starts fresh.
  const [opening, setOpening] = useState<LibraryLinkRow | null>(null)
  const pages = data ? Math.max(1, Math.ceil(data.totalCount / pageSize)) : 1
  // A removal can leave fewer links than the page on screen needs: move to the last page there is.
  // Only on this page's own answer, not the one kept on screen while it loads.
  if (data && !isPlaceholderData && page > pages) setPage(pages)

  const changePageSize = (next: number) => {
    setPageSize(next)
    setPage(1)
  }

  const removeRow = async (row: LibraryLinkRow) => {
    const confirmed = await confirm({
      title: t('rows.removeTitle'),
      message: t('rows.removeMessage', { title: rowTitle(row) }),
      confirmLabel: t('rows.remove'),
      destructive: true,
    })
    if (!confirmed) return
    setRemoveErrors((errors) => {
      const next = { ...errors }
      delete next[row.linkId]
      return next
    })
    try {
      await remove.mutateAsync(row.linkId)
    } catch (e: unknown) {
      setRemoveErrors((errors) => ({ ...errors, [row.linkId]: getApiErrorMessage(e, t('rows.removeFailed')) }))
    }
  }

  return (
    <>
      <p className="settings-lead">{t(`rows.lead.${filter}`)}</p>

      {/* Outside the ternary below, so a failed refresh adds a line above the cards instead of
          replacing them. */}
      {error && data && !isPlaceholderData && (
        <Banner action={<RetryButton onRetry={() => list.refetch()} />}>
          {t('rows.refreshFailed')}{' '}
          {getApiErrorMessage(error)}
        </Banner>
      )}

      {load.error ? (
        <ErrorState
          title={t('rows.loadFailed')}
          onRetry={retryCanHelp(load.error) ? load.retry : undefined}
          retrying={load.retrying}
        >
          {getApiErrorMessage(load.error)}
        </ErrorState>
      ) : isLoading ? (
        <p className="settings-muted">{t('common:loading')}</p>
      ) : data && data.totalCount === 0 ? (
        <div className="settings-empty">
          <p>{t(`rows.empty.${filter}`)}</p>
        </div>
      ) : data ? (
        <>
          <ul className="library-works">
            {data.items.map((row) => (
              <li key={row.linkId}>
                <LinkCard
                  row={row}
                  removing={remove.isPending && remove.variables === row.linkId}
                  removeError={removeErrors[row.linkId]}
                  onOpen={filter === 'Conflict' && row.workId !== null ? () => setOpening(row) : undefined}
                  onRemove={filter === 'Unattached' ? () => void removeRow(row) : undefined}
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
            onPage={setPage}
            onPageSize={changePageSize}
          />
        </>
      ) : null}

      {opening && opening.workId !== null && (
        <TmdbLinkDialog
          work={{ id: opening.workId, title: opening.workTitle, originalTitle: null }}
          onClose={() => setOpening(null)}
        />
      )}
    </>
  )
}

// ── a link ──

/**
 * One link: the item it is on, what it is linked to, by whom and when, its state against Jellyfin and
 * a conflict when there is one; "TMDb…" or Remove at the right edge (under the facts on a phone), and
 * a refused removal's sentence under them.
 */
function LinkCard({ row, removing, removeError, onOpen, onRemove }: {
  row: LibraryLinkRow
  removing: boolean
  removeError: string | undefined
  onOpen?: () => void
  onRemove?: () => void
}) {
  const { t } = useTranslation('libraryLinks')
  const kind = t(`candidate.kind.${row.catalogKind}`)
  const link = row.catalogTitle === null
    ? t('dialog.linkedId', { kind, id: row.catalogId })
    : t('dialog.linkedTitle', { title: row.catalogTitle, kind, id: row.catalogId })
  const removeLabel = removing ? t('rows.removing') : t('rows.remove')

  return (
    <article className="ui-card tmdb-link-row">
      <div className="tmdb-link-row-body">
        <p className="tmdb-link-row-title">{rowTitle(row)}</p>
        <p className="library-work-meta">
          <span>{i18n.t('libraryLinks:dialog.item', { source: row.sourceName, title: row.itemTitle ?? i18n.t('libraryLinks:dialog.untitledItem') })}</span>
          {row.target === 'ShowEpisode' && row.seasonNumber !== null && (
            <span>
              {row.episodeNumber === null
                ? t('dialog.seasonOnly', { season: row.seasonNumber })
                : t('dialog.seasonEpisode', { season: row.seasonNumber, episode: row.episodeNumber })}
            </span>
          )}
        </p>
        <p>{t('rows.linkedTo', { link })}</p>
        <div className="tmdb-link-state">
          <LinkStateBadge state={row.writeState} error={row.writeError} />
          {row.conflictingValue !== null && (
            <span className="ui-badge library-badge-warning" title={t('card.conflictHint')}>
              <TriangleAlert className="h-3 w-3" aria-hidden="true" />
              {t('card.conflict', { library: row.conflictingValue, link: row.catalogId })}
            </span>
          )}
        </div>
        <p className="library-work-meta">
          {t('card.setBy', { user: row.setByUserName, time: formatDateTime(row.setUtc, INSTANT_FORMAT) })}
        </p>
        {removeError && (
          <p role="alert" className="library-source-status" data-status="Failed">
            <CircleAlert className="h-3.5 w-3.5" aria-hidden="true" />
            <span>{removeError}</span>
          </p>
        )}
      </div>
      {(onOpen || onRemove) && (
        <div className="tmdb-link-actions">
          {onOpen && (
            <Button type="button" variant="outline" size="sm" title={t('card.actionTitle')} onClick={onOpen}>
              <Link2 className="h-3.5 w-3.5" aria-hidden="true" />
              <span className="label-text">{t('card.action')}</span>
            </Button>
          )}
          {onRemove && (
            // A destructive trigger in a row: the outline danger button; the confirmation's is filled.
            <Button type="button" variant="outline" size="sm" className="ui-button-danger" title={t('rows.remove')} disabled={removing} onClick={onRemove}>
              <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />
              <span className="label-text">{removeLabel}</span>
            </Button>
          )}
        </div>
      )}
    </article>
  )
}

/** What a link's card is headed by: its work's title, else the item's as it was linked. */
function rowTitle(row: LibraryLinkRow): string {
  return row.workTitle ?? row.itemTitle ?? i18n.t('libraryLinks:dialog.untitledItem')
}
