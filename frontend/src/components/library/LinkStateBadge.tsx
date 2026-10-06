import { CircleCheck, Clock, Link2, Loader2, TriangleAlert, type LucideIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import type { LibraryLinkWriteState } from '@/lib/types'
import '@/styles/library.css' // .library-badge-warning — the amber of a link Jellyfin doesn't hold

/** The icon of each state; a write under way spins. */
const STATE_ICONS: Record<LibraryLinkWriteState, LucideIcon> = {
  KeptInApp: Link2,
  Queued: Clock,
  Writing: Loader2,
  NotWritten: TriangleAlert,
  Written: CircleCheck,
  InLibrary: CircleCheck,
  Lost: TriangleAlert,
}

/**
 * MAM-34 — where a TMDb link stands against the library, as a badge: "In Jellyfin", "Not in Jellyfin
 * yet", "Jellyfin lost it", "Kept in the app", "Writing…". The two the owner has to act on (Retry,
 * Write again) take the warning tone; the others are plain facts. `error` — the owner's sentence of why
 * the library doesn't hold it — is its tooltip; the dialog shows it as a line too, as a phone shows no
 * tooltip.
 */
export function LinkStateBadge({ state, error = null }: { state: LibraryLinkWriteState; error?: string | null }) {
  const { t } = useTranslation('libraryLinks')
  const Icon = STATE_ICONS[state]
  const warning = state === 'NotWritten' || state === 'Lost'

  return (
    <span className={warning ? 'ui-badge library-badge-warning' : 'ui-badge ui-badge-outline'} title={error ?? undefined}>
      <Icon className={state === 'Writing' ? 'h-3 w-3 animate-spin' : 'h-3 w-3'} aria-hidden="true" />
      {t(`writeState.${state}`)}
    </span>
  )
}
