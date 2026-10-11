import { CircleStop } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { useConfirm } from '@/components/shared/ConfirmDialog'
import { Button } from '@/components/ui/button'
import { useCancelDownload } from '@/lib/queries'
import type { DownloadJob } from '@/lib/types'
import { getApiErrorMessage } from '@/lib/utils'
import '@/styles/settings-list.css' // .settings-actions .label-text — the label that hides on a phone

interface CancelJobButtonProps {
  job: DownloadJob
  /**
   * MAM-27 — another action on the job or its replacement is on its way (a Jobs row's one busy flag):
   * this one waits. Left out where the button stands alone.
   */
  busy?: boolean
  /** The cancel was confirmed and is about to be sent: the page clears its last refusal. */
  onStart: () => void
  /** The cancel failed: the page shows `message` in a banner above the job. */
  onError: (message: string) => void
}

/**
 * MAM-14 — Cancel on a job's row or details page: after a confirmation that says the torrent and its
 * files stay in the client, the app stops following the job. Nothing is sent to the client (ADR
 * 0014). Each button has its own mutation, so only the job being cancelled waits.
 *
 * Not the red style of Remove: nothing is removed anywhere. Placed in a `.settings-actions` row, its
 * label hides on a phone and the icon keeps its title.
 */
export function CancelJobButton({ job, busy = false, onStart, onError }: CancelJobButtonProps) {
  const { t } = useTranslation('jobs')
  const confirm = useConfirm()
  const cancel = useCancelDownload()

  const cancelJob = async () => {
    const confirmed = await confirm({
      title: t('cancel.title', { title: job.releaseTitle }),
      message: t('cancel.message', { client: job.downloadClientName }),
      confirmLabel: t('cancel.confirm'),
      // "Cancel" would sit beside "Cancel job" and say the opposite; this says what it keeps.
      cancelLabel: t('cancel.keep'),
    })
    if (!confirmed) return
    onStart()
    try {
      await cancel.mutateAsync(job.id)
    } catch (e: unknown) {
      onError(getApiErrorMessage(e, t('actionFailed')))
    }
  }

  return (
    <Button
      type="button"
      variant="outline"
      size="sm"
      title={t('cancel.action')}
      disabled={busy || cancel.isPending}
      onClick={() => void cancelJob()}
    >
      <CircleStop className="h-3.5 w-3.5" aria-hidden="true" />
      <span className="label-text">{t('cancel.action')}</span>
    </Button>
  )
}
