import { useId } from 'react'
import { CircleCheck, CirclePlus, Clock, TriangleAlert, type LucideIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { JobProgress } from '@/components/jobs/JobProgress'
import { NewFileFacts } from '@/components/jobs/NewFileFacts'
import { isReplacementBeingChecked } from '@/lib/download-jobs'
import { expectedIdentifiers, identifierName, newFilePlace, workName } from '@/lib/replacement-details'
import type { ReplacementDetails, ReplacementJobNewFile, ReplacementJobWork, ReplacementNewFileProgress } from '@/lib/types'
import '@/styles/settings-list.css' // .settings-* — the table that turns into cards
import '@/styles/library.css' // .library-source-*, .library-version-chip* — how far each file got, its chips and ids
import '@/styles/search.css' // .search-details-heading — the table's heading inside the Replacement section
import '@/styles/jobs.css' // .jobs-* — the paths that break anywhere, the stacked cells

/** Each step's icon. */
const PROGRESS_ICONS: Record<ReplacementNewFileProgress, LucideIcon> = {
  Stopped: TriangleAlert,
  Downloading: Clock,
  NotChecked: Clock,
  Checked: Clock,
  AskedToLook: Clock,
  NotListed: Clock,
  NotRead: Clock,
  Extra: CirclePlus,
  NoId: Clock,
  NotNumbered: Clock,
  Identified: CircleCheck,
}

/**
 * The steps a file waits at for the next one. Waiting while the check runs; once it is over — it
 * stopped on another file, or was cancelled — a file left at one of them is a warning, as the check
 * won't take it further.
 */
const WAITING_STEPS: ReadonlySet<ReplacementNewFileProgress> = new Set<ReplacementNewFileProgress>([
  'Downloading', 'NotChecked', 'Checked', 'AskedToLook', 'NotListed', 'NotRead', 'NoId', 'NotNumbered',
])

/** The steps after which the ids a file was expected to carry no longer matter: it is identified, or simply added. */
const SETTLED_STEPS: ReadonlySet<ReplacementNewFileProgress> = new Set<ReplacementNewFileProgress>(['Identified', 'Extra'])

/**
 * MAM-27 — every new video file the last check of a replacement listed, by position, with what each
 * check found of it (ADR 0035): the file as the client names it; on the disk, how far the client got
 * and then the size, running time and picture the media inspector read; and in Jellyfin, how far it
 * got — not listed, not read, no id yet, identified — with the ids its item carries beside the ones
 * the replacement expects, and the server's sentence when it stopped the replacement.
 *
 * Nothing at all before the check listed a file: the section's progress line says where it stands.
 */
export function ReplacementNewFiles({ details }: { details: ReplacementDetails }) {
  const { t } = useTranslation('jobs')
  const titleId = useId()
  if (details.newFiles.length === 0) return null

  const checking = isReplacementBeingChecked(details.state)
  const expected = expectedIdentifiers(details.works)

  return (
    <section className="jobs-section" aria-labelledby={titleId}>
      <h3 id={titleId} className="search-details-heading">{t('oldCopies.newFiles.title')}</h3>
      <div className="settings-table-wrap">
        <table className="settings-table">
          <thead>
            <tr>
              <th scope="col">{t('oldCopies.newFiles.columns.file')}</th>
              <th scope="col">{t('oldCopies.newFiles.columns.disk')}</th>
              <th scope="col">{t('oldCopies.newFiles.columns.library')}</th>
            </tr>
          </thead>
          <tbody>
            {details.newFiles.map((file) => (
              <NewFileRow
                key={file.id}
                file={file}
                work={file.workId === null ? undefined : details.works.find((work) => work.workId === file.workId)}
                checking={checking}
                expected={expected}
              />
            ))}
          </tbody>
        </table>
      </div>
    </section>
  )
}

// ── one new file ──

interface NewFileRowProps {
  file: ReplacementJobNewFile
  /** The work its item's ids identified it as; undefined while none is. */
  work: ReplacementJobWork | undefined
  /** The check still runs, so a step not reached yet is waited for. */
  checking: boolean
  /** The ids the replacement expects, joined; null when its works carry none. */
  expected: string | null
}

/** One new file: a table row on a wide screen, a card on a phone. */
function NewFileRow({ file, work, checking, expected }: NewFileRowProps) {
  const { t } = useTranslation('jobs')
  const Icon = PROGRESS_ICONS[file.progress]
  const warning = file.progress === 'Stopped' || (!checking && WAITING_STEPS.has(file.progress))
  const place = newFilePlace(file)
  const downloading = file.checkedUtc === null && file.clientProgress !== null && file.clientProgress < 1

  return (
    <tr>
      <td data-label={t('oldCopies.newFiles.columns.file')} className="jobs-stacked-cell">
        <span className="jobs-file-path">{file.appPath ?? file.relativePath}</span>
      </td>
      <td data-label={t('oldCopies.newFiles.columns.disk')} className="jobs-stacked-cell">
        {/* How far the client got until the file is read; what the media inspector read after. */}
        {downloading ? <JobProgress progress={file.clientProgress} /> : <NewFileFacts file={file} withPath={false} />}
      </td>
      <td data-label={t('oldCopies.newFiles.columns.library')} className="jobs-stacked-cell">
        <div className="jobs-status">
          <p
            className={warning ? 'library-source-warning' : 'library-source-status'}
            data-status={file.progress === 'Identified' ? 'Succeeded' : undefined}
          >
            <Icon className="h-3.5 w-3.5" aria-hidden="true" />
            <span>
              {t(`oldCopies.newFiles.progress.${file.progress}`)}
              {/* "Stopped the replacement:" introduces the server's own sentence. */}
              {file.progress === 'Stopped' && file.problem !== null && <>{' '}{file.problem}</>}
            </span>
          </p>
          {/* While the check still waits on another file, what a pass found of this one stops nothing yet, but
              the owner may fix it in Jellyfin meanwhile (Vasyl's rule of 2026-10-10), so it shows as a warning. */}
          {file.progress !== 'Stopped' && file.problem !== null && (
            <p className="library-source-warning">
              <TriangleAlert className="h-3.5 w-3.5" aria-hidden="true" />
              <span>{file.problem}</span>
            </p>
          )}
          {file.progress === 'Identified' && work !== undefined && (
            <p className="ui-hint">{t('oldCopies.newFiles.identifiedAs', { title: workName(work) })}</p>
          )}
          {place !== null && <p className="ui-hint">{place}</p>}
          {file.identifiers.length > 0 && (
            <span className="library-version-chips">
              {file.identifiers.map((identifier) => (
                <span key={`${identifier.scheme}:${identifier.value}:${identifier.ofSeries}`} className="library-version-chip">
                  {identifierName(identifier)}
                </span>
              ))}
            </span>
          )}
          {/* What Jellyfin's ids are held against, until they named the work. */}
          {expected !== null && !SETTLED_STEPS.has(file.progress) && (
            <p className="ui-hint">{t('oldCopies.newFiles.expected', { ids: expected })}</p>
          )}
        </div>
      </td>
    </tr>
  )
}
