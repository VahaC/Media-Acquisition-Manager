import { Fragment, useState } from 'react'
import { Pencil, Plus, Trash2 } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { DestinationDialog } from '@/components/destinations/DestinationDialog'
import { DestinationSavesTo } from '@/components/destinations/DestinationSavesTo'
import { Banner } from '@/components/shared/Banner'
import { useConfirm } from '@/components/shared/ConfirmDialog'
import { ErrorState } from '@/components/shared/ErrorState'
import { ListPager } from '@/components/shared/ListPager'
import { RetryButton } from '@/components/shared/RetryButton'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { useAuthStore } from '@/lib/auth-store'
import { DOWNLOAD_CLIENTS, sharedKindOf } from '@/lib/download-clients'
import { useDeleteDestination, useDestinations, useIntegrations } from '@/lib/queries'
import type { Destination, DestinationList, DownloadDestinationKind } from '@/lib/types'
import { useDebounced } from '@/lib/use-debounced'
import { useLoadRetry } from '@/lib/use-load-retry'
import { PAGE_SIZES, usePageSize } from '@/lib/use-page-size'
import { getApiErrorMessage, retryCanHelp } from '@/lib/utils'
import '@/styles/settings-list.css' // .settings-* — the header, search, table and row actions of this page
import '@/styles/destinations.css' // .destinations-key*, .destinations-saves-to — the Category or Folder cell and Saves to

/** Per device: a phone and a desktop want different page lengths. */
const PAGE_SIZE_STORAGE_KEY = 'mam:destinations:pageSize'
const SEARCH_DEBOUNCE_MS = 300

/**
 * MAM-11 — Settings → Destinations, owner-only: where downloads go, searched and paged by the
 * server. Each destination is a category of a download client, never a folder of the app's own
 * (ADR 0010); its row says where that category saves and whether the folder lands in a media library,
 * as the API checked it while answering. One is the default. Add one, edit it, remove it after a
 * confirmation; removing changes nothing in the client.
 *
 * MAM-30 — or a folder of a client that saves to folders (Transmission): the column that names the
 * category says Folder when every row on the page is a folder, and "Category or folder" when they
 * are mixed, while each phone card names its own; the removal says the folder and label of torrents
 * already sent stay as they are.
 *
 * MAM-31 — the lead and the empty state speak of what the installation's download clients save to:
 * a category when every one has categories, a folder when every one saves to folders, and neither
 * while they differ or aren't known (on their way, none, failed, or a non-owner's page), as the live
 * check found "category" said with Transmission alone. The clients are the destination dialog's own
 * query, so the dialog opens on them in the same words.
 *
 * A non-owner who opens the address gets the API's 403 sentence in place of the list.
 */
