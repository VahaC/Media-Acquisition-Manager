import { useTranslation } from 'react-i18next'
import { ReplacementCopies } from '@/components/jobs/ReplacementCopies'
import { useDropCopy } from '@/components/jobs/use-drop-copy'
import { Banner } from '@/components/shared/Banner'
import { ErrorState } from '@/components/shared/ErrorState'
import { RetryButton } from '@/components/shared/RetryButton'
import { isReplacementBeingChecked } from '@/lib/download-jobs'
import { JOBS_POLL_MS, useReplacement } from '@/lib/queries'
import type { DownloadJobReplacement } from '@/lib/types'
import { useLoadRetry } from '@/lib/use-load-retry'
import { getApiErrorMessage, retryCanHelp } from '@/lib/utils'
import '@/styles/settings-list.css' // .settings-muted — the loading line

interface ReplacementCopiesPanelProps {
  /** The replacement the row's job carries, as the list last answered it. */
  replacement: DownloadJobReplacement
  /** Another action on the row's job or replacement is on its way (the row's one busy flag): every Drop waits. */
  busy: boolean
  /** A drop was confirmed and is about to be sent: the page clears its last refusal. */
  onStart: () => void
  /** A drop was refused or failed: the page shows `message` in the banner above the rows. */
  onError: (message: string) => void
}

/**
 * MAM-27 — what the Copies button of an owner's Jobs row opens under it (Vasyl's answer of
 * 2026-10-11): the old copies the replacement ticked, the new file each is paired with, where each
 * stands, and Drop — the job's page's own table (ReplacementCopies), with its own confirmation
 * (useDropCopy). Mounted only while the row is open, so a closed list asks for no details at all; the
 * row shows the button to the owner only, as the route is owner-only.
 *
 * While the check runs it asks again every 2 s, as what the check finds changes on every pass with
 * nothing on the row changing; otherwise the list asks for it again whenever its answer brings the
 * row's replacement changed (useDownloadJobs), and each action once it answers. A refusal goes to the
 * page's banner above the rows, as every row action's does; a drop that went through needs no news of
 * its own: the copy shows dropped here, and the row's line says so. While a drop is on its way the
 * row's Retry, Keep both and Cancel job wait, and every Drop here waits while one of theirs is (`busy`).
 *
 * MAM-41 — no longer behind a Copies button: it is part of the details the row's chevron opens
 * (JobRowDetails), under the row's sentences, as the copies are details, not an action.
 */
export function ReplacementCopiesPanel({ replacement, busy, onStart, onError }: ReplacementCopiesPanelProps) {
  const { t } = useTranslation(['jobs', 'common'])
  const details = useReplacement(replacement.id, true, isReplacementBeingChecked(replacement.state) ? JOBS_POLL_MS : false)
  const { data, error, isLoading } = details
  const load = useLoadRetry(details)
  const { drop, droppingCopyId } = useDropCopy({
    onStart,
    // Nothing to add: the list has no banner for news, and the drop shows on the copy and the row.
    onDone: () => undefined,
    onError,
  })

  if (load.error) {
    return (
      <ErrorState
        title={t('oldCopies.loadFailed')}
        onRetry={retryCanHelp(load.error) ? load.retry : undefined}
        retrying={load.retrying}
      >
        {getApiErrorMessage(load.error)}
      </ErrorState>
    )
  }
  if (isLoading) return <p className="settings-muted">{t('common:loading')}</p>
  if (!data) return null
  return (
    <>
      {/* Above the copies, which stay on screen as last loaded. */}
      {error && (
        <Banner action={<RetryButton onRetry={() => details.refetch()} />}>
          {t('oldCopies.refreshFailed')}{' '}
          {getApiErrorMessage(error)}
        </Banner>
      )}
      <ReplacementCopies
        details={data}
        busy={busy || droppingCopyId !== null}
        droppingCopyId={droppingCopyId}
        onDrop={(copy) => void drop(data, copy)}
      />
    </>
  )
}
