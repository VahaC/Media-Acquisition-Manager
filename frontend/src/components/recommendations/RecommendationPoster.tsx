import { useState } from 'react'
import { Film, Tv } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { usePoster } from '@/lib/queries'
import type { CatalogWorkKind } from '@/lib/types'
import { useNearViewport } from '@/lib/use-near-viewport'
import '@/styles/recommendations.css' // .recommendation-poster — the 2:3 box and its placeholder

interface RecommendationPosterProps {
  /** The list's `posterUrl`; null when the catalog has no poster. */
  url: string | null
  /** The work's title, for the poster's alt text. */
  title: string
  /** Picks the placeholder: a film strip for a film, a screen for a show. */
  kind: CatalogWorkKind
}

/**
 * MAM-33 — a recommended work's poster, fetched through the API client with the user's token and
 * shown as a `data:` URL (usePoster says why). Until it arrives, and when there is none or it can't
 * be fetched or decoded, the box holds a film or TV icon instead, at the poster's size, so the card
 * never changes shape.
 *
 * It is asked for only once the box comes within about a screen of the window, so a page of cards
 * fetches the posters the person scrolls towards rather than every one at once. `loading="lazy"`
 * can't do that here: the request is the API client's, and a `data:` URL has nothing left to load.
 */
export function RecommendationPoster({ url, title, kind }: RecommendationPosterProps) {
  const { t } = useTranslation('recommendations')
  const [box, near] = useNearViewport<HTMLDivElement>()
  const poster = usePoster(url, near)
  // A fetched file the browser can't decode (a truncated download the disk cache kept) shows the
  // placeholder too, rather than a broken-image icon.
  const [brokenSource, setBrokenSource] = useState<string | null>(null)
  const source = poster.data !== undefined && poster.data !== brokenSource ? poster.data : null
  const Placeholder = kind === 'Series' ? Tv : Film

  return (
    <div ref={box} className="recommendation-poster">
      {source !== null ? (
        <img src={source} alt={t('card.posterAlt', { title })} onError={() => setBrokenSource(source)} />
      ) : (
        <Placeholder className="h-6 w-6" aria-hidden="true" />
      )}
    </div>
  )
}
