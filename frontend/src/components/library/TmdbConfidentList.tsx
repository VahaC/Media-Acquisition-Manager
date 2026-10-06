import { useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { ArrowLeft, ArrowRight, CircleAlert, CircleCheck, Link2, Loader2, TriangleAlert } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { LinkEvidenceBadges } from '@/components/library/LinkEvidenceBadges'
import { LinkStateBadge } from '@/components/library/LinkStateBadge'
import { Banner } from '@/components/shared/Banner'
import { CatalogPoster } from '@/components/shared/CatalogPoster'
import { DisabledReason } from '@/components/shared/DisabledReason'
import { ErrorState } from '@/components/shared/ErrorState'
import { ListPager } from '@/components/shared/ListPager'
import { RetryButton } from '@/components/shared/RetryButton'
import { Button } from '@/components/ui/button'
import { formatNumber } from '@/lib/format'
import { i18n } from '@/lib/i18n'
import { isBatchUnderWay } from '@/lib/library-links'
import { qk, useAcceptConfidentSuggestions, useConfidentSuggestions, useLibraryLinkBatch } from '@/lib/queries'
import type {
  ConfidentSuggestion,
  LibraryLinkBatch,
  LibraryLinkBatchItem,
  LibraryLinkLocationCount,
  LibraryLinkRow,
  LibraryWork,
  LinkCandidate,
} from '@/lib/types'
import { useLoadRetry } from '@/lib/use-load-retry'
import { PAGE_SIZES, usePageSize } from '@/lib/use-page-size'
import { getApiErrorMessage, retryCanHelp } from '@/lib/utils'
import '@/styles/settings-list.css' // .settings-muted, .settings-empty, .settings-filter — the lines and the library select
import '@/styles/settings-dialog.css' // .settings-check — a row's box beside its title
import '@/styles/library.css' // .library-work-meta, .library-source-status, -warning — the facts and the result lines
import '@/styles/library-links.css' // .tmdb-confident-* — the counts, the toolbar and the rows

/** Per device: a phone and a desktop want different page lengths. */
const PAGE_SIZE_STORAGE_KEY = 'mam:library-tmdb:confidentPageSize'

/** One click of "Link N works": the rows as they were clicked, and the click's own answer. */
interface Accepted {
  rows: ConfidentSuggestion[]
  /**
   * The batch as the click answered it: the only answer that names the works skipped and why. They
   * aren't stored, so every later answer, rebuilt from the stored links, leaves them out.
   */
  first: LibraryLinkBatch
}

/**
 * MAM-34 — the TMDb page's Confident tab: every work whose suggestion is confident (decision 8 — the
 * one candidate whose kind, title, year and runtime all agree), a page at a time, with each library's
 * counts above them. Every row starts ticked; "Link N works" links the ticked rows of the page shown
 * in one click, each to its suggestion — a series' like a film's —, and the library writes happen in
 * the background.
 *
 * After the click the rows stay as they were clicked — a local snapshot, not the list, which would
 * drop the linked works under the pointer (R24) — and each shows how its write went, asked for every
 * 2 s until none waits or is being written. "Back to the confident suggestions" leaves them for the
 * list as it now is.
 */
export function TmdbConfidentList() {
  const queryClient = useQueryClient()
  const [accepted, setAccepted] = useState<Accepted | null>(null)

  const done = () => {
    // The list left alone while the rows showed: the next one asked for starts from scratch, rather
    // than showing the linked works ticked again until it answers.
    queryClient.removeQueries({ queryKey: qk.library.links.confidentAll })
    setAccepted(null)
  }

  return accepted === null
    ? <ConfidentPage onAccepted={setAccepted} />
    : <AcceptedRows accepted={accepted} onDone={done} />
}

// ── the list ──

/** The counts, the library filter with "Link N works", the rows with their boxes, and the pager. */
function ConfidentPage({ onAccepted }: { onAccepted: (accepted: Accepted) => void }) {
  const { t } = useTranslation(['libraryLinks', 'common'])
  const [pageSize, setPageSize] = usePageSize(PAGE_SIZE_STORAGE_KEY)
  const [view, setView] = useState({ locationId: '', page: 1 })
  // The rows not ticked, by work: every row — of another page too — starts ticked.
  const [unticked, setUnticked] = useState<string[]>([])
  const [acceptError, setAcceptError] = useState<string | null>(null)
  const accept = useAcceptConfidentSuggestions()
  // A double click links once.
  const inFlight = useRef(false)

  const list = useConfidentSuggestions({ locationId: view.locationId || undefined, page: view.page, pageSize })
  const { data, error, isLoading, isPlaceholderData } = list
  const load = useLoadRetry(list)
  const pages = data ? Math.max(1, Math.ceil(data.totalCount / pageSize)) : 1
  // A link elsewhere can leave fewer works than the page on screen needs: move to the last page there
  // is. Only on this page's own answer, not the one kept on screen while it loads.
  if (data && !isPlaceholderData && view.page > pages) setView({ ...view, page: pages })
  // The rows the click links: the ticked ones of the page on screen, and only of its own answer.
  const ticked = data && !isPlaceholderData ? data.items.filter((row) => !unticked.includes(row.work.id)) : []
  const linkReason = ticked.length === 0 ? t('confident.noneTicked') : undefined

  const showView = (next: { locationId: string; page: number }) => {
    setView(next)
    setUnticked([])
    setAcceptError(null)
  }

  const changePageSize = (next: number) => {
    setPageSize(next)
    showView({ ...view, page: 1 })
  }

  const toggle = (workId: string, on: boolean) =>
    setUnticked((ids) => (on ? ids.filter((id) => id !== workId) : [...ids, workId]))

  const link = async () => {
    if (ticked.length === 0 || inFlight.current) return
    inFlight.current = true
    setAcceptError(null)
    try {
      const batch = await accept.mutateAsync(ticked.map((row) => ({
        workId: row.work.id,
        catalogKind: row.candidate.catalogKind,
        catalogId: row.candidate.catalogId,
      })))
      onAccepted({ rows: ticked, first: batch })
    } catch (e: unknown) {
      setAcceptError(getApiErrorMessage(e, t('confident.linkFailed')))
    } finally {
      inFlight.current = false
    }
  }

  const libraries = (data?.locations ?? []).filter((location) => location.locationId !== null)
  const linkLabel = accept.isPending
    ? t('confident.linking')
    : t('confident.link', { count: ticked.length, value: formatNumber(ticked.length) })

  return (
    <>
      {data && data.locations.length > 0 && <LibraryCounts locations={data.locations} />}

      {data && (libraries.length > 1 || view.locationId !== '' || data.totalCount > 0) && (
        <div className="tmdb-confident-toolbar">
          {(libraries.length > 1 || view.locationId !== '') && (
            <select
              className="ui-input settings-filter"
              value={view.locationId}
              aria-label={t('confident.libraryFilter')}
              onChange={(event) => showView({ locationId: event.target.value, page: 1 })}
            >
              <option value="">{t('confident.allLibraries')}</option>
              {libraries.map((location) => (
                <option key={location.locationId} value={location.locationId ?? ''}>{libraryName(location)}</option>
              ))}
            </select>
          )}
          <DisabledReason reason={linkReason}>
            <Button type="button" size="sm" disabled={linkReason !== undefined || accept.isPending} onClick={() => void link()}>
              {accept.isPending
                ? <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />
                : <Link2 className="h-3.5 w-3.5" aria-hidden="true" />}
              {linkLabel}
            </Button>
          </DisabledReason>
        </div>
      )}

      {acceptError && <Banner onDismiss={() => setAcceptError(null)}>{acceptError}</Banner>}
      {/* Outside the ternary below, so a failed refresh adds a line above the rows instead of
          replacing them. */}
      {error && data && !isPlaceholderData && (
        <Banner action={<RetryButton onRetry={() => list.refetch()} />}>
          {t('confident.refreshFailed')}{' '}
          {getApiErrorMessage(error)}
        </Banner>
      )}

      {load.error ? (
        <ErrorState
          title={t('confident.loadFailed')}
          onRetry={retryCanHelp(load.error) ? load.retry : undefined}
          retrying={load.retrying}
        >
          {getApiErrorMessage(load.error)}
        </ErrorState>
      ) : isLoading ? (
        <p className="settings-muted">{t('common:loading')}</p>
      ) : data && data.totalCount === 0 && view.locationId !== '' ? (
        <div className="settings-empty">
          <p>{t('confident.noMatch')}</p>
          <Button type="button" variant="outline" size="sm" onClick={() => showView({ locationId: '', page: 1 })}>
            {t('confident.clearFilter')}
          </Button>
        </div>
      ) : data && data.totalCount === 0 ? (
        <div className="settings-empty">
          <p>{t('confident.empty')}</p>
        </div>
      ) : data ? (
        <>
          <ul className="tmdb-confident-rows" aria-label={t('confident.rows')}>
            {data.items.map((row) => (
              <li key={row.work.id} className="tmdb-confident-row">
                <div className="tmdb-confident-work">
                  <label className="settings-check">
                    <input
                      type="checkbox"
                      checked={!unticked.includes(row.work.id)}
                      onChange={(event) => toggle(row.work.id, event.target.checked)}
                    />
                    <span>{workHeading(row.work)}</span>
                  </label>
                  <WorkFacts work={row.work} />
                </div>
                <ArrowRight className="h-3.5 w-3.5 tmdb-confident-arrow" aria-hidden="true" />
                <CandidateFacts candidate={row.candidate} />
              </li>
            ))}
          </ul>

          <ListPager
            page={view.page}
            pages={pages}
            pageSize={pageSize}
            pageSizes={PAGE_SIZES}
            totalCount={data.totalCount}
            onPage={(page) => showView({ ...view, page })}
            onPageSize={changePageSize}
          />
        </>
      ) : null}
    </>
  )
}

/** Each library's works without a TMDb id, and how many of them have a confident suggestion. */
function LibraryCounts({ locations }: { locations: LibraryLinkLocationCount[] }) {
  const { t } = useTranslation('libraryLinks')
  return (
    <ul className="tmdb-confident-counts" aria-label={t('confident.counts')}>
      {locations.map((location) => (
        <li key={location.locationId ?? ''}>
          <span className="ui-badge ui-badge-outline">
            {t('confident.count', {
              library: libraryName(location),
              confident: formatNumber(location.confidentCount),
              population: formatNumber(location.populationCount),
            })}
          </span>
        </li>
      ))}
    </ul>
  )
}

// ── the rows as they were clicked ──

/**
 * The rows of one click, each with what came of it: its links' states against Jellyfin (with the
 * sentence of a write that failed), or why it wasn't linked. The batch is asked for every 2 s until
 * no link waits or is being written.
 */
function AcceptedRows({ accepted, onDone }: { accepted: Accepted; onDone: () => void }) {
  const { t } = useTranslation(['libraryLinks', 'common'])
  const { first } = accepted
  // A click that linked no work has no batch to ask for: the API would answer 404.
  const batch = useLibraryLinkBatch(first.batchId, first.items.some((item) => item.outcome === 'Linked'))
  const { data, error } = batch
  // The latest answer: the click's own until a poll brings a fresher one.
  const latest = data ?? first
  const links = latest.items.flatMap((item) => item.links)
  const finished = links.filter((one) => one.writeState !== 'Queued' && one.writeState !== 'Writing').length
  // Counted on the click's own answer, which still holds the works skipped.
  const linked = first.items.filter((item) => item.outcome === 'Linked').length
  const underWay = isBatchUnderWay(latest)

  return (
    <>
      <p role="status" className="library-source-status" data-status={underWay ? undefined : 'Succeeded'}>
        {underWay
          ? <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />
          : <CircleCheck className="h-3.5 w-3.5" aria-hidden="true" />}
        <span>
          {underWay
            ? t('confident.writing', { done: formatNumber(finished), total: formatNumber(links.length) })
            : t('confident.summary', { count: accepted.rows.length, value: formatNumber(accepted.rows.length), linked: formatNumber(linked) })}
        </span>
      </p>

      {/* The rows stay on what the last answer said. */}
      {error && (
        <Banner action={<RetryButton onRetry={() => batch.refetch()} />}>
          {t('confident.batchRefreshFailed')}{' '}
          {getApiErrorMessage(error)}
        </Banner>
      )}

      <ul className="tmdb-confident-rows" aria-label={t('confident.rows')}>
        {accepted.rows.map((row) => (
          <li key={row.work.id} className="tmdb-confident-row">
            <div className="tmdb-confident-work">
              <p className="tmdb-link-row-title">{workHeading(row.work)}</p>
              <Outcome item={outcomeOf(first, links, row.work.id)} />
            </div>
            <ArrowRight className="h-3.5 w-3.5 tmdb-confident-arrow" aria-hidden="true" />
            <CandidateFacts candidate={row.candidate} />
          </li>
        ))}
      </ul>

      <div>
        <Button type="button" variant="outline" size="sm" onClick={onDone}>
          <ArrowLeft className="h-3.5 w-3.5" aria-hidden="true" />
          {t('confident.back')}
        </Button>
      </div>
    </>
  )
}

/**
 * What came of one clicked work, as the latest answer has it. A work skipped is told by the click's
 * own answer alone, as no later answer holds it. A linked work's links are found by their ids among
 * the latest answer's (`latest`), which files a link under the work its item sits on now (a sync may
 * have joined two works) and leaves out one whose item the library no longer reports.
 */
function outcomeOf(first: LibraryLinkBatch, latest: LibraryLinkRow[], workId: string): LibraryLinkBatchItem | undefined {
  const clicked = first.items.find((item) => item.workId === workId)
  if (clicked === undefined || clicked.outcome === 'Skipped') return clicked
  return { ...clicked, links: latest.filter((link) => clicked.links.some((one) => one.linkId === link.linkId)) }
}

/** What came of one work of the click: its links' states, or why it was skipped. */
function Outcome({ item }: { item: LibraryLinkBatchItem | undefined }) {
  const { t } = useTranslation('libraryLinks')
  if (item === undefined) return null
  if (item.outcome === 'Skipped') {
    return (
      <p className="library-source-warning">
        <TriangleAlert className="h-3.5 w-3.5" aria-hidden="true" />
        <span>{t('confident.skipped', { reason: item.reason ?? '' })}</span>
      </p>
    )
  }
  if (item.links.length === 0) return <p className="settings-muted">{t('confident.noLinks')}</p>
  return (
    <>
      {item.links.map((one) => (
        <div key={one.linkId} className="tmdb-link-state">
          <LinkStateBadge state={one.writeState} error={one.writeError} />
          {/* The sentence on screen too, as a phone shows no tooltip. */}
          {one.writeError !== null && (one.writeState === 'NotWritten' || one.writeState === 'Lost') && (
            <p className="library-source-status" data-status="Failed">
              <CircleAlert className="h-3.5 w-3.5" aria-hidden="true" />
              <span>{one.writeError}</span>
            </p>
          )}
        </div>
      ))}
    </>
  )
}

// ── a row's halves ──

/** The library the work's files sit in, under its title; nothing when none is known (a series' files are its episodes'). */
function WorkFacts({ work }: { work: LibraryWork }) {
  const location = work.versions.flatMap((version) => version.files).find((file) => file.locationName !== null)?.locationName
  return location ? <p className="library-work-meta">{location}</p> : null
}

/** The confident suggestion: a small poster beside its title, facts and evidence. */
function CandidateFacts({ candidate }: { candidate: LinkCandidate }) {
  const { t } = useTranslation('libraryLinks')
  return (
    <div className="tmdb-confident-candidate">
      <CatalogPoster url={candidate.posterUrl} title={candidate.title} kind={candidate.catalogKind} />
      <div className="tmdb-link-candidate-body">
        <span className="tmdb-link-candidate-title">
          {candidate.year === null ? candidate.title : t('candidate.titleYear', { title: candidate.title, year: candidate.year })}
        </span>
        <span className="library-work-meta">
          <span>{t(`candidate.kind.${candidate.catalogKind}`)}</span>
          {candidate.originalTitle !== null && candidate.originalTitle !== candidate.title && <span>{candidate.originalTitle}</span>}
          <span>{t('candidate.id', { id: candidate.catalogId })}</span>
          {candidate.imdbId !== null && <span>{candidate.imdbId}</span>}
        </span>
        <LinkEvidenceBadges evidence={candidate.evidence} catalogKind={candidate.catalogKind} />
      </div>
    </div>
  )
}

// ── helpers ──

/** A library's name, or the words for works in no known library. */
function libraryName(location: LibraryLinkLocationCount): string {
  return location.locationName ?? i18n.t('libraryLinks:confident.unknownLibrary')
}

/** "120. Landing Stripling (1962)", the year left out when unknown. */
function workHeading(work: LibraryWork): string {
  const title = work.title ?? work.originalTitle ?? i18n.t('library:work.unknownTitle')
  return work.year === null ? title : i18n.t('libraryLinks:candidate.titleYear', { title, year: work.year })
}
