import { useId, useState, type ReactNode } from 'react'
import { CircleCheck, EyeOff, Rows3, Table2, TriangleAlert, Undo2 } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { DuplicateFileFacts } from '@/components/duplicates/DuplicateFileFacts'
import { DuplicateHash } from '@/components/duplicates/DuplicateHash'
import { DuplicateHolders } from '@/components/duplicates/DuplicateHolders'
import { VersionRows } from '@/components/library/VersionRows'
import { VersionTable } from '@/components/library/VersionTable'
import { ActionButton } from '@/components/shared/ActionButton'
import { FieldErrorLine } from '@/components/shared/FieldErrorLine'
import { SegmentedControl } from '@/components/shared/SegmentedControl'
import { editionsText, fileVersions, groupHeading, identityText, isGroupGone, type DuplicateLeaving } from '@/lib/duplicates'
import { formatDateTime, formatDuration, formatFileSize, formatNumber } from '@/lib/format'
import { splitPath } from '@/lib/library-versions'
import { useDecideDuplicate, useUndoDuplicateDecision } from '@/lib/queries'
import type {
  DuplicateDistinction,
  DuplicateIdentifier,
  DuplicateMatchDecision,
  LibraryDuplicateFile,
  LibraryDuplicateGroup,
  ShownVersion,
} from '@/lib/types'
import { useNarrowScreen } from '@/lib/use-narrow-screen'
import { useVersionsView } from '@/lib/use-versions-view'
import { getApiErrorMessage } from '@/lib/utils'
import '@/styles/library.css' // .library-work-meta, .library-badge-warning — the facts under the heading, the warning badges
import '@/styles/duplicates.css' // .duplicates-* — the card, its head, badges, view switch, evidence and actions

interface DuplicateGroupCardProps {
  group: LibraryDuplicateGroup
  /** Whether the installation owner is signed in: only the owner confirms or dismisses a possible match. */
  isOwner: boolean
  /**
   * A decision of its own took it off the tab on screen — Dismiss, or Undo of a dismissal on Dismissed: the page
   * puts its short row in the card's place, with the way back (DuplicateLeftRow).
   */
  onLeft: (change: DuplicateLeaving) => void
}

const INSTANT_FORMAT: Intl.DateTimeFormatOptions = { dateStyle: 'medium', timeStyle: 'short' }

/**
 * The most files the table compares, as LibraryWorkCard's MAX_TABLE_VERSIONS: past six a column is a few
 * characters wide, and the rows read any number.
 */
const MAX_TABLE_FILES = 6

/**
 * MAM-21 — one group of the Duplicates page. Headed by its first work (an episode by its series, with
 * where the episode sits and its own title under it), with its class, "Needs review" while a possible
 * match or a copy with a distinction is undecided, the owner's decision, and what tells its files apart
 * (ids that disagree, editions, dynamic ranges, running times far apart). Then the evidence its class
 * gives — the one file and its names, the SHA-256, the shared ids, or the shared titles and whether the
 * running times agree — the room its files take, the room removing the extra copies would free only where
 * the server gives it (or why it can't), and the decision with when it was made.
 *
 * Its files are the Library's version rows or, for two to six, its comparison table (the same switch and
 * the same choice as the Library's cards, rows always on a phone), each file named by the end of its path
 * that tells it from the others (fileVersions) — two cuts of a film by their names, a hardlink's names
 * or two items' copies by their folders; its open row has its whole path and its own facts
 * (DuplicateFileFacts); the table has them as rows.
 *
 * A possible match has Confirm and Dismiss, or Undo once decided — the owner's, disabled with the reason
 * for anyone else. Nothing here removes, moves or renames a file, and a confirmed match authorises nothing.
 */
