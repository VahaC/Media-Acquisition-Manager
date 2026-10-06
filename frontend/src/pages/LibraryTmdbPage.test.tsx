import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { ConfirmProvider } from '@/components/shared/ConfirmDialog'
import { formatDateTime } from '@/lib/format'
import { libraryLinksApi } from '@/lib/library-links-api'
import type { CurrentUser } from '@/lib/types'
import { LibraryTmdbPage } from '@/pages/LibraryTmdbPage'
import { apiError } from '@/test/api-error'
import { member, owner, signInAs } from '@/test/auth-session'
import { catchUpOf, confidentPage, confidentStripling, dialogOf, linksPage, queueOf, suggestionRunOf } from '@/test/library-links'

vi.mock('@/lib/library-links-api')
vi.mock('@/lib/recommendations-api')

// MAM-34 — the TMDb page around its tabs: the way back to the Library's "Missing an id" list, how the
// suggestions were last prepared with Prepare again, the four tabs with the current one in the
// address, and a member's sentence instead of all of it.

/** The address the page is at, so a test can read what the tabs put there. */
function Address() {
  const location = useLocation()
  return <output aria-label="Address">{location.pathname + location.search}</output>
}

function renderPage(path = '/library/tmdb', signedIn: CurrentUser = owner) {
  signInAs(signedIn)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <ConfirmProvider>
        <MemoryRouter initialEntries={[path]}>
          <Routes>
            <Route path="/library/tmdb" element={<LibraryTmdbPage />} />
            <Route path="/library" element={<p>Library page</p>} />
          </Routes>
          <Address />
        </MemoryRouter>
      </ConfirmProvider>
    </QueryClientProvider>,
  )
  return { client, user: userEvent.setup() }
}

const address = () => screen.getByRole('status', { name: 'Address' }).textContent

/** When the default run finished, as the line shows it. */
const finished = formatDateTime('2026-10-05T08:01:00Z', { dateStyle: 'medium', timeStyle: 'short' })

/** Lets the polling interval pass once, with the answers it brings. */
const nextPoll = () => act(() => vi.advanceTimersByTimeAsync(2_000))

