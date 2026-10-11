import { Copy } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { useKeepBoth } from '@/components/jobs/use-keep-both'
import { ActionButton } from '@/components/shared/ActionButton'
import type { DownloadJob, DownloadJobReplacement } from '@/lib/types'
import '@/styles/settings-list.css' // .settings-actions .label-text — the label that hides on a phone

interface KeepBothButtonProps {
  /** The job the replacement rides on: its release names the confirmation, its client the message. */
  job: DownloadJob
  /** The replacement that job carries. */
  replacement: DownloadJobReplacement
  /** Another action on the job or its replacement is on its way (the row's one busy flag): this one waits. */
  busy: boolean
  /** Keep both was confirmed and is about to be sent: the page clears its last refusal. */
  onStart: () => void
  /** Keep both was refused or failed: the page shows `message` in a banner above the jobs. */
  onError: (message: string) => void
}

/**
 * MAM-27 — the owner's Keep both on a Jobs row: cancels only the replacement the job carries, so both
 * copies stay and the download goes on as it is — nothing is sent to the client. The word is the
 * Download dialog's own, so the same choice reads the same before and after the send; and on a job
 * still downloading it sits beside Cancel job without being taken for it, which stops following the
 * download (and ends the replacement with it).
 *
 * Asks first, as Cancel job does, in the job's page's own words (useKeepBoth). Not red: nothing is
 * deleted anywhere. While it is on its way the row's other actions wait, and it waits while one of
 * theirs is (`busy`); the row's line then says the replacement was cancelled.
 */
export function KeepBothButton({ job, replacement, busy, onStart, onError }: KeepBothButtonProps) {
  const { t } = useTranslation('jobs')
  // Nothing to add when it goes through: the list has no banner for news, and the row's line says so.
  const { keepBoth, pending } = useKeepBoth({ onStart, onDone: () => undefined, onError })

  return (
    <ActionButton
      icon={Copy}
      label={t('oldCopies.keepBoth.action')}
      pendingLabel={t('oldCopies.keepBoth.pending')}
      title={t('oldCopies.keepBoth.title')}
      pending={pending}
      disabled={busy || pending}
      reason={undefined}
      onClick={() => void keepBoth(job, replacement)}
    />
  )
}
