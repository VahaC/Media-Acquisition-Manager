import { CircleCheck, Clock, Link2, Loader2, TriangleAlert, type LucideIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { linkBadgeOf, type LinkBadgeState } from '@/lib/library-links'
import type { LibraryLinkWriteState } from '@/lib/types'
import '@/styles/library.css' // .library-badge-warning — the amber of a link Jellyfin didn't identify

/** The icon of each badge; an identification under way spins. */
const STATE_ICONS: Record<LinkBadgeState, LucideIcon> = {
  KeptInApp: Link2,
  Queued: Clock,
  Writing: Loader2,
  NotWritten: TriangleAlert,
  Written: CircleCheck,
  InLibrary: CircleCheck,
  Lost: TriangleAlert,
  Identified: CircleCheck,
  NotIdentified: TriangleAlert,
}

/**
 * MAM-34 — where a TMDb link stands against Jellyfin, as a badge: "Identified in Jellyfin", "Not
 * identified yet", "Jellyfin lost it", "Kept in the app", "Waiting to identify", "Identifying…". The
 * three the owner has to act on (Retry, Identify again, Identify in Jellyfin) take the warning tone; the
 * others are plain facts.
 *
 * `identified` is whether the app identified the item (`identifiedUtc !== null` on an item or a row,
 * `identified` on a Library card): `Written` and `InLibrary` only say Jellyfin holds the link's ids, and
 * a link set under the first rules put the ids and nothing else, so the item still shows the title,
 * overview and images Jellyfin took from the file name. With it the badge never says a neutral "In
 * Jellyfin" for such a link; without it (undefined) the state itself is shown.
 *
 * `error` — the owner's sentence of why Jellyfin didn't identify it — is its tooltip; the dialog and the
 * lists show it as a line too, as a phone shows no tooltip.
 */
export function LinkStateBadge({ state, identified, error = null }: {
  state: LibraryLinkWriteState
  identified?: boolean
  error?: string | null
}) {
  const { t } = useTranslation('libraryLinks')
  const shown = linkBadgeOf(state, identified)
  const Icon = STATE_ICONS[shown]
  const warning = shown === 'NotWritten' || shown === 'Lost' || shown === 'NotIdentified'

  // role="status": the badge turns over by itself while a poll brings the end of an identification
  // ("Waiting to identify", "Identifying…", "Identified in Jellyfin"), and a screen reader hears each turn
  // without the focus moving. Nothing is read out for a badge that is there as the page loads.
  return (
    <span role="status">
      <span className={warning ? 'ui-badge library-badge-warning' : 'ui-badge ui-badge-outline'} title={error ?? undefined}>
        <Icon className={shown === 'Writing' ? 'h-3 w-3 animate-spin' : 'h-3 w-3'} aria-hidden="true" />
        {t(`writeState.${shown}`)}
      </span>
    </span>
  )
}
