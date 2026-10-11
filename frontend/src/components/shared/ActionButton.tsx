import { Loader2, type LucideIcon } from 'lucide-react'
import { DisabledReason } from '@/components/shared/DisabledReason'
import { Button } from '@/components/ui/button'

interface ActionButtonProps {
  icon: LucideIcon
  label: string
  /** The label while its own request is pending ("Restoring…"). */
  pendingLabel: string
  title: string
  /** Its own request is pending: a spinner in place of the icon, and the pending label. */
  pending: boolean
  /** Another action of the same card or section is under way (one shared busy flag). */
  disabled: boolean
  /** Why it can't be used, shown on hover; undefined when it can. */
  reason: string | undefined
  /** MAM-26 — `ui-button-danger` for a destructive trigger in an action row (red outline). */
  className?: string
  onClick: () => void
}

/**
 * An owner's action on a card or a section: its icon and label, a spinner and "…ing" while its own
 * request is pending, and the reason on hover when it can't be used. The label hides on a phone in a
 * `.settings-actions` or `.duplicates-actions` row; the title names the button then.
 *
 * MAM-26 — moved out of the MAM-21 Duplicates card, so a replacement's Retry, Restore and Delete for
 * good on the job's page look and behave as Confirm, Dismiss and Undo do.
 */
export function ActionButton({ icon: Icon, label, pendingLabel, title, pending, disabled, reason, className, onClick }: ActionButtonProps) {
  return (
    <DisabledReason reason={reason}>
      <Button
        type="button"
        variant="outline"
        size="sm"
        className={className}
        title={title}
        disabled={disabled || reason !== undefined}
        onClick={onClick}
      >
        {pending
          ? <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />
          : <Icon className="h-3.5 w-3.5" aria-hidden="true" />}
        <span className="label-text">{pending ? pendingLabel : label}</span>
      </Button>
    </DisabledReason>
  )
}
