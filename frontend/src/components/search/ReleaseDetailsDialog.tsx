import { useId } from 'react'
import { useTranslation } from 'react-i18next'
import { VersionItem } from '@/components/library/VersionItem'
import { ClaimedQuality } from '@/components/search/ClaimedQuality'
import { MatchBadges } from '@/components/search/MatchBadges'
import { ReleaseSourceLink } from '@/components/search/ReleaseSourceLink'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { formatDateTime, formatFileSize, formatNumber } from '@/lib/format'
import { compareVersions } from '@/lib/library-versions'
import type { LibraryMatch, LibraryWork, ReleaseLibraryMatch, SearchRelease } from '@/lib/types'
import '@/styles/settings-list.css' // .settings-muted — the "nothing here" lines
import '@/styles/settings-dialog.css' // .settings-dialog* — the one scrolling body above the buttons
import '@/styles/library.css' // .library-work-meta, .library-versions — a work's line and its versions
import '@/styles/search.css' // .search-details-* — the two columns, the facts and the works

interface ReleaseDetailsDialogProps {
  release: SearchRelease
  /**
   * Every work the search lists, the ones listed by words too (MAM-12): the works the release's own
   * matches point at.
   */
  library: LibraryMatch[]
  onClose: () => void
}

/**
 * MAM-12 — one release in full, beside the library copies it may be. On the left, what the listing
 * says: its facts, what its title claims about its quality, what the title was read as (its titles
 * and year — the part matched against the library) and the ids the listing gives. On the right,
 * every library work the release matches, strongest first with why, and that work's versions with
 * the facts the app verified — the Library page's own blocks — so a claim and a fact can be
 * compared before anything is downloaded. Two columns on a wide screen, stacked on a phone.
 *
 * The parent mounts it only while it is open. Its body is the one scroller; Close sits last.
 */
export function ReleaseDetailsDialog({ release, library, onClose }: ReleaseDetailsDialogProps) {
  const { t } = useTranslation(['search', 'library', 'common'])
  const listingId = useId()
  const libraryId = useId()
  const { claimedWork } = release
  // A match whose work the search no longer lists (the library was synced since) has nothing to show.
  const matches = release.libraryMatches.flatMap((match) => {
    const work = library.find((candidate) => candidate.work.id === match.workId)?.work
    return work ? [{ match, work }] : []
  })
  const unknown = t('details.unknown')

  return (
    <Dialog open onOpenChange={(open) => { if (!open) onClose() }}>
      <DialogContent className="settings-dialog search-details-dialog">
        <DialogHeader>
          <DialogTitle className="search-details-title">{release.title}</DialogTitle>
          <DialogDescription>{t('details.description', { source: release.sourceName })}</DialogDescription>
        </DialogHeader>
        <div className="settings-dialog-body search-details-body">
          <section className="search-details-column" aria-labelledby={listingId}>
            <h3 id={listingId} className="search-details-heading">{t('details.listing')}</h3>
            <dl className="search-details-facts">
              <dt>{t('details.source')}</dt>
              <dd>{release.sourceName}</dd>
              <dt>{t('details.size')}</dt>
              <dd>{release.sizeBytes === null ? unknown : formatFileSize(release.sizeBytes)}</dd>
              <dt>{t('details.seeders')}</dt>
              <dd>{release.seeders === null ? unknown : formatNumber(release.seeders)}</dd>
              <dt>{t('details.peers')}</dt>
              <dd>{release.peers === null ? unknown : formatNumber(release.peers)}</dd>
              <dt>{t('details.published')}</dt>
              <dd>{release.publishedUtc === null ? unknown : formatDateTime(release.publishedUtc)}</dd>
              <dt>{t('details.payload')}</dt>
              {/* MAM-10 — a gateway's link can turn out to be a magnet, so the listing doesn't always say. */}
              <dd>{release.payloadKind === null ? t('details.payloadUnknown') : t(`details.payloadKind.${release.payloadKind}`)}</dd>
            </dl>
            <ClaimedQuality claimed={release.claimed} />

            <h3 className="search-details-heading">{t('details.readAs')}</h3>
            {claimedWork.titles.length === 0 ? (
              <p className="settings-muted">{t('details.noClaimedWork')}</p>
            ) : (
              <dl className="search-details-facts">
                <dt>{t('details.titles')}</dt>
                <dd>{claimedWork.titles.join(t('common:separator'))}</dd>
                <dt>{t('details.year')}</dt>
                <dd>{claimedWork.year ?? unknown}</dd>
              </dl>
            )}

            <h3 className="search-details-heading">{t('details.identifiers')}</h3>
            {release.identifiers.length === 0 ? (
              <p className="settings-muted">{t('details.noIdentifiers')}</p>
            ) : (
              <ul className="search-badges" aria-label={t('details.identifiers')}>
                {release.identifiers.map((identifier) => (
                  <li key={`${identifier.scheme}:${identifier.value}`}>
                    <span className="ui-badge ui-badge-outline">
                      {t('library:work.identifier', { scheme: t(`library:schemes.${identifier.scheme}`), value: identifier.value })}
                    </span>
                  </li>
                ))}
              </ul>
            )}
          </section>

          <section className="search-details-column" aria-labelledby={libraryId}>
            <h3 id={libraryId} className="search-details-heading">{t('details.library')}</h3>
            {matches.length === 0
              ? <p className="settings-muted">{t('details.noLibrary')}</p>
              : matches.map(({ match, work }) => <MatchedWork key={work.id} match={match} work={work} />)}
          </section>
        </div>
        <DialogFooter>
          <ReleaseSourceLink url={release.sourcePageUrl} size="default" />
          <Button type="button" variant="outline" onClick={onClose}>{t('common:close')}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

// ── one library work the release matches ──

/** The work's title and year, why the release matched it, and its versions as the Library page shows them. */
function MatchedWork({ match, work }: { match: ReleaseLibraryMatch; work: LibraryWork }) {
  const { t } = useTranslation('library')
  const titleId = useId()
  const versions = work.versions.toSorted(compareVersions)
  const showOriginalTitle = work.originalTitle !== null && work.originalTitle !== work.title

  return (
    <section className="search-details-work" aria-labelledby={titleId}>
      <h4 id={titleId} className="search-details-work-title">{work.title ?? t('work.unknownTitle')}</h4>
      <p className="library-work-meta">
        {showOriginalTitle && <span>{t('work.originalTitle', { title: work.originalTitle })}</span>}
        <span>{work.year ?? t('work.unknownYear')}</span>
      </p>
      <MatchBadges evidence={match.evidence} conflictingSchemes={match.conflictingSchemes} />
      {versions.length === 0 ? (
        <p className="settings-muted">{t('work.noVersions')}</p>
      ) : (
        <ul className="library-versions" aria-label={t('work.versions')}>
          {versions.map((version) => <VersionItem key={version.id} version={version} />)}
        </ul>
      )}
    </section>
  )
}