export function DestinationsPage() {
  const { t } = useTranslation(['destinations', 'common'])
  const isOwner = useAuthStore((state) => state.user?.isOwner === true)
  const confirm = useConfirm()
  const remove = useDeleteDestination()
  const [search, setSearch] = useState('')
  const [pageSize, setPageSize] = usePageSize(PAGE_SIZE_STORAGE_KEY)
  // The page remembers the search it belongs to, so a different search starts again at page 1
  // without an effect — the idiom of UsersPage.
  const [paging, setPaging] = useState({ search: '', page: 1 })
  // Open with no destination = Add; with one = Edit that one.
  const [dialog, setDialog] = useState<{ destination?: Destination } | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)

  // Typing waits for a pause; emptying the box shows every destination at once.
  const debouncedSearch = useDebounced(search.trim(), SEARCH_DEBOUNCE_MS)
  const appliedSearch = search.trim() === '' ? '' : debouncedSearch
  // MAM-4 — the stored page is reset on every change of the applied search, not only derived: kept,
  // it came back with an earlier search, which by then could lie past the last page. The derivation
  // below keeps this discarded render off the stale page.
  if (paging.search !== appliedSearch) setPaging({ search: appliedSearch, page: 1 })
  const page = paging.search === appliedSearch ? paging.page : 1
  const destinations = useDestinations({ search: appliedSearch || undefined, page, pageSize })
  const { data, error, isLoading, isPlaceholderData } = destinations
  const load = useLoadRetry(destinations)
  // MAM-31 — asked for the owner only: the API refuses a non-owner the clients as it does the list.
  const clients = useIntegrations(DOWNLOAD_CLIENTS, { enabled: isOwner })
  const words = wordsOf(clients.data ? sharedKindOf(clients.data.items) : null)
  const pages = data ? Math.max(1, Math.ceil(data.totalCount / pageSize)) : 1

  const goToPage = (next: number) => setPaging({ search: appliedSearch, page: next })

  const changePageSize = (next: number) => {
    setPageSize(next)
    goToPage(1)
  }

  const removeDestination = async (destination: Destination) => {
    const values = { name: destination.name, client: destination.downloadClientName }
    const folder = destination.destinationKind === 'Folder'
    const confirmed = await confirm({
      title: t('remove.title', { name: destination.name }),
      // Decision 2 — nothing takes the default over on its own; the owner is told before, not after.
      // MAM-30 — a folder has no category that could stay: the torrents keep their folder and label.
      message: destination.isDefault
        ? folder ? t('remove.messageDefaultFolder', values) : t('remove.messageDefault', values)
        : folder ? t('remove.messageFolder', values) : t('remove.message', values),
      confirmLabel: t('remove.confirm'),
      destructive: true,
    })
    if (!confirmed) return
    setActionError(null)
    try {
      await remove.mutateAsync(destination.id)
      // The last row of a later page is gone: show the page before it rather than an empty one.
      if (data?.items.length === 1 && page > 1) goToPage(page - 1)
    } catch (e: unknown) {
      setActionError(getApiErrorMessage(e, t('actionFailed')))
    }
  }

  return (
    <div className="settings-page">
      <div className="settings-header">
        <div className="settings-intro">
          <h1 className="settings-title">{t('title')}</h1>
          <p className="settings-lead">{t(`lead${words}`)}</p>
        </div>
        {isOwner && (
          <Button type="button" onClick={() => setDialog({})}>
            <Plus className="h-4 w-4" aria-hidden="true" />
            {t('add')}
          </Button>
        )}
      </div>

      {isOwner && (
        <Input
          type="search"
          className="settings-search"
          value={search}
          placeholder={t('search.placeholder')}
          aria-label={t('search.label')}
          onChange={(event) => setSearch(event.target.value)}
        />
      )}

      {/* Outside the ternary below, so a failed refresh or a refused removal adds a line above the
          rows instead of replacing them. */}
      {error && data && !isPlaceholderData && (
        <Banner action={<RetryButton onRetry={() => destinations.refetch()} />}>
          {t('refreshFailed')}{' '}
          {getApiErrorMessage(error)}
        </Banner>
      )}
      {actionError && <Banner onDismiss={() => setActionError(null)}>{actionError}</Banner>}

      {/* A member's 403 is the server's lasting answer: no Try again for it (retryCanHelp). */}
      {load.error ? (
        <ErrorState
          title={t('loadFailed')}
          onRetry={retryCanHelp(load.error) ? load.retry : undefined}
          retrying={load.retrying}
        >
          {getApiErrorMessage(load.error)}
        </ErrorState>
      ) : isLoading ? (
        <p className="settings-muted">{t('common:loading')}</p>
      ) : data && data.items.length === 0 && appliedSearch !== '' ? (
        <div className="settings-empty">
          <p>{t('noMatch', { search: appliedSearch })}</p>
          <Button type="button" variant="outline" size="sm" onClick={() => setSearch('')}>
            {t('clearSearch')}
          </Button>
        </div>
      ) : data && data.items.length === 0 ? (
        <div className="settings-empty">
          <p>{t(`empty${words}`)}</p>
        </div>
      ) : data ? (
        <>
          <div className="settings-table-wrap">
            <table className="settings-table">
              <thead>
                <tr>
                  <th scope="col">{t('columns.name')}</th>
                  <th scope="col">{t('columns.client')}</th>
                  <th scope="col">{t(keyColumnOf(data))}</th>
                  <th scope="col">{t('columns.savesTo')}</th>
                  <th scope="col">{t('columns.actions')}</th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((destination) => (
                  <DestinationRow
                    key={destination.id}
                    destination={destination}
                    removing={remove.isPending && remove.variables === destination.id}
                    onEdit={() => setDialog({ destination })}
                    onRemove={() => void removeDestination(destination)}
                  />
                ))}
              </tbody>
            </table>
          </div>

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

      {dialog && <DestinationDialog destination={dialog.destination} onClose={() => setDialog(null)} />}
    </div>
  )
}

