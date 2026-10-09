import { useId, useState } from 'react'
import { ChevronDown, ChevronRight, TriangleAlert } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { FileChips } from '@/components/library/FileChips'
import { ClaimedQuality } from '@/components/search/ClaimedQuality'
import { Button } from '@/components/ui/button'
import { distinctPathEnds, editionsText } from '@/lib/duplicates'
import { formatFileSize, formatNumber } from '@/lib/format'
import { seasonName } from '@/lib/library-episodes'
import { splitPath } from '@/lib/library-versions'
import {
  canTick,
  copyPath,
  episodeName,
  heldTorrentsOf,
  holdKey,
  seasonsOf,
  versionLabels,
  workNames,
  type ReplacementSeason,
} from '@/lib/replacements'
import type { ReplacementCopy, ReplacementEpisode, ReplacementOptions, ReplacementWork, SearchRelease } from '@/lib/types'
import '@/styles/auth.css' // .auth-hint, .auth-muted — the reasons and the counts
import '@/styles/settings-dialog.css' // .settings-check — a copy's box before its name
import '@/styles/library.css' // .library-source-warning, .library-version-chips, .library-file-folder — the marks, the chips, the folder
import '@/styles/search.css' // .search-details-heading — the list's heading, as the summary's
import '@/styles/downloads.css' // .download-* — the works, seasons, episodes and copies

interface ReplacementWorksProps {
  release: SearchRelease
  /** The server's answer, with works: no release-level reason stands in the way. */
  options: ReplacementOptions
  /** The ticked copies' file ids, every one of them tickable. */
  ticked: ReadonlySet<string>
  /** The works whose title-and-year match the owner confirmed. */
  confirmed: ReadonlySet<string>
  /** Keep both is picked, or a send is on its way: every box shows but none changes. */
  readOnly: boolean
  /** Ticks (`on`) or unticks the copies of `fileIds`. */
  onTick: (fileIds: readonly string[], on: boolean) => void
  onConfirm: (workId: string, on: boolean) => void
}

/**
 * MAM-24 — the old copies of the works a release matched, for the owner to tick the ones its download
 * would replace (ADR 0032): beside the release's claim, each work as a group — a film with every file
 * of every version, a series with the held episodes the release claims, by season — and, under each
 * copy, what stands in its way or what to know before it can move. Nothing comes ticked.
 *
 * Shown whichever of Keep both and Replace is picked, read-only under Keep both, so the owner sees
 * why a copy can't be replaced (an unticked folder, a title-only match) without picking Replace
 * first. A torrent holding copies is said once per work, in full, and each copy it holds carries a
 * short mark whose box reads the full sentence (ADR 0031). The dialog holds the ticks until Send.
 */
export function ReplacementWorks({ release, options, ticked, confirmed, readOnly, onTick, onConfirm }: ReplacementWorksProps) {
  const { t } = useTranslation('downloads')
  const titleId = useId()
  const names = workNames(options.works)

  return (
    <section className="download-replacement" aria-labelledby={titleId}>
      <h3 id={titleId} className="search-details-heading">{t('replace.copiesTitle')}</h3>
      {/* The release's claim beside the copies, the owner's basis for ticking (ADR 0032). */}
      <ClaimedQuality claimed={release.claimed} kind={release.kind} episodes={release.claimedWork.episodes} />
      {options.releaseEditions.length > 0 && (
        <p className="auth-hint">{t('replace.releaseEditions', { editions: editionsText(options.releaseEditions) })}</p>
      )}
      {/* A list, each work in an item: the line between two works can't be a fieldset's own border,
          which its legend would sit across. */}
      <ul className="download-works">
        {options.works.map((work, at) => (
          <li key={work.workId}>
            <WorkCopies
              work={work}
              name={names[at]}
              ticked={ticked}
              confirmed={confirmed.has(work.workId)}
              readOnly={readOnly}
              onTick={onTick}
              onConfirm={(on) => onConfirm(work.workId, on)}
            />
          </li>
        ))}
      </ul>
    </section>
  )
}

// ── one work ──

/** What every copy of a work needs from it: its ticks, and the ids of its torrents' sentences. */
interface CopyContext {
  work: ReplacementWork
  ticked: ReadonlySet<string>
  readOnly: boolean
  onTick: (fileIds: readonly string[], on: boolean) => void
  /** The id of the full sentence about each torrent holding copies of the work, by holdKey. */
  heldIds: ReadonlyMap<string, string>
}

/**
 * A work as a group named by it: why it can't be replaced, or the confirmation its title-and-year match
 * needs, then the torrents holding its copies, then its copies — by season for a series.
 */
