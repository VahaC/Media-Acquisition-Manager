import { ExternalLink } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { DisabledReason } from '@/components/shared/DisabledReason'
import { Button } from '@/components/ui/button'

/**
 * MAM-12 — Open source: the release's page at its source, in a new tab, as an outline button. The
 * address is the public one the server kept (any session removed, ADR 0004), so opening it hands
 * the source nothing of this app's. `noreferrer` leaves the search page's address out of the request
 * and gives the new tab no handle back to this one. A listing without a page shows the button
 * disabled with the reason, rather than leaving it out.
 *
 * The label collapses to the icon where the row's stylesheet says so; `title` keeps the name.
 */
export function ReleaseSourceLink({ url, size = 'sm' }: { url: string | null; size?: 'sm' | 'default' }) {
  const { t } = useTranslation('search')
  const label = t('release.openSource')

  if (url === null) {
    return (
      <DisabledReason reason={t('release.noSourcePage')}>
        <Button type="button" variant="outline" size={size} title={label} disabled>
          <ExternalLink className="h-3.5 w-3.5" aria-hidden="true" />
          <span className="label-text">{label}</span>
        </Button>
      </DisabledReason>
    )
  }
  return (
    <Button asChild variant="outline" size={size}>
      <a href={url} target="_blank" rel="noreferrer" title={label}>
        <ExternalLink className="h-3.5 w-3.5" aria-hidden="true" />
        <span className="label-text">{label}</span>
      </a>
    </Button>
  )
}