// ── one row ──

interface DestinationRowProps {
  destination: Destination
  /** Its removal is under way. */
  removing: boolean
  onEdit: () => void
  onRemove: () => void
}

/**
 * A table row on a wide screen, a card on a phone: every cell carries its column name in
 * `data-label` for the stacked layout. The category is the client's own name for it, shown as the
 * client spells it. MAM-30 — a folder is shown as a path, and its card names it a folder whatever
 * the column says.
 */
function DestinationRow({ destination, removing, onEdit, onRemove }: DestinationRowProps) {
  const { t } = useTranslation('destinations')
  const folder = destination.destinationKind === 'Folder'

  return (
    <tr>
      <td data-label={t('columns.name')}>
        <span className="settings-name">
          <span className="settings-name-text">{destination.name}</span>
          {destination.isDefault && <span className="ui-badge ui-badge-default">{t('default')}</span>}
        </span>
      </td>
      <td data-label={t('columns.client')}>{destination.downloadClientName}</td>
      <td data-label={folder ? t('columns.folder') : t('columns.category')}>
        {folder ? (
          <span className="destinations-key destinations-key-folder">
            <FolderPath path={destination.clientDestinationKey} />
          </span>
        ) : (
          <span className="destinations-key">{destination.clientDestinationKey}</span>
        )}
      </td>
      <td data-label={t('columns.savesTo')} className="destinations-saves-to">
        <DestinationSavesTo check={destination.check} />
      </td>
      <td className="settings-actions-cell">
        <div className="settings-actions">
          <Button
            type="button"
            variant="outline"
            size="sm"
            title={t('actions.edit')}
            disabled={removing}
            onClick={onEdit}
          >
            <Pencil className="h-3.5 w-3.5" aria-hidden="true" />
            <span className="label-text">{t('actions.edit')}</span>
          </Button>
          <Button
            type="button"
            variant="outline"
            size="sm"
            className="ui-button-danger"
            title={t('actions.remove')}
            disabled={removing}
            onClick={onRemove}
          >
            <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />
            <span className="label-text">{t('actions.remove')}</span>
          </Button>
        </div>
      </td>
    </tr>
  )
}

/**
 * MAM-30 — a folder that may break after each `/` or, as a client on Windows writes it (`D:\Downloads`),
 * each `\`: a path is one long word to the browser, which kept the column as wide as the whole path on
 * a wide screen (and past the table's edge for a long one) yet broke it anywhere once squeezed.
 * Breaking between its folders keeps each name whole; never between the two `\` that begin a network
 * path (`\\nas\media`).
 */
function FolderPath({ path }: { path: string }) {
  const parts = path.split(/(?<=[\\/])(?![\\/])/)
  return parts.map((part, index) => (
    <Fragment key={index}>
      {index > 0 && <wbr />}
      {part}
    </Fragment>
  ))
}

// ── helpers ──

/**
 * MAM-31 — which of a sentence's wordings the clients' kind takes: the category's (the key with no
 * suffix, as every destination was one before), the folder's, or the neutral one while the kind isn't
 * known.
 */
function wordsOf(kind: DownloadDestinationKind | null) {
  if (kind === 'Category') return '' as const
  return kind === 'Folder' ? 'Folder' as const : 'Neutral' as const
}

/**
 * MAM-30 — the name of the column that holds each row's key, from the rows on the page: Folder when
 * every one is a folder, "Category or folder" when some are, and Category when none is — a
 * destination whose client type this build doesn't register counts as a category, as every
 * destination was one before.
 */
function keyColumnOf(page: DestinationList) {
  const folders = page.items.filter((destination) => destination.destinationKind === 'Folder').length
  if (folders === 0) return 'columns.category' as const
  return folders === page.items.length ? 'columns.folder' as const : 'columns.categoryOrFolder' as const
}
