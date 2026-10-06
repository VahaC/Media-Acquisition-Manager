import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen } from '@testing-library/react'
import { CatalogPoster } from '@/components/shared/CatalogPoster'
import { qk } from '@/lib/queries'
import { recommendationsApi } from '@/lib/recommendations-api'
import type { CatalogWorkKind } from '@/lib/types'
import { apiError } from '@/test/api-error'
import { stubIntersectionObserver } from '@/test/intersection-observer'
import { guardians } from '@/test/recommendations'

vi.mock('@/lib/recommendations-api')

const posterUrl = guardians.posterUrl

const client = () => new QueryClient({ defaultOptions: { queries: { retry: false } } })

function renderPoster(url: string | null = posterUrl, kind: CatalogWorkKind = 'Movie', queryClient = client()) {
  return render(
    <QueryClientProvider client={queryClient}>
      <CatalogPoster url={url} title="Вартові Галактики 3" kind={kind} />
    </QueryClientProvider>,
  )
}

/** Waits until the poster's query has failed, so a placeholder seen after it is the final one. */
const posterFailed = (queryClient: QueryClient) =>
  vi.waitFor(() => expect(queryClient.getQueryState(qk.recommendations.poster(posterUrl!))?.status).toBe('error'))

// Unless a test stubs one, there is no IntersectionObserver here (jsdom has none), and every poster
// is asked for at once, as in a browser without one.
describe('CatalogPoster', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    vi.stubGlobal('IntersectionObserver', undefined)
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('asks for the poster at once without an IntersectionObserver, and shows it as a data: URL named by the title', async () => {
    // Arrange
    vi.mocked(recommendationsApi.poster).mockResolvedValue(new Blob(['jpeg bytes'], { type: 'image/jpeg' }))

    // Act
    renderPoster()

    // Assert
    const image = await screen.findByRole('img', { name: 'Poster of Вартові Галактики 3' })
    expect(image).toHaveAttribute('src', `data:image/jpeg;base64,${btoa('jpeg bytes')}`)
    expect(recommendationsApi.poster).toHaveBeenCalledExactlyOnceWith(posterUrl)
  })

  it('shows a film placeholder while the poster is on its way', () => {
    // Arrange
    vi.mocked(recommendationsApi.poster).mockReturnValue(new Promise<Blob>(() => {}))

    // Act
    const { container } = renderPoster()

    // Assert
    expect(screen.queryByRole('img')).not.toBeInTheDocument()
    expect(container.querySelector('.lucide-film')).toBeInTheDocument()
  })

  it('keeps the placeholder when the poster cannot be fetched', async () => {
    // Arrange
    vi.mocked(recommendationsApi.poster).mockRejectedValue(apiError(502, 'TMDb could not be reached.'))
    const queryClient = client()

    // Act
    const { container } = renderPoster(posterUrl, 'Movie', queryClient)

    // Assert
    await posterFailed(queryClient)
    expect(screen.queryByRole('img')).not.toBeInTheDocument()
    expect(container.querySelector('.lucide-film')).toBeInTheDocument()
  })

  it('keeps the placeholder for an answer that is not an image', async () => {
    // Arrange
    vi.mocked(recommendationsApi.poster).mockResolvedValue(new Blob(['<html>'], { type: 'text/html' }))
    const queryClient = client()

    // Act
    const { container } = renderPoster(posterUrl, 'Movie', queryClient)

    // Assert
    await posterFailed(queryClient)
    expect(screen.queryByRole('img')).not.toBeInTheDocument()
    expect(container.querySelector('.lucide-film')).toBeInTheDocument()
  })

  it('goes back to the placeholder when the browser cannot decode the poster', async () => {
    // Arrange
    vi.mocked(recommendationsApi.poster).mockResolvedValue(new Blob(['truncated'], { type: 'image/jpeg' }))
    const { container } = renderPoster()
    const image = await screen.findByRole('img', { name: 'Poster of Вартові Галактики 3' })

    // Act
    fireEvent.error(image)

    // Assert
    expect(screen.queryByRole('img')).not.toBeInTheDocument()
    expect(container.querySelector('.lucide-film')).toBeInTheDocument()
  })

  it('asks for nothing without a poster, and shows a show\'s placeholder for a series', () => {
    // Arrange
    const url = null

    // Act
    const { container } = renderPoster(url, 'Series')

    // Assert
    expect(recommendationsApi.poster).not.toHaveBeenCalled()
    expect(container.querySelector('.lucide-tv')).toBeInTheDocument()
  })

  describe('as the person scrolls', () => {
    it('asks for nothing while its box is far from the window, and shows the placeholder', () => {
      // Arrange
      const observer = stubIntersectionObserver()

      // Act
      const { container } = renderPoster()

      // Assert
      const box = container.querySelector('.catalog-poster')
      expect(recommendationsApi.poster).not.toHaveBeenCalled()
      expect(screen.queryByRole('img')).not.toBeInTheDocument()
      expect(container.querySelector('.lucide-film')).toBeInTheDocument()
      expect(observer.watched()).toEqual([box])
      expect(observer.options()).toEqual([{ rootMargin: '100% 0px' }])
    })

    it('fetches the poster once its box comes within a screen of the window, and stops watching it', async () => {
      // Arrange
      vi.mocked(recommendationsApi.poster).mockResolvedValue(new Blob(['jpeg bytes'], { type: 'image/jpeg' }))
      const observer = stubIntersectionObserver()
      const { container } = renderPoster()

      // Act
      observer.scrollNear(container.querySelector('.catalog-poster')!)

      // Assert
      const image = await screen.findByRole('img', { name: 'Poster of Вартові Галактики 3' })
      expect(image).toHaveAttribute('src', `data:image/jpeg;base64,${btoa('jpeg bytes')}`)
      expect(image).not.toHaveAttribute('loading')
      expect(recommendationsApi.poster).toHaveBeenCalledExactlyOnceWith(posterUrl)
      expect(observer.watched()).toEqual([])
    })

    it('shows a poster already fetched at once, wherever its box is', async () => {
      // Arrange
      stubIntersectionObserver()
      const queryClient = client()
      queryClient.setQueryData(qk.recommendations.poster(posterUrl!), `data:image/jpeg;base64,${btoa('jpeg bytes')}`)

      // Act
      renderPoster(posterUrl, 'Movie', queryClient)

      // Assert
      expect(await screen.findByRole('img', { name: 'Poster of Вартові Галактики 3' }))
        .toHaveAttribute('src', `data:image/jpeg;base64,${btoa('jpeg bytes')}`)
      expect(recommendationsApi.poster).not.toHaveBeenCalled()
    })
  })
})
