import { CircleAlert, CircleCheck } from 'lucide-react'
import '@/styles/integrations.css' // .integrations-test-result — the colour of each outcome

/** A finished connection test: whether it connected, and the sentence to read. */
export interface ConnectionTestOutcome {
  ok: boolean
  text: string
}

/**
 * The answer to the dialog's Test connection, under its button: the service's own sentence, or the
 * API's refusal of the draft. Nothing until a test has finished.
 *
 * `role="status"`, so a screen reader reads the answer out without moving the focus off the button.
 */
export function ConnectionTestResultLine({ result }: { result: ConnectionTestOutcome | null }) {
  if (result === null) return null
  return (
    <p role="status" className="integrations-test-result" data-outcome={result.ok ? 'ok' : 'err'}>
      {result.ok
        ? <CircleCheck className="h-3.5 w-3.5" aria-hidden="true" />
        : <CircleAlert className="h-3.5 w-3.5" aria-hidden="true" />}
      <span>{result.text}</span>
    </p>
  )
}