export function DuplicateGroupCard({ group, isOwner, onLeft }: DuplicateGroupCardProps) {
  const { t } = useTranslation(['duplicates', 'library', 'common'])
  const titleId = useId()
  const [view, setView] = useVersionsView()
  // On a phone the files are always rows, as on the Library's cards: a table of several leaves a few
  // characters a column at 375px. duplicates.css hides the switch at the same width.
  const narrow = useNarrowScreen()
  const decide = useDecideDuplicate()
  const undo = useUndoDuplicateDecision()
  const [error, setError] = useState<string | null>(null)
  const heading = groupHeading(group)
  const versions = fileVersions(group)
  const filesById = new Map(group.files.map((file) => [file.file.id, file]))
  const comparable = versions.length > 1 && versions.length <= MAX_TABLE_FILES
  const { decision, space } = group
  const reason = isOwner ? undefined : t('actions.notOwner')
  const busy = decide.isPending || undo.isPending

  const spaceText = [
    // Nothing measured yet says only that: "0 files · 0 B" would read as empty files.
    ...(space.fileCount === 0
      ? []
      : [t('space.files', { count: space.fileCount, value: formatNumber(space.fileCount), size: formatFileSize(space.sizeBytes) })]),
    // Named, never left out: a size without them would read as the whole group's.
    ...(space.unmeasuredCount > 0
      ? [t('space.unmeasured', { count: space.unmeasuredCount, value: formatNumber(space.unmeasuredCount) })]
      : []),
  ].join(t('common:separator'))

  // Only where the server gives one: copies of a work and possible matches are never redundant, and a
  // byte-identical group whose room can't be told says why rather than showing a number.
  const recoverable = group.recoverable === null
    ? null
    : group.recoverable.gap !== null
      ? t(`recoverable.gap.${group.recoverable.gap}`)
      : group.recoverable.sizeBytes === null
        ? null
        : group.class === 'SameFile' && group.recoverable.sizeBytes === 0
          ? t('recoverable.nothing')
          : t('recoverable.about', { size: formatFileSize(group.recoverable.sizeBytes) })

  // Who decided is the owner's to read (MAM-8's rule for who did what); a member reads when.
  const decisionLine = decision === null
    ? null
    : decision.decidedByUserName === null
      ? t(`decision.${decision.decision}On`, { date: formatDateTime(decision.decidedUtc, INSTANT_FORMAT) })
      : t(`decision.${decision.decision}By`, { name: decision.decidedByUserName, date: formatDateTime(decision.decidedUtc, INSTANT_FORMAT) })

  const distinctionText = (distinction: DuplicateDistinction) => {
    if (distinction.kind !== 'IdentifierConflict') return t(`distinction.${distinction.kind}`)
    if (distinction.scheme === null) return t('distinction.IdentifierConflictUnnamed')
    const scheme = t(`library:schemes.${distinction.scheme}`)
    return distinction.ofSeries
      ? t('distinction.IdentifierConflictOfSeries', { scheme })
      : t('distinction.IdentifierConflict', { scheme })
  }

  /** A table row's cell for the file a column shows. */
  const ofFile = (value: (file: LibraryDuplicateFile) => ReactNode) => (version: ShownVersion) => {
    const file = filesById.get(version.id)
    return file === undefined ? null : value(file)
  }
  // The table's File row names each file; its folder comes next, as two copies can share a name.
  const tableRows = [
    {
      label: t('facts.folder'),
      value: ofFile(({ file }) => <span className="duplicates-path">{splitPath(file.servicePath).folder ?? t('facts.noFolder')}</span>),
    },
    { label: t('facts.identity'), value: ofFile(identityText) },
    { label: t('facts.sha256'), value: ofFile((file) => <DuplicateHash value={file.sha256} />) },
    { label: t('facts.editions'), value: ofFile((file) => editionsText(file.editions)) },
    { label: t('facts.heldBy'), value: ofFile((file) => <DuplicateHolders works={file.works} />) },
  ]

  /** A bare 404 is a group whose members changed meanwhile: the list is asked for again (useDecideDuplicate). */
  const failure = (e: unknown, fallback: string) => (isGroupGone(e) ? t('actions.changed') : getApiErrorMessage(e, fallback))

  const choose = async (next: DuplicateMatchDecision) => {
    setError(null)
    // A dismissed match leaves whichever tab shows it: only Dismissed lists dismissed ones, and they offer Undo alone.
    const leavesList = next === 'Dismissed'
    try {
      await decide.mutateAsync({ key: group.key, decision: next, leavesList })
      if (leavesList) onLeft('Dismissed')
    } catch (e: unknown) {
      setError(failure(e, t(next === 'Confirmed' ? 'actions.confirmFailed' : 'actions.dismissFailed')))
    }
  }

  const forget = async () => {
    setError(null)
    // An undone dismissal leaves Dismissed, the one tab that lists it; an undone confirmation stays where it is.
    const leavesList = decision?.decision === 'Dismissed'
    try {
      await undo.mutateAsync({ key: group.key, leavesList })
      if (leavesList) onLeft('Restored')
    } catch (e: unknown) {
      setError(failure(e, t('actions.undoFailed')))
    }
  }

  return (
    <article className="ui-card duplicates-group" aria-labelledby={titleId}>
      <div className="duplicates-group-header">
        <div className="duplicates-group-heading">
          <h2 id={titleId} className="ui-card-title duplicates-group-title">{heading.title}</h2>
          {heading.meta.length > 0 && (
            // The parts have no id, and their order is fixed.
            <p className="library-work-meta">{heading.meta.map((part, index) => <span key={index}>{part}</span>)}</p>
          )}
        </div>
        <div className="duplicates-group-side">
          <ul className="duplicates-badges" aria-label={t('group.about')}>
            <li><span className="ui-badge ui-badge-secondary">{t(`class.${group.class}`)}</span></li>
            {/* A decided possible match stays one to review on the server; the badge says what was decided instead. */}
            {group.needsReview && decision === null && (
              <li>
                <span className="ui-badge library-badge-warning">
                  <TriangleAlert className="h-3 w-3" aria-hidden="true" />
                  {t('group.needsReview')}
                </span>
              </li>
            )}
            {decision !== null && (
              <li>
                <span className="ui-badge ui-badge-outline">
                  {decision.decision === 'Confirmed'
                    ? <CircleCheck className="h-3 w-3" aria-hidden="true" />
                    : <EyeOff className="h-3 w-3" aria-hidden="true" />}
                  {t(`decision.${decision.decision}`)}
                </span>
              </li>
            )}
            {group.distinctions.map((distinction) => (
              <li key={`${distinction.kind}:${distinction.scheme ?? ''}:${String(distinction.ofSeries)}`}>
                <span className="ui-badge library-badge-warning">
                  <TriangleAlert className="h-3 w-3" aria-hidden="true" />
                  {distinctionText(distinction)}
                </span>
              </li>
            ))}
          </ul>
          {comparable && (
            <div className="duplicates-view">
              <SegmentedControl
                label={t('library:versions.view.label')}
                options={[
                  { value: 'rows', label: t('library:versions.view.rows'), title: t('library:versions.view.rowsTitle'), icon: Rows3 },
                  { value: 'table', label: t('library:versions.view.table'), title: t('library:versions.view.tableTitle'), icon: Table2 },
                ]}
                value={view}
                onChange={setView}
                showLabels
              />
            </div>
          )}
          {/* Only a possible match is decided: the other classes are one by the files' identity, bytes or
              registry ids, which no one has to confirm. Never a delete, move or bulk action. */}
          {group.class === 'Possible' && (
            <div className="duplicates-actions" role="group" aria-label={t('actions.label')}>
              {decision === null ? (
                <>
                  <ActionButton
                    icon={CircleCheck}
                    label={t('actions.confirm')}
                    pendingLabel={t('actions.confirming')}
                    title={t('actions.confirmTitle')}
                    pending={decide.isPending && decide.variables.decision === 'Confirmed'}
                    disabled={busy}
                    reason={reason}
                    onClick={() => void choose('Confirmed')}
                  />
                  <ActionButton
                    icon={EyeOff}
                    label={t('actions.dismiss')}
                    pendingLabel={t('actions.dismissing')}
                    title={t('actions.dismissTitle')}
                    pending={decide.isPending && decide.variables.decision === 'Dismissed'}
                    disabled={busy}
                    reason={reason}
                    onClick={() => void choose('Dismissed')}
                  />
                </>
              ) : (
                <ActionButton
                  icon={Undo2}
                  label={t('actions.undo')}
                  pendingLabel={t('actions.undoing')}
                  title={t('actions.undoTitle')}
                  pending={undo.isPending}
                  disabled={busy}
                  reason={reason}
                  onClick={() => void forget()}
                />
              )}
            </div>
          )}
        </div>
      </div>

      <div className="duplicates-evidence">
        <Evidence group={group} />
        <p>{spaceText}</p>
        {recoverable !== null && <p>{recoverable}</p>}
        {decisionLine !== null && <p>{decisionLine}</p>}
      </div>

      {error && <FieldErrorLine>{error}</FieldErrorLine>}

      {comparable && view === 'table' && !narrow ? (
        <VersionTable versions={versions} rows={tableRows} label={t('group.files')} caption={t('facts.caption')} />
      ) : (
        <VersionRows
          versions={versions}
          label={t('group.files')}
          details={(version) => {
            const file = filesById.get(version.id)
            return file === undefined ? null : <DuplicateFileFacts file={file} />
          }}
        />
      )}
    </article>
  )
}

