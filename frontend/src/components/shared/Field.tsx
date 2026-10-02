import { useId, type ReactNode } from 'react'
import { FieldErrorLine } from '@/components/shared/FieldErrorLine'

/** What Field hands its control to spread onto it: the id its label points at, and its description. */
interface FieldControlProps {
  id: string
  'aria-describedby'?: string
  'aria-invalid'?: true
}

interface FieldProps {
  /** The visible name. A caller adds "(optional)" itself, as the forms here do. */
  label: ReactNode
  /** How to fill the field, under the control. */
  hint?: ReactNode
  /** The rule the value broke. It takes the hint's place and marks the field invalid. */
  error?: string | null
  /** The control, given the props that tie it to the label and to the line under it. */
  children: (control: FieldControlProps) => ReactNode
}

/**
 * MAM-16 — a label, its control, and under it a hint or the error that replaces it.
 *
 * The hint and the error sit outside the <label>, tied to the control by `aria-describedby`: inside
 * it, as the design kit writes it, they would join the control's name, and a screen reader would
 * read the whole sentence as the field's name (the settings dialogs keep their .auth-hint lines
 * outside for the same reason). The control comes from a render prop because only the caller knows
 * which input it is, and the ids are Field's own.
 */
export function Field({ label, hint, error, children }: FieldProps) {
  const controlId = useId()
  const messageId = useId()
  const invalid = Boolean(error)

  return (
    <div className="ui-field" data-invalid={invalid ? '' : undefined}>
      <label htmlFor={controlId} className="ui-label">{label}</label>
      {children({
        id: controlId,
        'aria-describedby': invalid || hint ? messageId : undefined,
        'aria-invalid': invalid || undefined,
      })}
      {invalid ? (
        <FieldErrorLine id={messageId}>{error}</FieldErrorLine>
      ) : hint ? (
        <p id={messageId} className="ui-hint">{hint}</p>
      ) : null}
    </div>
  )
}
