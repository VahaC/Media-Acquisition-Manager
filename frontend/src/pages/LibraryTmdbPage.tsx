import { useId } from 'react'
import { ArrowLeft } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Link, useSearchParams } from 'react-router-dom'
import { TmdbConfidentList } from '@/components/library/TmdbConfidentList'
import { TmdbLinkRows } from '@/components/library/TmdbLinkRows'
import { TmdbLinkTabs, type TmdbLinkTab } from '@/components/library/TmdbLinkTabs'
import { TmdbReviewQueue } from '@/components/library/TmdbReviewQueue'
import { TmdbSuggestionRunPanel } from '@/components/library/TmdbSuggestionRunPanel'
import { Button } from '@/components/ui/button'
import { useAuthStore } from '@/lib/auth-store'
import '@/styles/settings-list.css' // .settings-* — the header and the member's sentence
import '@/styles/recommendations.css' // .recommendation-attribution — TMDb's notice
import '@/styles/library-links.css' // .tmdb-page-panel — the tab's column

/** The tabs, in their order; the first is the address's default. */
const TABS: readonly TmdbLinkTab[] = ['Review', 'Confident', 'Conflicts', 'Unattached']

/**
 * Where "Library" goes back to: the Library with its "Missing an id" filter on, which the page reads
 * once from the address (as its `?search=`), so going back keeps the list the owner came from.
 */
const LIBRARY_MISSING = '/library?identifiers=Missing'

/**
 * MAM-34 — the TMDb page (`/library/tmdb`, reached from the Library's "Missing an id" filter): the
 * works without a TMDb id linked to their film or show by hand, the owner's (decision 5). Above the
 * tabs, how the suggestions were last prepared, with Prepare again. The tabs (R24): **Review** — one
 * work at a time with the TMDb dialog's content inline, and keys for every step; **Confident** — every
 * confident suggestion, linked a page at a time in one click; **Conflicts** — the links Jellyfin holds
 * another TMDb id against; **Unattached** — the links whose item the library no longer reports.
 *
 * The tab is in the address (`?tab=Confident`, left out for Review), so F5 and Back keep it. A member
 * reads why the page isn't theirs, and nothing is asked for: every route behind it is the owner's.
 * The header's Library link stays highlighted here (its NavLink matches `/library/*`).
 */
export function LibraryTmdbPage() {
  const { t } = useTranslation(['libraryLinks', 'recommendations'])
  const isOwner = useAuthStore((state) => state.user?.isOwner === true)
  const [searchParams, setSearchParams] = useSearchParams()
  const asked = searchParams.get('tab')
  const tab = TABS.find((candidate) => candidate === asked) ?? TABS[0]
  const idPrefix = useId()
  const panelId = `${idPrefix}-panel`
  const tabId = (of: TmdbLinkTab) => `${idPrefix}-tab-${of}`

  const selectTab = (next: TmdbLinkTab) => {
    if (next !== tab) setSearchParams(next === TABS[0] ? {} : { tab: next }, { replace: true })
  }

  return (
    <div className="settings-page">
      <div>
        <Button asChild variant="outline" size="sm">
          <Link to={LIBRARY_MISSING}>
            <ArrowLeft className="h-3.5 w-3.5" aria-hidden="true" />
            {t('page.back')}
          </Link>
        </Button>
      </div>

      <div className="settings-header">
        <div className="settings-intro">
          <h1 className="settings-title">{t('page.title')}</h1>
          <p className="settings-lead">{t('page.lead')}</p>
        </div>
      </div>

      {!isOwner ? (
        <div className="settings-empty">
          <p>{t('card.notOwner')}</p>
        </div>
      ) : (
        <>
          <TmdbSuggestionRunPanel />

          <TmdbLinkTabs tabs={TABS} current={tab} panelId={panelId} tabId={tabId} onSelect={selectTab} />

          <div id={panelId} role="tabpanel" aria-labelledby={tabId(tab)} className="tmdb-page-panel">
            {/* Each tab mounts only while it is on screen: the Review tab's keys are taken only then,
                and a list's page starts at 1 when its tab comes back. */}
            {tab === 'Review' ? (
              <TmdbReviewQueue />
            ) : tab === 'Confident' ? (
              <TmdbConfidentList />
            ) : (
              <TmdbLinkRows key={tab} filter={tab === 'Conflicts' ? 'Conflict' : 'Unattached'} />
            )}
          </div>

          {/* The Review tab's editor carries TMDb's notice, as the dialog does; the lists need it here. */}
          {tab !== 'Review' && <p className="recommendation-attribution">{t('recommendations:attribution')}</p>}
        </>
      )}
    </div>
  )
}
