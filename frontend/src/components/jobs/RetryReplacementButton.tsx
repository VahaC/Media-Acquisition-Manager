import { RotateCw } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { ActionButton } from '@/components/shared/ActionButton'
import { useRetryReplacement } from '@/lib/queries'
import type { DownloadJobReplacement } from '@/lib/types'
import { getApiErrorMessage } from '@/lib/utils'
import '@/styles/settings-list.css' // .settings-actions .label-text — the label that hides on a phone

interface RetryReplacementButtonProps {
  replacement: DownloadJobReplacement
  /** Another action on the job or its replacement is on its way (the row's one busy flag): this one waits. */
  busy: boolean
  /** The retry is about to be sent: the page clears its last refusal. */
  onStart: () => void
  /** The retry failed: the page shows `message` in a banner above the jobs. */
  onError: (message: string) => void
}

/**
 * MAM-26 — the owner's Retry on a Jobs row, for a replacement the mover stopped for review: the row's
 * own sentence ends "…then press Retry." (ADR 0031), so the button sits beside it rather than only on
 * the job's page. No confirmation: the request writes nothing to the media, and the next pass checks
 * everything again before anything moves. Its own mutation, so only this row waits; the row's line
 * then shows the replacement verified again, and the list asks again while it moves.
 *
 * MAM-27 — also for one the check of its new files stopped: the row's line then ends "…then press
 * Retry", and Retry starts that check again, so its title says the new files are checked again and
 * Jellyfin's ids decide, rather than that the old copies are. While it is on its way the row's other
 * actions wait, and it waits while one of theirs is (`busy`): a Retry sent while a Drop is pending
 * would be refused, or send the copy being dropped back to the check.
 */
export function RetryReplacementButton({ replacement, busy, onStart, onError }: RetryReplacementButtonProps) {
  const { t } = useTranslation('jobs')
  const retry = useRetryReplacement()

  const send = async () => {
    onStart()
    try {
      await retry.mutateAsync(replacement.id)
    } catch (e: unknown) {
      onError(getApiErrorMessage(e, t('oldCopies.retry.failed')))
    }
  }

  return (
    <ActionButton
      icon={RotateCw}
      label={t('oldCopies.retry.action')}
      pendingLabel={t('oldCopies.retry.pending')}
      title={replacement.stop === 'Check' ? t('oldCopies.retry.titleCheck') : t('oldCopies.retry.title')}
      pending={retry.isPending}
      disabled={busy || retry.isPending}
      reason={undefined}
      onClick={() => void send()}
    />
  )
}
