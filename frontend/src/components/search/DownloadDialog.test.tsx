import { onlineManager, QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, fireEvent, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { DownloadDialog } from '@/components/search/DownloadDialog'
import { downloadsApi } from '@/lib/downloads-api'
import { qk } from '@/lib/queries'
import { replacementsApi } from '@/lib/replacements-api'
import type { CurrentUser, DownloadClientOption, DownloadJob, DownloadOptions, DownloadOverwriteCheckAnswer } from '@/lib/types'
import { apiError, networkError } from '@/test/api-error'
import { member, signInAs } from '@/test/auth-session'
import {
  cartoonsOption,
  jobOf,
  localClient,
  moviesOption,
  nasClient,
  optionsOf,
  seedboxClient,
  suggestedOf,
  transmissionClient,
} from '@/test/downloads'
import { optionsUnavailable } from '@/test/replacements'
import { tolokaRemux } from '@/test/search'

vi.mock('@/lib/downloads-api')
// MAM-24 — the owner's copies: never asked here, where a member sends; the owner's Replace is in
// DownloadDialog.replace.test.tsx.
vi.mock('@/lib/replacements-api')

const UUID_V4 = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/

/**
 * The dialog for the Toloka release, signed in as `user`: a member by default, who may send but never
 * replace (MAM-24), so the owner's read of the copies is never asked.
 */
function renderDialog({ user = member }: { user?: CurrentUser } = {}) {
  signInAs(user)
  const onClose = vi.fn()
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <DownloadDialog release={tolokaRemux} onClose={onClose} />
    </QueryClientProvider>,
  )
  return { client, onClose, user: userEvent.setup(), dialog: screen.getByRole('dialog', { name: `Download ${tolokaRemux.title}` }) }
}

const clientSelect = (dialog: HTMLElement) => within(dialog).getByRole('combobox', { name: 'Download client' })
const destinationSelect = (dialog: HTMLElement) => within(dialog).getByRole('combobox', { name: 'Destination' })
const sendButton = (dialog: HTMLElement) => within(dialog).getByRole('button', { name: 'Send' })

/** Why Send waits while the picked destination's folder is asked about (MAM-38), as its hover title. */
const CHECKING = 'Checking the destination\'s folder…'

/** MAM-38 — the two warnings, as the dialog shows them before Send. */
const OUTSIDE_WARNING = 'The app can\'t check whether this release would write over a file already there: the destination\'s folder isn\'t under Media folders, which the installation owner sets in the user menu. A release sent there goes without that check.'
const UNKNOWN_WARNING = 'The app can\'t check whether this release would write over a file already there: the destination\'s folder has no path in the app, or its download client can\'t say where it is. A release sent there goes without that check.'
/** MAM-30 — a folder its download client can't see, which no send goes to (D5). */
const NOT_SEEN_WARNING = 'This destination\'s download client can\'t see its folder, so nothing will be sent there until the owner fixes the destination under Destinations in the user menu.'

/**
 * Waits for the options to arrive — the client picker is on screen — and, when a destination is
 * picked, for the answer about its folder, which Send waits for (MAM-38).
 */
