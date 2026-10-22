import { useId } from 'react'
import { ArrowUp, Download, Info, Users } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { MatchBadges } from '@/components/search/MatchBadges'
import { ReleaseClaims } from '@/components/search/ReleaseClaims'
import { ReleaseSourceLink } from '@/components/search/ReleaseSourceLink'
import { Button } from '@/components/ui/button'
import { formatDate, formatFileSize, formatNumber } from '@/lib/format'
import { releaseName } from '@/lib/release-name'
import type { LibraryMatch, SearchRelease } from '@/lib/types'
import '@/styles/search.css' // .search-release* — the row, its title, its corner and its foot

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
 * MAM-12 — one release a source listed for the search: its title, what the title claims about its
 * quality (ReleaseClaims), and — when it matches a copy in the library — a badge for the strongest
 * match, naming that copy, with any conflict. MAM-32 — a TV release says so in its claims, with the
 * seasons or episodes it claims, and its match how many of those episodes the library holds. Headed by
 * the name it goes by (releaseName): the listing's own description when it gives one — Toloka's whole
 * title, where the title can be punctuation alone (Vasyl's decision of 2026-10-22) —, with the title no
 * more beside it; the Details dialog shows both.
 *
 * What the listing says sits in the top-right corner (Vasyl's layout of 2026-10-14): the seeders and
 * the peers, each saying on hover what it counts, the size under them and the publish date under
 * that; a phone puts them on one line under the text. The source that listed it is at the bottom left,
 * named as the source.
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
      <div className="search-release-top">
        <div className="search-release-body">
          <h3 id={titleId} className="search-release-title">{releaseName(release)}</h3>
          <ReleaseClaims claimed={release.claimed} kind={release.kind} episodes={release.claimedWork.episodes} />
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
        {/* The swarm, the size and the date in the top-right corner; each count says on hover what it
            counts, the words a phone (no hover) reads in the Details dialog. */}
        <div className="search-release-stats">
          <p className="search-release-swarm">
            <span className="search-release-seeders" title={t('release.seedersHint')}>
              <ArrowUp className="h-3.5 w-3.5" aria-hidden="true" />
              <span aria-hidden="true">{release.seeders === null ? t('release.countUnknown') : formatNumber(release.seeders)}</span>
              <span className="sr-only">
                {release.seeders === null
                  ? t('release.seedersUnknown')
                  : t('release.seeders', { count: release.seeders, value: formatNumber(release.seeders) })}
              </span>
            </span>
            <span className="search-release-peers" title={t('release.peersHint')}>
              <Users className="h-3.5 w-3.5" aria-hidden="true" />
              <span aria-hidden="true">{release.peers === null ? t('release.countUnknown') : formatNumber(release.peers)}</span>
              <span className="sr-only">
                {release.peers === null
                  ? t('release.peersUnknown')
                  : t('release.peers', { count: release.peers, value: formatNumber(release.peers) })}
              </span>
            </span>
          </p>
          <p className="search-release-size">
            {release.sizeBytes === null ? t('release.sizeUnknown') : formatFileSize(release.sizeBytes)}
          </p>
          <p className="search-release-date">
            {release.publishedUtc === null
              ? t('release.publishedUnknown')
              : t('release.published', { date: formatDate(release.publishedUtc) })}
          </p>
        </div>
      </div>
      {/* The source that listed it at the bottom left, named as one, the actions at the right. */}
      <div className="search-release-foot">
        <p className="search-release-source">{t('release.source', { name: release.sourceName })}</p>
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
      </div>
    </article>
  )
}