describe('LibraryTmdbPage', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    localStorage.clear()
    vi.mocked(libraryLinksApi.latestRun).mockResolvedValue(suggestionRunOf())
    vi.mocked(libraryLinksApi.queue).mockImplementation(async (index) => queueOf(index, 1, dialogOf()))
    vi.mocked(libraryLinksApi.confident).mockImplementation(async (query) => confidentPage(query, [confidentStripling]))
    vi.mocked(libraryLinksApi.links).mockImplementation(async (query) => linksPage(query, []))
    vi.mocked(libraryLinksApi.catchUp).mockResolvedValue(catchUpOf())
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('shows its title, the way back to the Library\'s missing ids, the four tabs on Review, and TMDb\'s notice', async () => {
    // Arrange
    renderPage()

    // Act
    const tabs = within(screen.getByRole('tablist', { name: 'TMDb links' })).getAllByRole('tab')

    // Assert
    expect(screen.getByRole('heading', { level: 1, name: 'TMDb links' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Library' })).toHaveAttribute('href', '/library?identifiers=Missing')
    expect(tabs.map((tab) => tab.textContent)).toEqual(['Review', 'Confident', 'Conflicts', 'Unattached'])
    expect(screen.getByRole('tab', { name: 'Review' })).toHaveAttribute('aria-selected', 'true')
    expect(await screen.findByRole('heading', { level: 2, name: '120. Landing Stripling (1962)' })).toBeInTheDocument()
    expect(screen.getByRole('tabpanel', { name: 'Review' })).toBeInTheDocument()
    // Once: the Review tab's editor carries it, as the dialog does.
    expect(screen.getAllByText(/This product uses the TMDB API/)).toHaveLength(1)
  })

  it('shows TMDb\'s notice under the other tabs too', async () => {
    // Arrange
    renderPage('/library/tmdb?tab=Confident')

    // Act
    await screen.findByText('Посадка Стріплінга (1962)')

    // Assert
    expect(screen.getByText(/This product uses the TMDB API/)).toBeInTheDocument()
  })

  it('puts the tab picked in the address, and leaves Review out of it', async () => {
    // Arrange
    const { user } = renderPage()

    // Act
    await user.click(screen.getByRole('tab', { name: 'Confident' }))
    const confident = address()
    await user.click(screen.getByRole('tab', { name: 'Review' }))

    // Assert
    expect(confident).toBe('/library/tmdb?tab=Confident')
    expect(address()).toBe('/library/tmdb')
  })

  it('opens at the tab its address names, as F5 does, and at Review for one that isn\'t a tab', async () => {
    // Arrange
    renderPage('/library/tmdb?tab=Unattached')

    // Act
    const tab = screen.getByRole('tab', { name: 'Unattached' })

    // Assert
    expect(tab).toHaveAttribute('aria-selected', 'true')
    expect(await screen.findByText('Every link\'s item is still in the library.')).toBeInTheDocument()
    expect(libraryLinksApi.queue).not.toHaveBeenCalled()
  })

  it('reads a tab that isn\'t one as Review', async () => {
    // Arrange
    renderPage('/library/tmdb?tab=Everything')

    // Act
    const tab = screen.getByRole('tab', { name: 'Review' })

    // Assert
    expect(tab).toHaveAttribute('aria-selected', 'true')
    expect(await screen.findByText('Work 1 of 1')).toBeInTheDocument()
  })

  it('moves between the tabs with the arrow keys of the tab list', async () => {
    // Arrange
    const { user } = renderPage()
    screen.getByRole('tab', { name: 'Review' }).focus()

    // Act
    await user.keyboard('{ArrowLeft}')

    // Assert
    expect(screen.getByRole('tab', { name: 'Unattached' })).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByRole('tab', { name: 'Unattached' })).toHaveFocus()
    expect(address()).toBe('/library/tmdb?tab=Unattached')
  })

  it('says when the suggestions were prepared', async () => {
    // Arrange
    renderPage()

    // Act
    const line = await screen.findByText(/^Suggestions prepared/)

    // Assert
    expect(line.closest('p')).toHaveAttribute('role', 'status')
    expect(line).toHaveTextContent(`Suggestions prepared ${finished}.`)
  })

  it('says how far a preparation under way is, and asks again every 2 s until it ends', async () => {
    // Arrange
    vi.useFakeTimers({ shouldAdvanceTime: true })
    vi.mocked(libraryLinksApi.latestRun).mockResolvedValue(suggestionRunOf({ status: 'Running', finishedUtc: null, worksDone: 120 }))
    renderPage()
    expect(await screen.findByText('Preparing the suggestions: 120 of 240 works…')).toBeInTheDocument()
    vi.mocked(libraryLinksApi.latestRun).mockResolvedValue(suggestionRunOf())

    // Act
    await nextPoll()
    await screen.findByText(/^Suggestions prepared/)
    await nextPoll()

    // Assert
    expect(libraryLinksApi.latestRun).toHaveBeenCalledTimes(2)
  })

  it('asks for the queue again when a preparation it followed has ended', async () => {
    // Arrange
    vi.useFakeTimers({ shouldAdvanceTime: true })
    vi.mocked(libraryLinksApi.latestRun).mockResolvedValue(suggestionRunOf({ status: 'Running', finishedUtc: null }))
    renderPage()
    await screen.findByText('Work 1 of 1')
    vi.mocked(libraryLinksApi.latestRun).mockResolvedValue(suggestionRunOf())

    // Act
    await nextPoll()
    await screen.findByText(/^Suggestions prepared/)

    // Assert
    expect(libraryLinksApi.queue).toHaveBeenCalledTimes(2)
  })

  it('says the suggestions were never prepared before the first preparation, and offers to prepare them', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.latestRun).mockResolvedValue(null)

    // Act
    renderPage()

    // Assert
    expect(await screen.findByText('The suggestions haven\'t been prepared yet.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Prepare suggestions' })).toBeEnabled()
    expect(screen.queryByRole('button', { name: 'Prepare again' })).not.toBeInTheDocument()
  })

  it.each([
    ['waiting', suggestionRunOf({ status: 'Queued', finishedUtc: null }), 'Waiting to prepare the suggestions…'],
    ['running', suggestionRunOf({ status: 'Running', finishedUtc: null, worksDone: 48, worksTotal: 225 }), 'Preparing the suggestions: 48 of 225 works…'],
  ])('keeps Prepare again off with the reason while a preparation is %s', async (_, run, line) => {
    // Arrange
    vi.mocked(libraryLinksApi.latestRun).mockResolvedValue(run)
    const { user } = renderPage()
    await screen.findByText(line)
    const button = screen.getByRole('button', { name: 'Prepare again' })

    // Act
    await user.click(button)

    // Assert
    expect(button).toBeDisabled()
    expect(button.parentElement).toHaveAttribute('title', 'The suggestions are already being prepared.')
    expect(libraryLinksApi.startRun).not.toHaveBeenCalled()
  })

  it('shows why a preparation failed', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.latestRun).mockResolvedValue(suggestionRunOf({ status: 'Failed', error: 'TMDb refused the API key.' }))

    // Act
    renderPage()

    // Assert
    expect(await screen.findByText(`Preparing the suggestions failed ${finished}: TMDb refused the API key.`)).toBeInTheDocument()
  })

  it('prepares the suggestions again on request and shows the run it queued', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.startRun).mockResolvedValue(suggestionRunOf({ id: '5a990000-0000-4000-8000-000000000002', status: 'Queued', trigger: 'Requested', finishedUtc: null }))
    const { user } = renderPage()
    await screen.findByText(/^Suggestions prepared/)
    vi.mocked(libraryLinksApi.latestRun).mockResolvedValue(suggestionRunOf({ status: 'Queued', finishedUtc: null }))

    // Act
    await user.click(screen.getByRole('button', { name: 'Prepare again' }))

    // Assert
    expect(libraryLinksApi.startRun).toHaveBeenCalledOnce()
    expect(await screen.findByText('Waiting to prepare the suggestions…')).toBeInTheDocument()
  })

  it('says why preparing again was refused, under the line', async () => {
    // Arrange
    vi.mocked(libraryLinksApi.startRun).mockRejectedValue(apiError(409, 'Connect a metadata catalog under Integrations first.'))
    const { user } = renderPage()
    await screen.findByText(/^Suggestions prepared/)

    // Act
    await user.click(screen.getByRole('button', { name: 'Prepare again' }))

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Connect a metadata catalog under Integrations first.')
  })

  it('tells a member the page is the owner\'s, and asks for nothing', () => {
    // Arrange
    renderPage('/library/tmdb', member)

    // Act
    const sentence = screen.getByText('Only the owner can link works to TMDb.')

    // Assert
    expect(sentence).toBeInTheDocument()
    expect(screen.queryByRole('tablist')).not.toBeInTheDocument()
    expect(libraryLinksApi.latestRun).not.toHaveBeenCalled()
    expect(libraryLinksApi.queue).not.toHaveBeenCalled()
  })
})
