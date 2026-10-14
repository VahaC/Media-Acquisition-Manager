import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { DestinationDialog } from '@/components/destinations/DestinationDialog'
import { destinationsApi } from '@/lib/destinations-api'
import { integrationsApi } from '@/lib/integrations-api'
import { qk } from '@/lib/queries'
import type { ClientDestinationList, Destination } from '@/lib/types'
import { apiError, networkError } from '@/test/api-error'
import { cartoons, checkOf, destinationsPage, movies, sampleCategories, sectionsOf, spareQbittorrent, tolokaSections } from '@/test/destinations'
import { localQbittorrent } from '@/test/integrations'

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
  return { client, onClose, user: userEvent.setup(), dialog: screen.getByRole('dialog', { name }) }
}

const clientSelect = (dialog: HTMLElement) => within(dialog).getByRole('combobox', { name: 'Download client' })
const categorySelect = (dialog: HTMLElement) => within(dialog).getByRole('combobox', { name: 'Category' })

/** The options of a select as the owner reads them, the placeholder included. */
const optionTexts = (select: HTMLElement) => [...select.querySelectorAll('option')].map((option) => option.textContent)

/** Picks `clientId` once the clients have arrived. */
async function pickClient(user: User, dialog: HTMLElement, clientId: string) {
  await vi.waitFor(() => expect(clientSelect(dialog)).toBeEnabled())
  await user.selectOptions(clientSelect(dialog), clientId)
}

/** Picks `key` once the client has answered with its categories. */
async function pickCategory(user: User, dialog: HTMLElement, key: string) {
  await vi.waitFor(() => expect(categorySelect(dialog)).toBeEnabled())
  await user.selectOptions(categorySelect(dialog), key)
}

