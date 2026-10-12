import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { allowedRootsApi } from '@/lib/allowed-roots-api'
import { qk } from '@/lib/queries'
import type { AllowedRoots, CurrentUser, RenamePlaceProblem, ReplacementFolder, WriteAccess } from '@/lib/types'
import { MediaFoldersPage } from '@/pages/settings/MediaFoldersPage'
import {
  allowedRootsOf,
  cartoonsFolder,
  collectionsFolder,
  libraryFolderOf,
  moviesFolder,
  quarantineOf,
  tvShowsFolder,
  writeAccessOf,
} from '@/test/allowed-roots'
import { apiError, networkError } from '@/test/api-error'
import { member, owner, signInAs } from '@/test/auth-session'
import { byteIdenticalGroupOf, duplicatesPage } from '@/test/duplicates'
import { sourceOf, sourcesOf } from '@/test/library'
import { moviesOption } from '@/test/downloads'
import { optionsUnavailable } from '@/test/replacements'
import { tolokaRemux } from '@/test/search'

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

/** The library folders' boxes, in order, as the paths that name them. */
const libraryFolderNames = () =>
  screen.queryAllByRole('checkbox', { name: /^\// })
    .map((box) => document.getElementById(box.getAttribute('aria-labelledby') ?? '')?.textContent)

/** The replacement switch. */
const replacementSwitch = () => screen.getByRole('checkbox', { name: 'Replace copies after verification' })

/** The box of the library folder at `appPath`, which names it. */
const folderBox = (appPath: string) => screen.getByRole('checkbox', { name: appPath })

/** The settings are stored as `roots`, and every read answers them. */
function storedRoots(roots: AllowedRoots) {
  vi.mocked(allowedRootsApi.get).mockResolvedValue(roots)
}

/** The folders are stored as `paths`, with replacement off and no library folder, and every read answers them. */
function stored(...paths: string[]) {
  storedRoots(allowedRootsOf(paths))
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
    expect(allowedRootsApi.update).toHaveBeenCalledExactlyOnceWith({
      paths: ['/media', '/data/films', '/data//series/'],
      replacementEnabled: false,
      replacementFolders: [],
    })
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
    expect(allowedRootsApi.update).toHaveBeenCalledExactlyOnceWith({ paths: [], replacementEnabled: false, replacementFolders: [] })
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
    expect(error).toHaveClass('ui-banner')
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

  it('asks for the Duplicates page\'s groups again after a save, as the switch and the ticks decide which copy can be removed', async () => {
    // Arrange
    vi.mocked(allowedRootsApi.update).mockResolvedValue()
    const { client, user } = renderPage()
    const groups = qk.library.duplicates({ page: 1, pageSize: 25 })
    client.setQueryData(groups, duplicatesPage({ page: 1, pageSize: 25 }, [byteIdenticalGroupOf()]))
    await screen.findByRole('textbox', { name: 'Folder 1' })

    // Act
    await user.click(screen.getByRole('button', { name: 'Save' }))

    // Assert
    await screen.findByText('Saved.')
    expect(client.getQueryState(groups)?.isInvalidated).toBe(true)
  })

  it('shows the API sentence in place of the form to anyone but the owner', async () => {
    // Arrange
    vi.mocked(allowedRootsApi.get).mockRejectedValue(apiError(403, 'Only the installation owner can do this.'))

    // Act
    renderPage(member)

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Only the installation owner can do this.')
    expect(screen.queryByRole('button', { name: 'Try again' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Save' })).not.toBeInTheDocument()
    expect(screen.queryByRole('checkbox', { name: 'Replace copies after verification' })).not.toBeInTheDocument()
  })

  it('names the failure when the folders fail to load without a sentence, and Try again shows the form', async () => {
    // Arrange
    vi.mocked(allowedRootsApi.get).mockRejectedValueOnce(apiError(500))
    const { user } = renderPage()
    const state = await screen.findByRole('alert')

    // Act
    await user.click(screen.getByRole('button', { name: 'Try again' }))

    // Assert
    expect(state).toHaveTextContent('Could not load the media folders.')
    expect(await screen.findByRole('textbox', { name: 'Folder 1' })).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
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

  it('asks again from Retry on the refresh banner, keeping what was typed', async () => {
    // Arrange
    const { client, user } = renderPage()
    await screen.findByRole('textbox', { name: 'Folder 1' })
    await user.type(screen.getByRole('textbox', { name: 'Folder 2' }), '/x')
    vi.mocked(allowedRootsApi.get).mockRejectedValueOnce(networkError())
    await client.refetchQueries()
    await screen.findByRole('alert')

    // Act
    await user.click(screen.getByRole('button', { name: 'Retry' }))

    // Assert
    await vi.waitFor(() => expect(screen.queryByRole('alert')).not.toBeInTheDocument())
    expect(folderValues()).toEqual(['/media', '/data/films/x'])
  })

  // MAM-24 — the replacement gates of ADR 0033, saved by the same Save as the folders.
  describe('replacing copies', () => {
    const switchHint = 'Off by default. When on, the Download dialog offers Replace after verification for copies in the folders ticked below, and the Duplicates page offers Remove this copy for files in them, to you alone. Once the new file is verified, the old copy moves into its media folder\'s quarantine, as a removed copy does at once, and stays there until you restore it or delete it for good. Moving needs the media folders mounted read-write and the app running as their owner, set with PUID and PGID.'
    const outside = 'Outside the Media folders.'
    const outsideTicked = 'Outside the Media folders: this tick has no effect until the folder is inside one.'
    const noLonger = 'No longer a library folder the app knows, so a tick here has no effect. Saved unticked, it leaves this list.'
    const noFolders = 'No library folders yet: sync a library first.'
    const unknownFolder = '“/media/old” isn\'t a library folder the app knows, so it can\'t be ticked for replacement.'
    const ownerOnly = 'Only the installation owner can do this.'

    /** `/media` allowed, the sample's library folders listed, replacement off and nothing ticked. */
    const sample = allowedRootsOf(['/media'], {
      libraryFolders: [collectionsFolder, cartoonsFolder, moviesFolder, tvShowsFolder],
    })

    it('shows replacement off and every library folder unticked by default, each with its libraries', async () => {
      // Arrange
      storedRoots(sample)

      // Act
      renderPage()

      // Assert
      expect(await screen.findByRole('region', { name: 'Replacing copies' })).toBeInTheDocument()
      expect(replacementSwitch()).not.toBeChecked()
      expect(replacementSwitch()).toHaveAccessibleDescription(switchHint)
      expect(screen.getByRole('group', { name: 'Library folders replacement may touch' })).toBeInTheDocument()
      expect(libraryFolderNames()).toEqual(['/config/data/collections', '/media/cartoons', '/media/movies', '/media/tv-shows'])
      for (const folder of [cartoonsFolder, moviesFolder, tvShowsFolder]) {
        expect(folderBox(folder.appPath)).not.toBeChecked()
        expect(folderBox(folder.appPath)).toBeEnabled()
      }
      expect(folderBox('/media/movies')).toHaveAccessibleDescription('In the library Фільми')
      expect(folderBox('/media/cartoons')).toHaveAccessibleDescription('In the libraries Мульфільми and Мультсеріали')
      expect(screen.queryByText(noFolders)).not.toBeInTheDocument()
    })

    it('shows the switch and the ticks as stored', async () => {
      // Arrange
      storedRoots(allowedRootsOf(['/media'], {
        replacementEnabled: true,
        libraryFolders: [{ ...moviesFolder, ticked: true }, tvShowsFolder],
      }))

      // Act
      renderPage()

      // Assert
      expect(await screen.findByRole('checkbox', { name: '/media/movies' })).toBeChecked()
      expect(replacementSwitch()).toBeChecked()
      expect(folderBox('/media/tv-shows')).not.toBeChecked()
    })

    it('sends the folders, the switch and every tick with the one Save, then shows them as stored', async () => {
      // Arrange
      storedRoots(sample)
      vi.mocked(allowedRootsApi.update).mockImplementation(async () => {
        storedRoots(allowedRootsOf(['/media', '/data/films'], {
          replacementEnabled: true,
          libraryFolders: [collectionsFolder, cartoonsFolder, { ...moviesFolder, ticked: true }, { ...tvShowsFolder, ticked: true }],
        }))
      })
      const { user } = renderPage()
      await screen.findByRole('checkbox', { name: '/media/movies' })
      await user.click(screen.getByRole('button', { name: 'Add folder' }))
      await user.type(screen.getByRole('textbox', { name: 'Folder 2' }), '/data/films')
      await user.click(replacementSwitch())
      // Ticked out of the list's order: the request follows the list.
      await user.click(folderBox('/media/tv-shows'))
      await user.click(folderBox('/media/movies'))

      // Act
      await user.click(screen.getByRole('button', { name: 'Save' }))

      // Assert
      expect(await screen.findByRole('status')).toHaveTextContent('Saved.')
      expect(screen.getAllByRole('button', { name: 'Save' })).toHaveLength(1)
      expect(allowedRootsApi.update).toHaveBeenCalledExactlyOnceWith({
        paths: ['/media', '/data/films'],
        replacementEnabled: true,
        replacementFolders: ['/media/movies', '/media/tv-shows'],
      })
      expect(allowedRootsApi.get).toHaveBeenCalledTimes(2)
      expect(folderValues()).toEqual(['/media', '/data/films'])
      expect(replacementSwitch()).toBeChecked()
      expect(folderBox('/media/movies')).toBeChecked()
      expect(folderBox('/media/tv-shows')).toBeChecked()
      expect(folderBox('/media/cartoons')).not.toBeChecked()
    })

    it('keeps the ticks editable while replacement is off, and saves them with it off', async () => {
      // Arrange
      storedRoots(sample)
      vi.mocked(allowedRootsApi.update).mockResolvedValue()
      const { user } = renderPage()
      await screen.findByRole('checkbox', { name: '/media/movies' })
      await user.click(folderBox('/media/cartoons'))

      // Act
      await user.click(screen.getByRole('button', { name: 'Save' }))

      // Assert
      await screen.findByRole('status')
      expect(allowedRootsApi.update).toHaveBeenCalledExactlyOnceWith({
        paths: ['/media'],
        replacementEnabled: false,
        replacementFolders: ['/media/cartoons'],
      })
    })

    it('switches replacement off and unticks a folder in the same save', async () => {
      // Arrange
      storedRoots(allowedRootsOf(['/media'], {
        replacementEnabled: true,
        libraryFolders: [{ ...moviesFolder, ticked: true }, { ...tvShowsFolder, ticked: true }],
      }))
      vi.mocked(allowedRootsApi.update).mockResolvedValue()
      const { user } = renderPage()
      await screen.findByRole('checkbox', { name: '/media/movies' })
      await user.click(replacementSwitch())
      await user.click(folderBox('/media/movies'))

      // Act
      await user.click(screen.getByRole('button', { name: 'Save' }))

      // Assert
      await screen.findByRole('status')
      expect(allowedRootsApi.update).toHaveBeenCalledExactlyOnceWith({
        paths: ['/media'],
        replacementEnabled: false,
        replacementFolders: ['/media/tv-shows'],
      })
    })

    it('keeps an unticked folder outside the media folders disabled, saying why in a line of its own', async () => {
      // Arrange
      storedRoots(sample)
      vi.mocked(allowedRootsApi.update).mockResolvedValue()
      const { user } = renderPage()
      await screen.findByRole('checkbox', { name: '/config/data/collections' })

      // Act
      await user.click(folderBox('/config/data/collections'))
      await user.click(screen.getByRole('button', { name: 'Save' }))

      // Assert
      await screen.findByRole('status')
      expect(folderBox('/config/data/collections')).toBeDisabled()
      expect(folderBox('/config/data/collections')).not.toBeChecked()
      expect(folderBox('/config/data/collections')).toHaveAccessibleDescription(`In the library Колекції ${outside}`)
      // Text on the page, not a tooltip: a phone has none.
      expect(screen.getByText(outside)).toBeVisible()
      expect(allowedRootsApi.update).toHaveBeenCalledExactlyOnceWith({ paths: ['/media'], replacementEnabled: false, replacementFolders: [] })
    })

    it('lets a ticked folder outside the media folders be unticked, sends it unticked, and then keeps it unticked', async () => {
      // Arrange
      storedRoots(allowedRootsOf(['/media'], {
        replacementEnabled: true,
        libraryFolders: [{ ...collectionsFolder, ticked: true }, { ...moviesFolder, ticked: true }],
      }))
      vi.mocked(allowedRootsApi.update).mockImplementation(async () => {
        storedRoots(allowedRootsOf(['/media'], {
          replacementEnabled: true,
          libraryFolders: [collectionsFolder, { ...moviesFolder, ticked: true }],
        }))
      })
      const { user } = renderPage()
      const box = await screen.findByRole<HTMLInputElement>('checkbox', { name: '/config/data/collections' })
      const asStored = { checked: box.checked, disabled: box.disabled, saysNoEffect: screen.queryByText(outsideTicked) !== null }

      // Act
      await user.click(box)
      await user.click(screen.getByRole('button', { name: 'Save' }))

      // Assert
      await screen.findByRole('status')
      expect(asStored).toEqual({ checked: true, disabled: false, saysNoEffect: true })
      expect(allowedRootsApi.update).toHaveBeenCalledExactlyOnceWith({
        paths: ['/media'],
        replacementEnabled: true,
        replacementFolders: ['/media/movies'],
      })
      expect(folderBox('/config/data/collections')).not.toBeChecked()
      expect(folderBox('/config/data/collections')).toBeDisabled()
      expect(folderBox('/config/data/collections')).toHaveAccessibleDescription(`In the library Колекції ${outside}`)
    })

    it('lets a ticked folder outside the media folders be ticked again before saving, as stored', async () => {
      // Arrange
      storedRoots(allowedRootsOf(['/media'], { libraryFolders: [{ ...collectionsFolder, ticked: true }] }))
      const { user } = renderPage()
      const box = await screen.findByRole<HTMLInputElement>('checkbox', { name: '/config/data/collections' })
      await user.click(box)
      const unticked = { checked: box.checked, disabled: box.disabled, saysOutside: screen.queryByText(outside) !== null }

      // Act
      await user.click(box)

      // Assert
      expect(unticked).toEqual({ checked: false, disabled: false, saysOutside: true })
      expect(box).toBeChecked()
      expect(box).toHaveAccessibleDescription(`In the library Колекції ${outsideTicked}`)
    })

    it('lists a tick on a folder that is no longer a library folder, sends it while ticked, and drops it once unticked', async () => {
      // Arrange
      const stale = libraryFolderOf('/media/old', [], { ticked: true, status: 'NoLongerALibraryFolder' })
      storedRoots(allowedRootsOf(['/media'], { libraryFolders: [moviesFolder, stale] }))
      vi.mocked(allowedRootsApi.update).mockResolvedValue()
      const { user } = renderPage()
      const box = await screen.findByRole<HTMLInputElement>('checkbox', { name: '/media/old' })
      const asStored = { checked: box.checked, disabled: box.disabled }
      await user.click(screen.getByRole('button', { name: 'Save' }))
      await screen.findByRole('status')

      // Act
      await user.click(folderBox('/media/old'))
      await user.click(screen.getByRole('button', { name: 'Save' }))

      // Assert
      await vi.waitFor(() => expect(allowedRootsApi.update).toHaveBeenCalledTimes(2))
      expect(asStored).toEqual({ checked: true, disabled: false })
      expect(folderBox('/media/old')).toHaveAccessibleDescription(noLonger)
      expect(vi.mocked(allowedRootsApi.update).mock.calls).toEqual([
        [{ paths: ['/media'], replacementEnabled: false, replacementFolders: ['/media/old'] }],
        [{ paths: ['/media'], replacementEnabled: false, replacementFolders: [] }],
      ])
    })

    it('says to sync a library first while no library folder is stored', async () => {
      // Arrange
      storedRoots(allowedRootsOf(['/media']))

      // Act
      renderPage()

      // Assert
      expect(await screen.findByText(noFolders)).toBeInTheDocument()
      expect(libraryFolderNames()).toEqual([])
      expect(replacementSwitch()).toBeEnabled()
    })

    it.each([
      [1, '1 library folder has no path in this app: add a path mapping to the Jellyfin integration.'],
      [3, '3 library folders have no path in this app: add path mappings to the Jellyfin integration.'],
    ])('says how many library folders have no path in this app rather than to sync again (%i)', async (count, sentence) => {
      // Arrange
      storedRoots(allowedRootsOf(['/media'], { unmappedLibraryFolderCount: count }))

      // Act
      renderPage()

      // Assert
      expect(await screen.findByText(sentence)).toBeInTheDocument()
      expect(screen.queryByText(noFolders)).not.toBeInTheDocument()
    })

    it('lists the folders it can place above the count of those it can\'t', async () => {
      // Arrange
      storedRoots(allowedRootsOf(['/media'], { libraryFolders: [moviesFolder], unmappedLibraryFolderCount: 2 }))

      // Act
      renderPage()

      // Assert
      const box = await screen.findByRole('checkbox', { name: '/media/movies' })
      const count = screen.getByText('2 library folders have no path in this app: add path mappings to the Jellyfin integration.')
      expect(box.compareDocumentPosition(count) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    })

    it('takes "Saved." back once the switch or a tick changes', async () => {
      // Arrange
      storedRoots(sample)
      vi.mocked(allowedRootsApi.update).mockResolvedValue()
      const { user } = renderPage()
      await screen.findByRole('checkbox', { name: '/media/movies' })
      await user.click(screen.getByRole('button', { name: 'Save' }))
      await screen.findByText('Saved.')

      // Act
      await user.click(replacementSwitch())
      const afterSwitch = screen.queryByText('Saved.')
      await user.click(screen.getByRole('button', { name: 'Save' }))
      await screen.findByText('Saved.')
      await user.click(folderBox('/media/movies'))

      // Assert
      expect(afterSwitch).not.toBeInTheDocument()
      expect(screen.queryByText('Saved.')).not.toBeInTheDocument()
    })

    it.each([
      ['a folder the app doesn\'t know', 400, unknownFolder],
      ['anyone but the owner', 403, ownerOnly],
    ])('shows a refused save\'s sentence above Save and keeps the switch and the ticks as picked (%s)', async (_, status, sentence) => {
      // Arrange
      storedRoots(sample)
      vi.mocked(allowedRootsApi.update).mockRejectedValue(apiError(status, sentence))
      const { user } = renderPage()
      await screen.findByRole('checkbox', { name: '/media/movies' })
      await user.click(replacementSwitch())
      await user.click(folderBox('/media/movies'))

      // Act
      await user.click(screen.getByRole('button', { name: 'Save' }))

      // Assert
      const error = await screen.findByRole('alert')
      expect(error).toHaveTextContent(sentence)
      expect(error.nextElementSibling).toContainElement(screen.getByRole('button', { name: 'Save' }))
      expect(replacementSwitch()).toBeChecked()
      expect(folderBox('/media/movies')).toBeChecked()
      expect(screen.queryByText('Saved.')).not.toBeInTheDocument()
      expect(allowedRootsApi.get).toHaveBeenCalledOnce()
    })

    it('asks every answer about what a release may replace again after a save, as the switch and the ticks decide them', async () => {
      // Arrange
      storedRoots(sample)
      vi.mocked(allowedRootsApi.update).mockResolvedValue()
      const { client, user } = renderPage()
      const options = qk.replacements.options(tolokaRemux.id, moviesOption.id, false)
      client.setQueryData(options, optionsUnavailable('SwitchedOff'))
      await screen.findByRole('checkbox', { name: '/media/movies' })
      await user.click(replacementSwitch())

      // Act
      await user.click(screen.getByRole('button', { name: 'Save' }))

      // Assert
      await screen.findByText('Saved.')
      expect(client.getQueryState(options)?.isInvalidated).toBe(true)
    })

    it('shows the switch and the ticks stored elsewhere once they change', async () => {
      // Arrange
      storedRoots(sample)
      const { client, user } = renderPage()
      await screen.findByRole('checkbox', { name: '/media/movies' })
      await user.click(folderBox('/media/cartoons'))
      storedRoots(allowedRootsOf(['/media'], {
        replacementEnabled: true,
        libraryFolders: [collectionsFolder, cartoonsFolder, { ...moviesFolder, ticked: true }, tvShowsFolder],
      }))

      // Act
      await act(() => client.refetchQueries())

      // Assert
      await vi.waitFor(() => expect(replacementSwitch()).toBeChecked())
      expect(folderBox('/media/movies')).toBeChecked()
      expect(folderBox('/media/cartoons')).not.toBeChecked()
    })

    it('keeps what was picked when the settings are read again unchanged, and lists a folder a sync added unticked', async () => {
      // Arrange
      storedRoots(sample)
      const { client, user } = renderPage()
      await screen.findByRole('checkbox', { name: '/media/movies' })
      await user.click(replacementSwitch())
      await user.click(folderBox('/media/movies'))
      const documentary = libraryFolderOf('/media/documentary', ['Документалки'])
      storedRoots(allowedRootsOf(['/media'], {
        libraryFolders: [collectionsFolder, cartoonsFolder, documentary, moviesFolder, tvShowsFolder],
      }))

      // Act
      await act(() => client.refetchQueries())

      // Assert
      expect(await screen.findByRole('checkbox', { name: '/media/documentary' })).not.toBeChecked()
      expect(replacementSwitch()).toBeChecked()
      expect(folderBox('/media/movies')).toBeChecked()
    })

    it('holds both cards in the one form above the one Save, each folder\'s path in a line of its own that breaks on a phone', async () => {
      // Arrange
      storedRoots(sample)

      // Act
      renderPage()

      // Assert
      const replacing = await screen.findByRole('region', { name: 'Replacing copies' })
      const reading = screen.getByRole('region', { name: 'Folders the app may read' })
      const save = screen.getByRole('button', { name: 'Save' })
      const form = save.closest('form')
      expect(form).toContainElement(reading)
      expect(form).toContainElement(replacing)
      expect(reading).not.toContainElement(save)
      expect(replacing).not.toContainElement(save)
      // .media-folders-path breaks a long path anywhere (media-folders.css), so the card never widens past a phone.
      expect(screen.getByText('/media/movies')).toHaveClass('media-folders-path')
    })
  })

  // MAM-26 — whether a move into the quarantine can work in each ticked folder (ADR 0033's hint), the
  // room each quarantine takes, and the Save that would leave one outside every media folder.
  describe('the quarantine', () => {
    const allClear = 'A move into the quarantine can work here: the same mount as the quarantine, mounted read-write, and writable by the app.'
    const problems: Record<RenamePlaceProblem, string> = {
      OtherMount: 'Not on the same mount as its media folder\'s quarantine, so a move would fail: mount the media folder as one read-write mount.',
      ReadOnly: 'Mounted read-only, so a move would fail: mount the media folders read-write.',
      // MAM-39 — when the server couldn't read the ids (writeAccess null).
      NotWritable: 'The app\'s user can\'t write here or in its media folder, so a move would fail: set PUID and PGID for the app to the user and group that own your media folders, and restart it.',
      CouldNotCheck: 'The app couldn\'t check whether a move can work here; the move itself will tell.',
    }
    const empty = 'The quarantine is empty.'

    it.each(Object.entries(problems) as [RenamePlaceProblem, string][])('says on a ticked folder why a move would fail there: %s', async (problem, sentence) => {
      // Arrange
      storedRoots(allowedRootsOf(['/media'], {
        replacementEnabled: true,
        libraryFolders: [libraryFolderOf('/media/movies', ['Фільми'], { ticked: true, renameProblems: [problem] })],
      }))

      // Act
      renderPage()

      // Assert
      const box = await screen.findByRole('checkbox', { name: '/media/movies' })
      const line = screen.getByText(sentence)
      expect(line.closest('.library-source-warning')).not.toBeNull()
      expect(line.closest('.library-source-warning')?.querySelector('svg')).toHaveAttribute('aria-hidden', 'true')
      expect(box).toHaveAccessibleDescription(`In the library Фільми ${sentence}`)
      expect(screen.queryByText(allClear)).not.toBeInTheDocument()
    })

    it('gives each problem a line of its own, every one part of the box\'s description', async () => {
      // Arrange
      storedRoots(allowedRootsOf(['/media'], {
        libraryFolders: [libraryFolderOf('/media/movies', ['Фільми'], { ticked: true, renameProblems: ['ReadOnly', 'NotWritable'] })],
      }))

      // Act
      renderPage()

      // Assert
      const box = await screen.findByRole('checkbox', { name: '/media/movies' })
      const lines = [problems.ReadOnly, problems.NotWritable].map((sentence) => screen.getByText(sentence).closest('.library-source-warning'))
      expect(lines[0]).not.toBe(lines[1])
      // Spans inside the label: a paragraph can't sit in one.
      expect(lines.map((line) => line?.tagName)).toEqual(['SPAN', 'SPAN'])
      expect(box).toHaveAccessibleDescription(`In the library Фільми ${problems.ReadOnly} ${problems.NotWritable}`)
    })

    // MAM-39 — NotWritable says exactly what to set, from the ids the server read.
    describe('when the app can\'t write there', () => {
      /** The folder at `appPath`, ticked, where the app can't write; `access` as the server read it. */
      const notWritable = (access: WriteAccess | null, appPath = '/media/movies') =>
        allowedRootsOf(['/media'], {
          replacementEnabled: true,
          libraryFolders: [libraryFolderOf(appPath, ['Фільми'], { ticked: true, renameProblems: ['NotWritable'], writeAccess: access })],
        })
      /** What `writeAccessOf()` says: user 1000, group 100 own `/media/movies`, the app runs as 10001:10001. */
      const otherOwner = 'The app runs as user 10001, group 10001, and can\'t write to /media/movies, which belongs to user 1000, group 100, so a move would fail: set PUID=1000 and PGID=100 for the app in docker-compose.yml or .env, and restart it.'

      it.each<[string, WriteAccess | null, string]>([
        ['another user owns the folder: the ids to set', writeAccessOf(), otherOwner],
        [
          'root owns the folder: the app never runs as root',
          writeAccessOf({ path: '/media/.mam-quarantine', ownerUserId: 0, ownerGroupId: 0 }),
          'The app can\'t write to /media/.mam-quarantine, which belongs to root, so a move would fail here: the app never runs as root, so it can move copies only in folders that belong to the user it runs as (now user 10001, group 10001).',
        ],
        [
          'the app already runs as the owner: the permissions are wrong',
          writeAccessOf({ ownerUserId: 10001, ownerGroupId: 100 }),
          'The app already runs as the owner of /media/movies, user 10001, but the folder\'s permissions don\'t let its owner write there, so a move would fail: give the owner write permission on it.',
        ],
        ['the ids couldn\'t be read: the general way out', null, problems.NotWritable],
        [
          'the owner is the overflow id 65534: the general way out, never "set PUID=65534"',
          writeAccessOf({ ownerUserId: 65534, ownerGroupId: 65534 }),
          problems.NotWritable,
        ],
        [
          'only the group is the overflow id 65534: the general way out, never "PGID=65534"',
          writeAccessOf({ ownerGroupId: 65534 }),
          problems.NotWritable,
        ],
      ])('says what to change when %s', async (_, access, sentence) => {
        // Arrange
        storedRoots(notWritable(access))

        // Act
        renderPage()

        // Assert
        const box = await screen.findByRole('checkbox', { name: '/media/movies' })
        const line = screen.getByText(sentence)
        expect(line.closest('.library-source-warning')).not.toBeNull()
        expect(line.closest('.library-source-warning')?.querySelector('svg')).toHaveAttribute('aria-hidden', 'true')
        expect(box).toHaveAccessibleDescription(`In the library Фільми ${sentence}`)
        expect(screen.queryByText(allClear)).not.toBeInTheDocument()
      })

      it('reads a folder the server sent without the ids as one whose ids it couldn\'t read', async () => {
        // Arrange
        const withoutIds: Partial<ReplacementFolder> = libraryFolderOf('/media/movies', ['Фільми'], { ticked: true, renameProblems: ['NotWritable'] })
        delete withoutIds.writeAccess
        storedRoots(allowedRootsOf(['/media'], { libraryFolders: [withoutIds as ReplacementFolder] }))

        // Act
        renderPage()

        // Assert
        const box = await screen.findByRole('checkbox', { name: '/media/movies' })
        expect(box).toHaveAccessibleDescription(`In the library Фільми ${problems.NotWritable}`)
      })

      it('writes the ids as plain digits, never grouped the locale\'s way', async () => {
        // Arrange
        storedRoots(notWritable(writeAccessOf({ ownerUserId: 10001, ownerGroupId: 10001, appUserId: 1000, appGroupId: 100 })))

        // Act
        renderPage()

        // Assert
        const box = await screen.findByRole('checkbox', { name: '/media/movies' })
        const line = box.closest('li')?.querySelector('.library-source-warning')
        expect(line).toHaveTextContent('The app runs as user 1000, group 100, and can\'t write to /media/movies, which belongs to user 10001, group 10001,')
        expect(line).toHaveTextContent('set PUID=10001 and PGID=10001 for the app')
        expect(line?.textContent).not.toMatch(/\d[,.\u00a0\u202f]\d{3}/)
      })

      it('shows a path with "&" and quotes as it is, not escaped', async () => {
        // Arrange
        const path = '/media/cartoons/Tom & Jerry\'s "Classic" Shorts'
        storedRoots(notWritable(writeAccessOf({ path }), path))

        // Act
        renderPage()

        // Assert
        const box = await screen.findByRole('checkbox', { name: path })
        const sentence = `The app runs as user 10001, group 10001, and can't write to ${path}, which belongs to user 1000, group 100, so a move would fail: set PUID=1000 and PGID=100 for the app in docker-compose.yml or .env, and restart it.`
        expect(screen.getByText(sentence)).toBeInTheDocument()
        expect(box).toHaveAccessibleDescription(`In the library Фільми ${sentence}`)
        expect(box.closest('li')?.textContent).not.toMatch(/&amp;|&quot;|&#39;|&#x27;/)
      })

      it('keeps the other problems\' sentences next to the one that names the ids', async () => {
        // Arrange
        storedRoots(allowedRootsOf(['/media'], {
          libraryFolders: [libraryFolderOf('/media/movies', ['Фільми'], {
            ticked: true,
            renameProblems: ['OtherMount', 'ReadOnly', 'NotWritable'],
            writeAccess: writeAccessOf(),
          })],
        }))

        // Act
        renderPage()

        // Assert
        const box = await screen.findByRole('checkbox', { name: '/media/movies' })
        expect(box).toHaveAccessibleDescription(`In the library Фільми ${problems.OtherMount} ${problems.ReadOnly} ${otherOwner}`)
        expect(screen.queryByText(problems.NotWritable)).not.toBeInTheDocument()
      })
    })

    it('says all is clear only on a folder ticked in the form, from the tick on, saved or not', async () => {
      // Arrange
      storedRoots(allowedRootsOf(['/media'], {
        libraryFolders: [{ ...moviesFolder, ticked: true }, tvShowsFolder],
      }))
      const { user } = renderPage()
      await screen.findByRole('checkbox', { name: '/media/movies' })
      const beforeTick = screen.getAllByText(allClear).length

      // Act
      await user.click(folderBox('/media/tv-shows'))

      // Assert
      expect(beforeTick).toBe(1)
      expect(screen.getAllByText(allClear)).toHaveLength(2)
      expect(folderBox('/media/tv-shows')).toHaveAccessibleDescription(`In the library Серіали ${allClear}`)
      await user.click(folderBox('/media/movies'))
      expect(folderBox('/media/movies')).toHaveAccessibleDescription('In the library Фільми')
      expect(screen.getAllByText(allClear)).toHaveLength(1)
    })

    it('says nothing of a move on a ticked folder the app didn\'t check, outside the media folders', async () => {
      // Arrange
      storedRoots(allowedRootsOf(['/media'], { libraryFolders: [{ ...collectionsFolder, ticked: true }] }))

      // Act
      renderPage()

      // Assert
      const box = await screen.findByRole('checkbox', { name: '/config/data/collections' })
      expect(box).toHaveAccessibleDescription('In the library Колекції Outside the Media folders: this tick has no effect until the folder is inside one.')
      expect(screen.queryByText(allClear)).not.toBeInTheDocument()
      expect(document.querySelector('.media-folders-library .library-source-warning')).toBeNull()
    })

    it('lists the room each quarantine takes and whose names it holds, with links to where they are restored or deleted', async () => {
      // Arrange
      storedRoots(allowedRootsOf(['/media', '/data', '/films', '/tv'], {
        quarantines: [
          quarantineOf(),
          quarantineOf({ path: '/data/.mam-quarantine', replacementCount: 1, fileCount: 1, sizeBytes: 1_073_741_824 }),
          quarantineOf({ path: '/films/.mam-quarantine', replacementCount: 0, removalCount: 1, fileCount: 2, sizeBytes: 2_147_483_648 }),
          quarantineOf({ path: '/tv/.mam-quarantine', replacementCount: 1, removalCount: 2, fileCount: 4, sizeBytes: 4_294_967_296 }),
        ],
      }))

      // Act
      renderPage()

      // Assert
      const heading = await screen.findByRole('heading', { level: 3, name: 'Quarantine' })
      const list = screen.getByRole('list', { name: 'Quarantine' })
      expect(within(list).getAllByRole('listitem').map((item) => item.textContent)).toEqual([
        '/media/.mam-quarantine: 42 GiB in 3 files, from 2 replacements.',
        '/data/.mam-quarantine: 1 GiB in 1 file, from 1 replacement.',
        '/films/.mam-quarantine: 2 GiB in 2 files, from 1 removal.',
        '/tv/.mam-quarantine: 4 GiB in 4 files, from 1 replacement and 2 removals.',
      ])
      expect(screen.getByRole('region', { name: 'Replacing copies' })).toContainElement(heading)
      expect(screen.getByRole('link', { name: 'Jobs' })).toHaveAttribute('href', '/jobs')
      expect(screen.getByRole('link', { name: 'Removed copies' })).toHaveAttribute('href', '/library/duplicates?tab=Removed')
      expect(screen.queryByText(empty)).not.toBeInTheDocument()
    })

    it('says the quarantine is empty while it holds nothing', async () => {
      // Arrange
      storedRoots(allowedRootsOf(['/media']))

      // Act
      renderPage()

      // Assert
      expect(await screen.findByText(empty)).toBeInTheDocument()
      expect(screen.queryByRole('list', { name: 'Quarantine' })).not.toBeInTheDocument()
    })

    it('shows the server\'s refusal of a Save that would leave a quarantine outside every media folder above Save, keeping what was typed', async () => {
      // Arrange
      const sentence = '“/media/.mam-quarantine” still holds replaced or removed copies, so its media folder can\'t be taken out of the list. Restore them or delete them for good first.'
      storedRoots(allowedRootsOf(['/media'], { quarantines: [quarantineOf()] }))
      vi.mocked(allowedRootsApi.update).mockRejectedValue(apiError(400, sentence))
      const { user } = renderPage()
      await screen.findByRole('textbox', { name: 'Folder 1' })
      await user.clear(screen.getByRole('textbox', { name: 'Folder 1' }))
      await user.type(screen.getByRole('textbox', { name: 'Folder 1' }), '/media/movies')

      // Act
      await user.click(screen.getByRole('button', { name: 'Save' }))

      // Assert
      const error = await screen.findByRole('alert')
      expect(error).toHaveTextContent(sentence)
      expect(error.nextElementSibling).toContainElement(screen.getByRole('button', { name: 'Save' }))
      expect(folderValues()).toEqual(['/media/movies'])
      expect(screen.getByRole('list', { name: 'Quarantine' })).toHaveTextContent('/media/.mam-quarantine: 42 GiB in 3 files, from 2 replacements.')
      expect(allowedRootsApi.get).toHaveBeenCalledOnce()
    })
  })
})
