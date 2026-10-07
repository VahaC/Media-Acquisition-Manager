import { PosterBox } from '@/components/shared/PosterBox'
import { useLibraryPoster } from '@/lib/queries'
import type { MediaWorkKind } from '@/lib/types'
import { useNearViewport } from '@/lib/use-near-viewport'

interface LibraryPosterProps {
  /** The work's `posterUrl` (`/api/library/posters/{itemId}-{tag}.jpg`); null when its library holds no picture of it. */
  url: string | null
  /** The work's title, for the poster's alt text. */
  title: string
  /** Picks the placeholder: a film strip for a film, a screen for a series or an episode. */
  kind: MediaWorkKind
}

/**
 * MAM-35 — a library work's own picture, the one its library (Jellyfin) holds for it, fetched through
 * the API client with the user's token and shown as a `data:` URL, as CatalogPoster shows a catalog's.
 * Until it arrives, and when there is none or it can't be fetched or decoded, the box holds a film or
 * TV icon at the poster's size. It is asked for only once the box comes within about a screen of the
 * window, so a page of cards fetches the pictures the person scrolls towards.
 *
 * Without a URL there is nothing to ask for, so no query is made at all: the box alone, which needs no
 * query client (the Library card's own tests render it without one).
 */
export function LibraryPoster({ url, title, kind }: LibraryPosterProps) {
  if (url === null) return <PosterBox title={title} kind={kind} />
  return <FetchedLibraryPoster url={url} title={title} kind={kind} />
}

// ── the fetched picture ──

/** A poster with a URL: hooks can't be skipped by a condition, so the query lives in a component of its own. */
function FetchedLibraryPoster({ url, title, kind }: LibraryPosterProps & { url: string }) {
  const [box, near] = useNearViewport<HTMLDivElement>()
  const poster = useLibraryPoster(url, near)

  return <PosterBox ref={box} source={poster.data} title={title} kind={kind} />
}
