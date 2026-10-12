import { Archive, CircleAlert, Clock, Loader2, Trash2, TriangleAlert, Undo2, type LucideIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { formatFileSize } from '@/lib/format'
import type { ReplacementName, ReplacementNameState } from '@/lib/types'
import { cn } from '@/lib/utils'
import '@/styles/settings-list.css' // .settings-table — the row, a card on a phone with each cell's data-label
import '@/styles/library.css' // .library-source-status, .library-source-warning — where the name is, why it stayed
import '@/styles/jobs.css' // .jobs-* — the paths that break anywhere, the size that doesn't, the stacked cells

/** Each name state's icon; Moving, Restoring and Deleting spin, as work under way. */
const NAME_ICONS: Record<ReplacementNameState, LucideIcon> = {
  Planned: Clock,
  Moving: Loader2,
  Quarantined: Archive,
  Stayed: TriangleAlert,
  Restoring: Loader2,
  Restored: Undo2,
  Deleting: Loader2,
  Deleted: Trash2,
  Lost: TriangleAlert,
}

/** The name states worded as a warning: the name isn't where the move meant it to be. */
const WARNING_STATES: ReadonlySet<ReplacementNameState> = new Set<ReplacementNameState>(['Stayed', 'Lost'])

/** The name states that spin. */
const UNDER_WAY_STATES: ReadonlySet<ReplacementNameState> = new Set<ReplacementNameState>(['Moving', 'Restoring', 'Deleting'])

interface QuarantineNameRowProps {
  name: ReplacementName
  /** The heading of the path column, which a phone shows as the cell's caption: the table's own. */
  pathLabel: string
}

/**
 * One name a move into the quarantine planned: a table row on a wide screen, a card on a phone. Where it
 * was, with its place in the quarantine under it — where it went, or would go —, its size, and where it is
 * now; the server's sentence under that when it stayed, wasn't put back or deleted, or was lost.
 *
 * MAM-40 — moved out of MAM-26's ReplacementSection, so a copy removed on the Duplicates page lists its
 * names as a replacement's old copies do. The caller's table gives the path column its heading.
 */
export function QuarantineNameRow({ name, pathLabel }: QuarantineNameRowProps) {
  const { t } = useTranslation('jobs')
  const Icon = NAME_ICONS[name.state]
  const warning = WARNING_STATES.has(name.state)

  return (
    <tr>
      <td data-label={pathLabel} className="jobs-stacked-cell">
        <span className="jobs-name-paths">
          <span className="jobs-file-path">{name.originalPath}</span>
          <span className="ui-hint">
            {t('oldCopies.quarantinePath')}{' '}
            <span className="jobs-file-path">{name.quarantinePath}</span>
          </span>
        </span>
      </td>
      <td data-label={t('oldCopies.columns.size')} className="jobs-size">{formatFileSize(name.sizeBytes)}</td>
      <td data-label={t('oldCopies.columns.where')} className="jobs-stacked-cell">
        <div className="jobs-status">
          <p className={warning ? 'library-source-warning' : 'library-source-status'}>
            <Icon className={cn('h-3.5 w-3.5', UNDER_WAY_STATES.has(name.state) && 'animate-spin')} aria-hidden="true" />
            <span>{t(`oldCopies.state.${name.state}`)}</span>
          </p>
          {name.problem !== null && (
            <p className="library-source-warning">
              <CircleAlert className="h-3.5 w-3.5" aria-hidden="true" />
              <span>{name.problem}</span>
            </p>
          )}
          {name.otherNames && <p className="ui-hint">{t('oldCopies.otherNames')}</p>}
        </div>
      </td>
    </tr>
  )
}
