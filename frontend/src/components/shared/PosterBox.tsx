import { useState, type Ref } from 'react'
import { Film, Tv } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import type { MediaWorkKind } from '@/lib/types'
import '@/styles/poster-box.css' // .poster-box — the 2:3 box and its placeholder

interface PosterBoxProps {
  /** The poster as a `data:` URL; none while it is on its way, and when there is none or it couldn't be fetched. */
  source?: string | null
  /** The work's title, for the poster's alt text. */
  title: string
  /** Picks the placeholder: a film strip for a film, a screen for a show or an episode. */
  kind: MediaWorkKind
  /** On the box itself: the element a caller watches to fetch the poster once it comes near the window. */
  ref?: Ref<HTMLDivElement>
}

/**
 * MAM-35 — the box a poster is shown in, at a 2:3 shape, with a film or TV icon in its place while it
 * is on its way and when there is none, so a card never changes shape. Taken out of CatalogPoster
 * (MAM-33/MAM-34) when the Library's cards showed their library's own posters (LibraryPoster): each
 * fetches its picture its own way, and both show it here.
 */
export function PosterBox({ source = null, title, kind, ref }: PosterBoxProps) {
  const { t } = useTranslation()
  // A fetched file the browser can't decode (a truncated download the disk cache kept) shows the
  // placeholder too, rather than a broken-image icon.
  const [brokenSource, setBrokenSource] = useState<string | null>(null)
  const shown = source !== null && source !== brokenSource ? source : null
  const Placeholder = kind === 'Movie' ? Film : Tv

  return (
    <div ref={ref} className="poster-box">
      {shown !== null ? (
        <img src={shown} alt={t('posterAlt', { title })} onError={() => setBrokenSource(shown)} />
      ) : (
        <Placeholder className="h-6 w-6" aria-hidden="true" />
      )}
    </div>
  )
}
