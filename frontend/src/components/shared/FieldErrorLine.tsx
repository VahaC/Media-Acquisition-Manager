import type { ReactNode } from 'react'
import { CircleAlert } from 'lucide-react'

interface FieldErrorLineProps {
  /** For the `aria-describedby` of the control the line is about, when it has one. */
  id?: string
  /** The rule the value broke, or why the control has nothing to offer. */
  children: ReactNode
}

/**
 * MAM-16 — the red line under a field (`.ui-field-error`, ui-primitives.css): the icon and the
 * sentence. Field writes it for a rule its control broke; the size filters and a select whose list
 * failed to load write it on their own, so the line reads the same wherever it is.
 *
 * role="alert" so a line that appears on submit or while typing is read out without moving focus to it.
 */
export function FieldErrorLine({ id, children }: FieldErrorLineProps) {
  return (
    <p id={id} className="ui-field-error" role="alert">
      <CircleAlert className="h-3 w-3" aria-hidden="true" />
      <span>{children}</span>
    </p>
  )
}
