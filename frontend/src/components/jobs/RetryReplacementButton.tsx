import { RotateCw } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { ActionButton } from '@/components/shared/ActionButton'
import { useRetryReplacement } from '@/lib/queries'
import type { DownloadJobReplacement } from '@/lib/types'
import { getApiErrorMessage } from '@/lib/utils'
import '@/styles/settings-list.css' // .settings-actions .label-text — the label that hides on a phone

interface RetryReplacementButtonProps {
  replacement: DownloadJobReplacement
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
 */
export function RetryReplacementButton({ replacement, onStart, onError }: RetryReplacementButtonProps) {
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
      title={t('oldCopies.retry.title')}
      pending={retry.isPending}
      disabled={retry.isPending}
      reason={undefined}
      onClick={() => void send()}
    />
  )
}
