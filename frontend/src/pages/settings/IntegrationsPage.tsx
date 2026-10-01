import { useState } from 'react'
import { CircleAlert, Loader2, Pencil, Plus, PlugZap, Trash2 } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { IntegrationDialog } from '@/components/integrations/IntegrationDialog'
import { useConfirm } from '@/components/shared/ConfirmDialog'
import { DisabledReason } from '@/components/shared/DisabledReason'
import { ListPager } from '@/components/shared/ListPager'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { useAuthStore } from '@/lib/auth-store'
import { useDeleteIntegration, useIntegrations, useTestIntegration } from '@/lib/queries'
import type { IntegrationKind, IntegrationSummary } from '@/lib/types'
import { useDebounced } from '@/lib/use-debounced'
import { PAGE_SIZES, usePageSize } from '@/lib/use-page-size'
import { getApiErrorMessage } from '@/lib/utils'
import '@/styles/settings-list.css' // .settings-* — the header, toolbar, banners, table and row actions of this page
import '@/styles/integrations.css' // .integrations-service*, .integrations-address — the cells only this page has

/** Per device: a phone and a desktop want different page lengths. */
const PAGE_SIZE_STORAGE_KEY = 'mam:integrations:pageSize'
const SEARCH_DEBOUNCE_MS = 300
/** The kind filter's options, in the order the API lists the kinds. */
const KINDS: readonly IntegrationKind[] = ['MediaLibrary', 'ReleaseSearch', 'DownloadClient']

/** The last row test that answered: the integration it tested and what the service said. */
interface RowTestResult {
  name: string
  ok: boolean
  message: string
}

/**
 * Settings → Integrations, owner-only: the services the app works with — media libraries, release
 * sources and download clients — searched, filtered by kind and paged by the server. Add one, edit
 * it, test its connection from its row, remove it after a confirmation. An integration whose type
 * this version no longer connects to can only be removed, and says why.
 *
 * A non-owner who opens the address gets the API's 403 sentence in place of the list.
 */
