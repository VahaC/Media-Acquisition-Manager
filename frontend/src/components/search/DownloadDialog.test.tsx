import { onlineManager, QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, fireEvent, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { DownloadDialog } from '@/components/search/DownloadDialog'
import { downloadsApi } from '@/lib/downloads-api'
import { qk } from '@/lib/queries'
import type { DownloadClientOption, DownloadJob, DownloadOptions } from '@/lib/types'
import { apiError, networkError } from '@/test/api-error'
import {
  cartoonsOption,
  jobOf,
  localClient,
  moviesOption,
  nasClient,
  optionsOf,
  seedboxClient,
} from '@/test/downloads'
import { tolokaRemux } from '@/test/search'

vi.mock('@/lib/downloads-api')

const UUID_V4 = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/

function renderDialog() {
  const onClose = vi.fn()
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <DownloadDialog release={tolokaRemux} onClose={onClose} />
    </QueryClientProvider>,
  )
  return { onClose, user: userEvent.setup(), dialog: screen.getByRole('dialog', { name: `Download ${tolokaRemux.title}` }) }
}

const clientSelect = (dialog: HTMLElement) => within(dialog).getByRole('combobox', { name: 'Download client' })
const destinationSelect = (dialog: HTMLElement) => within(dialog).getByRole('combobox', { name: 'Destination' })
const sendButton = (dialog: HTMLElement) => within(dialog).getByRole('button', { name: 'Send' })

/** Waits for the options to arrive: the client picker is on screen. */
const optionsLoaded = (dialog: HTMLElement) => within(dialog).findByRole('combobox', { name: 'Download client' })

/** The options of a select as the person reads them, the placeholder included. */
const optionTexts = (select: HTMLElement) => [...select.querySelectorAll('option')].map((option) => option.textContent)

/** The summary as `[term, value]` pairs. */
function summary(dialog: HTMLElement): [string, string][] {
  const terms = [...within(dialog).getByRole('region', { name: 'Summary' }).querySelectorAll('dt')]
  return terms.map((term) => [term.textContent!, term.nextElementSibling!.textContent!])
}

/** The texts of the buttons in the dialog's footer, in order. */
const footerButtons = (dialog: HTMLElement) =>
  within(dialog.querySelector<HTMLElement>('.ui-dialog-footer')!).getAllByRole('button').map((button) => button.textContent)

/** The request ids of every send so far, in order. */
const sentRequestIds = () => vi.mocked(downloadsApi.send).mock.calls.map(([request]) => request.requestId)

