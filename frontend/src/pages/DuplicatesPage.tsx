import { useId, useState } from 'react'
import { CircleHelp, Copy, EyeOff, Fingerprint, Layers, Link, type LucideIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { useSearchParams } from 'react-router-dom'
import { DuplicateGroupCard } from '@/components/duplicates/DuplicateGroupCard'
import { DuplicateLeftRow } from '@/components/duplicates/DuplicateLeftRow'
import { Banner } from '@/components/shared/Banner'
import { ErrorState } from '@/components/shared/ErrorState'
import { ListPager } from '@/components/shared/ListPager'
import { PageTabs } from '@/components/shared/PageTabs'
import { RetryButton } from '@/components/shared/RetryButton'
import { useAuthStore } from '@/lib/auth-store'
import { DUPLICATE_TABS, tabFrom, tabQuery, type DuplicateLeaving, type DuplicateTab } from '@/lib/duplicates'
import { useLibraryDuplicates } from '@/lib/queries'
import type { LibraryDuplicateGroup } from '@/lib/types'
import { useLoadRetry } from '@/lib/use-load-retry'
import { PAGE_SIZES, usePageSize } from '@/lib/use-page-size'
import { getApiErrorMessage, retryCanHelp } from '@/lib/utils'
import '@/styles/settings-list.css' // .settings-* — the header, the loading line and the empty states of this page
import '@/styles/duplicates.css' // .duplicates-panel, -groups — the tab's column and the column of group cards

/** Per device: a phone and a desktop want different page lengths. */
const PAGE_SIZE_STORAGE_KEY = 'mam:duplicates:pageSize'

const TAB_ICONS: Record<DuplicateTab, LucideIcon> = {
  All: Layers,
  SameFile: Link,
  ByteIdentical: Fingerprint,
  SameWork: Copy,
  Possible: CircleHelp,
  Dismissed: EyeOff,
}

/**
 * A possible match the owner's own decision took off the tab on screen, kept as a short row where its card
 * was: the group as it was shown, its place among the rows, and what took it off.
 */
interface LeftGroup {
  group: LibraryDuplicateGroup
  index: number
  change: DuplicateLeaving
}

/**
 * MAM-21 — the Duplicates page (`/library/duplicates`, from the header and the Library page's Duplicates
 * button): the groups of MAM-20, each with the evidence behind it (DuplicateGroupCard), for every signed-in
 * user. The tabs are the classes of specification §3.3 in its order — All, Same file, Byte-identical, Same
 * work, Possible — and Dismissed, the possible matches the owner took off the list, which every other tab
 * leaves out. The server filters and pages them.
 *
 * The tab is in the address (`?tab=Possible`, left out for All; one that isn't a tab reads as All), so F5
 * and Back keep it, and a new tab starts at page 1 — the page belongs to the tab it was reached on, the
 * idiom of LibraryPage, without an effect.
 *
 * The owner confirms or dismisses a possible match on its card (Vasyl's decision of 2026-10-07; a member
 * sees the buttons disabled with the reason). A dismissed one leaves the list for Dismissed, and an undone
 * dismissal leaves Dismissed: either way a short row with the way back takes the card's place
 * (DuplicateLeftRow), as "Not interested" leaves a recommendation, rather than the list closing up under the
 * pointer. The rows stay until the tab or the page changes. Nothing on the page removes, moves or renames a
 * file, and nothing acts on many groups at once.
 */
export function DuplicatesPage() {
  const { t } = useTranslation(['duplicates', 'common'])
  const isOwner = useAuthStore((state) => state.user?.isOwner === true)
  const [searchParams, setSearchParams] = useSearchParams()
  const tab = tabFrom(searchParams.get('tab'))
  const idPrefix = useId()
  const panelId = `${idPrefix}-panel`
  const tabId = (of: DuplicateTab) => `${idPrefix}-tab-${of}`
  const [pageSize, setPageSize] = usePageSize(PAGE_SIZE_STORAGE_KEY)
  // The page belongs to the tab it was reached on, so another tab starts at page 1.
  const [paging, setPaging] = useState({ tab, page: 1 })
  const onTab = paging.tab === tab
  if (!onTab) setPaging({ tab, page: 1 })
  const page = onTab ? paging.page : 1
  const groups = useLibraryDuplicates({ ...tabQuery(tab), page, pageSize })
  const { data, error, isLoading, isPlaceholderData } = groups
  const load = useLoadRetry(groups)
  const pages = data ? Math.max(1, Math.ceil(data.totalCount / pageSize)) : 1
  // A decision, a sync or an inspection can leave fewer groups than the page on screen needs: move to the
  // last page there is. Only on this page's own answer, not the one kept on screen while it loads.
  if (onTab && data && !isPlaceholderData && page > pages) setPaging({ tab, page: pages })
  // The short rows belong to the tab and the page they were made on, as the page belongs to its tab.
  const [left, setLeft] = useState<{ tab: DuplicateTab; page: number; groups: LeftGroup[] }>({ tab, page: 1, groups: [] })
  const leftGroups = left.tab === tab && left.page === page ? left.groups : []
  const isLeft = (key: string) => leftGroups.some((entry) => entry.group.key === key)
  // The cards in the server's order, each short row put back at the place its card had.
  const rows: ({ kind: 'card'; group: LibraryDuplicateGroup } | ({ kind: 'left' } & LeftGroup))[] = (data?.items ?? [])
    .filter((group) => !isLeft(group.key))
    .map((group) => ({ kind: 'card', group }))
  for (const entry of leftGroups.toSorted((a, b) => a.index - b.index)) {
    rows.splice(Math.min(entry.index, rows.length), 0, { kind: 'left', ...entry })
  }

  const selectTab = (next: DuplicateTab) => {
    if (next !== tab) setSearchParams(next === 'All' ? {} : { tab: next }, { replace: true })
  }

  const goToPage = (next: number) => setPaging({ tab, page: next })

  const changePageSize = (next: number) => {
    setPageSize(next)
    goToPage(1)
  }

  // From the rows as they are when the request ends, not as they were when its button was pressed: two cards
  // can leave while each other's request runs.
  const leave = (group: LibraryDuplicateGroup, index: number, change: DuplicateLeaving) =>
    setLeft((current) => ({
      tab,
      page,
      groups: [...(current.tab === tab && current.page === page ? current.groups : []), { group, index, change }],
    }))

  // Its way back went through, and the list it waited for shows the group again.
  const back = (key: string) =>
    setLeft((current) => ({
      tab,
      page,
      groups: (current.tab === tab && current.page === page ? current.groups : []).filter((entry) => entry.group.key !== key),
    }))

  return (
    <div className="settings-page">
      <div className="settings-header">
        <div className="settings-intro">
          <h1 className="settings-title">{t('title')}</h1>
          <p className="settings-lead">{t('lead')}</p>
        </div>
      </div>

      <PageTabs
        label={t('tabs.label')}
        tabs={DUPLICATE_TABS.map((value) => ({ value, label: t(`tabs.${value}`), icon: TAB_ICONS[value] }))}
        current={tab}
        panelId={panelId}
        tabId={tabId}
        onSelect={selectTab}
      />

      <div id={panelId} role="tabpanel" aria-labelledby={tabId(tab)} className="duplicates-panel">
        {/* Outside the ternary below, so a failed refresh adds a line above the cards instead of
            replacing them. */}
        {error && data && !isPlaceholderData && (
          <Banner action={<RetryButton onRetry={() => groups.refetch()} />}>
            {t('refreshFailed')}{' '}
            {getApiErrorMessage(error)}
          </Banner>
        )}

        {load.error ? (
          // A page or filter the server refuses (400) is its lasting answer: no Try again for it.
          <ErrorState
            title={t('loadFailed')}
            onRetry={retryCanHelp(load.error) ? load.retry : undefined}
            retrying={load.retrying}
          >
            {getApiErrorMessage(load.error)}
          </ErrorState>
        ) : isLoading ? (
          <p className="settings-muted">{t('common:loading')}</p>
        ) : data && data.totalCount === 0 && leftGroups.length === 0 ? (
          <div className="settings-empty">
            <p>{t(`empty.${tab}`)}</p>
          </div>
        ) : data ? (
          <>
            <ul className="duplicates-groups">
              {rows.map((row, index) => (
                <li key={row.group.key}>
                  {row.kind === 'left'
                    ? <DuplicateLeftRow group={row.group} change={row.change} onBack={() => back(row.group.key)} />
                    : <DuplicateGroupCard group={row.group} isOwner={isOwner} onLeft={(change) => leave(row.group, index, change)} />}
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
      </div>
    </div>
  )
}
