import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { DestinationDialog } from '@/components/destinations/DestinationDialog'
import { destinationsApi } from '@/lib/destinations-api'
import { integrationsApi } from '@/lib/integrations-api'
import type { ClientDestinationList, Destination, IntegrationSummary } from '@/lib/types'
import { apiError, networkError } from '@/test/api-error'
import {
  checkOf,
  filmsFolder,
  notSeenProblem,
  sampleCategories,
  sampleFolders,
  sectionsOf,
  tolokaSections,
  unplacedProblem,
} from '@/test/destinations'
import { localQbittorrent, localTransmission } from '@/test/integrations'

// MAM-30 — the destination dialog for a client that saves to folders (Transmission, ADR 0039): its
// media library folders mapped back through its path mappings and its own download folder, a folder it
// can't see listed but not pickable, and the libraries it offers no folder for listed with the fix.

vi.mock('@/lib/destinations-api')
vi.mock('@/lib/integrations-api')

type User = ReturnType<typeof userEvent.setup>

function renderDialog(destination?: Destination) {
  const onClose = vi.fn()
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <DestinationDialog destination={destination} onClose={onClose} />
    </QueryClientProvider>,
  )
  const name = destination ? `Edit ${destination.name}` : 'Add destination'
  return { onClose, user: userEvent.setup(), dialog: screen.getByRole('dialog', { name }) }
}

/** The server lists `clients` as the download clients. */
function listing(...clients: IntegrationSummary[]) {
  vi.mocked(integrationsApi.list).mockImplementation(async (query) =>
    ({ items: clients, totalCount: clients.length, page: query.page, pageSize: query.pageSize }))
}

const clientSelect = (dialog: HTMLElement) => within(dialog).getByRole('combobox', { name: 'Download client' })
const folderSelect = (dialog: HTMLElement) => within(dialog).getByRole('combobox', { name: 'Folder' })

/** The options of a select as the owner reads them, the placeholder included. */
const optionTexts = (select: HTMLElement) => [...select.querySelectorAll('option')].map((option) => option.textContent)

/** The option of `select` whose value is `value`. */
const optionOf = (select: HTMLElement, value: string) =>
  [...select.querySelectorAll('option')].find((option) => option.value === value)!

/** The lines under the Folder field naming the folders the client can't save to. */
const problemLines = (dialog: HTMLElement) =>
  within(within(dialog).getByRole('list', { name: 'Folders Local Transmission can\'t save to' }))
    .getAllByRole('listitem')

/** Picks `clientId` once the clients have arrived. */
async function pickClient(user: User, dialog: HTMLElement, clientId: string) {
  await vi.waitFor(() => expect(clientSelect(dialog)).toBeEnabled())
  await user.selectOptions(clientSelect(dialog), clientId)
}

/** Picks Local Transmission and waits for its folders. */
async function pickTransmission(user: User, dialog: HTMLElement) {
  await pickClient(user, dialog, localTransmission.id)
  await vi.waitFor(() => expect(folderSelect(dialog)).toBeEnabled())
}

