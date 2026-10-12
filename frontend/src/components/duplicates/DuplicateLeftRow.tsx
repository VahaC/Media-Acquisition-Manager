import { useId, useState } from 'react'
import { Archive, CopyCheck, EyeOff, Loader2, Undo2 } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { useSearchParams } from 'react-router-dom'
import { FieldErrorLine } from '@/components/shared/FieldErrorLine'
import { Button } from '@/components/ui/button'
import { groupName, holdsCopies, isGroupGone, type DuplicateLeaving } from '@/lib/duplicates'
import { splitPath } from '@/lib/library-versions'
import { useDecideDuplicate, useUndoDuplicateDecision } from '@/lib/queries'
import type { LibraryDuplicateGroup } from '@/lib/types'
import { getApiErrorMessage } from '@/lib/utils'
import '@/styles/duplicates.css' // .duplicates-left — the short row in the card's place

interface DuplicateLeftRowProps {
  group: LibraryDuplicateGroup
  /**
   * What took it off the tab on screen: the owner dismissed it or kept its copies, undid that on Dismissed, or
   * removed one of its copies into the quarantine.
   */
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
 * MAM-40 — copies kept as they are leave the same way, worded as kept, with Undo, and Keep again once undone
 * on Kept as they are. A removed copy leaves a row naming its file with the way to Removed copies, where
 * Restore puts it back after a confirmation: the group itself, when files enough are left, comes back on the
 * list without it. A removal the server answered as needing review, or with a sentence (a companion stayed),
 * says so with that sentence rather than that it waits in the quarantine.
 *
 * Only the owner's own action makes one, so its button takes the focus the card's button had, described by
 * the line, which names the group or the file.
 */
export function DuplicateLeftRow({ group, change, onBack }: DuplicateLeftRowProps) {
  const { t } = useTranslation('duplicates')
  const textId = useId()
  const decide = useDecideDuplicate()
  const undo = useUndoDuplicateDecision()
  const [error, setError] = useState<string | null>(null)
  const [, setSearchParams] = useSearchParams()
  const copies = holdsCopies(group)
  const pending = decide.isPending || undo.isPending

  if (change.kind === 'Removed') {
    const file = splitPath(change.path).name
    // Not "it waits in the quarantine" alone when the server said something of it needs a look: its sentence,
    // or that the removal stopped for review.
    const text = change.reason !== null
      ? t('left.removedReason', { file, reason: change.reason })
      : change.needsReview ? t('left.removedReview', { file }) : t('left.removed', { file })
    return (
      <div className="duplicates-left">
        <span id={textId}>{text}</span>
        {/* A button, as the tabs are: it opens the page's Removed copies tab, in the address as a tab is. */}
        <Button
          type="button"
          variant="outline"
          size="sm"
          aria-describedby={textId}
          // Mounted only by the owner's own click, which took the button it replaces away.
          autoFocus
          onClick={() => setSearchParams({ tab: 'Removed' }, { replace: true })}
        >
          <Archive className="h-3.5 w-3.5" aria-hidden="true" />
          {t('left.openRemoved')}
        </Button>
      </div>
    )
  }

  const dismissed = change.kind === 'Dismissed'
  const name = groupName(group)
  const text = dismissed
    ? t(copies ? 'left.kept' : 'left.dismissed', { name })
    : t(copies ? 'left.restoredCopies' : 'left.restored', { name })

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
        : getApiErrorMessage(e, t(dismissed ? 'actions.undoFailed' : copies ? 'actions.keepFailed' : 'actions.dismissFailed')))
    }
  }

  const AgainIcon = copies ? CopyCheck : EyeOff

  return (
    <div className="duplicates-left">
      <span id={textId}>{text}</span>
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
            : <AgainIcon className="h-3.5 w-3.5" aria-hidden="true" />}
        {dismissed
          ? (pending ? t('actions.undoing') : t('actions.undo'))
          : pending
            ? t(copies ? 'actions.keeping' : 'actions.dismissing')
            : t(copies ? 'left.keepAgain' : 'left.dismissAgain')}
      </Button>
      {error && <FieldErrorLine>{error}</FieldErrorLine>}
    </div>
  )
}
