import { useRef, useState } from 'react'
import { Copy } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Field } from '@/components/shared/Field'
import { Button } from '@/components/ui/button'
import { copyFieldText } from '@/lib/clipboard'
import { cn } from '@/lib/utils'
import '@/styles/profile.css' // .profile-actions, .profile-muted — the Copy row and the line beside it

/** What Copy last did: nothing yet, copied, or failed in both ways (lib/clipboard.ts). */
type CopyOutcome = 'idle' | 'copied' | 'failed'

interface CopyableFieldProps {
  /** The field's name, above it. */
  label: string
  /** What to do with the text, under the field. */
  hint?: string
  value: string
  rows: number
  /** The field's look beside `.ui-textarea`: the secret's, or a command's. */
  className: string
  /**
   * Copy's name. The button still reads "Copy"; the name tells it from the other Copy buttons of the same
   * dialog, which a screen reader lists side by side.
   */
  copyLabel: string
  /** The line when the browser copies neither way: what to select before pressing Ctrl+C. */
  copyFailed: string
  autoFocus?: boolean
}

/**
 * MAM-45 — a read-only text to copy: a field that selects itself on focus, Copy under it, and a line
 * saying how Copy went. Taken out of MAM-5's shown-once step once it offered three such texts — the
 * token, the Claude Code command and the Claude Desktop configuration — each with its own outcome.
 *
 * A textarea, not a `<code>` block: on a plain-HTTP LAN address Copy falls back to selecting the field
 * itself (lib/clipboard.ts). When that fails too, the line says to press Ctrl+C, with the text already
 * selected.
 */
export function CopyableField({ label, hint, value, rows, className, copyLabel, copyFailed, autoFocus }: CopyableFieldProps) {
  const { t } = useTranslation('profile')
  const field = useRef<HTMLTextAreaElement>(null)
  const [outcome, setOutcome] = useState<CopyOutcome>('idle')

  const copy = async () => {
    if (!field.current) return
    setOutcome((await copyFieldText(field.current)) ? 'copied' : 'failed')
  }

  return (
    // Two items of the dialog body's grid, spaced as the field and its Copy row were before MAM-45.
    <>
      <Field label={label} hint={hint}>
        {(control) => (
          <textarea
            {...control}
            ref={field}
            className={cn('ui-textarea', className)}
            value={value}
            readOnly
            rows={rows}
            spellCheck={false}
            autoFocus={autoFocus}
            onFocus={(event) => event.currentTarget.select()}
          />
        )}
      </Field>
      <div className="profile-actions">
        <Button type="button" variant="outline" size="sm" aria-label={copyLabel} onClick={() => void copy()}>
          <Copy className="h-3.5 w-3.5" aria-hidden="true" />
          {t('accessTokens.secret.copy')}
        </Button>
        {outcome === 'copied' && <span role="status" className="profile-muted">{t('accessTokens.secret.copied')}</span>}
        {outcome === 'failed' && <span role="status">{copyFailed}</span>}
      </div>
    </>
  )
}
