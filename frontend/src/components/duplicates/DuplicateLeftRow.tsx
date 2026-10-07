import { useId, useState } from 'react'
import { EyeOff, Loader2, Undo2 } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { FieldErrorLine } from '@/components/shared/FieldErrorLine'
import { Button } from '@/components/ui/button'
import { groupName, isGroupGone, type DuplicateLeaving } from '@/lib/duplicates'
import { useDecideDuplicate, useUndoDuplicateDecision } from '@/lib/queries'
import type { LibraryDuplicateGroup } from '@/lib/types'
import { getApiErrorMessage } from '@/lib/utils'
import '@/styles/duplicates.css' // .duplicates-left — the short row in the card's place

interface DuplicateLeftRowProps {
  group: LibraryDuplicateGroup
  /** What took it off the tab on screen: the owner dismissed it, or undid its dismissal on Dismissed. */
  change: DuplicateLeaving
  /** The way back went through and the list shows the group again: the card takes the row's place. */
  onBack: () => void
}

/**
 * MAM-21 — a possible match the owner just took off the tab on screen, as one line in its card's place with
 * the way back, as "Not interested" leaves a recommendation (RecommendationCard): Dismiss would otherwise
 * close the list up under the pointer, with nothing at hand to undo it. Dismissed, it says where it went and
 * offers Undo; restored on the Dismissed tab, it says so and offers Dismiss again. The page keeps it until
 * the tab or the page changes (DuplicatesPage).
 *
 * Only the owner's own decision makes one, so its button takes the focus the card's button had, described by
 * the line, which names the group.
 */
export function DuplicateLeftRow({ group, change, onBack }: DuplicateLeftRowProps) {
  const { t } = useTranslation('duplicates')
  const textId = useId()
  const decide = useDecideDuplicate()
  const undo = useUndoDuplicateDecision()
  const [error, setError] = useState<string | null>(null)
  const dismissed = change === 'Dismissed'
  const pending = decide.isPending || undo.isPending

  const goBack = async () => {
    setError(null)
    try {
      // The group comes back where the row is: the call ends once the list has it again.
      if (dismissed) await undo.mutateAsync({ key: group.key, leavesList: false })
      else await decide.mutateAsync({ key: group.key, decision: 'Dismissed', leavesList: false })
      onBack()
    } catch (e: unknown) {
      setError(isGroupGone(e)
        ? t('actions.changed')
        : getApiErrorMessage(e, t(dismissed ? 'actions.undoFailed' : 'actions.dismissFailed')))
    }
  }

  return (
    <div className="duplicates-left">
      <span id={textId}>{t(dismissed ? 'left.dismissed' : 'left.restored', { name: groupName(group) })}</span>
      <Button
        type="button"
        variant="outline"
        size="sm"
        aria-describedby={textId}
        // Mounted only by the owner's own click, which took the button it replaces away.
        autoFocus
        disabled={pending}
        onClick={() => void goBack()}
      >
        {pending
          ? <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />
          : dismissed
            ? <Undo2 className="h-3.5 w-3.5" aria-hidden="true" />
            : <EyeOff className="h-3.5 w-3.5" aria-hidden="true" />}
        {dismissed
          ? (pending ? t('actions.undoing') : t('actions.undo'))
          : (pending ? t('actions.dismissing') : t('left.dismissAgain'))}
      </Button>
      {error && <FieldErrorLine>{error}</FieldErrorLine>}
    </div>
  )
}
