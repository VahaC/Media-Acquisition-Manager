import type { ReactNode } from 'react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { LibraryWorkCard } from '@/components/library/LibraryWorkCard'
import { libraryApi } from '@/lib/library-api'
import type { LibraryWork } from '@/lib/types'
import {
  aliens,
  aliensPosterUrl,
  daybreakPosterUrl,
  tomAndJerryKids,
  tomAndJerryKidsPosterUrl,
  unknownWork,
  worksPage,
  yellowstone,
  yellowstoneEpisodeOf,
  yellowstonePosterUrl,
} from '@/test/library'

vi.mock('@/lib/library-api')

// MAM-35 — the card's poster, which fetches through React Query: these cards render inside a query
// client, unlike the other card tests, whose works carry no posterUrl.
function renderCard(work: LibraryWork, { headingLevel, evidence }: { headingLevel?: 2 | 3; evidence?: ReactNode } = {}) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <LibraryWorkCard work={work} headingLevel={headingLevel} evidence={evidence} />
    </QueryClientProvider>,
  )
  return { card: screen.getByRole('article'), user: userEvent.setup() }
}

/** The block the poster shares with the title: the poster's box, then the title block. */
const leadOf = (card: HTMLElement) => card.querySelector<HTMLElement>('.library-work-lead')!

describe('LibraryWorkCard poster', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    vi.mocked(libraryApi.poster).mockResolvedValue(new Blob(['jpeg bytes'], { type: 'image/jpeg' }))
  })

  it('shows the work\'s own poster beside its title block, the badges and versions outside them', async () => {
    // Arrange
    const work: LibraryWork = { ...aliens, posterUrl: aliensPosterUrl }

    // Act
    const { card } = renderCard(work)

    // Assert
    const image = await within(card).findByRole('img', { name: 'Poster of Чужі' })
    expect(image).toHaveAttribute('src', `data:image/jpeg;base64,${btoa('jpeg bytes')}`)
    const lead = leadOf(card)
    expect(lead.parentElement).toHaveClass('library-work-header')
    expect([...lead.children].map((child) => child.className)).toEqual(['poster-box', 'library-work-heading'])
    expect(lead).toContainElement(image)
    expect(within(lead).getByRole('heading', { level: 2, name: 'Чужі' })).toBeInTheDocument()
    expect(within(lead).queryByRole('list', { name: 'Identifiers' })).not.toBeInTheDocument()
    expect(within(lead).queryByRole('list', { name: 'Versions' })).not.toBeInTheDocument()
    expect(within(card).getByRole('list', { name: 'Identifiers' })).toBeInTheDocument()
    expect(within(card).getByRole('list', { name: 'Versions' })).toBeInTheDocument()
    expect(libraryApi.poster).toHaveBeenCalledExactlyOnceWith(aliensPosterUrl)
  })

  it('shows a film placeholder beside the title and asks for nothing when the library holds no picture of the work', () => {
    // Arrange
    const work = aliens

    // Act
    const { card } = renderCard(work)

    // Assert
    expect(leadOf(card).querySelector('.poster-box .lucide-film')).toBeInTheDocument()
    expect(within(card).queryByRole('img')).not.toBeInTheDocument()
    expect(libraryApi.poster).not.toHaveBeenCalled()
  })

  it('names the poster of a work without a title by the card\'s own words for it', async () => {
    // Arrange
    const work: LibraryWork = { ...unknownWork, posterUrl: aliensPosterUrl }

    // Act
    const { card } = renderCard(work)

    // Assert
    expect(await within(card).findByRole('img', { name: 'Poster of Unknown title' })).toBeInTheDocument()
    expect(within(card).getByRole('heading', { level: 2, name: 'Unknown title' })).toBeInTheDocument()
  })

  it('shows the poster on the Search page\'s card too, beside the title and why it matched', async () => {
    // Arrange
    const work: LibraryWork = { ...aliens, posterUrl: aliensPosterUrl }

    // Act
    const { card } = renderCard(work, { headingLevel: 3, evidence: <p>Matched by IMDb tt0090605</p> })

    // Assert
    const lead = leadOf(card)
    expect(await within(lead).findByRole('img', { name: 'Poster of Чужі' })).toBeInTheDocument()
    expect(within(lead).getByRole('heading', { level: 3, name: 'Чужі' })).toBeInTheDocument()
    expect(within(lead).getByText('Matched by IMDb tt0090605')).toBeInTheDocument()
  })

  it('shows the series\' poster, and none on the episodes of an opened season', async () => {
    // Arrange
    const series: LibraryWork = { ...yellowstone, posterUrl: yellowstonePosterUrl }
    vi.mocked(libraryApi.episodes).mockImplementation(async (_, query) =>
      worksPage(query, [yellowstoneEpisodeOf({ posterUrl: daybreakPosterUrl })]))
    const { card, user } = renderCard(series)
    await within(leadOf(card)).findByRole('img', { name: 'Poster of Єллоустоун' })

    // Act
    await user.click(within(card).getByRole('button', { name: /^Season 1/ }))

    // Assert
    const episodes = await within(card).findByRole('list', { name: 'Episodes of Season 1' })
    expect(within(episodes).getByRole('article')).toHaveAccessibleName('1. Daybreak')
    expect(within(episodes).queryByRole('img')).not.toBeInTheDocument()
    expect(episodes.querySelector('.poster-box')).not.toBeInTheDocument()
    expect(within(leadOf(card)).getByRole('img', { name: 'Poster of Єллоустоун' })).toBeInTheDocument()
    expect(libraryApi.poster).toHaveBeenCalledExactlyOnceWith(yellowstonePosterUrl)
  })

  it('shows the own poster of an episode of no series', async () => {
    // Arrange
    const work: LibraryWork = { ...tomAndJerryKids, posterUrl: tomAndJerryKidsPosterUrl }

    // Act
    const { card } = renderCard(work)

    // Assert
    expect(await within(leadOf(card)).findByRole('img', { name: 'Poster of Tom & Jerry Kids' })).toBeInTheDocument()
    expect(libraryApi.poster).toHaveBeenCalledExactlyOnceWith(tomAndJerryKidsPosterUrl)
  })
})