async function optionsLoaded(dialog: HTMLElement) {
  const picker = await within(dialog).findByRole('combobox', { name: 'Download client' })
  await vi.waitFor(() => expect(within(dialog).queryByTitle(CHECKING)).not.toBeInTheDocument())
  return picker
}

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
    // MAM-38 — a folder the app can check: no warning, so the outcome lines below are the only status.
    vi.mocked(downloadsApi.overwriteCheck).mockResolvedValue({ notCheckedReason: null })
    vi.mocked(replacementsApi.options).mockResolvedValue(optionsUnavailable('SwitchedOff'))
  })

  it('is named by the action and the release, and says what a destination is', () => {
    // Arrange
    const release = tolokaRemux

    // Act
    const { dialog } = renderDialog()

    // Assert
    expect(dialog).toHaveAccessibleName(`Download ${release.title}`)
    expect(dialog).toHaveAccessibleDescription(
      'Send this release to a download client. The destination decides where it lands.')
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
    const state = await within(dialog).findByRole('alert')
    expect(state).toHaveTextContent('Could not load the download clients.')
    expect(state).toHaveTextContent('This release isn\'t available any more: a search keeps what it found for a day. Search again.')
    // A release that expired stays expired: no Try again.
    expect(within(dialog).queryByRole('button', { name: 'Try again' })).not.toBeInTheDocument()
    expect(within(dialog).queryByRole('combobox')).not.toBeInTheDocument()
    expect(sendButton(dialog)).toBeDisabled()
  })

  it('says the download clients could not be loaded in place of the pickers, and Try again loads them', async () => {
    // Arrange
    vi.mocked(downloadsApi.options).mockRejectedValueOnce(apiError(500))
    const { dialog, user } = renderDialog()
    const state = await within(dialog).findByRole('alert')

    // Act
    await user.click(within(dialog).getByRole('button', { name: 'Try again' }))

    // Assert
    expect(state).toHaveTextContent('Could not load the download clients.')
    expect(await optionsLoaded(dialog)).toHaveValue(localClient.id)
    expect(within(dialog).queryByRole('alert')).not.toBeInTheDocument()
  })

  it('keeps the pickers under a banner when the download clients fail to load again, and Retry asks again', async () => {
    // Arrange
    const { client, dialog, user } = renderDialog()
    await optionsLoaded(dialog)
    vi.mocked(downloadsApi.options).mockRejectedValueOnce(networkError())
    await act(() => client.refetchQueries({ queryKey: qk.downloads.options(tolokaRemux.id) }))
    const banner = await within(dialog).findByRole('alert')

    // Act
    await user.click(within(dialog).getByRole('button', { name: 'Retry' }))

    // Assert
    expect(banner).toHaveTextContent(
      'Could not load the download clients again, showing the last loaded ones. The server could not be reached.')
    await vi.waitFor(() => expect(within(dialog).queryByRole('alert')).not.toBeInTheDocument())
    expect(clientSelect(dialog)).toHaveValue(localClient.id)
    expect(downloadsApi.options).toHaveBeenCalledTimes(3)
  })

  it('offers no Retry on the banner when the release expired since the pickers loaded', async () => {
    // Arrange
    const { client, dialog } = renderDialog()
    await optionsLoaded(dialog)
    vi.mocked(downloadsApi.options).mockRejectedValueOnce(
      apiError(404, 'This release isn\'t available any more: a search keeps what it found for a day. Search again.'))

    // Act
    await act(() => client.refetchQueries({ queryKey: qk.downloads.options(tolokaRemux.id) }))

    // Assert
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('This release isn\'t available any more')
    expect(within(dialog).queryByRole('button', { name: 'Retry' })).not.toBeInTheDocument()
    expect(clientSelect(dialog)).toHaveValue(localClient.id)
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

  it('opens on the destination the server suggests for the tracker section of the release, over the default', async () => {
    // Arrange — MAM-32: Toloka's «Мультсеріали» is mapped to Series of the seedbox; Movies stays the default.
    const [, series] = seedboxClient.destinations
    vi.mocked(downloadsApi.options).mockResolvedValue(suggestedOf(series.id, localClient, seedboxClient))
    const { dialog } = renderDialog()

    // Act
    await optionsLoaded(dialog)

    // Assert
    expect(clientSelect(dialog)).toHaveValue(seedboxClient.id)
    expect(destinationSelect(dialog)).toHaveValue(series.id)
    expect(summary(dialog)).toContainEqual(['Destination', 'Series — category “tv”'])
    expect(sendButton(dialog)).toBeEnabled()
  })

  it('opens on the placeholders when the server suggests none, though a default is listed', async () => {
    // Arrange — two sections of the release mapped to two destinations: the server picks neither.
    vi.mocked(downloadsApi.options).mockResolvedValue(suggestedOf(null, localClient, seedboxClient))
    const { dialog } = renderDialog()

    // Act
    await optionsLoaded(dialog)

    // Assert
    expect(clientSelect(dialog)).toHaveValue('')
    expect(destinationSelect(dialog)).toHaveValue('')
    expect(sendButton(dialog)).toBeDisabled()
  })

  it('opens on the only client but on no destination when the server suggests none, though the client holds the default', async () => {
    // Arrange — the default is a pick for a client chosen by hand, never for the opening one.
    vi.mocked(downloadsApi.options).mockResolvedValue(suggestedOf(null, localClient))
    const { dialog } = renderDialog()

    // Act
    await optionsLoaded(dialog)

    // Assert
    expect(clientSelect(dialog)).toHaveValue(localClient.id)
    expect(destinationSelect(dialog)).toHaveValue('')
    expect(sendButton(dialog)).toBeDisabled()
  })

  // A client picked by hand comes with, in this order: the suggested destination, the default, its
  // only destination, or none.

  it('picks the suggested destination of a client picked by hand, over the default it holds too', async () => {
    // Arrange — Cartoons is suggested; Movies, of the same client, stays the default.
    vi.mocked(downloadsApi.options).mockResolvedValue(suggestedOf(cartoonsOption.id, localClient, seedboxClient))
    const { dialog, user } = renderDialog()
    await optionsLoaded(dialog)
    await user.selectOptions(clientSelect(dialog), seedboxClient.id)

    // Act
    await user.selectOptions(clientSelect(dialog), localClient.id)

    // Assert
    expect(destinationSelect(dialog)).toHaveValue(cartoonsOption.id)
    expect(sendButton(dialog)).toBeEnabled()
  })

  it.each<[string, string | null]>([
    ['the suggested one is another client\'s', seedboxClient.destinations[0].id],
    ['the server suggests none', null],
  ])('picks the default destination of a client picked by hand when %s', async (_, suggestedId) => {
    // Arrange
    vi.mocked(downloadsApi.options).mockResolvedValue(suggestedOf(suggestedId, localClient, seedboxClient))
    const { dialog, user } = renderDialog()
    await optionsLoaded(dialog)

    // Act
    await user.selectOptions(clientSelect(dialog), localClient.id)

    // Assert
    expect(destinationSelect(dialog)).toHaveValue(moviesOption.id)
    expect(summary(dialog)).toContainEqual(['Destination', 'Movies — category “Movies”'])
    expect(sendButton(dialog)).toBeEnabled()
  })

  it('picks the only destination of a client picked by hand that holds neither the suggested one nor the default', async () => {
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

  it('picks no destination of a client picked by hand that holds several but neither the suggested one nor the default, and forgets the last client\'s pick', async () => {
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
      ['Existing copies', 'Kept: nothing is replaced or removed, and a release that would write over a different file isn\'t sent.'],
    ])
  })

  it('sums up a folder destination, which comes without its folder, by its name alone', async () => {
    // Arrange
    const folderClient: DownloadClientOption = {
      ...localClient,
      destinationKind: 'Folder',
      destinations: [{ ...moviesOption, clientDestinationKey: null }],
    }
    vi.mocked(downloadsApi.options).mockResolvedValue(optionsOf(folderClient))
    const { dialog } = renderDialog()

    // Act
    await optionsLoaded(dialog)

    // Assert
    expect(summary(dialog)).toContainEqual(['Destination', 'Movies'])
  })

  it('sums up a folder destination by its name alone even when its folder comes along, never as a category', async () => {
    // Arrange
    const folderClient: DownloadClientOption = {
      ...localClient,
      destinationKind: 'Folder',
      destinations: [{ ...moviesOption, clientDestinationKey: '/downloads/movies' }],
    }
    vi.mocked(downloadsApi.options).mockResolvedValue(optionsOf(folderClient))
    const { dialog } = renderDialog()

    // Act
    await optionsLoaded(dialog)

    // Assert
    expect(summary(dialog)).toContainEqual(['Destination', 'Movies'])
    expect(within(dialog).getByRole('region', { name: 'Summary' })).not.toHaveTextContent(/category/i)
  })

  it('lists a folder client\'s destination by its name and sends to it, naming no folder before or after Send', async () => {
    // Arrange
    // MAM-30 — Local Transmission's Films comes without its folder (D13), and so does the job it makes.
    vi.mocked(downloadsApi.options).mockResolvedValue(optionsOf(transmissionClient))
    vi.mocked(downloadsApi.send).mockResolvedValue(jobOf({
      downloadClientName: transmissionClient.name,
      destinationName: 'Films',
      clientDestinationKey: null,
      destinationKind: 'Folder',
    }))
    const { dialog, user } = renderDialog()
    await optionsLoaded(dialog)
    const before = { destinations: optionTexts(destinationSelect(dialog)), summary: summary(dialog) }

    // Act
    await user.click(sendButton(dialog))

    // Assert
    await within(dialog).findByRole('status')
    expect(before.destinations).toEqual(['Choose a destination', 'Films'])
    expect(before.summary).toContainEqual(['Destination', 'Films'])
    expect(summary(dialog)).toContainEqual(['Destination', 'Films'])
    expect(downloadsApi.send).toHaveBeenCalledExactlyOnceWith(expect.objectContaining({
      downloadClientId: transmissionClient.id,
      destinationId: transmissionClient.destinations[0].id,
    }))
    expect(dialog).not.toHaveTextContent(/\/downloads|category/i)
  })

  it('offers a member Keep both, picked, and Replace disabled with why, and never asks for the copies', async () => {
    // Arrange
    const { dialog } = renderDialog()

    // Act
    await optionsLoaded(dialog)

    // Assert
    const modes = within(dialog).getByRole('group', { name: 'Existing copies' })
    expect(within(modes).getByRole('radio', { name: 'Keep both' })).toBeChecked()
    const replace = within(modes).getByRole('radio', { name: 'Replace after verification' })
    expect(replace).toBeDisabled()
    expect(replace).not.toBeChecked()
    expect(replace).toHaveAccessibleDescription('Only the installation owner can replace a copy.')
    expect(within(dialog).queryByRole('region', { name: 'Your copies' })).not.toBeInTheDocument()
    expect(dialog).not.toHaveClass('download-dialog-wide')
    expect(replacementsApi.options).not.toHaveBeenCalled()
  })

  it('sends a member\'s release as a download that keeps the existing copies, never as a replacement', async () => {
    // Arrange
    const { dialog, user } = renderDialog()
    await optionsLoaded(dialog)

    // Act
    await user.click(sendButton(dialog))

    // Assert
    await within(dialog).findByRole('status')
    expect(downloadsApi.send).toHaveBeenCalledOnce()
    expect(replacementsApi.schedule).not.toHaveBeenCalled()
    expect(replacementsApi.options).not.toHaveBeenCalled()
    expect(summary(dialog)).toContainEqual(
      ['Existing copies', 'Kept: nothing is replaced or removed, and a release that would write over a different file isn\'t sent.'])
  })

  it('warns before Send, read with Send, when the destination\'s folder is outside the Media folders, and still sends', async () => {
    // Arrange
    vi.mocked(downloadsApi.overwriteCheck).mockResolvedValue({ notCheckedReason: 'NotCheckedFolderOutsideMediaFolders' })
    const { dialog, user } = renderDialog()
    await optionsLoaded(dialog)
    const banner = (await within(dialog).findByText(OUTSIDE_WARNING)).closest('.ui-banner')
    const readWithSend = sendButton(dialog).getAttribute('aria-describedby')

    // Act
    await user.click(sendButton(dialog))

    // Assert
    expect(banner).toHaveAttribute('data-tone', 'warning')
    // Focusing Send reads the warning: a live region inserted already filled is often not read out.
    expect(readWithSend).not.toBeNull()
    expect(banner).toHaveAttribute('id', readWithSend)
    expect(downloadsApi.overwriteCheck).toHaveBeenCalledWith(moviesOption.id)
    await vi.waitFor(() => expect(downloadsApi.send).toHaveBeenCalledOnce())
    expect(downloadsApi.send).toHaveBeenCalledWith(expect.objectContaining({ destinationId: moviesOption.id }))
  })

  it('warns before Send when the destination\'s folder has no path in the app', async () => {
    // Arrange
    vi.mocked(downloadsApi.overwriteCheck).mockResolvedValue({ notCheckedReason: 'NotCheckedFolderUnknown' })
    const { dialog } = renderDialog()

    // Act
    await optionsLoaded(dialog)

    // Assert
    expect(await within(dialog).findByText(UNKNOWN_WARNING)).toBeInTheDocument()
    expect(sendButton(dialog)).toHaveAccessibleDescription(UNKNOWN_WARNING)
  })

  it('warns before a folder client\'s Send that nothing is sent to a folder the client can\'t see, never that the release goes anyway, and holds Send', async () => {
    // Arrange
    // MAM-30 — the server tells this folder by a reason of its own: the send refuses it (D5), so Keep both's
    // Send waits too rather than fail a job.
    vi.mocked(downloadsApi.options).mockResolvedValue(optionsOf(transmissionClient))
    vi.mocked(downloadsApi.overwriteCheck).mockResolvedValue({ notCheckedReason: 'NotCheckedFolderNotSeen' })
    const { dialog, user } = renderDialog()
    await optionsLoaded(dialog)
    const banner = (await within(dialog).findByText(NOT_SEEN_WARNING)).closest('.ui-banner')

    // Act
    await user.click(sendButton(dialog))

    // Assert
    expect(banner).toHaveAttribute('data-tone', 'warning')
    expect(within(dialog).getByRole('radio', { name: 'Keep both' })).toBeChecked()
    expect(sendButton(dialog)).toHaveAccessibleDescription(NOT_SEEN_WARNING)
    expect(sendButton(dialog)).toBeDisabled()
    expect(sendButton(dialog).parentElement).toHaveAttribute('title', NOT_SEEN_WARNING)
    expect(within(dialog).queryByText(/goes without that check/)).not.toBeInTheDocument()
    expect(downloadsApi.send).not.toHaveBeenCalled()
  })

  it('warns a folder client\'s Send of a folder with no path in the app as any client\'s, as the release goes unchecked', async () => {
    // Arrange
    // MAM-30 — a folder the client sees, which no mapping brings into the app: sent, without the check.
    vi.mocked(downloadsApi.options).mockResolvedValue(optionsOf(transmissionClient))
    vi.mocked(downloadsApi.overwriteCheck).mockResolvedValue({ notCheckedReason: 'NotCheckedFolderUnknown' })
    const { dialog } = renderDialog()

    // Act
    await optionsLoaded(dialog)

    // Assert
    expect(await within(dialog).findByText(UNKNOWN_WARNING)).toBeInTheDocument()
    expect(sendButton(dialog)).toHaveAccessibleDescription(UNKNOWN_WARNING)
    expect(within(dialog).queryByText(NOT_SEEN_WARNING)).not.toBeInTheDocument()
  })

  it('warns a folder client\'s Send of a folder outside the Media folders as any client\'s, as the release goes unchecked', async () => {
    // Arrange
    vi.mocked(downloadsApi.options).mockResolvedValue(optionsOf(transmissionClient))
    vi.mocked(downloadsApi.overwriteCheck).mockResolvedValue({ notCheckedReason: 'NotCheckedFolderOutsideMediaFolders' })
    const { dialog } = renderDialog()

    // Act
    await optionsLoaded(dialog)

    // Assert
    expect(await within(dialog).findByText(OUTSIDE_WARNING)).toBeInTheDocument()
    expect(sendButton(dialog)).toHaveAccessibleDescription(OUTSIDE_WARNING)
  })

  it('asks about the folder of each destination picked, and takes the warning away when the next one\'s folder can be checked', async () => {
    // Arrange
    vi.mocked(downloadsApi.overwriteCheck).mockImplementation(async (destinationId) => ({
      notCheckedReason: destinationId === moviesOption.id ? 'NotCheckedFolderOutsideMediaFolders' : null,
    }))
    const { dialog, user } = renderDialog()
    await optionsLoaded(dialog)
    await within(dialog).findByText(OUTSIDE_WARNING)

    // Act
    await user.selectOptions(destinationSelect(dialog), cartoonsOption.id)

    // Assert
    await vi.waitFor(() => expect(within(dialog).queryByRole('status')).not.toBeInTheDocument())
    expect(vi.mocked(downloadsApi.overwriteCheck).mock.calls).toEqual([[moviesOption.id], [cartoonsOption.id]])
    expect(sendButton(dialog)).not.toHaveAttribute('aria-describedby')
  })

  it('warns of nothing and leaves Send enabled when the question about the folder fails', async () => {
    // Arrange
    vi.mocked(downloadsApi.overwriteCheck).mockRejectedValue(networkError())

    // Act
    const { dialog } = renderDialog()

    // Assert
    // Send waits only for a first answer: a failed question lets it go, and the send checks again anyway.
    await within(dialog).findByRole('combobox', { name: 'Download client' })
    await vi.waitFor(() => expect(sendButton(dialog)).toBeEnabled())
    expect(within(dialog).queryByRole('status')).not.toBeInTheDocument()
    expect(within(dialog).queryByRole('alert')).not.toBeInTheDocument()
    expect(downloadsApi.overwriteCheck).toHaveBeenCalledOnce()
  })

  it('holds Send, and says why, until the first answer about the picked destination\'s folder arrives', async () => {
    // Arrange
    let answer: (check: DownloadOverwriteCheckAnswer) => void = () => {}
    vi.mocked(downloadsApi.overwriteCheck).mockReturnValue(new Promise((resolve) => {
      answer = resolve
    }))
    const { dialog } = renderDialog()
    await within(dialog).findByRole('combobox', { name: 'Download client' })
    const whileChecking = {
      sendDisabled: (sendButton(dialog) as HTMLButtonElement).disabled,
      reason: within(dialog).queryByTitle(CHECKING) !== null,
    }

    // Act
    await act(async () => answer({ notCheckedReason: 'NotCheckedFolderUnknown' }))

    // Assert
    expect(whileChecking).toEqual({ sendDisabled: true, reason: true })
    expect(await within(dialog).findByText(UNKNOWN_WARNING)).toBeInTheDocument()
    expect(sendButton(dialog)).toBeEnabled()
    expect(within(dialog).queryByTitle(CHECKING)).not.toBeInTheDocument()
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
    // MAM-16 — a banner above the buttons, outside the scrolling fields.
    const banner = await within(dialog).findByRole('alert')
    expect(banner).toHaveClass('ui-banner')
    expect(banner.nextElementSibling).toContainElement(sendButton(dialog))
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
