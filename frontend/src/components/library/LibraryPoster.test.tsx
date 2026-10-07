import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen } from '@testing-library/react'
import { LibraryPoster } from '@/components/library/LibraryPoster'
import { libraryApi } from '@/lib/library-api'
import { qk } from '@/lib/queries'
import type { MediaWorkKind } from '@/lib/types'
import { apiError } from '@/test/api-error'
import { stubIntersectionObserver } from '@/test/intersection-observer'
import { aliensPosterUrl } from '@/test/library'

vi.mock('@/lib/library-api')

const jpeg = `data:image/jpeg;base64,${btoa('jpeg bytes')}`

const client = () => new QueryClient({ defaultOptions: { queries: { retry: false } } })

function renderPoster(url: string | null = aliensPosterUrl, kind: MediaWorkKind = 'Movie', queryClient = client()) {
  return render(
    <QueryClientProvider client={queryClient}>
      <LibraryPoster url={url} title="Чужі" kind={kind} />
    </QueryClientProvider>,
  )
}

/** Waits until the poster's query has failed, so a placeholder seen after it is the final one. */
const posterFailed = (queryClient: QueryClient) =>
  vi.waitFor(() => expect(queryClient.getQueryState(qk.library.poster(aliensPosterUrl))?.status).toBe('error'))