// ── why the group is one ──

/**
 * The evidence of the group's class: the one file its names lead to and how many names the file system
 * counts; the SHA-256 the copies share; the ids that make the works one (a series' with its season and
 * episode), with the shortest and longest copies' running times when they differ widely (MAM-37); or the
 * titles and years a possible match shares, with whether its running times agree.
 */
function Evidence({ group }: { group: LibraryDuplicateGroup }) {
  const { t } = useTranslation(['duplicates', 'library'])
  const { evidence } = group

  const identifierText = (identifier: DuplicateIdentifier) => {
    const scheme = t(`library:schemes.${identifier.scheme}`)
    const { value, seasonNumber: season, episodeNumber: episode } = identifier
    if (season === null) return t('evidence.identifier', { scheme, value })
    return episode === null
      ? t('evidence.seriesSeason', { scheme, value, season })
      : t('evidence.seriesEpisode', { scheme, value, season, episode })
  }

  switch (group.class) {
    case 'SameFile': {
      const { identity } = evidence
      if (identity === null) return null
      return (
        <p>
          {t('evidence.sameFile', {
            count: identity.nameCount,
            value: formatNumber(identity.nameCount),
            // Ids, not amounts: no thousands separators.
            device: String(identity.deviceId),
            inode: String(identity.inode),
          })}{' '}
          {identity.linkCount === null
            ? t('evidence.linksUnknown')
            : t('evidence.links', { count: identity.linkCount, value: formatNumber(identity.linkCount) })}
        </p>
      )
    }
    case 'ByteIdentical':
      return (
        <p className="duplicates-evidence-line">
          <span>{t('evidence.sha256')}</span>
          <DuplicateHash value={evidence.sha256} />
        </p>
      )
    case 'SameWork': {
      // MAM-37 — the running times back the "Running times differ" badge only: the server gives them for any
      // two measured copies, and close ones (a remux and a rip of one cut) say nothing worth reading.
      const { runningTimes } = evidence
      const runningTimesDiffer = runningTimes !== null && group.distinctions.some((distinction) => distinction.kind === 'RunningTimes')
      return (
        <>
          <p className="duplicates-evidence-line">
            <span>{t('evidence.sameWork')}</span>
            {evidence.identifiers.map((identifier) => (
              <span
                key={`${identifier.scheme}:${identifier.value}:${identifier.seasonNumber ?? ''}:${identifier.episodeNumber ?? ''}`}
                className="ui-badge ui-badge-outline"
              >
                {identifierText(identifier)}
              </span>
            ))}
          </p>
          {runningTimesDiffer && (
            <p>
              {t('evidence.runningTimes', {
                shortest: formatDuration(runningTimes.shortestMilliseconds),
                longest: formatDuration(runningTimes.longestMilliseconds),
              })}
            </p>
          )}
        </>
      )
    }
    case 'Possible':
      return (
        <>
          <p className="duplicates-evidence-line">
            <span>{t('evidence.titles')}</span>
            {evidence.titles.map((title) => (
              <span key={`${title.text}:${title.year}`} className="ui-badge ui-badge-outline">
                {t('group.titleYear', { title: title.text, year: title.year })}
              </span>
            ))}
          </p>
          <p>
            {evidence.durationsAgree === null
              ? t('evidence.durationsUnknown')
              : evidence.durationsAgree ? t('evidence.durationsAgree') : t('evidence.durationsDiffer')}
          </p>
        </>
      )
  }
}
