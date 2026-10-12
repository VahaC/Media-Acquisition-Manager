import { useQueryClient } from '@tanstack/react-query'
import { Undo2 } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { ActionButton } from '@/components/shared/ActionButton'
import { useConfirm } from '@/components/shared/ConfirmDialog'
import { useRestoreReplacement } from '@/lib/queries'
import { qk } from '@/lib/queries/keys'
import { quarantinedNames } from '@/lib/replacement-details'
import { replacementsApi } from '@/lib/replacements-api'
import type { DownloadJobReplacement } from '@/lib/types'
import { getApiErrorMessage } from '@/lib/utils'
import '@/styles/settings-list.css' // .settings-actions .label-text — the label that hides on a phone

interface RestoreButtonProps {
  /** The replacement the row's job carries, as the list last answered it. */
  replacement: DownloadJobReplacement
  /** Another action on the row's job or replacement is on its way (the row's one busy flag): this one waits. */
  busy: boolean
  /** The restore was confirmed and is about to be sent: the page clears its last refusal. */
  onStart: () => void
  /** The restore, or the question for what it puts back, failed: the page shows `message` in a banner above the jobs. */
  onError: (message: string) => void
}

/**
 * MAM-41 — the owner's Restore on a Jobs row whose replacement holds old copies in the quarantine, so it
 * is settled where the Needs you tab lists it rather than only on the job's page. It is that page's own
 * (ReplacementSection), with the same plain confirmation, its title counted by the copies the quarantine
 * holds when the owner clicks: the row knows only counts, so the replacement's names are asked for then.
 *
 * Shown while the server says it can be done (`canRestore`) — a replacement in review may hold earlier
 * copies there already —, and disabled with the reason while replacement is switched off, as the server
 * refuses it then. While it is on its way the row's other actions wait, and it waits while one of theirs
 * is (`busy`); the row's Replacement cell then says the old copies are back.
 */
export function RestoreButton({ replacement, busy, onStart, onError }: RestoreButtonProps) {
  const { t } = useTranslation('jobs')
  const confirm = useConfirm()
  const queryClient = useQueryClient()
  const restore = useRestoreReplacement()

  const send = async () => {
    try {
      const details = await queryClient.fetchQuery({
        queryKey: qk.replacements.job(replacement.id),
        queryFn: () => replacementsApi.get(replacement.id),
      })
      const copies = quarantinedNames(details).filter((name) => name.kind === 'Video').length
      const confirmed = await confirm({
        title: t('oldCopies.restore.confirmTitle', { count: copies }),
        message: t('oldCopies.restore.message'),
        confirmLabel: t('oldCopies.restore.confirm'),
      })
      if (!confirmed) return
      onStart()
      await restore.mutateAsync(replacement.id)
    } catch (e: unknown) {
      onError(getApiErrorMessage(e, t('oldCopies.restore.failed')))
    }
  }

  return (
    <ActionButton
      icon={Undo2}
      label={t('oldCopies.restore.action')}
      pendingLabel={t('oldCopies.restore.pending')}
      title={t('oldCopies.restore.title')}
      pending={restore.isPending}
      disabled={busy || restore.isPending}
      reason={replacement.switchedOff ? t('oldCopies.switchedOff') : undefined}
      onClick={() => void send()}
    />
  )
}
