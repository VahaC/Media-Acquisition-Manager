import { useQueryClient } from '@tanstack/react-query'
import { Trash2 } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { ActionButton } from '@/components/shared/ActionButton'
import { useConfirm } from '@/components/shared/ConfirmDialog'
import { DeleteForGoodMessage } from '@/components/shared/DeleteForGoodMessage'
import { useDeleteReplacementForGood } from '@/lib/queries'
import { qk } from '@/lib/queries/keys'
import { quarantinedNames } from '@/lib/replacement-details'
import { replacementsApi } from '@/lib/replacements-api'
import type { DownloadJobReplacement } from '@/lib/types'
import { getApiErrorMessage } from '@/lib/utils'
import '@/styles/settings-list.css' // .settings-actions .label-text — the label that hides on a phone

interface DeleteForGoodButtonProps {
  /** The replacement the row's job carries, as the list last answered it. */
  replacement: DownloadJobReplacement
  /** Another action on the row's job or replacement is on its way (the row's one busy flag): this one waits. */
  busy: boolean
  /** The deletion was confirmed and is about to be sent: the page clears its last refusal. */
  onStart: () => void
  /** The deletion, or the question for what it deletes, failed: the page shows `message` in a banner above the jobs. */
  onError: (message: string) => void
}

/**
 * MAM-41 — the owner's Delete for good on a Jobs row whose replacement holds old copies in the quarantine,
 * so it is settled where the Needs you tab lists it rather than only on the job's page. It is that page's
 * own (ReplacementSection): a red outline, and the wide confirmation that lists every name it deletes with
 * its size and the room it frees. The row knows only counts, so the names are asked for when the owner
 * clicks — what the quarantine holds then —, and the names listed are the names sent: a list that changed
 * meanwhile is the server's 409 to tell, never a reason to delete what the owner wasn't shown.
 *
 * Shown while the server says it can be done (`canDeleteForGood`), and disabled with the reason while
 * replacement is switched off, as the server refuses it then. While it is on its way the row's other
 * actions wait, and it waits while one of theirs is (`busy`); the row's Replacement cell then says the old
 * copies were deleted.
 */
export function DeleteForGoodButton({ replacement, busy, onStart, onError }: DeleteForGoodButtonProps) {
  const { t } = useTranslation('jobs')
  const confirm = useConfirm()
  const queryClient = useQueryClient()
  const remove = useDeleteReplacementForGood()

  const send = async () => {
    try {
      const details = await queryClient.fetchQuery({
        queryKey: qk.replacements.job(replacement.id),
        queryFn: () => replacementsApi.get(replacement.id),
      })
      const listed = quarantinedNames(details)
      const copies = listed.filter((name) => name.kind === 'Video').length
      const confirmed = await confirm({
        title: t('oldCopies.deleteForGood.confirmTitle', { count: copies }),
        message: <DeleteForGoodMessage names={listed} freesBytes={details.freesBytes} />,
        confirmLabel: t('oldCopies.deleteForGood.confirm'),
        destructive: true,
        wide: true,
      })
      if (!confirmed) return
      onStart()
      await remove.mutateAsync({ id: replacement.id, names: listed.map((name) => name.id) })
    } catch (e: unknown) {
      onError(getApiErrorMessage(e, t('oldCopies.deleteForGood.failed')))
    }
  }

  return (
    <ActionButton
      icon={Trash2}
      label={t('oldCopies.deleteForGood.action')}
      pendingLabel={t('oldCopies.deleteForGood.pending')}
      title={t('oldCopies.deleteForGood.title')}
      pending={remove.isPending}
      disabled={busy || remove.isPending}
      reason={replacement.switchedOff ? t('oldCopies.switchedOff') : undefined}
      className="ui-button-danger"
      onClick={() => void send()}
    />
  )
}