function WorkCopies({ work, name, ticked, confirmed, readOnly, onTick, onConfirm }: {
  work: ReplacementWork
  name: string
  ticked: ReadonlySet<string>
  confirmed: boolean
  readOnly: boolean
  onTick: (fileIds: readonly string[], on: boolean) => void
  onConfirm: (on: boolean) => void
}) {
  const { t } = useTranslation(['downloads', 'library', 'common'])
  const id = useId()
  const held = heldTorrentsOf(work)
  const heldIds = new Map(held.map((torrent, at) => [holdKey(torrent), `${id}-held-${at}`]))
  const context: CopyContext = { work, ticked, readOnly, onTick, heldIds }
  const seasons = seasonsOf(work)
  const schemes = work.conflictingSchemes.map((scheme) => t(`library:schemes.${scheme}`)).join(t('common:separator'))

  return (
    <fieldset className="download-work">
      <legend className="download-work-name">{name}</legend>
      {work.unavailable !== null && (
        <p className="auth-hint">{t(`replace.workUnavailable.${work.unavailable}`, { schemes })}</p>
      )}
      {work.unavailable === null && work.needsConfirmation && (
        <div className="download-confirm">
          <label className="settings-check">
            <input
              type="checkbox"
              checked={confirmed}
              disabled={readOnly}
              aria-describedby={`${id}-confirm`}
              onChange={(event) => onConfirm(event.target.checked)}
            />
            <span className="download-copy-name">{work.confirmationText}</span>
          </label>
          <p id={`${id}-confirm`} className="auth-hint">{t('replace.confirmHint')}</p>
        </div>
      )}
      {held.map((torrent, at) => (
        <p key={holdKey(torrent)} id={`${id}-held-${at}`} className="library-source-warning">
          <TriangleAlert className="h-3.5 w-3.5" aria-hidden="true" />
          <span>
            {t('replace.heldBy', {
              count: torrent.count,
              value: formatNumber(torrent.count),
              torrent: torrent.torrentName,
              client: torrent.clientName,
            })}
          </span>
        </p>
      ))}
      {work.copies.length > 0 && <CopyList copies={work.copies} context={context} />}
      {seasons.length > 0 && (
        <ul className="download-seasons">
          {seasons.map((season) => (
            <li key={season.number ?? 'none'}>
              <SeasonCopies
                season={season}
                // One season is all there is to look at, so it shows at once; several open one by one.
                collapsible={seasons.length > 1}
                context={context}
              />
            </li>
          ))}
        </ul>
      )}
    </fieldset>
  )
}

// ── a season ──

/**
 * A season of a series as a group named by it: how many of its copies are ticked, a button that ticks
 * every one that can be replaced (an explicit click, never a pre-tick), and its episodes. With more
 * than one season, each opens on its name, as a release source's sections do in the destination dialog.
 */
function SeasonCopies({ season, collapsible, context }: { season: ReplacementSeason; collapsible: boolean; context: CopyContext }) {
  const { t } = useTranslation('downloads')
  const panelId = useId()
  const [open, setOpen] = useState(!collapsible)
  const name = seasonName(season.number)
  const tickable = [...new Set(season.episodes.flatMap((episode) => episode.copies)
    .filter((copy) => canTick(context.work, copy))
    .map((copy) => copy.file.id))]
  const tickedCount = tickable.filter((fileId) => context.ticked.has(fileId)).length
  const all = tickable.length > 0 && tickedCount === tickable.length
  const Chevron = open ? ChevronDown : ChevronRight

  return (
    <fieldset className="download-season">
      <legend className="download-season-name">
        {collapsible ? (
          <button
            type="button"
            className="download-season-toggle"
            aria-expanded={open}
            aria-controls={open ? panelId : undefined}
            onClick={() => setOpen(!open)}
          >
            <Chevron className="h-4 w-4" aria-hidden="true" />
            <span>{name}</span>
          </button>
        ) : name}
      </legend>
      {tickable.length > 0 && (
        <div className="download-season-head">
          <span className="auth-muted">
            {t('replace.seasonTicked', { ticked: formatNumber(tickedCount), total: formatNumber(tickable.length) })}
          </span>
          <Button
            type="button"
            variant="outline"
            size="sm"
            disabled={context.readOnly}
            onClick={() => context.onTick(tickable, !all)}
          >
            {all ? t('replace.untickSeason') : t('replace.tickSeason', { value: formatNumber(tickable.length) })}
          </Button>
        </div>
      )}
      {open && (
        <div id={panelId} className="download-season-episodes">
          {season.episodes.map((episode) => <EpisodeCopies key={episode.workId} episode={episode} context={context} />)}
        </div>
      )}
    </fieldset>
  )
}

