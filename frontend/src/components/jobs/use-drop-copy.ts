import { useTranslation } from 'react-i18next'
import { useConfirm } from '@/components/shared/ConfirmDialog'
import { splitPath } from '@/lib/library-versions'
import { useDropReplacementCopy } from '@/lib/queries'
import type { ReplacementDetails, ReplacementJobCopy } from '@/lib/types'
import { getApiErrorMessage } from '@/lib/utils'

interface DropCopyCallbacks {
  /**
   * The drop was confirmed and is about to be sent: the page clears its last result and refusal.
   * Optional: a page whose only news is a refusal clears it in `onError`'s place.
   */
  onStart?: () => void
  /** The copy was dropped: the page shows `message` where its actions' results go. */
  onDone: (message: string) => void
  /** The drop was refused or failed: the page shows `message` in its error banner. */
  onError: (message: string) => void
}

/**
 * MAM-27 — the owner's Drop of one ticked copy, for every place that lists a replacement's copies (the
 * job's page and the Jobs list's Copies panel), so both ask and answer alike: a confirmation that says
 * the copy stays where it is and nothing else happens until Retry (the owner's decision of
 * 2026-10-11), with Drop it first and Keep it in last — "Cancel" would read as cancelling the
 * replacement. Not red: nothing is deleted anywhere.
 *
 * One mutation for the caller's whole list, so `droppingCopyId` says which row waits — the copy whose
 * drop is on its way, or null. The caller holds every other action of the replacement meanwhile.
 */
export function useDropCopy({ onStart, onDone, onError }: DropCopyCallbacks) {
  const { t } = useTranslation('jobs')
  const confirm = useConfirm()
  const dropCopy = useDropReplacementCopy()

  const drop = async (details: ReplacementDetails, copy: ReplacementJobCopy) => {
    // The copy is named by its file, as the server's sentences name it.
    const name = splitPath(copy.appPath).name
    const confirmed = await confirm({
      title: t('oldCopies.drop.confirmTitle', { name }),
      message: t('oldCopies.drop.message'),
      confirmLabel: t('oldCopies.drop.confirm'),
      cancelLabel: t('oldCopies.drop.keep'),
    })
    if (!confirmed) return
    onStart?.()
    try {
      await dropCopy.mutateAsync({ id: details.id, copyId: copy.id })
      onDone(t('oldCopies.drop.done', { name }))
    } catch (e: unknown) {
      onError(getApiErrorMessage(e, t('oldCopies.drop.failed')))
    }
  }

  return { drop, droppingCopyId: dropCopy.isPending ? dropCopy.variables.copyId : null }
}
