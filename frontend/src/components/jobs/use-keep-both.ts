import { useTranslation } from 'react-i18next'
import { useConfirm } from '@/components/shared/ConfirmDialog'
import { formatNumber } from '@/lib/format'
import { useCancelReplacement } from '@/lib/queries'
import type { DownloadJob, DownloadJobReplacement } from '@/lib/types'
import { getApiErrorMessage } from '@/lib/utils'

interface KeepBothCallbacks {
  /**
   * Keep both was confirmed and is about to be sent: the page clears its last result and refusal.
   * Optional: a page whose only news is a refusal clears it in `onError`'s place.
   */
  onStart?: () => void
  /** The replacement was cancelled: the page shows `message` where its actions' results go. */
  onDone: (message: string) => void
  /** Keep both was refused or failed: the page shows `message` in its error banner. */
  onError: (message: string) => void
}

/**
 * MAM-27 — the owner's Keep both, for every place that offers it (the Jobs row's KeepBothButton and the
 * job's page's Replacement section), so both ask and answer alike: it cancels only the replacement a
 * job carries, so both copies stay and the download goes on as it is — nothing is sent to the client.
 * The confirmation names the job's release and its client, with Keep both first and Keep replacing
 * last: "Cancel" there would read as the very cancel it asks about. Not red: nothing is deleted
 * anywhere.
 *
 * The caller passes the job it shows, never one read from the cache: a page opened at an address that
 * spells the job's id in capitals caches it under that spelling. `pending` says Keep both is on its
 * way; the caller holds every other action of the replacement meanwhile.
 */
export function useKeepBoth({ onStart, onDone, onError }: KeepBothCallbacks) {
  const { t } = useTranslation('jobs')
  const confirm = useConfirm()
  const cancel = useCancelReplacement()

  const keepBoth = async (job: DownloadJob, replacement: DownloadJobReplacement) => {
    const confirmed = await confirm({
      title: t('oldCopies.keepBoth.confirmTitle', { title: job.releaseTitle }),
      message: t('oldCopies.keepBoth.message', {
        count: replacement.copyCount,
        value: formatNumber(replacement.copyCount),
        client: job.downloadClientName,
      }),
      confirmLabel: t('oldCopies.keepBoth.confirm'),
      cancelLabel: t('oldCopies.keepBoth.keep'),
    })
    if (!confirmed) return
    onStart?.()
    try {
      await cancel.mutateAsync(replacement.id)
      onDone(t('oldCopies.keepBoth.done'))
    } catch (e: unknown) {
      onError(getApiErrorMessage(e, t('oldCopies.keepBoth.failed')))
    }
  }

  return { keepBoth, pending: cancel.isPending }
}
