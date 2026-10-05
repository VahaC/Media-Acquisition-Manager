import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { IntegrationDialog } from '@/components/integrations/IntegrationDialog'
import { integrationsApi } from '@/lib/integrations-api'
import { qk } from '@/lib/queries'
import type { IntegrationSummary } from '@/lib/types'
import { apiError, networkError } from '@/test/api-error'
import { adapterTypes, homeJellyfin, localQbittorrent } from '@/test/integrations'
import { locations, sourceOf, sourcesOf } from '@/test/library'

vi.mock('@/lib/integrations-api')

type User = ReturnType<typeof userEvent.setup>

function renderDialog(integration?: IntegrationSummary) {
  const onClose = vi.fn()
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <IntegrationDialog integration={integration} onClose={onClose} />
    </QueryClientProvider>,
  )
  const name = integration ? `Edit ${integration.name}` : 'Add integration'
  return { client, onClose, user: userEvent.setup(), dialog: screen.getByRole('dialog', { name }) }
}

/** Waits until the service types have arrived: until then nothing can be tested or saved. */
async function whenReady(dialog: HTMLElement) {
  await vi.waitFor(() => expect(within(dialog).getByRole('button', { name: 'Test connection' })).toBeEnabled())
}

async function pickService(user: User, dialog: HTMLElement, typeId: string) {
  await user.selectOptions(within(dialog).getByRole('combobox', { name: 'Service' }), typeId)
}

/** What the last save or draft test sent. */
const lastCreate = () => vi.mocked(integrationsApi.create).mock.lastCall?.[0]
const lastUpdate = () => vi.mocked(integrationsApi.update).mock.lastCall?.[1]
const lastTestDraft = () => vi.mocked(integrationsApi.testDraft).mock.lastCall?.[0]

