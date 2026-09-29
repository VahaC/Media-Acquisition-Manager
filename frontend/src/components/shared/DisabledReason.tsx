import type { ReactNode } from 'react'
import '@/styles/settings-list.css' // .settings-action-reason — the hover target that carries the reason

/**
 * Shows `reason` on hover over a button disabled for that reason; without a reason the button is
 * rendered as is. A disabled `.ui-button` ignores the pointer, so its own `title` would never show:
 * the wrapper takes the pointer and the title instead.
 *
 * MAM-7 — moved out of the MAM-4 Users page; a settings row disables an action and says why.
 */
export function DisabledReason({ reason, children }: { reason: string | undefined; children: ReactNode }) {
  if (reason === undefined) return children
  return <span className="settings-action-reason" title={reason}>{children}</span>
}