describe('DestinationDialog folders', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    listing(localQbittorrent, localTransmission)
    vi.mocked(destinationsApi.clientDestinations).mockImplementation(async (id) =>
      id === localTransmission.id ? sampleFolders : sampleCategories)
    vi.mocked(destinationsApi.sections).mockResolvedValue(sectionsOf(tolokaSections))
  })

  it('calls the field Folder as soon as a client that saves to folders is picked, while its folders are on their way', async () => {
    // Arrange
    let answer: (folders: ClientDestinationList) => void = () => {}
    vi.mocked(destinationsApi.clientDestinations).mockReturnValue(new Promise((resolve) => {
      answer = resolve
    }))
    const { dialog, user } = renderDialog()

    // Act
    await pickClient(user, dialog, localTransmission.id)

    // Assert
    expect(folderSelect(dialog)).toBeDisabled()
    expect(optionTexts(folderSelect(dialog))).toEqual(['Choose a folder'])
    expect(folderSelect(dialog)).toHaveAccessibleDescription('Asking Local Transmission which folders it can reach…')
    expect(dialog).toHaveAccessibleDescription(
      'Pick a folder your download client can reach: a media library folder, or its own download folder. The app sends each download there, with the destination\'s name as its label.')
    expect(within(dialog).queryByRole('combobox', { name: 'Category' })).not.toBeInTheDocument()
    await act(async () => answer(sampleFolders))
  })

  it('names each folder by its libraries or as the client\'s download folder, then its path', async () => {
    // Arrange
    const { dialog, user } = renderDialog()

    // Act
    await pickTransmission(user, dialog)

    // Assert
    expect(destinationsApi.clientDestinations).toHaveBeenCalledExactlyOnceWith(localTransmission.id)
    expect(optionTexts(folderSelect(dialog))).toEqual([
      'Choose a folder',
      'Local Transmission\'s download folder — /downloads/complete',
      'Мульфільми — /downloads/cartoons',
      'Фільми — /downloads/movies',
      'Серіали — /downloads/tv-shows (can\'t be used)',
    ])
  })

  it('names a folder by every library it holds and as the client\'s own, and one that is neither by its path alone', async () => {
    // Arrange
    vi.mocked(destinationsApi.clientDestinations).mockResolvedValue({
      ...sampleFolders,
      items: [
        // D18 — the client's download folder is a library's root folder: one entry, saying both.
        { key: '/downloads/movies', displayName: '/downloads/movies', check: checkOf({ libraryNames: ['Фільми', 'Колекції'] }), isClientDefaultFolder: true },
        {
          key: '/downloads/incoming',
          displayName: '/downloads/incoming',
          check: checkOf({ status: 'PathUnknown', servicePath: null, appPath: null, libraryNames: [], problem: 'No path mapping of this integration covers “/downloads/incoming”.' }),
          isClientDefaultFolder: false,
        },
      ],
      unplaced: [],
    })
    const { dialog, user } = renderDialog()

    // Act
    await pickTransmission(user, dialog)

    // Assert
    expect(optionTexts(folderSelect(dialog))).toEqual([
      'Choose a folder',
      'Фільми and Колекції, Local Transmission\'s download folder — /downloads/movies',
      '/downloads/incoming',
    ])
  })

  it('keeps a folder the client can\'t see listed but not pickable, and says why under the field with the libraries it offers no folder for', async () => {
    // Arrange
    const { dialog, user } = renderDialog()

    // Act
    await pickTransmission(user, dialog)

    // Assert
    expect(optionOf(folderSelect(dialog), '/downloads/tv-shows')).toBeDisabled()
    expect(optionOf(folderSelect(dialog), '/downloads/movies')).toBeEnabled()
    const lines = problemLines(dialog)
    expect(lines.map((line) => line.textContent)).toEqual([
      `Серіали: ${notSeenProblem}`,
      `Документалки: ${unplacedProblem}`,
    ])
    // Amber icon lines, as the Jobs page's warnings are; the sentences quote folders, so they wrap.
    for (const line of lines) expect(line).toHaveClass('library-source-warning')
    expect(lines[0].parentElement).toHaveClass('destinations-folder-problems')
    // Read with the select, so a screen reader hears why an option is disabled and which library has no folder.
    expect(folderSelect(dialog)).toHaveAccessibleDescription(`Серіали: ${notSeenProblem} Документалки: ${unplacedProblem}`)
  })

  it('marks the client\'s download folder that is too long to store as one that can\'t be used, with why under the field', async () => {
    // Arrange
    // MAM-30 — listed, as a folder the client can't see is, but for another reason: the suffix fits both (D1).
    const longFolder = `/downloads/${'c'.repeat(250)}`
    const tooLong = `The folder “${longFolder}” in “Local Transmission” is longer than the 256 characters a destination can hold.`
    vi.mocked(destinationsApi.clientDestinations).mockResolvedValue({
      ...sampleFolders,
      items: [
        {
          key: longFolder,
          displayName: longFolder,
          check: checkOf({ status: 'FolderNotSeen', servicePath: longFolder, appPath: null, libraryNames: [], problem: tooLong }),
          isClientDefaultFolder: true,
        },
        ...sampleFolders.items.filter((folder) => !folder.isClientDefaultFolder),
      ],
      unplaced: [],
    })
    const { dialog, user } = renderDialog()

    // Act
    await pickTransmission(user, dialog)

    // Assert
    const option = optionOf(folderSelect(dialog), longFolder)
    expect(option).toHaveTextContent(`Local Transmission's download folder — ${longFolder} (can't be used)`)
    expect(option).toBeDisabled()
    expect(problemLines(dialog).map((line) => line.textContent)).toContain(`Local Transmission's download folder: ${tooLong}`)
  })

  it('shows where a picked folder saves, and adds a destination with that folder', async () => {
    // Arrange
    vi.mocked(destinationsApi.create).mockResolvedValue(filmsFolder)
    const { dialog, onClose, user } = renderDialog()
    await user.type(within(dialog).getByLabelText('Name'), 'Films')
    await pickTransmission(user, dialog)

    // Act
    await user.selectOptions(folderSelect(dialog), '/downloads/movies')
    await user.click(within(dialog).getByRole('button', { name: 'Add destination' }))

    // Assert
    await vi.waitFor(() => expect(onClose).toHaveBeenCalledOnce())
    // Where it saves, then the lines under the field, in the order they are shown.
    expect(folderSelect(dialog)).toHaveAccessibleDescription(/In the media library Фільми Серіали: Transmission can't see/)
    expect(within(dialog).getByText('/media/movies')).toBeInTheDocument()
    expect(destinationsApi.create).toHaveBeenCalledExactlyOnceWith({
      name: 'Films',
      downloadClientId: localTransmission.id,
      clientDestinationKey: '/downloads/movies',
      isDefault: false,
    })
  })

  it('says under the name that a folder client shows it as the torrent\'s label, and a category client says nothing of it', async () => {
    // Arrange
    const { dialog, user } = renderDialog()
    const name = within(dialog).getByLabelText('Name')
    await pickTransmission(user, dialog)
    const forTransmission = name.getAttribute('aria-describedby') !== null

    // Act
    await pickClient(user, dialog, localQbittorrent.id)

    // Assert
    expect(forTransmission).toBe(true)
    expect(name).not.toHaveAttribute('aria-describedby')
    expect(within(dialog).queryByText(/shows this name as the torrent's label/)).not.toBeInTheDocument()
    expect(within(dialog).getByRole('combobox', { name: 'Category' })).toBeInTheDocument()
    expect(within(dialog).queryByRole('list', { name: /can't save to$/ })).not.toBeInTheDocument()
  })

  it('names the label rule under the name, where the field\'s name stays Name', async () => {
    // Arrange
    const { dialog, user } = renderDialog()

    // Act
    await pickTransmission(user, dialog)

    // Assert
    const name = within(dialog).getByRole('textbox', { name: 'Name' })
    expect(name).toHaveAccessibleDescription('Local Transmission shows this name as the torrent\'s label, so it can\'t contain a comma.')
  })

  it('says when the client offers no folder at all, and how to get one', async () => {
    // Arrange
    vi.mocked(destinationsApi.clientDestinations).mockResolvedValue({ ...sampleFolders, items: [], unplaced: [] })
    const { dialog, user } = renderDialog()

    // Act
    await pickClient(user, dialog, localTransmission.id)

    // Assert
    expect(await within(dialog).findByText(
      'Local Transmission offers no folder. Sync a library and check the path mappings of Local Transmission, then open this dialog again.'))
      .toBeInTheDocument()
    expect(within(dialog).queryByRole('list', { name: /can't save to$/ })).not.toBeInTheDocument()
  })

  it('says in a folder\'s words when the folders can\'t be read and the API gives no sentence', async () => {
    // Arrange
    vi.mocked(destinationsApi.clientDestinations).mockRejectedValue(apiError(404))
    const { dialog, user } = renderDialog()

    // Act
    await pickClient(user, dialog, localTransmission.id)

    // Assert
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('Could not ask the client for its folders.')
    expect(folderSelect(dialog)).toBeDisabled()
  })

  it('shows the client\'s sentence alone when it couldn\'t be asked, with no folder lines under it', async () => {
    // Arrange
    const sentence = 'Transmission didn\'t answer at http://127.0.0.1:9. Check the address and that Transmission is running.'
    vi.mocked(destinationsApi.clientDestinations).mockResolvedValue({ ...sampleFolders, items: [], error: sentence })
    const { dialog, user } = renderDialog()

    // Act
    await pickClient(user, dialog, localTransmission.id)

    // Assert
    expect(await within(dialog).findByRole('alert')).toHaveTextContent(sentence)
    expect(within(dialog).queryByRole('list', { name: /can't save to$/ })).not.toBeInTheDocument()
  })

  it('says Folder before a client is picked when every download client saves to folders', async () => {
    // Arrange
    listing(localTransmission)

    // Act
    const { dialog } = renderDialog()

    // Assert
    await vi.waitFor(() => expect(clientSelect(dialog)).toBeEnabled())
    expect(optionTexts(folderSelect(dialog))).toEqual(['Choose a folder'])
    expect(folderSelect(dialog)).toHaveAccessibleDescription('Choose a download client first.')
    expect(destinationsApi.clientDestinations).not.toHaveBeenCalled()
  })

  it('says Category before a client is picked while the download clients differ', async () => {
    // Arrange
    listing(localQbittorrent, localTransmission)

    // Act
    const { dialog } = renderDialog()

    // Assert
    await vi.waitFor(() => expect(clientSelect(dialog)).toBeEnabled())
    expect(within(dialog).getByRole('combobox', { name: 'Category' })).toHaveAccessibleDescription('Choose a download client first.')
    expect(within(dialog).queryByRole('combobox', { name: 'Folder' })).not.toBeInTheDocument()
  })

  it('edits a folder destination: its own client is asked about the stored folder, and a new name saves the same folder', async () => {
    // Arrange
    vi.mocked(destinationsApi.update).mockResolvedValue()
    const { dialog, onClose, user } = renderDialog(filmsFolder)
    await vi.waitFor(() => expect(folderSelect(dialog)).toBeEnabled())
    const shown = (folderSelect(dialog) as HTMLSelectElement).value
    await user.clear(within(dialog).getByLabelText('Name'))
    await user.type(within(dialog).getByLabelText('Name'), 'Movies')

    // Act
    await user.click(within(dialog).getByRole('button', { name: 'Save changes' }))

    // Assert
    await vi.waitFor(() => expect(onClose).toHaveBeenCalledOnce())
    expect(shown).toBe('/downloads/movies')
    expect(destinationsApi.clientDestinations).toHaveBeenCalledExactlyOnceWith(localTransmission.id, filmsFolder.id)
    expect(dialog).toHaveAccessibleDescription('A new client or folder is checked with the client before it is saved.')
    expect(destinationsApi.update).toHaveBeenCalledExactlyOnceWith(filmsFolder.id, {
      name: 'Movies',
      downloadClientId: localTransmission.id,
      clientDestinationKey: '/downloads/movies',
      isDefault: false,
    })
  })

  it.each([
    ['failed to load', () => vi.mocked(integrationsApi.list).mockRejectedValue(networkError())],
    ['doesn\'t hold its client', () => listing(localQbittorrent)],
  ])('edits a folder destination in a folder\'s words while the clients list %s and its folders are on their way', async (_, clientsListed) => {
    // Arrange
    // MAM-30 — the saved destination's own kind names the field while the list can't say what its client saves to.
    clientsListed()
    vi.mocked(destinationsApi.clientDestinations).mockReturnValue(new Promise(() => {}))

    // Act
    const { dialog } = renderDialog(filmsFolder)

    // Assert
    await vi.waitFor(() => expect(integrationsApi.list).toHaveBeenCalled())
    expect(clientSelect(dialog)).toHaveValue(localTransmission.id)
    expect(folderSelect(dialog)).toHaveAccessibleDescription('Asking Local Transmission which folders it can reach…')
    expect(dialog).toHaveAccessibleDescription('A new client or folder is checked with the client before it is saved.')
    expect(within(dialog).queryByRole('combobox', { name: 'Category' })).not.toBeInTheDocument()
    expect(destinationsApi.clientDestinations).toHaveBeenCalledExactlyOnceWith(localTransmission.id, filmsFolder.id)
  })

  it('names the field by the client\'s own answer when the clients list doesn\'t say what the picked client saves to', async () => {
    // Arrange
    // MAM-30 — the clients list names no kind for it: the folders it lists say it saves to folders.
    listing(localQbittorrent, { ...localTransmission, destinationKind: null })
    const { dialog, user } = renderDialog()

    // Act
    await pickClient(user, dialog, localTransmission.id)

    // Assert
    await vi.waitFor(() => expect(folderSelect(dialog)).toBeEnabled())
    expect(optionTexts(folderSelect(dialog))).toContain('Фільми — /downloads/movies')
    expect(within(dialog).getByRole('textbox', { name: 'Name' }))
      .toHaveAccessibleDescription('Local Transmission shows this name as the torrent\'s label, so it can\'t contain a comma.')
  })

  it('asks another client of an edit without the destination, as an add would', async () => {
    // Arrange
    const { dialog, user } = renderDialog(filmsFolder)
    await vi.waitFor(() => expect(folderSelect(dialog)).toBeEnabled())

    // Act
    await user.selectOptions(clientSelect(dialog), localQbittorrent.id)

    // Assert
    await vi.waitFor(() => expect(destinationsApi.clientDestinations).toHaveBeenLastCalledWith(localQbittorrent.id))
    expect(vi.mocked(destinationsApi.clientDestinations).mock.lastCall).toHaveLength(1)
    expect(within(dialog).getByRole('combobox', { name: 'Category' })).toHaveValue('')
  })

  it('keeps a saved folder the client can\'t see choosable, with the client\'s sentence under it and not again in the lines', async () => {
    // Arrange
    vi.mocked(destinationsApi.clientDestinations).mockResolvedValue({
      ...sampleFolders,
      items: sampleFolders.items.map((folder) => folder.key === '/downloads/movies'
        ? { ...folder, check: checkOf({ status: 'FolderNotSeen', appPath: null, problem: 'Transmission can\'t see “/downloads/movies” (No such file or directory).' }) }
        : folder),
    })

    // Act
    const { dialog } = renderDialog(filmsFolder)

    // Assert
    await vi.waitFor(() => expect(folderSelect(dialog)).toBeEnabled())
    expect(folderSelect(dialog)).toHaveValue('/downloads/movies')
    expect(optionOf(folderSelect(dialog), '/downloads/movies')).toBeEnabled()
    expect(optionOf(folderSelect(dialog), '/downloads/movies')).toHaveTextContent('Фільми — /downloads/movies (can\'t be used)')
    expect(within(dialog).getByText('Not seen by the client')).toHaveClass('library-badge-warning')
    expect(within(dialog).getByText('Transmission can\'t see “/downloads/movies” (No such file or directory).'))
      .toHaveClass('destinations-check-problem')
    expect(problemLines(dialog).map((line) => line.textContent)).toEqual([
      `Серіали: ${notSeenProblem}`,
      `Документалки: ${unplacedProblem}`,
    ])
  })

  it('marks a saved folder the client no longer offers in a folder\'s words', async () => {
    // Arrange
    vi.mocked(destinationsApi.clientDestinations).mockResolvedValue({
      ...sampleFolders,
      items: sampleFolders.items.filter((folder) => folder.key !== '/downloads/movies'),
    })

    // Act
    const { dialog } = renderDialog(filmsFolder)

    // Assert
    await vi.waitFor(() => expect(optionTexts(folderSelect(dialog))).toContain('/downloads/movies (missing)'))
    expect(folderSelect(dialog)).toHaveValue('/downloads/movies')
    expect(within(dialog).getByText('Not seen by the client')).toHaveClass('library-badge-warning')
    expect(within(dialog).getByText(
      'Local Transmission no longer offers this folder. Pick another one, or check the library and the path mappings.'))
      .toBeInTheDocument()
  })
})