describe('IntegrationDialog', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    vi.mocked(integrationsApi.adapterTypes).mockResolvedValue(adapterTypes)
  })

  it.each([
    ['jellyfin', ['API key'], ['User name', 'Password'], true],
    ['torznab', ['API key'], ['User name', 'Password'], false],
    ['qbittorrent', ['User name (optional)', 'Password (optional)'], ['API key'], true],
    ['tmdb', ['API key'], ['User name', 'Password'], false],
  ])('offers the services grouped by kind and shows only the fields %s takes', async (typeId, shown, hidden, mappings) => {
    // Arrange
    const { dialog, user } = renderDialog()
    await whenReady(dialog)

    // Act
    await pickService(user, dialog, typeId)

    // Assert
    const groups = [...dialog.querySelectorAll('optgroup')].map((group) =>
      [group.label, [...group.querySelectorAll('option')].map((option) => option.textContent)])
    expect(groups).toEqual([
      ['Media libraries', ['Jellyfin']],
      ['Release sources', ['Torznab']],
      ['Download clients', ['qBittorrent']],
      ['Metadata catalogs', ['TMDb']],
    ])
    for (const label of shown) expect(within(dialog).getByLabelText(label)).toBeInTheDocument()
    for (const label of hidden) expect(within(dialog).queryByLabelText(new RegExp(`^${label}`))).not.toBeInTheDocument()
    expect(within(dialog).queryByRole('group', { name: 'Path mappings' }) !== null).toBe(mappings)
    expect(within(dialog).getByLabelText('Name')).toBeInTheDocument()
    expect(within(dialog).getByLabelText('Address')).toBeInTheDocument()
  })

  it.each<[string, IntegrationSummary | undefined, string | undefined, string]>([
    ['a new integration before a service is picked', undefined, undefined, 'http://192.168.1.10'],
    ['Jellyfin', undefined, 'jellyfin', 'http://192.168.1.10:8096'],
    ['Torznab', undefined, 'torznab', 'http://192.168.1.10:9696/1/api'],
    ['qBittorrent', undefined, 'qbittorrent', 'http://192.168.1.10:8080'],
    ['TMDb', undefined, 'tmdb', 'https://api.themoviedb.org'],
    ['a saved qBittorrent', localQbittorrent, undefined, 'http://192.168.1.10:8080'],
  ])('shows an example address that fits %s', async (_, integration, typeId, example) => {
    // Arrange
    const { dialog, user } = renderDialog(integration)
    await whenReady(dialog)

    // Act
    if (typeId) await pickService(user, dialog, typeId)

    // Assert
    expect(within(dialog).getByLabelText('Address')).toHaveAttribute('placeholder', example)
  })

  it('marks optional credentials as optional', async () => {
    // Arrange
    const { dialog, user } = renderDialog()
    await whenReady(dialog)
    // Exact names: "(optional)" is part of each caption, or of none.
    const field = (name: string) => within(dialog).queryByLabelText(name)

    // Act
    await pickService(user, dialog, 'qbittorrent')
    const qbittorrent = [field('User name (optional)'), field('Password (optional)')]
    await pickService(user, dialog, 'jellyfin')
    const jellyfin = [field('API key'), field('API key (optional)')]

    // Assert
    expect(qbittorrent).not.toContain(null)
    // Jellyfin can't do without its key: no "(optional)" on it.
    expect(jellyfin[0]).toBeInTheDocument()
    expect(jellyfin[1]).toBeNull()
  })

  it('shows TMDb’s attribution notice while TMDb is the service, and only then', async () => {
    // Arrange
    const notice = 'This product uses the TMDB API but is not endorsed or certified by TMDB.'
    const { dialog, user } = renderDialog()
    await whenReady(dialog)

    // Act
    await pickService(user, dialog, 'tmdb')
    const shownForTmdb = within(dialog).queryByText(notice)
    await pickService(user, dialog, 'jellyfin')
    const shownForJellyfin = within(dialog).queryByText(notice)

    // Assert
    expect(shownForTmdb).toHaveClass('auth-hint')
    expect(shownForJellyfin).toBeNull()
  })

  it('shows TMDb’s attribution notice on a saved TMDb too', async () => {
    // Arrange
    const savedTmdb: IntegrationSummary = {
      ...homeJellyfin,
      id: '5d1c7a90-0000-4000-8000-000000000005',
      name: 'Catalog',
      adapterType: 'tmdb',
      adapterDisplayName: 'TMDb',
      kind: 'Metadata',
      pathMappings: [],
    }

    // Act
    const { dialog } = renderDialog(savedTmdb)
    await whenReady(dialog)

    // Assert
    expect(within(dialog).getByText('This product uses the TMDB API but is not endorsed or certified by TMDB.')).toBeInTheDocument()
    expect(within(dialog).getByLabelText('Address')).toHaveAttribute('placeholder', 'https://api.themoviedb.org')
  })

  it('adds an integration with its secrets, switch and path mappings', async () => {
    // Arrange
    vi.mocked(integrationsApi.create).mockResolvedValue(localQbittorrent)
    const { dialog, onClose, user } = renderDialog()
    await whenReady(dialog)
    await pickService(user, dialog, 'qbittorrent')
    await user.type(within(dialog).getByLabelText('Name'), ' Local qBittorrent ')
    await user.type(within(dialog).getByLabelText('Address'), 'http://127.0.0.1:9/qbt')
    await user.type(within(dialog).getByLabelText('User name (optional)'), 'admin')
    await user.type(within(dialog).getByLabelText('Password (optional)'), ' secret pass ')
    await user.click(within(dialog).getByRole('checkbox', { name: 'Use this integration' }))
    await user.click(within(dialog).getByRole('button', { name: 'Add mapping' }))
    await user.type(within(dialog).getByLabelText('Path in qBittorrent'), '/downloads')
    await user.type(within(dialog).getByLabelText('Path in this app'), '/media')

    // Act
    await user.click(within(dialog).getByRole('button', { name: 'Add integration' }))

    // Assert
    await vi.waitFor(() => expect(onClose).toHaveBeenCalledOnce())
    expect(integrationsApi.create).toHaveBeenCalledExactlyOnceWith({
      adapterType: 'qbittorrent',
      name: 'Local qBittorrent',
      baseUrl: 'http://127.0.0.1:9/qbt',
      userName: 'admin',
      password: { mode: 'Set', value: ' secret pass ' },
      isEnabled: false,
      pathMappings: [{ servicePrefix: '/downloads', appPrefix: '/media' }],
    })
    // qBittorrent takes no API key, so none is sent.
    expect(lastCreate()?.apiKey).toBeUndefined()
    expect(integrationsApi.testDraft).not.toHaveBeenCalled()
  })

  it('forgets what was typed for one service when another is picked', async () => {
    // Arrange
    const { dialog, user } = renderDialog()
    await whenReady(dialog)
    await pickService(user, dialog, 'qbittorrent')
    await user.type(within(dialog).getByLabelText('User name (optional)'), 'admin')
    await user.type(within(dialog).getByLabelText('Password (optional)'), 'adminadmin')
    await user.click(within(dialog).getByRole('button', { name: 'Add mapping' }))

    // Act
    await pickService(user, dialog, 'jellyfin')
    await pickService(user, dialog, 'qbittorrent')

    // Assert
    expect(within(dialog).getByLabelText('User name (optional)')).toHaveValue('')
    expect(within(dialog).getByLabelText('Password (optional)')).toHaveValue('')
    expect(within(dialog).queryByLabelText('Path in qBittorrent')).not.toBeInTheDocument()
  })

  it('shows the server sentence and stays open when saving is refused', async () => {
    // Arrange
    vi.mocked(integrationsApi.create).mockRejectedValue(apiError(409, 'An integration named “Home Jellyfin” already exists.'))
    const { dialog, onClose, user } = renderDialog()
    await whenReady(dialog)
    await pickService(user, dialog, 'jellyfin')
    await user.type(within(dialog).getByLabelText('Name'), 'Home Jellyfin')

    // Act
    await user.click(within(dialog).getByRole('button', { name: 'Add integration' }))

    // Assert
    const banner = await within(dialog).findByRole('alert')
    expect(banner).toHaveTextContent('An integration named “Home Jellyfin” already exists.')
    // Above the buttons, outside the scrolling fields.
    expect(banner).toHaveClass('ui-banner')
    expect(banner.nextElementSibling).toContainElement(within(dialog).getByRole('button', { name: 'Add integration' }))
    expect(onClose).not.toHaveBeenCalled()
    expect(within(dialog).getByRole('button', { name: 'Add integration' })).toBeEnabled()
    expect(within(dialog).getByLabelText('Name')).toHaveValue('Home Jellyfin')
  })

  it('says the services could not be loaded, keeping Save disabled, and Try again loads them', async () => {
    // Arrange
    vi.mocked(integrationsApi.adapterTypes).mockRejectedValueOnce(networkError()).mockResolvedValueOnce(adapterTypes)
    const { dialog, user } = renderDialog()
    const state = await within(dialog).findByRole('alert')
    const saveDisabledMeanwhile = within(dialog).getByRole('button', { name: 'Add integration' }).hasAttribute('disabled')

    // Act
    await user.click(within(dialog).getByRole('button', { name: 'Try again' }))

    // Assert
    expect(state).toHaveTextContent('Could not load the services this app can connect to.')
    expect(state).toHaveTextContent('The server could not be reached.')
    expect(saveDisabledMeanwhile).toBe(true)
    await whenReady(dialog)
    expect(within(dialog).queryByRole('alert')).not.toBeInTheDocument()
    expect(within(dialog).getByRole('combobox', { name: 'Service' })).toBeEnabled()
  })

  it('closes once the save succeeds', async () => {
    // Arrange
    let finishSaving: () => void = () => {}
    vi.mocked(integrationsApi.update).mockReturnValue(new Promise<void>((resolve) => {
      finishSaving = () => resolve()
    }))
    const { dialog, onClose, user } = renderDialog(homeJellyfin)
    await whenReady(dialog)
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

  it.each<[string, IntegrationSummary | undefined, string, (user: User, dialog: HTMLElement) => Promise<void>]>([
    ['an integration is added', undefined, 'Add integration', async (user, dialog) => {
      await pickService(user, dialog, 'jellyfin')
      await user.type(within(dialog).getByLabelText('Name'), 'Office Jellyfin')
    }],
    ['an integration is saved', homeJellyfin, 'Save changes', async () => {}],
  ])('drops the cached library once %s, so the Library page loads afresh', async (_, integration, save, fill) => {
    // Arrange — the Library was opened earlier in this visit, and its answers are still cached.
    vi.mocked(integrationsApi.create).mockResolvedValue(homeJellyfin)
    vi.mocked(integrationsApi.update).mockResolvedValue()
    const { client, dialog, onClose, user } = renderDialog(integration)
    client.setQueryData(qk.library.locations, locations)
    client.setQueryData(qk.library.sources, sourcesOf(sourceOf({})))
    const cachedBefore = client.getQueryCache().findAll({ queryKey: qk.library.all }).length
    await whenReady(dialog)
    await fill(user, dialog)

    // Act
    await user.click(within(dialog).getByRole('button', { name: save }))

    // Assert
    await vi.waitFor(() => expect(onClose).toHaveBeenCalledOnce())
    expect(cachedBefore).toBe(2)
    expect(client.getQueryCache().findAll({ queryKey: qk.library.all })).toEqual([])
  })

  it('edits without sending a secret left empty', async () => {
    // Arrange
    vi.mocked(integrationsApi.update).mockResolvedValue()
    const { dialog, onClose, user } = renderDialog(homeJellyfin)
    await whenReady(dialog)
    const name = within(dialog).getByLabelText('Name')
    await user.clear(name)
    await user.type(name, 'Living-room Jellyfin')

    // Act
    await user.click(within(dialog).getByRole('button', { name: 'Save changes' }))

    // Assert
    await vi.waitFor(() => expect(onClose).toHaveBeenCalledOnce())
    expect(integrationsApi.update).toHaveBeenCalledExactlyOnceWith(homeJellyfin.id, {
      name: 'Living-room Jellyfin',
      baseUrl: homeJellyfin.baseUrl,
      isEnabled: true,
      pathMappings: homeJellyfin.pathMappings,
    })
    // Left out, not sent as Keep or empty: the server keeps the stored key.
    expect(lastUpdate()).not.toHaveProperty('apiKey', expect.anything())
    expect(lastUpdate()).not.toHaveProperty('adapterType')
  })

  it.each<[string, IntegrationSummary, (user: User, dialog: HTMLElement) => Promise<void>, object]>([
    [
      'a new API key as Set',
      homeJellyfin,
      (user, dialog) => user.type(within(dialog).getByLabelText('API key'), 'b2c8f0e4a9d14f7e'),
      { apiKey: { mode: 'Set', value: 'b2c8f0e4a9d14f7e' } },
    ],
    [
      'a removed optional password as Clear',
      localQbittorrent,
      async (user, dialog) => {
        await user.click(within(dialog).getByRole('checkbox', { name: 'Remove the stored password' }))
        await user.clear(within(dialog).getByLabelText('User name (optional)'))
      },
      { password: { mode: 'Clear' }, userName: undefined },
    ],
  ])('sends %s', async (_, integration, change, expected) => {
    // Arrange
    vi.mocked(integrationsApi.update).mockResolvedValue()
    const { dialog, user } = renderDialog(integration)
    await whenReady(dialog)
    await change(user, dialog)

    // Act
    await user.click(within(dialog).getByRole('button', { name: 'Save changes' }))

    // Assert
    await vi.waitFor(() => expect(integrationsApi.update).toHaveBeenCalledOnce())
    expect(lastUpdate()).toMatchObject(expected)
  })

  it('never shows or prefills a stored secret', async () => {
    // Arrange
    const integration = { ...localQbittorrent, hasPassword: true }

    // Act
    const { dialog } = renderDialog(integration)
    await whenReady(dialog)

    // Assert
    const password = within(dialog).getByLabelText('Password (optional)')
    expect(password).toHaveValue('')
    expect(password).toHaveAttribute('type', 'password')
    expect(password).toHaveAttribute('placeholder', 'Stored. Leave empty to keep it.')
    expect(within(dialog).getByLabelText('User name (optional)')).toHaveValue('admin')
  })

  it('tests the unsaved draft and shows the result without saving', async () => {
    // Arrange
    vi.mocked(integrationsApi.testDraft).mockResolvedValue({ status: 'Succeeded', message: 'Connected to Jellyfin 12.1.0 (VahaC media).' })
    const { dialog, onClose, user } = renderDialog()
    await whenReady(dialog)
    await pickService(user, dialog, 'jellyfin')
    await user.type(within(dialog).getByLabelText('Address'), ' http://127.0.0.1:9 ')
    await user.type(within(dialog).getByLabelText('API key'), 'b2c8f0e4a9d14f7e')

    // Act
    await user.click(within(dialog).getByRole('button', { name: 'Test connection' }))

    // Assert
    const result = await within(dialog).findByRole('status')
    expect(result).toHaveTextContent('Connected to Jellyfin 12.1.0 (VahaC media).')
    expect(result).toHaveAttribute('data-outcome', 'ok')
    expect(integrationsApi.testDraft).toHaveBeenCalledExactlyOnceWith({
      adapterType: 'jellyfin',
      baseUrl: 'http://127.0.0.1:9',
      apiKey: { mode: 'Set', value: 'b2c8f0e4a9d14f7e' },
    })
    expect(integrationsApi.create).not.toHaveBeenCalled()
    expect(onClose).not.toHaveBeenCalled()
  })

  it('tests an edit with the integration id so a blank secret uses the stored one', async () => {
    // Arrange
    vi.mocked(integrationsApi.testDraft).mockResolvedValue({ status: 'Succeeded', message: 'Connected to Jellyfin 12.1.0 (VahaC media).' })
    const { dialog, user } = renderDialog(homeJellyfin)
    await whenReady(dialog)

    // Act
    await user.click(within(dialog).getByRole('button', { name: 'Test connection' }))

    // Assert
    expect(await within(dialog).findByRole('status')).toHaveTextContent('Connected to Jellyfin 12.1.0 (VahaC media).')
    expect(integrationsApi.testDraft).toHaveBeenCalledExactlyOnceWith({ integrationId: homeJellyfin.id, baseUrl: homeJellyfin.baseUrl })
    expect(lastTestDraft()).not.toHaveProperty('apiKey', expect.anything())
    expect(lastTestDraft()).not.toHaveProperty('adapterType')
    expect(integrationsApi.update).not.toHaveBeenCalled()
  })

  it.each<[string, string, () => void]>([
    [
      'the service\'s answer',
      'Jellyfin refused the API key. Create one in Jellyfin under Dashboard → API Keys and enter it here.',
      () => vi.mocked(integrationsApi.testDraft).mockResolvedValue({
        status: 'AuthenticationFailed',
        message: 'Jellyfin refused the API key. Create one in Jellyfin under Dashboard → API Keys and enter it here.',
      }),
    ],
    [
      'the API\'s refusal of the draft',
      'An API key must be at least 8 characters long.',
      () => vi.mocked(integrationsApi.testDraft).mockRejectedValue(apiError(400, 'An API key must be at least 8 characters long.')),
    ],
  ])('shows a refused test as the result line: %s', async (_, sentence, answer) => {
    // Arrange
    answer()
    const { dialog, user } = renderDialog(homeJellyfin)
    await whenReady(dialog)

    // Act
    await user.click(within(dialog).getByRole('button', { name: 'Test connection' }))

    // Assert
    const result = await within(dialog).findByRole('status')
    expect(result).toHaveTextContent(sentence)
    expect(result).toHaveAttribute('data-outcome', 'err')
    expect(within(dialog).queryByRole('alert')).not.toBeInTheDocument()
  })

  it('keeps the service fixed when editing', async () => {
    // Arrange
    const integration = homeJellyfin

    // Act
    const { dialog } = renderDialog(integration)
    await whenReady(dialog)

    // Assert
    expect(within(dialog).queryByRole('combobox', { name: 'Service' })).not.toBeInTheDocument()
    const service = within(dialog).getByLabelText('Service')
    expect(service).toHaveValue('Jellyfin')
    expect(service).toHaveAttribute('readonly')
    expect(service).toHaveAccessibleDescription('The service can\'t be changed. To connect another one, add a new integration.')
  })

  it('shows the stored path mappings, and saves none once they are all removed', async () => {
    // Arrange
    vi.mocked(integrationsApi.update).mockResolvedValue()
    const { dialog, user } = renderDialog(localQbittorrent)
    await whenReady(dialog)
    const shown = [
      within(dialog).getByLabelText('Path in qBittorrent'),
      within(dialog).getByLabelText('Path in this app'),
    ].map((input) => (input as HTMLInputElement).value)
    await user.click(within(dialog).getByRole('button', { name: 'Remove mapping' }))

    // Act
    await user.click(within(dialog).getByRole('button', { name: 'Save changes' }))

    // Assert
    await vi.waitFor(() => expect(integrationsApi.update).toHaveBeenCalledOnce())
    expect(shown).toEqual(['/downloads', '/media'])
    expect(lastUpdate()).toMatchObject({ pathMappings: [] })
  })

  it('hides path mappings for a service that reports no file paths', async () => {
    // Arrange
    vi.mocked(integrationsApi.create).mockResolvedValue(homeJellyfin)
    const { dialog, user } = renderDialog()
    await whenReady(dialog)
    await pickService(user, dialog, 'qbittorrent')
    await user.click(within(dialog).getByRole('button', { name: 'Add mapping' }))
    await pickService(user, dialog, 'torznab')

    // Act
    await user.click(within(dialog).getByRole('button', { name: 'Add integration' }))

    // Assert
    await vi.waitFor(() => expect(integrationsApi.create).toHaveBeenCalledOnce())
    expect(within(dialog).queryByRole('group', { name: 'Path mappings' })).not.toBeInTheDocument()
    expect(lastCreate()?.pathMappings).toBeUndefined()
  })

  it('puts the primary button first and Cancel last', async () => {
    // Arrange
    const { dialog } = renderDialog(homeJellyfin)
    await whenReady(dialog)

    // Act
    const footer = within(dialog).getByRole('button', { name: 'Cancel' }).parentElement!

    // Assert
    expect(within(footer).getAllByRole('button').map((button) => button.textContent)).toEqual(['Save changes', 'Cancel'])
  })

  it('uses new-password autocompletion for secrets', async () => {
    // Arrange
    const { dialog, user } = renderDialog()
    await whenReady(dialog)

    // Act
    await pickService(user, dialog, 'qbittorrent')
    const password = within(dialog).getByLabelText('Password (optional)').getAttribute('autocomplete')
    await pickService(user, dialog, 'jellyfin')
    const apiKey = within(dialog).getByLabelText('API key')

    // Assert
    // Anything else and the browser offers the owner's own sign-in for this app.
    expect(password).toBe('new-password')
    expect(apiKey).toHaveAttribute('autocomplete', 'new-password')
    expect(apiKey).toHaveAttribute('type', 'password')
  })

  it('closes on Cancel without saving anything', async () => {
    // Arrange
    const { dialog, onClose, user } = renderDialog(homeJellyfin)

    // Act
    await user.click(within(dialog).getByRole('button', { name: 'Cancel' }))

    // Assert
    expect(onClose).toHaveBeenCalledOnce()
    expect(integrationsApi.update).not.toHaveBeenCalled()
  })
})