describe('DestinationDialog', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    vi.mocked(integrationsApi.list).mockImplementation(async (query) =>
      ({ items: [localQbittorrent, spareQbittorrent], totalCount: 2, page: query.page, pageSize: query.pageSize }))
    vi.mocked(destinationsApi.clientDestinations).mockResolvedValue(sampleCategories)
    vi.mocked(destinationsApi.sections).mockResolvedValue(sectionsOf(tolokaSections))
  })

  it('offers the download clients by name, asked from the server by kind, a switched-off one marked', async () => {
    // Arrange
    const { dialog } = renderDialog()

    // Act
    await vi.waitFor(() => expect(clientSelect(dialog)).toBeEnabled())

    // Assert
    expect(optionTexts(clientSelect(dialog))).toEqual(['Choose a download client', 'Local qBittorrent', 'Spare qBittorrent (switched off)'])
    expect(integrationsApi.list).toHaveBeenCalledExactlyOnceWith({ kind: 'DownloadClient', page: 1, pageSize: 100 })
    expect(categorySelect(dialog)).toBeDisabled()
    expect(categorySelect(dialog)).toHaveAccessibleDescription('Choose a download client first.')
    expect(destinationsApi.clientDestinations).not.toHaveBeenCalled()
  })

  it('asks the chosen client for its categories and shows where the chosen one saves', async () => {
    // Arrange
    const { dialog, user } = renderDialog()
    await pickClient(user, dialog, localQbittorrent.id)

    // Act
    await pickCategory(user, dialog, 'Movies')

    // Assert
    expect(destinationsApi.clientDestinations).toHaveBeenCalledExactlyOnceWith(localQbittorrent.id)
    expect(optionTexts(categorySelect(dialog))).toEqual(['Choose a category', 'Cartoons', 'Documentary', 'Movies', 'TV Shows'])
    expect(within(dialog).getByText('/downloads/movies')).toBeInTheDocument()
    expect(within(dialog).getByText('/media/movies')).toBeInTheDocument()
    expect(categorySelect(dialog)).toHaveAccessibleDescription(/In the media library Фільми$/)
  })

  it('adds a destination with its client, category and the default ticked', async () => {
    // Arrange
    vi.mocked(destinationsApi.create).mockResolvedValue(movies)
    const { dialog, onClose, user } = renderDialog()
    await user.type(within(dialog).getByLabelText('Name'), ' Movies ')
    await pickClient(user, dialog, localQbittorrent.id)
    await pickCategory(user, dialog, 'TV Shows')
    await user.click(within(dialog).getByRole('checkbox', { name: 'Default destination' }))

    // Act
    await user.click(within(dialog).getByRole('button', { name: 'Add destination' }))

    // Assert
    await vi.waitFor(() => expect(onClose).toHaveBeenCalledOnce())
    expect(destinationsApi.create).toHaveBeenCalledExactlyOnceWith({
      name: 'Movies',
      downloadClientId: localQbittorrent.id,
      clientDestinationKey: 'TV Shows',
      isDefault: true,
    })
    expect(destinationsApi.update).not.toHaveBeenCalled()
  })

  it('leaves the client out when none is picked, and shows the server sentence that asks for one', async () => {
    // Arrange
    vi.mocked(destinationsApi.create).mockRejectedValue(apiError(400, 'Pick the download client this destination belongs to.'))
    const { dialog, onClose, user } = renderDialog()
    await user.type(within(dialog).getByLabelText('Name'), 'Movies')

    // Act
    await user.click(within(dialog).getByRole('button', { name: 'Add destination' }))

    // Assert
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('Pick the download client this destination belongs to.')
    expect(destinationsApi.create).toHaveBeenCalledOnce()
    expect(vi.mocked(destinationsApi.create).mock.lastCall?.[0]).not.toHaveProperty('downloadClientId', expect.anything())
    expect(onClose).not.toHaveBeenCalled()
  })

  it('says it is asking the client while the categories are on their way', async () => {
    // Arrange
    let answer: (categories: ClientDestinationList) => void = () => {}
    vi.mocked(destinationsApi.clientDestinations).mockReturnValue(new Promise((resolve) => {
      answer = resolve
    }))
    const { dialog, user } = renderDialog()
    await pickClient(user, dialog, localQbittorrent.id)
    const whileAsking = {
      said: within(dialog).queryByText('Asking the client for its categories…') !== null,
      disabled: (categorySelect(dialog) as HTMLSelectElement).disabled,
    }

    // Act
    await act(async () => answer(sampleCategories))

    // Assert
    await vi.waitFor(() => expect(categorySelect(dialog)).toBeEnabled())
    expect(whileAsking).toEqual({ said: true, disabled: true })
    expect(within(dialog).queryByText('Asking the client for its categories…')).not.toBeInTheDocument()
  })

  it('shows the client\'s own sentence when it couldn\'t be asked', async () => {
    // Arrange
    vi.mocked(destinationsApi.clientDestinations).mockResolvedValue({
      items: [],
      error: '“Spare qBittorrent” is switched off. Switch it on under Integrations to use its categories.',
      destinationKind: 'Category',
      unplaced: [],
    })
    const { dialog, user } = renderDialog()

    // Act
    await pickClient(user, dialog, spareQbittorrent.id)

    // Assert
    const line = await within(dialog).findByRole('alert')
    expect(line).toHaveTextContent('“Spare qBittorrent” is switched off. Switch it on under Integrations to use its categories.')
    // The red line under the select, which itself isn't marked invalid: nothing picked in it is wrong.
    expect(line).toHaveClass('ui-field-error')
    expect(categorySelect(dialog)).toHaveAccessibleDescription(
      '“Spare qBittorrent” is switched off. Switch it on under Integrations to use its categories.')
    expect(categorySelect(dialog)).not.toHaveAttribute('aria-invalid')
    expect(optionTexts(categorySelect(dialog))).toEqual(['Choose a category'])
  })

  it('shows its own sentence when the categories can\'t be read and the API gives none', async () => {
    // Arrange
    vi.mocked(destinationsApi.clientDestinations).mockRejectedValue(apiError(404))
    const { dialog, user } = renderDialog()

    // Act
    await pickClient(user, dialog, localQbittorrent.id)

    // Assert
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('Could not ask the client for its categories.')
    expect(categorySelect(dialog)).toBeDisabled()
  })

  it('says when the client has no categories at all', async () => {
    // Arrange
    vi.mocked(destinationsApi.clientDestinations).mockResolvedValue({ ...sampleCategories, items: [] })
    const { dialog, user } = renderDialog()

    // Act
    await pickClient(user, dialog, localQbittorrent.id)

    // Assert
    expect(await within(dialog).findByText('Local qBittorrent has no categories. Create one in the client, then open this dialog again.'))
      .toBeInTheDocument()
  })

  it('says under the client field when the download clients could not be loaded', async () => {
    // Arrange
    vi.mocked(integrationsApi.list).mockRejectedValue(networkError())

    // Act
    const { dialog } = renderDialog()

    // Assert
    const line = await within(dialog).findByRole('alert')
    expect(line).toHaveTextContent('The server could not be reached.')
    expect(line).toHaveClass('ui-field-error')
    expect(clientSelect(dialog)).toBeDisabled()
    expect(clientSelect(dialog)).not.toHaveAttribute('aria-invalid')
  })

  it('says to add a download client first when there is none', async () => {
    // Arrange
    vi.mocked(integrationsApi.list).mockImplementation(async (query) =>
      ({ items: [], totalCount: 0, page: query.page, pageSize: query.pageSize }))

    // Act
    const { dialog } = renderDialog()

    // Assert
    expect(await within(dialog).findByText('No download client yet. Add one under Integrations first.')).toBeInTheDocument()
    expect(optionTexts(clientSelect(dialog))).toEqual(['Choose a download client'])
  })

  // MAM-31 — the live check saw "Pick a category of your download client", "Category" and "Choose a
  // category" on the first open with Transmission alone, while the clients were on their way.
  it.each<[string, () => void, string | RegExp]>([
    ['are on their way', () => vi.mocked(integrationsApi.list).mockReturnValue(new Promise(() => {})), 'Loading…'],
    ['could not be loaded', () => vi.mocked(integrationsApi.list).mockRejectedValue(networkError()), /^The server could not be reached\./],
    ['are none', () => vi.mocked(integrationsApi.list).mockImplementation(async (query) =>
      ({ items: [], totalCount: 0, page: query.page, pageSize: query.pageSize })), 'No download client yet. Add one under Integrations first.'],
  ])('says neither a category nor a folder while the download clients %s', async (_, clientsListed, clientLine) => {
    // Arrange
    clientsListed()

    // Act
    const { dialog } = renderDialog()

    // Assert
    await within(dialog).findByText(clientLine)
    await within(dialog).findByRole('group', { name: 'Standard sections' })
    const field = within(dialog).getByRole('combobox', { name: 'Saves to' })
    expect(dialog).toHaveAccessibleDescription(
      'Pick a download client, then where it saves. The app checks with the client that it can save there.')
    expect(field).toBeDisabled()
    expect(optionTexts(field)).toEqual(['Choose where it saves'])
    expect(field).toHaveAccessibleDescription('Choose a download client first.')
    expect(dialog).not.toHaveTextContent(/categor|folder/i)
  })

  it('edits a destination whose client type isn\'t registered in neutral words while its client is being asked', async () => {
    // Arrange
    // MAM-31 — neither the clients list nor the destination says what its client saves to.
    vi.mocked(integrationsApi.list).mockImplementation(async (query) =>
      ({ items: [{ ...localQbittorrent, destinationKind: null }], totalCount: 1, page: query.page, pageSize: query.pageSize }))
    vi.mocked(destinationsApi.clientDestinations).mockReturnValue(new Promise(() => {}))

    // Act
    const { dialog } = renderDialog({ ...movies, destinationKind: null })

    // Assert
    const field = within(dialog).getByRole('combobox', { name: 'Saves to' })
    await vi.waitFor(() => expect(field).toHaveAccessibleDescription('Asking Local qBittorrent where it can save…'))
    expect(dialog).toHaveAccessibleDescription('A new client, or a new place it saves to, is checked with the client before it is saved.')
  })

  it('edits a destination whose client type isn\'t registered in neutral words when its client can\'t be asked and the API gives no sentence', async () => {
    // Arrange
    // MAM-31 — the fallback of the neutral words, as "Could not ask the client for its categories." is the category's.
    vi.mocked(integrationsApi.list).mockImplementation(async (query) =>
      ({ items: [{ ...localQbittorrent, destinationKind: null }], totalCount: 1, page: query.page, pageSize: query.pageSize }))
    vi.mocked(destinationsApi.clientDestinations).mockRejectedValue(apiError(404))

    // Act
    const { dialog } = renderDialog({ ...movies, destinationKind: null })

    // Assert
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('Could not ask the client where it can save.')
    expect(within(dialog).getByRole('combobox', { name: 'Saves to' })).toBeInTheDocument()
    expect(dialog).not.toHaveTextContent(/categor|folder/i)
  })

  it('forgets the category picked for one client when another is picked', async () => {
    // Arrange
    vi.mocked(destinationsApi.clientDestinations).mockImplementation(async (id) =>
      id === localQbittorrent.id ? sampleCategories : { ...sampleCategories, items: [sampleCategories.items[0]] })
    const { dialog, user } = renderDialog()
    await pickClient(user, dialog, localQbittorrent.id)
    await pickCategory(user, dialog, 'Movies')

    // Act
    await user.selectOptions(clientSelect(dialog), spareQbittorrent.id)

    // Assert
    await vi.waitFor(() => expect(optionTexts(categorySelect(dialog))).toEqual(['Choose a category', 'Cartoons']))
    expect(categorySelect(dialog)).toHaveValue('')
    expect(within(dialog).queryByText('/downloads/movies')).not.toBeInTheDocument()
    expect(destinationsApi.clientDestinations).toHaveBeenLastCalledWith(spareQbittorrent.id)
  })

  it('opens an edit with the saved values and saves a new name with the same category', async () => {
    // Arrange
    vi.mocked(destinationsApi.update).mockResolvedValue()
    const { dialog, onClose, user } = renderDialog(cartoons)
    await vi.waitFor(() => expect(categorySelect(dialog)).toBeEnabled())
    const shown = {
      name: (within(dialog).getByLabelText('Name') as HTMLInputElement).value,
      client: (clientSelect(dialog) as HTMLSelectElement).value,
      category: (categorySelect(dialog) as HTMLSelectElement).value,
      isDefault: (within(dialog).getByRole('checkbox', { name: 'Default destination' }) as HTMLInputElement).checked,
    }
    await user.clear(within(dialog).getByLabelText('Name'))
    await user.type(within(dialog).getByLabelText('Name'), 'Animation')

    // Act
    await user.click(within(dialog).getByRole('button', { name: 'Save changes' }))

    // Assert
    await vi.waitFor(() => expect(onClose).toHaveBeenCalledOnce())
    expect(shown).toEqual({ name: 'Cartoons', client: localQbittorrent.id, category: 'Cartoons', isDefault: false })
    expect(destinationsApi.update).toHaveBeenCalledExactlyOnceWith(cartoons.id, {
      name: 'Animation',
      downloadClientId: localQbittorrent.id,
      clientDestinationKey: 'Cartoons',
      isDefault: false,
    })
    expect(destinationsApi.create).not.toHaveBeenCalled()
  })

  it('unticks the default on an edit', async () => {
    // Arrange
    vi.mocked(destinationsApi.update).mockResolvedValue()
    const { dialog, user } = renderDialog(movies)
    await user.click(within(dialog).getByRole('checkbox', { name: 'Default destination' }))

    // Act
    await user.click(within(dialog).getByRole('button', { name: 'Save changes' }))

    // Assert
    await vi.waitFor(() => expect(destinationsApi.update).toHaveBeenCalledOnce())
    expect(vi.mocked(destinationsApi.update).mock.lastCall?.[1]).toMatchObject({ isDefault: false })
  })

  it('keeps a saved category the client no longer has choosable, marked as missing', async () => {
    // Arrange
    const gone: Destination = {
      ...movies,
      name: 'Movies 4K',
      clientDestinationKey: 'Movies 4K',
      check: checkOf({ status: 'CategoryMissing', servicePath: null, appPath: null, libraryNames: [], problem: '“Local qBittorrent” has no category “Movies 4K” any more.' }),
    }

    // Act
    const { dialog } = renderDialog(gone)

    // Assert
    await vi.waitFor(() => expect(optionTexts(categorySelect(dialog))).toContain('Movies 4K (missing)'))
    expect(categorySelect(dialog)).toHaveValue('Movies 4K')
    expect(within(dialog).getByText('Category missing')).toHaveClass('library-badge-warning')
    expect(within(dialog).getByText('Local qBittorrent no longer has this category. Pick another one, or create it in the client again.'))
      .toBeInTheDocument()
  })

  it('keeps the saved category as it is while the client couldn\'t be asked', async () => {
    // Arrange
    vi.mocked(destinationsApi.clientDestinations).mockResolvedValue({
      items: [],
      error: 'qBittorrent didn\'t answer at http://127.0.0.1:9/qbt. Check the address and that qBittorrent is running.',
      destinationKind: 'Category',
      unplaced: [],
    })

    // Act
    const { dialog } = renderDialog(cartoons)

    // Assert
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('qBittorrent didn\'t answer at http://127.0.0.1:9/qbt.')
    expect(optionTexts(categorySelect(dialog))).toEqual(['Choose a category', 'Cartoons'])
    expect(categorySelect(dialog)).toHaveValue('Cartoons')
  })

  it('shows the server sentence and stays open when saving is refused', async () => {
    // Arrange
    vi.mocked(destinationsApi.create).mockRejectedValue(
      apiError(502, 'qBittorrent didn\'t answer at http://127.0.0.1:9/qbt. Check the address and that qBittorrent is running.'))
    const { dialog, onClose, user } = renderDialog()
    await user.type(within(dialog).getByLabelText('Name'), 'Movies')
    await pickClient(user, dialog, localQbittorrent.id)
    await pickCategory(user, dialog, 'Movies')

    // Act
    await user.click(within(dialog).getByRole('button', { name: 'Add destination' }))

    // Assert
    const banner = await within(dialog).findByRole('alert')
    expect(banner).toHaveTextContent('qBittorrent didn\'t answer at http://127.0.0.1:9/qbt. Check the address and that qBittorrent is running.')
    // Above the buttons, outside the scrolling fields.
    expect(banner).toHaveClass('ui-banner')
    expect(banner.nextElementSibling).toContainElement(within(dialog).getByRole('button', { name: 'Add destination' }))
    expect(onClose).not.toHaveBeenCalled()
    expect(within(dialog).getByRole('button', { name: 'Add destination' })).toBeEnabled()
    expect(within(dialog).getByLabelText('Name')).toHaveValue('Movies')
    expect(categorySelect(dialog)).toHaveValue('Movies')
  })

  it('closes once the save succeeds', async () => {
    // Arrange
    let finishSaving: () => void = () => {}
    vi.mocked(destinationsApi.update).mockReturnValue(new Promise<void>((resolve) => {
      finishSaving = () => resolve()
    }))
    const { dialog, onClose, user } = renderDialog(movies)
    await user.click(within(dialog).getByRole('button', { name: 'Save changes' }))
    const disabledWhileSaving = (within(dialog).getByRole('button', { name: 'Saving…' }) as HTMLButtonElement).disabled
    const closedWhileSaving = onClose.mock.calls.length

    // Act
    await act(async () => finishSaving())

    // Assert
    await vi.waitFor(() => expect(onClose).toHaveBeenCalledOnce())
    expect(disabledWhileSaving).toBe(true)
    expect(closedWhileSaving).toBe(0)
  })

  it('refetches the destination lists once a save succeeds, and leaves the clients\' categories be', async () => {
    // Arrange
    vi.mocked(destinationsApi.update).mockResolvedValue()
    const { client, dialog, onClose, user } = renderDialog(movies)
    const listQuery = { search: undefined, page: 1, pageSize: 25 }
    client.setQueryData(qk.settings.destinations.list(listQuery), destinationsPage(listQuery, [movies]))
    await vi.waitFor(() => expect(categorySelect(dialog)).toBeEnabled())

    // Act
    await user.click(within(dialog).getByRole('button', { name: 'Save changes' }))

    // Assert
    await vi.waitFor(() => expect(onClose).toHaveBeenCalledOnce())
    expect(client.getQueryState(qk.settings.destinations.list(listQuery))?.isInvalidated).toBe(true)
    expect(client.getQueryState(qk.settings.destinations.clientDestinations(localQbittorrent.id, movies.id))?.isInvalidated).toBe(false)
  })

  it('puts the primary button first and Cancel last', async () => {
    // Arrange
    const { dialog } = renderDialog(movies)

    // Act
    const footer = within(dialog).getByRole('button', { name: 'Cancel' }).parentElement!

    // Assert
    expect(within(footer).getAllByRole('button').map((button) => button.textContent)).toEqual(['Save changes', 'Cancel'])
  })

  // MAM-31 — the owner asked for at least 1,024 px in the live check. The class gives the width
  // (destinations.css, pinned by downloads.test.ts); the settings dialog's keeps the scroller and buttons.
  it.each<[string, Destination | undefined]>([
    ['adding', undefined],
    ['editing', movies],
  ])('opens as the wide destination dialog when %s, laid out as a settings dialog', (_, destination) => {
    // Arrange

    // Act
    const { dialog } = renderDialog(destination)

    // Assert
    expect(dialog).toHaveClass('settings-dialog', 'destinations-dialog')
  })

  // MAM-31 — the owner asked in the live check for the name beside the download client on a wide screen,
  // as the integration dialog pairs its short fields; the pair's columns are destinations.css's, pinned by
  // downloads.test.ts. The category field and the rest keep the whole width.
  it('pairs the name with the download client, and leaves the category field out of the pair', async () => {
    // Arrange
    const { dialog } = renderDialog()

    // Act
    await vi.waitFor(() => expect(clientSelect(dialog)).toBeEnabled())

    // Assert
    const pair = within(dialog).getByRole('textbox', { name: 'Name' }).closest('.auth-field:not(label)')!.parentElement!
    expect(pair).toHaveClass('destinations-pair')
    expect(pair.children).toHaveLength(2)
    expect(pair).toContainElement(clientSelect(dialog))
    expect(pair).not.toContainElement(categorySelect(dialog))
    expect(pair.parentElement).toHaveClass('settings-dialog-body')
  })

  it('closes on Cancel without saving anything', async () => {
    // Arrange
    const { dialog, onClose, user } = renderDialog(movies)

    // Act
    await user.click(within(dialog).getByRole('button', { name: 'Cancel' }))

    // Assert
    expect(onClose).toHaveBeenCalledOnce()
    expect(destinationsApi.update).not.toHaveBeenCalled()
  })
})
