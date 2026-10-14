import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { profileApi } from '@/lib/profile-api'
import { recommendationsApi } from '@/lib/recommendations-api'
import type { Recommendation } from '@/lib/types'
import { RecommendationsPage } from '@/pages/RecommendationsPage'
import { apiError } from '@/test/api-error'
import { owner, signInAs } from '@/test/auth-session'
import { allTabsOn, covenant, guardians, listOf, runOf } from '@/test/recommendations'

vi.mock('@/lib/recommendations-api')
vi.mock('@/lib/profile-api')

// A recommendation's Description: what the work is about, in a dialog, asked for when it opens.

const OVERVIEW = 'The beloved band of misfits are settling into life on Knowhere.'

/** The card titled `title`, once the list has arrived. */
const cardOnScreen = (title: string) => screen.findByRole('article', { name: title })

function renderPage(items: Recommendation[] = [guardians, covenant]) {
  signInAs(owner)
  vi.mocked(recommendationsApi.list).mockImplementation(async (query) => listOf(query, items))
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={['/recommendations']}>
        <Routes>
          <Route path="/recommendations" element={<RecommendationsPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
  return userEvent.setup()
}

/** Opens the Description of the card titled `title`, and returns the dialog. */
async function openDescription(user: ReturnType<typeof userEvent.setup>, title: string) {
  await user.click(within(await cardOnScreen(title)).getByRole('button', { name: 'Description' }))
  return screen.findByRole('dialog', { name: title })
}

describe('RecommendationsPage description', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    localStorage.clear()
    vi.mocked(profileApi.recommendationSettings).mockResolvedValue(allTabsOn)
    vi.mocked(recommendationsApi.latestRun).mockResolvedValue(runOf())
    vi.mocked(recommendationsApi.poster).mockRejectedValue(apiError(404))
  })

  it('asks for no description until one is opened', async () => {
    // Arrange
    renderPage()

    // Act
    await cardOnScreen(guardians.title)

    // Assert
    expect(recommendationsApi.overview).not.toHaveBeenCalled()
  })

  it('opens what the work is about under its title, original title and year, asked for in the UI\'s language', async () => {
    // Arrange
    vi.mocked(recommendationsApi.overview).mockResolvedValue({ overview: OVERVIEW, language: 'en' })
    const user = renderPage()

    // Act
    const dialog = await openDescription(user, guardians.title)

    // Assert
    expect(recommendationsApi.overview).toHaveBeenCalledExactlyOnceWith('Movie', '447365', 'en')
    expect(within(dialog).getByText('Guardians of the Galaxy Vol. 3 · 2023')).toBeInTheDocument()
    // Marked with the language it is written in, for a screen reader.
    expect(await within(dialog).findByText(OVERVIEW)).toHaveAttribute('lang', 'en')
  })

  it('says when the catalog has no description of the work', async () => {
    // Arrange
    vi.mocked(recommendationsApi.overview).mockResolvedValue({ overview: null, language: null })
    const user = renderPage()

    // Act
    const dialog = await openDescription(user, guardians.title)

    // Assert
    expect(await within(dialog).findByText('The metadata catalog has no description of it.')).toBeInTheDocument()
  })

  it('says why the description could not be loaded, and asks again from Try again', async () => {
    // Arrange
    vi.mocked(recommendationsApi.overview)
      .mockRejectedValueOnce(apiError(502, 'The metadata catalog couldn\'t send this description.'))
      .mockResolvedValueOnce({ overview: OVERVIEW, language: 'en' })
    const user = renderPage()
    const dialog = await openDescription(user, guardians.title)
    const alert = await within(dialog).findByRole('alert')

    // Act
    await user.click(within(alert).getByRole('button', { name: 'Try again' }))

    // Assert
    expect(alert).toHaveTextContent('Could not load the description.')
    expect(alert).toHaveTextContent('The metadata catalog couldn\'t send this description.')
    expect(await within(dialog).findByText(OVERVIEW)).toBeInTheDocument()
    expect(recommendationsApi.overview).toHaveBeenCalledTimes(2)
  })

  it('offers no Try again when another try can\'t help', async () => {
    // Arrange
    // A 400 is the server's lasting answer to the same request.
    vi.mocked(recommendationsApi.overview).mockRejectedValue(apiError(400, '“english” is not a language code.'))
    const user = renderPage()

    // Act
    const dialog = await openDescription(user, guardians.title)

    // Assert
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('“english” is not a language code.')
    expect(within(dialog).queryByRole('button', { name: 'Try again' })).not.toBeInTheDocument()
  })

  it('closes on Close, back on the card\'s Description, and reads the kept answer when opened again', async () => {
    // Arrange
    vi.mocked(recommendationsApi.overview).mockResolvedValue({ overview: OVERVIEW, language: 'en' })
    const user = renderPage()
    const dialog = await openDescription(user, guardians.title)
    await within(dialog).findByText(OVERVIEW)

    // Act
    // The footer's Close, not the corner's ×.
    await user.click(within(dialog.querySelector<HTMLElement>('.ui-dialog-footer')!).getByRole('button', { name: 'Close' }))
    const closed = screen.queryByRole('dialog')
    const focusAfterClose = document.activeElement
    const description = within(screen.getByRole('article', { name: guardians.title })).getByRole('button', { name: 'Description' })
    const again = await openDescription(user, guardians.title)

    // Assert
    expect(closed).not.toBeInTheDocument()
    expect(within(again).getByText(OVERVIEW)).toBeInTheDocument()
    expect(focusAfterClose).toBe(description)
    expect(recommendationsApi.overview).toHaveBeenCalledOnce()
  })

  it('names a work without an original title or year by what the dialog shows', async () => {
    // Arrange
    vi.mocked(recommendationsApi.overview).mockResolvedValue({ overview: OVERVIEW, language: 'en' })
    const user = renderPage([{ ...guardians, originalTitle: guardians.title, year: null }])

    // Act
    const dialog = await openDescription(user, guardians.title)

    // Assert
    expect(within(dialog).getByText('What it is about, from the metadata catalog.')).toBeInTheDocument()
  })
})
