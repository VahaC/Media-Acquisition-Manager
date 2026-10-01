import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { allowedRootsApi } from '@/lib/allowed-roots-api'
import { qk } from '@/lib/queries'
import type { CurrentUser } from '@/lib/types'
import { MediaFoldersPage } from '@/pages/settings/MediaFoldersPage'
import { apiError, networkError } from '@/test/api-error'
import { member, owner, signInAs } from '@/test/auth-session'
import { sourceOf, sourcesOf } from '@/test/library'

vi.mock('@/lib/allowed-roots-api')

function renderPage(signedIn: CurrentUser = owner) {
  signInAs(signedIn)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <MediaFoldersPage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
  return { client, user: userEvent.setup() }
}

/** The folder inputs, in order, as their values. */
const folderValues = () =>
  screen.queryAllByRole('textbox', { name: /^Folder \d+$/ }).map((input) => (input as HTMLInputElement).value)

/** The folders are stored as `paths`, and every read answers them. */
function stored(...paths: string[]) {
  vi.mocked(allowedRootsApi.get).mockResolvedValue({ paths })
}

describe('MediaFoldersPage', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    stored('/media', '/data/films')
  })

  it('shows each stored folder in an input of its own, under the explanation', async () => {
    // Arrange
    const signedIn = owner

    // Act
    renderPage(signedIn)

    // Assert
    expect(screen.getByRole('heading', { level: 1, name: 'Media folders' })).toBeInTheDocument()
    expect(screen.getByText(/^The folders inside this app where it may read your library's files/)).toBeInTheDocument()
    await screen.findByRole('textbox', { name: 'Folder 1' })
    expect(folderValues()).toEqual(['/media', '/data/films'])
    expect(screen.getByRole('button', { name: 'Remove folder 2' })).toHaveClass('ui-button-danger')
    expect(screen.getByRole('button', { name: 'Save' })).toBeEnabled()
  })

  it('says Loading… while the folders are on their way', () => {
    // Arrange
    vi.mocked(allowedRootsApi.get).mockReturnValue(new Promise(() => {}))

    // Act
    renderPage()

    // Assert
    expect(screen.getByText('Loading…')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Save' })).not.toBeInTheDocument()
  })

  it('says the app reads no file while no folder is allowed', async () => {
    // Arrange
    stored()

    // Act
    renderPage()

    // Assert
    expect(await screen.findByText('No folder yet: the app reads no file until you add one.')).toBeInTheDocument()
    expect(folderValues()).toEqual([])
    expect(screen.getByRole('button', { name: 'Add folder' })).toBeInTheDocument()
  })

  it('adds an empty row with Add folder and removes a row with its own button', async () => {
    // Arrange
    const { user } = renderPage()
    await screen.findByRole('textbox', { name: 'Folder 1' })
    await user.click(screen.getByRole('button', { name: 'Add folder' }))
    await user.type(screen.getByRole('textbox', { name: 'Folder 3' }), '/data/series')

    // Act
    await user.click(screen.getByRole('button', { name: 'Remove folder 1' }))

    // Assert
    expect(folderValues()).toEqual(['/data/films', '/data/series'])
    expect(allowedRootsApi.update).not.toHaveBeenCalled()
  })

  it('saves the whole list trimmed, then shows the folders as stored and says so', async () => {
    // Arrange
    vi.mocked(allowedRootsApi.update).mockImplementation(async () => {
      // The server keeps them in canonical form.
      stored('/media', '/data/films', '/data/series')
    })
    const { user } = renderPage()
    await screen.findByRole('textbox', { name: 'Folder 1' })
    await user.click(screen.getByRole('button', { name: 'Add folder' }))
    await user.type(screen.getByRole('textbox', { name: 'Folder 3' }), '  /data//series/ ')

    // Act
    await user.click(screen.getByRole('button', { name: 'Save' }))

    // Assert
    expect(await screen.findByRole('status')).toHaveTextContent('Saved.')
    expect(allowedRootsApi.update).toHaveBeenCalledExactlyOnceWith(['/media', '/data/films', '/data//series/'])
    expect(folderValues()).toEqual(['/media', '/data/films', '/data/series'])
    expect(allowedRootsApi.get).toHaveBeenCalledTimes(2)
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('says Saving… and keeps Save disabled while the list is on its way', async () => {
    // Arrange
    let answer: () => void = () => {}
    vi.mocked(allowedRootsApi.update).mockReturnValue(new Promise((resolve) => {
      answer = resolve
    }))
    const { user } = renderPage()
    await screen.findByRole('textbox', { name: 'Folder 1' })

    // Act
    await user.click(screen.getByRole('button', { name: 'Save' }))

    // Assert
    expect(screen.getByRole('button', { name: 'Saving…' })).toBeDisabled()
    await act(async () => answer())
    expect(await screen.findByRole('button', { name: 'Save' })).toBeEnabled()
  })

  it('saves an empty list, which allows no folder', async () => {
    // Arrange
    vi.mocked(allowedRootsApi.update).mockResolvedValue()
    const { user } = renderPage()
    await screen.findByRole('textbox', { name: 'Folder 1' })
    await user.click(screen.getByRole('button', { name: 'Remove folder 2' }))
    await user.click(screen.getByRole('button', { name: 'Remove folder 1' }))

    // Act
    await user.click(screen.getByRole('button', { name: 'Save' }))

    // Assert
    await screen.findByRole('status')
    expect(allowedRootsApi.update).toHaveBeenCalledExactlyOnceWith([])
  })

  it('shows the sentence of a refused save above Save and keeps what was typed', async () => {
    // Arrange
    const sentence = '“data/series” is not a full path. Start it with /.'
    vi.mocked(allowedRootsApi.update).mockRejectedValue(apiError(400, sentence))
    const { user } = renderPage()
    await screen.findByRole('textbox', { name: 'Folder 1' })
    await user.click(screen.getByRole('button', { name: 'Add folder' }))
    await user.type(screen.getByRole('textbox', { name: 'Folder 3' }), 'data/series')

    // Act
    await user.click(screen.getByRole('button', { name: 'Save' }))

    // Assert
    const error = await screen.findByRole('alert')
    expect(error).toHaveTextContent(sentence)
    expect(error).toHaveClass('settings-error')
    expect(error.nextElementSibling).toContainElement(screen.getByRole('button', { name: 'Save' }))
    expect(folderValues()).toEqual(['/media', '/data/films', 'data/series'])
    expect(screen.queryByText('Saved.')).not.toBeInTheDocument()
    expect(allowedRootsApi.get).toHaveBeenCalledOnce()
  })

  it('names the failure when a refused save carries no sentence', async () => {
    // Arrange
    vi.mocked(allowedRootsApi.update).mockRejectedValue(apiError(500))
    const { user } = renderPage()
    await screen.findByRole('textbox', { name: 'Folder 1' })

    // Act
    await user.click(screen.getByRole('button', { name: 'Save' }))

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Could not save the media folders.')
  })

  it('takes "Saved." back once a folder is changed again', async () => {
    // Arrange
    vi.mocked(allowedRootsApi.update).mockResolvedValue()
    const { user } = renderPage()
    await screen.findByRole('textbox', { name: 'Folder 1' })
    await user.click(screen.getByRole('button', { name: 'Save' }))
    await screen.findByText('Saved.')

    // Act
    await user.type(screen.getByRole('textbox', { name: 'Folder 1' }), '/movies')

    // Assert
    expect(screen.queryByText('Saved.')).not.toBeInTheDocument()
    expect(folderValues()).toEqual(['/media/movies', '/data/films'])
  })

  it('asks for the Library sources again after a save, as it turns the inspection on or off', async () => {
    // Arrange
    vi.mocked(allowedRootsApi.update).mockResolvedValue()
    const { client, user } = renderPage()
    client.setQueryData(qk.library.sources, sourcesOf(sourceOf({})))
    await screen.findByRole('textbox', { name: 'Folder 1' })

    // Act
    await user.click(screen.getByRole('button', { name: 'Save' }))

    // Assert
    await screen.findByText('Saved.')
    expect(client.getQueryState(qk.library.sources)?.isInvalidated).toBe(true)
  })

  it('shows the API sentence in place of the form to anyone but the owner', async () => {
    // Arrange
    vi.mocked(allowedRootsApi.get).mockRejectedValue(apiError(403, 'Only the installation owner can do this.'))

    // Act
    renderPage(member)

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Only the installation owner can do this.')
    expect(screen.queryByRole('button', { name: 'Save' })).not.toBeInTheDocument()
  })

  it('names the failure when the folders fail to load without a sentence', async () => {
    // Arrange
    vi.mocked(allowedRootsApi.get).mockRejectedValue(apiError(500))

    // Act
    renderPage()

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Could not load the media folders.')
  })

  it('keeps what was typed when the folders are read again unchanged', async () => {
    // Arrange
    const { client, user } = renderPage()
    await screen.findByRole('textbox', { name: 'Folder 1' })
    await user.type(screen.getByRole('textbox', { name: 'Folder 2' }), '/x')
    await user.click(screen.getByRole('button', { name: 'Add folder' }))

    // Act
    // A refetch nobody asked for, such as the network coming back: the same folders, a new answer.
    await act(() => client.refetchQueries())

    // Assert
    await vi.waitFor(() => expect(client.getQueryState(qk.settings.allowedRoots)?.dataUpdateCount).toBe(2))
    // Lets React render the new answer before the rows are read.
    await act(() => new Promise((resolve) => setTimeout(resolve, 0)))
    expect(folderValues()).toEqual(['/media', '/data/films/x', ''])
  })

  it('shows the folders stored elsewhere once they change', async () => {
    // Arrange
    const { client } = renderPage()
    await screen.findByRole('textbox', { name: 'Folder 1' })
    stored('/srv/media')

    // Act
    await act(() => client.refetchQueries())

    // Assert
    await vi.waitFor(() => expect(folderValues()).toEqual(['/srv/media']))
  })

  it('keeps the form and what was typed under a banner when a refresh fails', async () => {
    // Arrange
    const { client, user } = renderPage()
    await screen.findByRole('textbox', { name: 'Folder 1' })
    await user.type(screen.getByRole('textbox', { name: 'Folder 2' }), '/x')
    vi.mocked(allowedRootsApi.get).mockRejectedValue(networkError())

    // Act
    await client.refetchQueries()

    // Assert
    const banner = await screen.findByRole('alert')
    expect(banner).toHaveTextContent('Refresh failed, showing the last loaded folders.')
    expect(banner).toHaveTextContent('The server could not be reached.')
    expect(folderValues()).toEqual(['/media', '/data/films/x'])
  })
})
