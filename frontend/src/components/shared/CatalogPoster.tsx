import { PosterBox } from '@/components/shared/PosterBox'
import { usePoster } from '@/lib/queries'
import type { CatalogWorkKind } from '@/lib/types'
import { useNearViewport } from '@/lib/use-near-viewport'

interface CatalogPosterProps {
  /** The API's `posterUrl` (`/api/recommendations/posters/{file}`); null when the catalog has no poster. */
  url: string | null
  /** The work's title, for the poster's alt text. */
  title: string
  /** Picks the placeholder: a film strip for a film, a screen for a show. */
  kind: CatalogWorkKind
}

/**
 * MAM-33 — a catalog film's or show's poster, fetched through the API client with the user's token and
 * shown as a `data:` URL (usePoster says why). Until it arrives, and when there is none or it can't
 * be fetched or decoded, the box holds a film or TV icon instead, at the poster's size, so the card
 * never changes shape.
 *
 * It is asked for only once the box comes within about a screen of the window, so a page of cards
 * fetches the posters the person scrolls towards rather than every one at once. `loading="lazy"`
 * can't do that here: the request is the API client's, and a `data:` URL has nothing left to load.
 *
 * MAM-34 — moved here from the recommendations (it was RecommendationPoster) for the TMDb dialog's
 * suggestions, whose posters the app serves under the same path. A surface sizes it through its own
 * sheet (a smaller box beside a suggestion).
 *
 * MAM-35 — the box itself is PosterBox, shared with the Library's own posters (LibraryPoster).
 */
export function CatalogPoster({ url, title, kind }: CatalogPosterProps) {
  const [box, near] = useNearViewport<HTMLDivElement>()
  const poster = usePoster(url, near)

  return <PosterBox ref={box} source={poster.data} title={title} kind={kind} />
}