describe('DownloadDialog', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    vi.mocked(downloadsApi.options).mockResolvedValue(optionsOf(localClient, seedboxClient))
    vi.mocked(downloadsApi.send).mockResolvedValue(jobOf({}))
  })

  it('is named by the action and the release, and says what a destination is', () => {
    // Arrange
    const release = tolokaRemux

    // Act
    const { dialog } = renderDialog()

    // Assert
    expect(dialog).toHaveAccessibleName(`Download ${release.title}`)
    expect(dialog).toHaveAccessibleDescription(
      'Send this release to a download client. The destination is a category of the client, and the category decides the folder.')
  })

  it('says it is loading while the download clients are on their way, with Send disabled', async () => {
    // Arrange
    let answer: (options: DownloadOptions) => void = () => {}
    vi.mocked(downloadsApi.options).mockReturnValue(new Promise((resolve) => {
      answer = resolve
    }))
    const { dialog } = renderDialog()
    const whileLoading = {
      said: within(dialog).queryByText('Loading…') !== null,
      pickers: within(dialog).queryAllByRole('combobox').length,
      sendDisabled: (sendButton(dialog) as HTMLButtonElement).disabled,
    }

    // Act
    await act(async () => answer(optionsOf(localClient)))

    // Assert
    await optionsLoaded(dialog)
    expect(whileLoading).toEqual({ said: true, pickers: 0, sendDisabled: true })
    expect(within(dialog).queryByText('Loading…')).not.toBeInTheDocument()
  })

  it('shows the server sentence when the release is no longer available', async () => {
    // Arrange
    vi.mocked(downloadsApi.options).mockRejectedValue(
      apiError(404, 'This release isn\'t available any more: a search keeps what it found for a day. Search again.'))

    // Act
    const { dialog } = renderDialog()

    // Assert
    expect(await within(dialog).findByRole('alert'))
      .toHaveTextContent('This release isn\'t available any more: a search keeps what it found for a day. Search again.')
    expect(within(dialog).queryByRole('combobox')).not.toBeInTheDocument()
    expect(sendButton(dialog)).toBeDisabled()
  })

  it('shows its own sentence when the download clients can\'t be loaded and the API gives none', async () => {
    // Arrange
    vi.mocked(downloadsApi.options).mockRejectedValue(apiError(500))

    // Act
    const { dialog } = renderDialog()

    // Assert
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('Could not load the download clients.')
  })

  it('says no download client can take the release yet, and where the owner sets them up, when none is listed', async () => {
    // Arrange
    vi.mocked(downloadsApi.options).mockResolvedValue(optionsOf())

    // Act
    const { dialog } = renderDialog()

    // Assert
    expect(await within(dialog).findByText(
      'No download client can take this release yet. The owner sets them up under Integrations and Destinations.')).toBeInTheDocument()
    expect(within(dialog).queryByRole('combobox')).not.toBeInTheDocument()
    expect(within(dialog).queryByRole('region', { name: 'Summary' })).not.toBeInTheDocument()
    expect(sendButton(dialog)).toBeDisabled()
  })

  it('offers only the clients the server lists for this release, by name', async () => {
    // Arrange
    const { dialog } = renderDialog()

    // Act
    await optionsLoaded(dialog)

    // Assert
    expect(downloadsApi.options).toHaveBeenCalledExactlyOnceWith(tolokaRemux.id)
    expect(optionTexts(clientSelect(dialog))).toEqual(['Choose a download client', 'Local qBittorrent', 'Seedbox qBittorrent'])
  })

  it('opens on the default destination and the client that owns it', async () => {
    // Arrange
    const [films, series] = seedboxClient.destinations
    const withoutDefault: DownloadClientOption = { ...localClient, destinations: [cartoonsOption, { ...moviesOption, isDefault: false }] }
    const owner: DownloadClientOption = { ...seedboxClient, destinations: [{ ...films, isDefault: true }, series] }
    vi.mocked(downloadsApi.options).mockResolvedValue(optionsOf(withoutDefault, owner))
    const { dialog } = renderDialog()

    // Act
    await optionsLoaded(dialog)

    // Assert
    expect(clientSelect(dialog)).toHaveValue(seedboxClient.id)
    expect(destinationSelect(dialog)).toHaveValue(films.id)
    expect(optionTexts(destinationSelect(dialog))).toEqual(['Choose a destination', 'Films', 'Series'])
    expect(sendButton(dialog)).toBeEnabled()
  })

  it('opens on the only client and its only destination when the default isn\'t one of them', async () => {
    // Arrange
    vi.mocked(downloadsApi.options).mockResolvedValue(optionsOf(nasClient))
    const { dialog } = renderDialog()

    // Act
    await optionsLoaded(dialog)

    // Assert
    expect(clientSelect(dialog)).toHaveValue(nasClient.id)
    expect(destinationSelect(dialog)).toHaveValue(nasClient.destinations[0].id)
    expect(sendButton(dialog)).toBeEnabled()
  })

  it('opens on the placeholders, with Send disabled and the reason, when several clients are listed and none holds the default', async () => {
    // Arrange
    vi.mocked(downloadsApi.options).mockResolvedValue(optionsOf(nasClient, seedboxClient))
    const { dialog } = renderDialog()

    // Act
    await optionsLoaded(dialog)

    // Assert
    expect(clientSelect(dialog)).toHaveValue('')
    expect(destinationSelect(dialog)).toHaveValue('')
    expect(destinationSelect(dialog)).toBeDisabled()
    expect(destinationSelect(dialog)).toHaveAccessibleDescription('Choose a download client first.')
    expect(sendButton(dialog)).toBeDisabled()
    expect(sendButton(dialog).parentElement).toHaveAttribute('title', 'Pick a download client and a destination first.')
    expect(summary(dialog)).toContainEqual(['Download client', 'Not picked yet'])
  })

  it('forgets the destination picked for one client when another is picked', async () => {
    // Arrange
    const { dialog, user } = renderDialog()
    await optionsLoaded(dialog)
    const before = (destinationSelect(dialog) as HTMLSelectElement).value

    // Act
    await user.selectOptions(clientSelect(dialog), seedboxClient.id)

    // Assert
    expect(before).toBe(moviesOption.id)
    expect(destinationSelect(dialog)).toHaveValue('')
    expect(optionTexts(destinationSelect(dialog))).toEqual(['Choose a destination', 'Films', 'Series'])
    expect(summary(dialog)).toContainEqual(['Destination', 'Not picked yet'])
    expect(sendButton(dialog)).toBeDisabled()
  })

  it('picks the only destination of a client picked by hand', async () => {
    // Arrange
    vi.mocked(downloadsApi.options).mockResolvedValue(optionsOf(localClient, nasClient))
    const { dialog, user } = renderDialog()
    await optionsLoaded(dialog)

    // Act
    await user.selectOptions(clientSelect(dialog), nasClient.id)

    // Assert
    expect(destinationSelect(dialog)).toHaveValue(nasClient.destinations[0].id)
    expect(sendButton(dialog)).toBeEnabled()
  })

  it('sums up the source, the release, the client, the destination with its category, and that existing copies are kept', async () => {
    // Arrange
    const { dialog } = renderDialog()

    // Act
    await optionsLoaded(dialog)

    // Assert
    expect(summary(dialog)).toEqual([
      ['Source', 'Toloka'],
      ['Release', tolokaRemux.title],
      ['Download client', 'Local qBittorrent'],
      ['Destination', 'Movies — category “Movies”'],
      ['Existing copies', 'Kept: this version of the app never replaces or removes a file.'],
    ])
  })

  it('sends the release once, with its request id, the release, the client and the destination', async () => {
    // Arrange
    const { dialog, user } = renderDialog()
    await optionsLoaded(dialog)

    // Act
    await user.click(sendButton(dialog))

    // Assert
    await within(dialog).findByRole('status')
    expect(downloadsApi.send).toHaveBeenCalledExactlyOnceWith({
      requestId: expect.stringMatching(UUID_V4),
      releaseId: tolokaRemux.id,
      downloadClientId: localClient.id,
      destinationId: moviesOption.id,
    })
  })

  it('disables Send and the pickers, and says it is sending, while the request is on its way', async () => {
    // Arrange
    let answer: (job: DownloadJob) => void = () => {}
    vi.mocked(downloadsApi.send).mockReturnValue(new Promise((resolve) => {
      answer = resolve
    }))
    const { dialog, user } = renderDialog()
    await optionsLoaded(dialog)
    await user.click(sendButton(dialog))
    const button = await within(dialog).findByRole('button', { name: 'Sending…' })
    const whileSending = {
      button: button.hasAttribute('disabled'),
      client: (clientSelect(dialog) as HTMLSelectElement).disabled,
      destination: (destinationSelect(dialog) as HTMLSelectElement).disabled,
    }

    // Act
    await act(async () => answer(jobOf({})))

    // Assert
    expect(whileSending).toEqual({ button: true, client: true, destination: true })
    expect(await within(dialog).findByRole('status')).toBeInTheDocument()
  })

  it('sends once on a double click, even when the second click lands before Send shows it is sending', async () => {
    // Arrange
    const { dialog } = renderDialog()
    await optionsLoaded(dialog)
    const button = sendButton(dialog)

    // Act
    act(() => {
      fireEvent.click(button)
      fireEvent.click(button)
    })

    // Assert
    await within(dialog).findByRole('status')
    expect(downloadsApi.send).toHaveBeenCalledOnce()
  })

  it('sends the same request id again when Send is clicked after the server could not be reached', async () => {
    // Arrange
    vi.mocked(downloadsApi.send).mockRejectedValueOnce(networkError()).mockResolvedValueOnce(jobOf({}))
    const { dialog, user } = renderDialog()
    await optionsLoaded(dialog)
    await user.click(sendButton(dialog))
    const said = (await within(dialog).findByRole('alert')).textContent

    // Act
    await user.click(sendButton(dialog))

    // Assert
    await within(dialog).findByRole('status')
    expect(said).toBe('The server could not be reached. Check that it is running, then try again.')
    const [first, second] = sentRequestIds()
    expect(first).toMatch(UUID_V4)
    expect(second).toBe(first)
  })

  it('shows a failed job\'s sentence in the form, keeps the form, and sends the next attempt with a new request id', async () => {
    // Arrange
    vi.mocked(downloadsApi.send)
      .mockResolvedValueOnce(jobOf({ status: 'Failed', clientJobId: null, error: '“Local qBittorrent” already has this torrent, so nothing was sent.' }))
      .mockResolvedValueOnce(jobOf({ destinationName: 'Cartoons', clientDestinationKey: 'Cartoons' }))
    const { dialog, user } = renderDialog()
    await optionsLoaded(dialog)
    await user.click(sendButton(dialog))
    const said = (await within(dialog).findByRole('alert')).textContent
    const formKept = { client: (clientSelect(dialog) as HTMLSelectElement).value, sendEnabled: !sendButton(dialog).hasAttribute('disabled') }
    await user.selectOptions(destinationSelect(dialog), cartoonsOption.id)

    // Act
    await user.click(sendButton(dialog))

    // Assert
    await within(dialog).findByRole('status')
    expect(said).toBe('“Local qBittorrent” already has this torrent, so nothing was sent.')
    expect(formKept).toEqual({ client: localClient.id, sendEnabled: true })
    const [first, second] = sentRequestIds()
    expect(second).toMatch(UUID_V4)
    expect(second).not.toBe(first)
    expect(vi.mocked(downloadsApi.send).mock.lastCall?.[0]).toMatchObject({ destinationId: cartoonsOption.id })
  })

  it('shows the server sentence inline when sending is refused, keeps the form, and resends with the same request id', async () => {
    // Arrange
    vi.mocked(downloadsApi.send)
      .mockRejectedValueOnce(apiError(409, 'This release was already sent to “Local qBittorrent”, so it isn\'t sent twice.'))
      .mockRejectedValueOnce(apiError(409, 'This release was already sent to “Local qBittorrent”, so it isn\'t sent twice.'))
    const { dialog, onClose, user } = renderDialog()
    await optionsLoaded(dialog)
    await user.click(sendButton(dialog))
    const said = (await within(dialog).findByRole('alert')).textContent

    // Act
    await user.click(sendButton(dialog))

    // Assert
    await vi.waitFor(() => expect(downloadsApi.send).toHaveBeenCalledTimes(2))
    expect(said).toBe('This release was already sent to “Local qBittorrent”, so it isn\'t sent twice.')
    expect(onClose).not.toHaveBeenCalled()
    expect(clientSelect(dialog)).toHaveValue(localClient.id)
    const [first, second] = sentRequestIds()
    expect(second).toBe(first)
  })

  it('says the client took the release, keeps the summary, and offers only Close once it is submitted', async () => {
    // Arrange
    const { dialog, onClose, user } = renderDialog()
    await optionsLoaded(dialog)
    await user.click(sendButton(dialog))
    const status = await within(dialog).findByRole('status')

    // Act
    await user.click(within(dialog.querySelector<HTMLElement>('.ui-dialog-footer')!).getByRole('button', { name: 'Close' }))

    // Assert
    expect(status).toHaveTextContent('Local qBittorrent took the release and lists it among its downloads.')
    expect(status).toHaveClass('library-source-status')
    expect(within(dialog).queryByRole('combobox')).not.toBeInTheDocument()
    expect(summary(dialog)).toContainEqual(['Destination', 'Movies — category “Movies”'])
    expect(footerButtons(dialog)).toEqual(['Close'])
    expect(onClose).toHaveBeenCalledOnce()
  })

  it('says the client hasn\'t listed the release yet when it took it without an id', async () => {
    // Arrange
    vi.mocked(downloadsApi.send).mockResolvedValue(jobOf({ clientJobId: null }))
    const { dialog, user } = renderDialog()
    await optionsLoaded(dialog)

    // Act
    await user.click(sendButton(dialog))

    // Assert
    expect(await within(dialog).findByRole('status'))
      .toHaveTextContent('Local qBittorrent took the release, but hasn\'t listed it among its downloads yet.')
    expect(footerButtons(dialog)).toEqual(['Close'])
  })

  it('says the release has to be linked by hand when the client took it but can\'t tell which download it is', async () => {
    // Arrange
    vi.mocked(downloadsApi.send).mockResolvedValue(jobOf({ status: 'NeedsLinking', clientJobId: null }))
    const { dialog, user } = renderDialog()
    await optionsLoaded(dialog)

    // Act
    await user.click(sendButton(dialog))

    // Assert
    const status = await within(dialog).findByRole('status')
    expect(status).toHaveTextContent(
      'Local qBittorrent took the release, but the app can\'t tell which of its downloads it is, so it has to be linked by hand.')
    expect(status).toHaveClass('library-source-warning')
    expect(footerButtons(dialog)).toEqual(['Close'])
  })

  it('says the release is being sent already when the same request is answered with a job still on its way', async () => {
    // Arrange
    vi.mocked(downloadsApi.send).mockResolvedValue(jobOf({ status: 'Created', clientJobId: null }))
    const { dialog, user } = renderDialog()
    await optionsLoaded(dialog)

    // Act
    await user.click(sendButton(dialog))

    // Assert
    const status = await within(dialog).findByRole('status')
    expect(status).toHaveTextContent('This release is already being sent to Local qBittorrent. Its outcome isn\'t known yet.')
    expect(status).toHaveClass('library-source-warning')
    expect(footerButtons(dialog)).toEqual(['Close'])
  })

  it.each<[DownloadJob['status'], string]>([
    ['Paused', 'Local qBittorrent took the release, but its download is paused there.'],
    ['Cancelled', 'Local qBittorrent took the release, but its job has been cancelled since, so the app doesn\'t follow it.'],
  ])('warns when the same request is answered with a job that is %s by now', async (status, sentence) => {
    // Arrange
    vi.mocked(downloadsApi.send).mockResolvedValue(jobOf({ status }))
    const { dialog, user } = renderDialog()
    await optionsLoaded(dialog)

    // Act
    await user.click(sendButton(dialog))

    // Assert
    const line = await within(dialog).findByRole('status')
    expect(line).toHaveTextContent(sentence)
    expect(line).toHaveClass('library-source-warning')
  })

  it('has every list of jobs asked for again once a send answers, and nothing else', async () => {
    // Arrange
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const invalidate = vi.spyOn(client, 'invalidateQueries')
    render(
      <QueryClientProvider client={client}>
        <DownloadDialog release={tolokaRemux} onClose={vi.fn()} />
      </QueryClientProvider>,
    )
    const dialog = screen.getByRole('dialog', { name: `Download ${tolokaRemux.title}` })
    await optionsLoaded(dialog)

    // Act
    await userEvent.setup().click(sendButton(dialog))

    // Assert
    await within(dialog).findByRole('status')
    expect(invalidate.mock.calls).toEqual([[{ queryKey: qk.downloads.jobs.lists }]])
  })

  it('puts Send first and Cancel last', async () => {
    // Arrange
    const { dialog } = renderDialog()
    await optionsLoaded(dialog)

    // Act
    const buttons = footerButtons(dialog)

    // Assert
    expect(buttons).toEqual(['Send', 'Cancel'])
  })

  it('closes on Cancel without sending anything', async () => {
    // Arrange
    const { dialog, onClose, user } = renderDialog()
    await optionsLoaded(dialog)

    // Act
    await user.click(within(dialog).getByRole('button', { name: 'Cancel' }))

    // Assert
    expect(onClose).toHaveBeenCalledOnce()
    expect(downloadsApi.send).not.toHaveBeenCalled()
  })

  describe('while the browser says it is offline', () => {
    afterEach(() => {
      act(() => onlineManager.setOnline(true))
    })

    it('sends at once and says the server can\'t be reached, and never sends again on its own once back online', async () => {
      // Arrange
      vi.mocked(downloadsApi.send).mockRejectedValue(networkError())
      const { dialog, onClose, user } = renderDialog()
      await optionsLoaded(dialog)
      act(() => onlineManager.setOnline(false))

      // Act
      await user.click(sendButton(dialog))
      const said = (await within(dialog).findByRole('alert')).textContent
      await user.click(within(dialog).getByRole('button', { name: 'Cancel' }))
      act(() => onlineManager.setOnline(true))

      // Assert
      // Held back instead, the send would wait unseen and go out by itself once the browser is online.
      expect(said).toBe('The server could not be reached. Check that it is running, then try again.')
      expect(onClose).toHaveBeenCalledOnce()
      await act(() => Promise.resolve())
      expect(downloadsApi.send).toHaveBeenCalledOnce()
    })

    it('asks for the download clients anyway, and says the server can\'t be reached rather than that no client takes the release', async () => {
      // Arrange
      act(() => onlineManager.setOnline(false))
      vi.mocked(downloadsApi.options).mockRejectedValue(networkError())

      // Act
      const { dialog } = renderDialog()

      // Assert
      expect(await within(dialog).findByRole('alert')).toHaveTextContent(
        'The server could not be reached. Check that it is running, then try again.')
      expect(downloadsApi.options).toHaveBeenCalledOnce()
      expect(within(dialog).queryByText(/No download client can take this release/)).not.toBeInTheDocument()
    })
  })
})
