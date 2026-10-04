import { useId } from 'react'
import { Download, Info } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { ClaimedQuality } from '@/components/search/ClaimedQuality'
import { MatchBadges } from '@/components/search/MatchBadges'
import { ReleaseSourceLink } from '@/components/search/ReleaseSourceLink'
import { Button } from '@/components/ui/button'
import { formatDate, formatFileSize, formatNumber } from '@/lib/format'
import type { LibraryMatch, SearchRelease } from '@/lib/types'
import '@/styles/library.css' // .library-file-facts — the line of listed facts
import '@/styles/search.css' // .search-release* — the row, its title and its actions

interface ReleaseRowProps {
  release: SearchRelease
  /**
   * Every work the search lists, the ones listed by words too (MAM-12), which name the work a release
   * matches — on the release's own evidence, never by words.
   */
  library: LibraryMatch[]
  onDetails: () => void
  /** MAM-13 — the parent opens the dialog that sends the release to a download client. */
  onDownload: () => void
}

/**
 * MAM-12 — one release a source listed for the search: its title, what the listing says (source,
 * size, seeders and peers, when it was published), what the title claims about its quality, and —
 * when it matches a copy in the library — a badge for the strongest match, naming that copy, with
 * any conflict. MAM-32 — a TV release says so in its chips, with the seasons or episodes it claims,
 * and its match how many of those episodes the library holds.
 *
 * Its actions: Details (the parent opens the dialog), Open source, and Download (MAM-13: the parent
 * opens the dialog that sends the release to a download client). On a phone the actions collapse to
 * their icons and keep their names in `title`.
 */
export function ReleaseRow({ release, library, onDetails, onDownload }: ReleaseRowProps) {
  const { t } = useTranslation(['search', 'library'])
  const titleId = useId()
  const strongest = release.libraryMatches.at(0)
  const work = strongest && library.find((match) => match.work.id === strongest.workId)?.work
  // Named on the badge itself, `Чужі (1986)`, not on its hover: a phone shows no tooltips, and two
  // copies of one title (Дюна of 1984 and of 2021) may differ by nothing but the year.
  const workName = (() => {
    if (!work) return undefined
    const title = work.title ?? t('library:work.unknownTitle')
    return work.year === null ? t('match.workYearUnknown', { title }) : t('match.work', { title, year: work.year })
  })()

  return (
    <article className="ui-card search-release" aria-labelledby={titleId}>
      <div className="search-release-body">
        <h3 id={titleId} className="search-release-title">{release.title}</h3>
        <p className="library-file-facts">
          <span>{release.sourceName}</span>
          <span>{release.sizeBytes === null ? t('release.sizeUnknown') : formatFileSize(release.sizeBytes)}</span>
          <span>
            {release.seeders === null
              ? t('release.seedersUnknown')
              : t('release.seeders', { count: release.seeders, value: formatNumber(release.seeders) })}
          </span>
          <span>
            {release.peers === null
              ? t('release.peersUnknown')
              : t('release.peers', { count: release.peers, value: formatNumber(release.peers) })}
          </span>
          <span>
            {release.publishedUtc === null
              ? t('release.publishedUnknown')
              : t('release.published', { date: formatDate(release.publishedUtc) })}
          </span>
        </p>
        <ClaimedQuality claimed={release.claimed} kind={release.kind} episodes={release.claimedWork.episodes} />
        {/* The strongest match only; Details lists every one. A work the search no longer lists
            (the library was synced since) leaves the badge out rather than naming nothing. */}
        {strongest && workName !== undefined && (
          <MatchBadges
            evidence={strongest.evidence}
            conflictingSchemes={strongest.conflictingSchemes}
            inLibraryOf={workName}
            heldEpisodeCount={strongest.heldEpisodeCount}
          />
        )}
      </div>
      <div className="search-release-actions" role="group" aria-label={t('release.actions')}>
        <Button type="button" variant="outline" size="sm" title={t('release.details')} onClick={onDetails}>
          <Info className="h-3.5 w-3.5" aria-hidden="true" />
          <span className="label-text">{t('release.details')}</span>
        </Button>
        <ReleaseSourceLink url={release.sourcePageUrl} />
        <Button type="button" variant="outline" size="sm" title={t('release.download')} onClick={onDownload}>
          <Download className="h-3.5 w-3.5" aria-hidden="true" />
          <span className="label-text">{t('release.download')}</span>
        </Button>
      </div>
    </article>
  )
}