export function IntegrationsPage() {
  const { t } = useTranslation(['integrations', 'common'])
  const isOwner = useAuthStore((state) => state.user?.isOwner === true)
  const confirm = useConfirm()
  const remove = useDeleteIntegration()
  const test = useTestIntegration()
  const [search, setSearch] = useState('')
  const [kind, setKind] = useState<IntegrationKind | ''>('')
  const [pageSize, setPageSize] = usePageSize(PAGE_SIZE_STORAGE_KEY)
  // The page remembers the filters it belongs to, so different filters start again at page 1
  // without an effect — the idiom of UsersPage, with the kind beside the search.
  const [paging, setPaging] = useState({ search: '', kind: '', page: 1 })
  // Open with no integration = Add; with one = Edit that one.
  const [dialog, setDialog] = useState<{ integration?: IntegrationSummary } | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  const [testResult, setTestResult] = useState<RowTestResult | null>(null)

  // Typing waits for a pause; emptying the box shows every integration at once.
  const debouncedSearch = useDebounced(search.trim(), SEARCH_DEBOUNCE_MS)
  const appliedSearch = search.trim() === '' ? '' : debouncedSearch
  // MAM-4 — the stored page is reset on every change of the applied filters, not only derived: kept,
  // it came back with earlier filters, which by then could lie past the last page. The derivation
  // below keeps this discarded render off the stale page.
  const onFilters = paging.search === appliedSearch && paging.kind === kind
  if (!onFilters) setPaging({ search: appliedSearch, kind, page: 1 })
  const page = onFilters ? paging.page : 1
  const filtered = appliedSearch !== '' || kind !== ''
  const { data, error, isLoading } = useIntegrations({
    search: appliedSearch || undefined,
    kind: kind || undefined,
    page,
    pageSize,
  })
  const pages = data ? Math.max(1, Math.ceil(data.totalCount / pageSize)) : 1

  const goToPage = (next: number) => setPaging({ search: appliedSearch, kind, page: next })

  const changePageSize = (next: number) => {
    setPageSize(next)
    goToPage(1)
  }

  const clearFilters = () => {
    setSearch('')
    setKind('')
  }

  /** Runs one row action; each action reports only its own outcome above the list. */
  const runAction = async (action: () => Promise<void>, fallback: string) => {
    setActionError(null)
    setTestResult(null)
    try {
      await action()
    } catch (e: unknown) {
      setActionError(getApiErrorMessage(e, fallback))
    }
  }

  const testIntegration = (integration: IntegrationSummary) =>
    void runAction(async () => {
      const result = await test.mutateAsync(integration.id)
      // 200 whatever the service said: a refusal is the answer to "does it connect?", not a failure.
      setTestResult({ name: integration.name, ok: result.status === 'Succeeded', message: result.message })
    }, t('testFailed'))

  const removeIntegration = async (integration: IntegrationSummary) => {
    const confirmed = await confirm({
      title: t('remove.title', { name: integration.name }),
      // MAM-8 — removing a media library also purges the library it imported (ADR 0006), which
      // every user sees on the Library page; the owner is told before, not after.
      message: integration.kind === 'MediaLibrary'
        ? t('remove.messageLibrary', {
          name: integration.name,
          service: integration.adapterDisplayName ?? integration.adapterType,
        })
        : t('remove.message', { name: integration.name }),
      confirmLabel: t('remove.confirm'),
      destructive: true,
    })
    if (!confirmed) return
    await runAction(async () => {
      await remove.mutateAsync(integration.id)
      // The last row of a later page is gone: show the page before it rather than an empty one.
      if (data?.items.length === 1 && page > 1) goToPage(page - 1)
    }, t('actionFailed'))
  }

  return (
    <div className="settings-page">
      <div className="settings-header">
        <div className="settings-intro">
          <h1 className="settings-title">{t('title')}</h1>
          <p className="settings-lead">{t('lead')}</p>
        </div>
        {isOwner && (
          <Button type="button" onClick={() => setDialog({})}>
            <Plus className="h-4 w-4" aria-hidden="true" />
            {t('add')}
          </Button>
        )}
      </div>

      {isOwner && (
        <div className="settings-toolbar">
          <Input
            type="search"
            className="settings-search"
            value={search}
            placeholder={t('search.placeholder')}
            aria-label={t('search.label')}
            onChange={(event) => setSearch(event.target.value)}
          />
          <select
            className="ui-input settings-filter"
            value={kind}
            aria-label={t('kindFilter.label')}
            onChange={(event) => setKind(event.target.value as IntegrationKind | '')}
          >
            <option value="">{t('kindFilter.all')}</option>
            {KINDS.map((option) => (
              <option key={option} value={option}>{t(`kindFilterOptions.${option}`)}</option>
            ))}
          </select>
        </div>
      )}

      {/* Outside the ternary below, so a failed refresh, a refused action or a test's answer adds a
          line above the rows instead of replacing them. */}
      {error && data && (
        <p className="settings-banner" role="alert">
          <CircleAlert className="h-3.5 w-3.5 inline" aria-hidden="true" />{' '}
          {t('refreshFailed')}{' '}
          {getApiErrorMessage(error)}
        </p>
      )}
      {actionError && <p className="settings-banner" role="alert">{actionError}</p>}
      {testResult && (
        <p className="settings-banner" data-tone={testResult.ok ? 'ok' : undefined} role="status">
          {t('testResult', { name: testResult.name, message: testResult.message })}
        </p>
      )}

      {isLoading ? (
        <p className="settings-muted">{t('common:loading')}</p>
      ) : error && !data ? (
        <p className="settings-error" role="alert">{getApiErrorMessage(error, t('loadFailed'))}</p>
      ) : data && data.items.length === 0 && filtered ? (
        <div className="settings-empty">
          <p>{t('noMatch')}</p>
          <Button type="button" variant="outline" size="sm" onClick={clearFilters}>
            {t('clearFilters')}
          </Button>
        </div>
      ) : data && data.items.length === 0 ? (
        <div className="settings-empty">
          <p>{t('empty')}</p>
        </div>
      ) : data ? (
        <>
          <div className="settings-table-wrap">
            <table className="settings-table">
              <thead>
                <tr>
                  <th scope="col">{t('columns.name')}</th>
                  <th scope="col">{t('columns.service')}</th>
                  <th scope="col">{t('columns.address')}</th>
                  <th scope="col">{t('columns.actions')}</th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((integration) => (
                  <IntegrationRow
                    key={integration.id}
                    integration={integration}
                    testing={test.isPending && test.variables === integration.id}
                    removing={remove.isPending && remove.variables === integration.id}
                    onTest={() => testIntegration(integration)}
                    onEdit={() => setDialog({ integration })}
                    onRemove={() => void removeIntegration(integration)}
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
            onPage={goToPage}
            onPageSize={changePageSize}
          />
        </>
      ) : null}

      {dialog && <IntegrationDialog integration={dialog.integration} onClose={() => setDialog(null)} />}
    </div>
  )
}

// ── one row ──

interface IntegrationRowProps {
  integration: IntegrationSummary
  /** Its connection test is under way. */
  testing: boolean
  /** Its removal is under way. */
  removing: boolean
  onTest: () => void
  onEdit: () => void
  onRemove: () => void
}

/**
 * A table row on a wide screen, a card on a phone: every cell carries its column name in
 * `data-label` for the stacked layout. Test answers above the list, in the page's banner.
 */
function IntegrationRow({ integration, testing, removing, onTest, onEdit, onRemove }: IntegrationRowProps) {
  const { t } = useTranslation('integrations')
  // null: this build no longer registers the type, so it can neither test nor edit it.
  const unknownType = integration.adapterDisplayName === null
    ? t('unknownType', { type: integration.adapterType })
    : undefined
  const testLabel = testing ? t('actions.testing') : t('actions.test')

  return (
    <tr data-disabled={!integration.isEnabled || undefined}>
      <td data-label={t('columns.name')}>
        <span className="settings-name">
          <span className="settings-name-text">{integration.name}</span>
          {!integration.isEnabled && <span className="ui-badge ui-badge-outline">{t('disabled')}</span>}
        </span>
      </td>
      <td data-label={t('columns.service')}>
        <span className="integrations-service">
          <span>{integration.adapterDisplayName ?? integration.adapterType}</span>
          <span className="integrations-service-kind">{t(`kinds.${integration.kind}`)}</span>
        </span>
      </td>
      <td data-label={t('columns.address')}>
        <span className="integrations-address">{integration.baseUrl}</span>
      </td>
      <td className="settings-actions-cell">
        <div className="settings-actions">
          <DisabledReason reason={unknownType}>
            <Button
              type="button"
              variant="outline"
              size="sm"
              title={testLabel}
              disabled={unknownType !== undefined || testing || removing}
              onClick={onTest}
            >
              {testing
                ? <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />
                : <PlugZap className="h-3.5 w-3.5" aria-hidden="true" />}
              <span className="label-text">{testLabel}</span>
            </Button>
          </DisabledReason>
          <DisabledReason reason={unknownType}>
            <Button
              type="button"
              variant="outline"
              size="sm"
              title={t('actions.edit')}
              disabled={unknownType !== undefined || removing}
              onClick={onEdit}
            >
              <Pencil className="h-3.5 w-3.5" aria-hidden="true" />
              <span className="label-text">{t('actions.edit')}</span>
            </Button>
          </DisabledReason>
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
