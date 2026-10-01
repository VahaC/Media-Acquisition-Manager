import { ArrowRight, CircleCheck, TriangleAlert } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import type { DestinationCheck } from '@/lib/types'
import '@/styles/library.css' // .library-badge-warning — the app's warning badge, amber on its border and icon only
import '@/styles/destinations.css' // .destinations-check*, .destinations-paths — the folders and the lines under them

/**
 * MAM-11 — where a destination saves, as the API checked it just now (ADR 0003): the folder as the
 * download client sees it, the same folder in this app, and the media libraries it lands in. Any
 * other status is a warning badge with the API's sentence under it, which says why — automatic
 * replacement stays off for such a destination. A folder that can't be determined is not shown at
 * all rather than guessed.
 *
 * A row of the Destinations page and the category picked in its dialog show it alike.
 */
export function DestinationSavesTo({ check }: { check: DestinationCheck }) {
  const { t } = useTranslation('destinations')
  // The same folder twice says nothing: a client without path mappings sees what the app sees.
  const showsAppPath = check.appPath !== null && check.appPath !== check.servicePath

  return (
    <div className="destinations-check" data-status={check.status}>
      {check.servicePath !== null && (
        <span className="destinations-paths">
          <span>{check.servicePath}</span>
          {showsAppPath && (
            <>
              <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
              {/* The arrow is drawn; a screen reader hears what it means. */}
              <span className="sr-only">{t('check.mapsTo')}</span>
              <span>{check.appPath}</span>
            </>
          )}
        </span>
      )}
      {check.status === 'InLibrary' ? (
        <p className="destinations-check-line">
          <CircleCheck className="h-3.5 w-3.5" aria-hidden="true" />
          <span>
            {check.libraryNames.length > 0
              ? t('check.inLibrary', { count: check.libraryNames.length, names: check.libraryNames })
              : t('status.InLibrary')}
          </span>
        </p>
      ) : (
        <>
          <span className="ui-badge library-badge-warning">
            <TriangleAlert className="h-3 w-3" aria-hidden="true" />
            {t(`status.${check.status}`)}
          </span>
          {/* The API's own sentence: it names the folder or the client, which a label can't. */}
          {check.problem && <p className="destinations-check-problem">{check.problem}</p>}
        </>
      )}
    </div>
  )
}