/** An episode as a group named by its place and title, with its versions' copies. */
function EpisodeCopies({ episode, context }: { episode: ReplacementEpisode; context: CopyContext }) {
  const labelId = useId()

  return (
    <div className="download-episode" role="group" aria-labelledby={labelId}>
      <p id={labelId} className="download-episode-name">{episodeName(episode)}</p>
      <CopyList copies={episode.copies} context={context} />
    </div>
  )
}

// ── the copies ──

/**
 * The copies of a film or of an episode, each named by the end of its path that tells it from the
 * others (two versions' files of one name in two folders read apart), its version by the Library's
 * label for it.
 */
function CopyList({ copies, context }: { copies: readonly ReplacementCopy[]; context: CopyContext }) {
  const names = distinctPathEnds(copies.map(copyPath))
  const versions = versionLabels(copies)

  return (
    <ul className="download-copies">
      {copies.map((copy, at) => (
        <li key={`${copy.versionId}-${copy.file.id}`}>
          <CopyCheck copy={copy} name={names[at]} version={versions.get(copy.versionId) ?? ''} context={context} />
        </li>
      ))}
    </ul>
  )
}

/**
 * One copy's box, named by its file: its folder, its version, edition words and size, its chips as
 * the Library shows them, then its marks — the torrent holding it, another name that keeps its space
 * taken — and why it can't be ticked. The box reads the marks and the reason (`aria-describedby`): the
 * held mark by the full sentence about its torrent.
 */
function CopyCheck({ copy, name, version, context }: { copy: ReplacementCopy; name: string; version: string; context: CopyContext }) {
  const { t } = useTranslation(['downloads', 'library', 'common'])
  const id = useId()
  const tickable = canTick(context.work, copy)
  const held = copy.holds.filter((hold) => !hold.secondName)
  const secondNames = [...new Set(copy.holds.filter((hold) => hold.secondName).map((hold) => hold.torrentName))]
  const otherNames = copy.otherNames || secondNames.length > 0
  // Why it can't be ticked, in words: the path mapper's own sentence where it has one (the owner
  // reads it, as FileLines shows it), the Library's words where they already say it.
  const reason = copy.unavailable === null
    ? null
    : copy.unavailable === 'NoAppPath'
      ? copy.file.pathProblem ?? t('replace.copyUnavailable.NoAppPath')
      : copy.unavailable === 'NotInspected'
        ? t('library:media.notInspected')
        : t(`replace.copyUnavailable.${copy.unavailable}`)
  const { folder } = splitPath(copyPath(copy))
  const facts = [
    version,
    copy.editions.length === 0 ? null : editionsText(copy.editions),
    copy.file.sizeBytes === null ? t('library:file.sizeUnknown') : formatFileSize(copy.file.sizeBytes),
  ].filter((fact) => fact !== null && fact !== '').join(t('common:separator'))
  const describedBy = [
    ...held.map((hold) => context.heldIds.get(holdKey(hold))),
    otherNames ? `${id}-names` : undefined,
    reason === null ? undefined : `${id}-reason`,
  ].filter((part) => part !== undefined)

  return (
    <div className="download-copy">
      <label className="settings-check">
        <input
          type="checkbox"
          checked={context.ticked.has(copy.file.id)}
          disabled={context.readOnly || !tickable}
          aria-labelledby={`${id}-name`}
          aria-describedby={describedBy.length === 0 ? undefined : [...new Set(describedBy)].join(' ')}
          onChange={(event) => context.onTick([copy.file.id], event.target.checked)}
        />
        <span className="download-copy-body">
          <span id={`${id}-name`} className="download-copy-name">{name}</span>
          {folder !== null && <span className="library-file-folder">{folder}</span>}
          <span className="download-copy-facts">{facts}</span>
          <span className="library-version-chips"><FileChips file={copy.file} /></span>
        </span>
      </label>
      {held.map((hold) => (
        <p key={holdKey(hold)} className="library-source-warning download-copy-mark">
          <TriangleAlert className="h-3.5 w-3.5" aria-hidden="true" />
          <span>{t('replace.heldMark', { torrent: hold.torrentName })}</span>
        </p>
      ))}
      {otherNames && (
        <p id={`${id}-names`} className="library-source-warning download-copy-mark">
          <TriangleAlert className="h-3.5 w-3.5" aria-hidden="true" />
          <span>
            {[t('replace.otherNames'), ...secondNames.map((torrent) => t('replace.otherNamesTorrent', { torrent }))].join(' ')}
          </span>
        </p>
      )}
      {reason !== null && <p id={`${id}-reason`} className="auth-hint download-copy-mark">{reason}</p>}
    </div>
  )
}