// Unless a test stubs one, there is no IntersectionObserver here (jsdom has none), and every poster
// is asked for at once, as in a browser without one.
describe('LibraryPoster', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    vi.stubGlobal('IntersectionObserver', undefined)
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('asks for the work\'s poster at the URL the works gave, and shows it as a data: URL named by the title', async () => {
    // Arrange
    vi.mocked(libraryApi.poster).mockResolvedValue(new Blob(['jpeg bytes'], { type: 'image/jpeg' }))

    // Act
    renderPoster()

    // Assert
    const image = await screen.findByRole('img', { name: 'Poster of Чужі' })
    expect(image).toHaveAttribute('src', jpeg)
    expect(image.parentElement).toHaveClass('poster-box')
    expect(libraryApi.poster).toHaveBeenCalledExactlyOnceWith(aliensPosterUrl)
  })

  it('shows a film placeholder while the poster is on its way', () => {
    // Arrange
    vi.mocked(libraryApi.poster).mockReturnValue(new Promise<Blob>(() => {}))

    // Act
    const { container } = renderPoster()

    // Assert
    expect(screen.queryByRole('img')).not.toBeInTheDocument()
    expect(container.querySelector('.poster-box .lucide-film')).toBeInTheDocument()
  })

  it.each([
    ['the library holds no such image (404)', apiError(404)],
    ['the library could not send it (502)', apiError(502, 'The media library couldn\'t send this poster.')],
  ])('keeps the placeholder when %s, and asks only once', async (_, failure) => {
    // Arrange
    vi.mocked(libraryApi.poster).mockRejectedValue(failure)
    // MAM-35 — the app's client retries once (lib/query-client.ts); only the hook's own retry: false
    // keeps this to one ask.
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: 1 } } })

    // Act
    const { container } = renderPoster(aliensPosterUrl, 'Movie', queryClient)

    // Assert
    await posterFailed(queryClient)
    expect(screen.queryByRole('img')).not.toBeInTheDocument()
    expect(container.querySelector('.lucide-film')).toBeInTheDocument()
    expect(libraryApi.poster).toHaveBeenCalledTimes(1)
  })

  it('keeps the placeholder for an answer that is not an image', async () => {
    // Arrange
    vi.mocked(libraryApi.poster).mockResolvedValue(new Blob(['"Follow the Mouse does not have an image of type Primary"'], { type: 'application/json' }))
    const queryClient = client()

    // Act
    const { container } = renderPoster(aliensPosterUrl, 'Movie', queryClient)

    // Assert
    await posterFailed(queryClient)
    expect(screen.queryByRole('img')).not.toBeInTheDocument()
    expect(container.querySelector('.lucide-film')).toBeInTheDocument()
  })

  it('goes back to the placeholder when the browser cannot decode the poster', async () => {
    // Arrange
    vi.mocked(libraryApi.poster).mockResolvedValue(new Blob(['truncated'], { type: 'image/jpeg' }))
    const { container } = renderPoster()
    const image = await screen.findByRole('img', { name: 'Poster of Чужі' })

    // Act
    fireEvent.error(image)

    // Assert
    expect(screen.queryByRole('img')).not.toBeInTheDocument()
    expect(container.querySelector('.lucide-film')).toBeInTheDocument()
  })

  it.each<[MediaWorkKind, string]>([
    ['Movie', 'lucide-film'],
    ['Series', 'lucide-tv'],
    ['Episode', 'lucide-tv'],
  ])('shows the placeholder of a %s without a poster, asking for nothing and needing no query client', (kind, icon) => {
    // Arrange
    const url = null

    // Act
    const { container } = render(<LibraryPoster url={url} title="Чужі" kind={kind} />)

    // Assert
    expect(container.querySelector(`.poster-box .${icon}`)).toBeInTheDocument()
    expect(screen.queryByRole('img')).not.toBeInTheDocument()
    expect(libraryApi.poster).not.toHaveBeenCalled()
  })

  it('is not asked for again when the works are refreshed (a sync, an inspection, a link change)', async () => {
    // Arrange
    vi.mocked(libraryApi.poster).mockResolvedValue(new Blob(['jpeg bytes'], { type: 'image/jpeg' }))
    const queryClient = client()
    renderPoster(aliensPosterUrl, 'Movie', queryClient)
    await screen.findByRole('img', { name: 'Poster of Чужі' })

    // Act
    await queryClient.invalidateQueries({ queryKey: qk.library.worksAll })
    await queryClient.invalidateQueries({ queryKey: qk.library.links.all })

    // Assert
    expect(screen.getByRole('img', { name: 'Poster of Чужі' })).toHaveAttribute('src', jpeg)
    expect(libraryApi.poster).toHaveBeenCalledTimes(1)
  })

  describe('as the person scrolls', () => {
    it('asks for nothing while its box is far from the window, and shows the placeholder', () => {
      // Arrange
      const observer = stubIntersectionObserver()

      // Act
      const { container } = renderPoster()

      // Assert
      const box = container.querySelector('.poster-box')
      expect(libraryApi.poster).not.toHaveBeenCalled()
      expect(screen.queryByRole('img')).not.toBeInTheDocument()
      expect(container.querySelector('.lucide-film')).toBeInTheDocument()
      expect(observer.watched()).toEqual([box])
      expect(observer.options()).toEqual([{ rootMargin: '100% 0px' }])
    })

    it('fetches the poster once its box comes within a screen of the window, and stops watching it', async () => {
      // Arrange
      vi.mocked(libraryApi.poster).mockResolvedValue(new Blob(['jpeg bytes'], { type: 'image/jpeg' }))
      const observer = stubIntersectionObserver()
      const { container } = renderPoster()

      // Act
      observer.scrollNear(container.querySelector('.poster-box')!)

      // Assert
      const image = await screen.findByRole('img', { name: 'Poster of Чужі' })
      expect(image).toHaveAttribute('src', jpeg)
      expect(image).not.toHaveAttribute('loading')
      expect(libraryApi.poster).toHaveBeenCalledExactlyOnceWith(aliensPosterUrl)
      expect(observer.watched()).toEqual([])
    })

    it('shows a poster already fetched at once, wherever its box is', async () => {
      // Arrange
      stubIntersectionObserver()
      const queryClient = client()
      queryClient.setQueryData(qk.library.poster(aliensPosterUrl), jpeg)

      // Act
      renderPoster(aliensPosterUrl, 'Movie', queryClient)

      // Assert
      expect(await screen.findByRole('img', { name: 'Poster of Чужі' })).toHaveAttribute('src', jpeg)
      expect(libraryApi.poster).not.toHaveBeenCalled()
    })
  })
})
