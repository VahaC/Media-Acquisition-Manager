import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { DownloadDialog } from '@/components/search/DownloadDialog'
import { downloadsApi } from '@/lib/downloads-api'
import { splitPath } from '@/lib/library-versions'
import { qk } from '@/lib/queries'
import { replacementsApi } from '@/lib/replacements-api'
import type { DownloadJob, ReplacementCopyUnavailableReason, ReplacementOptions, ReplacementUnavailableReason, SearchRelease } from '@/lib/types'
import { apiError } from '@/test/api-error'
import { owner, signInAs } from '@/test/auth-session'
import {
  cartoonsOption,
  jobOf,
  localClient,
  moviesOption,
  optionsOf,
  overwriteOf,
  seedboxClient,
  suggestedOf,
  transmissionClient,
} from '@/test/downloads'
import { aliens } from '@/test/library'
import {
  aliensWorkOf,
  copyOf,
  daybreak,
  optionsUnavailable,
  replacementOf,
  replacementOptionsOf,
  specialCopy,
  theatricalCopy,
  yellowstoneWorkOf,
} from '@/test/replacements'
import { tolokaRemux, yellowstonePack } from '@/test/search'

vi.mock('@/lib/downloads-api')
vi.mock('@/lib/replacements-api')

// MAM-24 — the installation owner's Existing copies in the Download dialog: Keep both or Replace after
// verification, the copies of the matched works with why each can or can't be replaced, the marks
// of the torrents holding them, the confirmation a title-and-year match needs, and the schedule.

const UUID_V4 = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/

/** The sentences the dialog says, spelled out rather than read from the locale. */
const CHECKING = 'Checking your copies…'
const NONE_AVAILABLE = 'None of your copies can be replaced: see why below.'
const NONE_TICKED = 'Tick the copies to replace.'
const CONFIRM_FIRST = 'Tick “This release is Чужі (1986)” first.'
const CONFIRM_HINT = 'Matched by title and year only. Your tick lets the app schedule this; nothing moves unless Jellyfin later gives the new file this work\'s id.'
const NOT_CHECKED = 'The app can\'t check this destination\'s folder for files already there, and a replacement is sent only after that check. Pick another destination, or Keep both.'
/** MAM-30 — a folder its download client can't see, which no send goes to (D5), Keep both's included. */
const NOT_SEEN = 'This destination\'s download client can\'t see its folder, so nothing will be sent there until the owner fixes the destination under Destinations in the user menu.'
const CONDITIONS = 'Conditions: the old copy moves into the quarantine only after the new one is downloaded, checked and shown in Jellyfin as the same work; a copy held by a torrent waits until you remove that torrent in Local qBittorrent. Once the download is done, the app checks that the new file is complete, readable and identified by Jellyfin as the old copy\'s work. The old copy stays in the quarantine until you restore it or delete it permanently on the job\'s page.'

/** The Theatrical Cut's and the Special Edition's names, the ends of their paths that tell them apart. */
const THEATRICAL = splitPath(theatricalCopy.file.appPath!).name
const SPECIAL = splitPath(specialCopy.file.appPath!).name

/** The dialog for `release`, signed in as the installation owner, inside a router for the Media folders link. */
function renderDialog(release: SearchRelease = tolokaRemux) {
  signInAs(owner)
  const onClose = vi.fn()
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <DownloadDialog release={release} onClose={onClose} />
      </MemoryRouter>
    </QueryClientProvider>,
  )
  return { client, onClose, user: userEvent.setup(), dialog: screen.getByRole('dialog', { name: `Download ${release.title}` }) }
}

/** The server answers `withoutHoldings` before Replace is picked, and `withHoldings` once it is. */
function answering(withoutHoldings: ReplacementOptions, withHoldings: ReplacementOptions = withoutHoldings) {
  vi.mocked(replacementsApi.options).mockImplementation(async (_, __, holdings) => (holdings ? withHoldings : withoutHoldings))
}

const keepBoth = (dialog: HTMLElement) => within(dialog).getByRole('radio', { name: 'Keep both' })
const replaceRadio = (dialog: HTMLElement) => within(dialog).getByRole('radio', { name: 'Replace after verification' })
const sendButton = (dialog: HTMLElement) => within(dialog).getByRole('button', { name: 'Send' })
const copyBox = (dialog: HTMLElement, name: string) => within(dialog).getByRole('checkbox', { name })

/** The hover reason of the disabled Send, or null when it gives none. */
const sendReason = (dialog: HTMLElement) => sendButton(dialog).parentElement?.getAttribute('title') ?? null

/** Waits for the pickers, the folder's first answer, and the answer about the copies. */
async function copiesLoaded(dialog: HTMLElement) {
  await within(dialog).findByRole('combobox', { name: 'Download client' })
  await vi.waitFor(() => expect(replacementsApi.options).toHaveBeenCalled())
  await vi.waitFor(() => expect(within(dialog).queryByText(CHECKING)).not.toBeInTheDocument())
  await vi.waitFor(() => expect(within(dialog).queryByTitle(/Checking/)).not.toBeInTheDocument())
}

/** Picks Replace and waits for the answer with the held marks. */
async function pickReplace(dialog: HTMLElement, user: ReturnType<typeof userEvent.setup>) {
  await user.click(replaceRadio(dialog))
  await vi.waitFor(() => expect(vi.mocked(replacementsApi.options).mock.calls.some(([, , holdings]) => holdings)).toBe(true))
  await vi.waitFor(() => expect(within(dialog).queryByTitle(CHECKING)).not.toBeInTheDocument())
}

/** The summary's Existing copies value. */
const existingCopies = (dialog: HTMLElement) =>
  within(within(dialog).getByRole('region', { name: 'Summary' })).getByText('Existing copies').nextElementSibling as HTMLElement

describe('DownloadDialog — replacing existing copies', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    vi.mocked(downloadsApi.options).mockResolvedValue(optionsOf(localClient))
    vi.mocked(downloadsApi.overwriteCheck).mockResolvedValue(overwriteOf(null))
    vi.mocked(downloadsApi.send).mockResolvedValue(jobOf({}))
    vi.mocked(replacementsApi.schedule).mockResolvedValue(jobOf({ replacement: replacementOf() }))
    answering(replacementOptionsOf([aliensWorkOf()]))
  })

  it('asks for the copies through the destination picked, without the held marks, and lists them read-only under Keep both', async () => {
    // Arrange
    const { dialog } = renderDialog()

    // Act
    await copiesLoaded(dialog)

    // Assert
    expect(vi.mocked(replacementsApi.options).mock.calls).toEqual([[tolokaRemux.id, moviesOption.id, false]])
    expect(keepBoth(dialog)).toBeChecked()
    expect(replaceRadio(dialog)).toBeEnabled()
    const copies = within(dialog).getByRole('region', { name: 'Your copies' })
    expect(within(copies).getByRole('group', { name: 'Чужі (1986)' })).toBeInTheDocument()
    expect(copyBox(dialog, THEATRICAL)).toBeDisabled()
    expect(copyBox(dialog, SPECIAL)).toHaveAccessibleDescription('Not inspected yet')
    expect(within(copies).getByText('Theatrical Cut · Theatrical cut · 13.83 GiB')).toBeInTheDocument()
    expect(within(copies).getByText('Its title names the edition: Theatrical cut.')).toBeInTheDocument()
    expect(dialog).toHaveClass('download-dialog-wide')
    expect(existingCopies(dialog)).toHaveTextContent(
      'Kept: nothing is replaced or removed, and a release that would write over a different file isn\'t sent.')
  })

  it('disables Replace until a destination is picked, and asks nothing about the copies before', async () => {
    // Arrange
    vi.mocked(downloadsApi.options).mockResolvedValue(suggestedOf(null, localClient, seedboxClient))
    const { dialog, user } = renderDialog()
    await within(dialog).findByRole('combobox', { name: 'Download client' })
    const beforePick = {
      disabled: (replaceRadio(dialog) as HTMLInputElement).disabled,
      reason: document.getElementById(replaceRadio(dialog).getAttribute('aria-describedby') ?? '')?.textContent,
      asked: vi.mocked(replacementsApi.options).mock.calls.length,
    }

    // Act
    await user.selectOptions(within(dialog).getByRole('combobox', { name: 'Download client' }), localClient.id)

    // Assert
    expect(beforePick).toEqual({ disabled: true, reason: 'Pick a destination first.', asked: 0 })
    await vi.waitFor(() => expect(replaceRadio(dialog)).toBeEnabled())
    expect(vi.mocked(replacementsApi.options).mock.calls).toEqual([[tolokaRemux.id, moviesOption.id, false]])
  })

  it.each<[ReplacementUnavailableReason, string | null, string]>([
    ['Magnet', null, 'This release is a magnet link: the app can\'t check its files before sending.'],
    ['DestinationNotUsable', 'The app couldn\'t ask “Local qBittorrent” where this destination saves: it isn\'t reachable.', 'The app couldn\'t ask “Local qBittorrent” where this destination saves: it isn\'t reachable.'],
    ['DestinationNotUsable', null, 'The destination\'s folder isn\'t confirmed inside a library the app knows.'],
    ['NoMatchedWork', null, 'This release matches no film or held episodes of yours.'],
  ])('disables Replace with the reason when nothing of the release can be replaced: %s', async (reason, problem, sentence) => {
    // Arrange
    answering(optionsUnavailable(reason, problem))
    const { dialog } = renderDialog()

    // Act
    await copiesLoaded(dialog)

    // Assert
    expect(replaceRadio(dialog)).toBeDisabled()
    expect(replaceRadio(dialog)).toHaveAccessibleDescription(sentence)
    expect(within(dialog).queryByRole('region', { name: 'Your copies' })).not.toBeInTheDocument()
    expect(dialog).not.toHaveClass('download-dialog-wide')
    expect(sendButton(dialog)).toBeEnabled()
  })

  it('says replacement is switched off, and links to the Media folders page where it is switched on', async () => {
    // Arrange
    answering(optionsUnavailable('SwitchedOff'))
    const { dialog } = renderDialog()

    // Act
    await copiesLoaded(dialog)

    // Assert
    expect(replaceRadio(dialog)).toBeDisabled()
    expect(replaceRadio(dialog)).toHaveAccessibleDescription(
      'Replacement is switched off. Switch it on, and tick the folders it may touch, on the Media folders page.')
    expect(within(dialog).getByRole('link', { name: 'Media folders' })).toHaveAttribute('href', '/settings/media-folders')
  })

  it('says none of the copies can be replaced, and shows why under Keep both, when every copy has a reason', async () => {
    // Arrange
    const unticked = { ...theatricalCopy, unavailable: 'FolderNotTicked' as const }
    answering(replacementOptionsOf([aliensWorkOf({ copies: [unticked, specialCopy] })]))
    const { dialog } = renderDialog()

    // Act
    await copiesLoaded(dialog)

    // Assert
    expect(replaceRadio(dialog)).toBeDisabled()
    expect(replaceRadio(dialog)).toHaveAccessibleDescription(NONE_AVAILABLE)
    expect(copyBox(dialog, THEATRICAL)).toHaveAccessibleDescription(
      'Its folder isn\'t ticked for replacement on the Media folders page.')
  })

  it.each<[ReplacementCopyUnavailableReason, string]>([
    ['ChangedSinceInspection', 'Changed since the app inspected it.'],
    ['Missing', 'The file isn\'t there any more.'],
    ['Unreadable', 'The app can\'t read this file now.'],
    ['OutsideTheClaim', 'The library places this version outside the episodes this release claims.'],
    ['AlreadyScheduled', 'A replacement scheduled earlier already holds this copy.'],
    ['SharedWithUnavailableWork', 'This file is also a copy of another work this release matched, which can\'t be replaced.'],
    ['HeldByRemoval', 'A removal under Removed copies on the Duplicates page holds this copy.'],
  ])('disables a copy the server gives a reason, and says it in words: %s', async (reason, sentence) => {
    // Arrange
    answering(replacementOptionsOf([aliensWorkOf({ copies: [{ ...theatricalCopy, unavailable: reason }, specialCopy] })]))
    const { dialog } = renderDialog()

    // Act
    await copiesLoaded(dialog)

    // Assert
    expect(copyBox(dialog, THEATRICAL)).toBeDisabled()
    expect(copyBox(dialog, THEATRICAL)).toHaveAccessibleDescription(sentence)
  })

  it.each<['TitleOnly' | 'ConflictingIds', string]>([
    ['TitleOnly', 'Matched by title alone, which never allows a replacement.'],
    ['ConflictingIds', 'Its ids conflict (TMDb), so it can\'t be replaced.'],
  ])('says why no copy of a work can be replaced, with no confirmation to tick: %s', async (reason, sentence) => {
    // Arrange
    answering(replacementOptionsOf([aliensWorkOf({ unavailable: reason, conflictingSchemes: reason === 'ConflictingIds' ? ['Tmdb'] : [], needsConfirmation: false })]))
    const { dialog } = renderDialog()

    // Act
    await copiesLoaded(dialog)

    // Assert
    const work = within(dialog).getByRole('group', { name: 'Чужі (1986)' })
    expect(within(work).getByText(sentence)).toBeInTheDocument()
    expect(within(work).queryByRole('checkbox', { name: 'This release is Чужі (1986)' })).not.toBeInTheDocument()
    expect(copyBox(dialog, THEATRICAL)).toBeDisabled()
    expect(replaceRadio(dialog)).toHaveAccessibleDescription(NONE_AVAILABLE)
  })

  it('asks again with the held marks once Replace is picked, and says once per work which torrent holds its copies', async () => {
    // Arrange
    const held = { ...theatricalCopy, holds: [{ clientName: 'Local qBittorrent', torrentId: 'e5f60718293a4b5c6d7e8f900112233445566778', torrentName: 'Aliens.1986.Theatrical.BDRip', secondName: false }] }
    answering(replacementOptionsOf([aliensWorkOf()]), replacementOptionsOf([aliensWorkOf({ copies: [held, specialCopy] })]))
    const { dialog, user } = renderDialog()
    await copiesLoaded(dialog)

    // Act
    await pickReplace(dialog, user)

    // Assert
    const sentence = 'Held by the torrent “Aliens.1986.Theatrical.BDRip” in “Local qBittorrent”: before this copy can move you\'ll remove that torrent there without its files — which also stops seeding its other files.'
    expect(vi.mocked(replacementsApi.options).mock.calls).toEqual([
      [tolokaRemux.id, moviesOption.id, false],
      [tolokaRemux.id, moviesOption.id, true],
    ])
    expect(await within(dialog).findByText(sentence)).toBeInTheDocument()
    expect(within(dialog).getByText('Held by “Aliens.1986.Theatrical.BDRip”')).toBeInTheDocument()
    expect(copyBox(dialog, THEATRICAL)).toHaveAccessibleDescription(sentence)
    expect(copyBox(dialog, THEATRICAL)).toBeEnabled()
  })

  it('says a copy with another name frees no space when it moves, naming the torrent whose file it is', async () => {
    // Arrange
    const linked = { ...theatricalCopy, otherNames: true, holds: [{ clientName: 'Local qBittorrent', torrentId: 'f60718293a4b5c6d7e8f9001122334455667788a', torrentName: 'Aliens.1986.Seeding', secondName: true }] }
    answering(replacementOptionsOf([aliensWorkOf({ copies: [linked, specialCopy] })]))
    const { dialog, user } = renderDialog()
    await copiesLoaded(dialog)

    // Act
    await pickReplace(dialog, user)

    // Assert
    expect(copyBox(dialog, THEATRICAL)).toHaveAccessibleDescription(
      'This file has another name, so moving it frees no space. It is the torrent “Aliens.1986.Seeding”\'s file.')
    expect(within(dialog).queryByText(/^Held by/)).not.toBeInTheDocument()
  })

  it('holds Send, saying why, until a copy is ticked and the title-and-year match is confirmed', async () => {
    // Arrange
    const { dialog, user } = renderDialog()
    await copiesLoaded(dialog)
    await pickReplace(dialog, user)
    const beforeTick = { disabled: (sendButton(dialog) as HTMLButtonElement).disabled, reason: sendReason(dialog) }
    await user.click(copyBox(dialog, THEATRICAL))
    const beforeConfirm = { disabled: (sendButton(dialog) as HTMLButtonElement).disabled, reason: sendReason(dialog) }
    const confirmation = within(dialog).getByRole('checkbox', { name: 'This release is Чужі (1986)' })

    // Act
    await user.click(confirmation)

    // Assert
    expect(beforeTick).toEqual({ disabled: true, reason: NONE_TICKED })
    expect(beforeConfirm).toEqual({ disabled: true, reason: CONFIRM_FIRST })
    expect(confirmation).toHaveAccessibleDescription(CONFIRM_HINT)
    expect(sendButton(dialog)).toBeEnabled()
    expect(sendReason(dialog)).toBeNull()
  })

  it('holds Send, saying the copies are being checked, while the answer with the held marks is on its way', async () => {
    // Arrange
    // A big client's list of every torrent takes seconds: meanwhile the answer without the held marks
    // stands in, and the copies can be ticked from it.
    let answerWithHoldings: (options: ReplacementOptions) => void = () => {}
    vi.mocked(replacementsApi.options).mockImplementation((_, __, holdings) => (holdings
      ? new Promise((resolve) => {
        answerWithHoldings = resolve
      })
      : Promise.resolve(replacementOptionsOf([aliensWorkOf()]))))
    const { dialog, user } = renderDialog()
    await copiesLoaded(dialog)
    await user.click(replaceRadio(dialog))
    await vi.waitFor(() => expect(vi.mocked(replacementsApi.options).mock.calls.some(([, , holdings]) => holdings)).toBe(true))
    await user.click(copyBox(dialog, THEATRICAL))
    await user.click(within(dialog).getByRole('checkbox', { name: 'This release is Чужі (1986)' }))
    await user.click(sendButton(dialog))
    const whileChecking = { disabled: (sendButton(dialog) as HTMLButtonElement).disabled, reason: sendReason(dialog) }

    // Act
    await act(async () => answerWithHoldings(replacementOptionsOf([aliensWorkOf()])))

    // Assert
    expect(whileChecking).toEqual({ disabled: true, reason: CHECKING })
    expect(replacementsApi.schedule).not.toHaveBeenCalled()
    await vi.waitFor(() => expect(sendButton(dialog)).toBeEnabled())
  })

  it('schedules the replacement of the ticked copies with the confirmed work, and sends no download of its own', async () => {
    // Arrange
    const { dialog, user } = renderDialog()
    await copiesLoaded(dialog)
    await pickReplace(dialog, user)
    await user.click(copyBox(dialog, THEATRICAL))
    await user.click(within(dialog).getByRole('checkbox', { name: 'This release is Чужі (1986)' }))

    // Act
    await user.click(sendButton(dialog))

    // Assert
    await within(dialog).findByRole('status')
    expect(replacementsApi.schedule).toHaveBeenCalledExactlyOnceWith({
      requestId: expect.stringMatching(UUID_V4),
      releaseId: tolokaRemux.id,
      downloadClientId: localClient.id,
      destinationId: moviesOption.id,
      fileIds: [theatricalCopy.file.id],
      confirmedWorkIds: [aliens.id],
    })
    expect(downloadsApi.send).not.toHaveBeenCalled()
  })

  it('says what was scheduled under the outcome, and keeps the summary as it was sent', async () => {
    // Arrange
    const { client, dialog, user } = renderDialog()
    await copiesLoaded(dialog)
    await pickReplace(dialog, user)
    await user.click(copyBox(dialog, THEATRICAL))
    await user.click(within(dialog).getByRole('checkbox', { name: 'This release is Чужі (1986)' }))
    await user.click(sendButton(dialog))
    await within(dialog).findByRole('status')
    answering(replacementOptionsOf([aliensWorkOf({ copies: [{ ...theatricalCopy, unavailable: 'AlreadyScheduled' }, specialCopy] })]))

    // Act
    await act(() => client.refetchQueries({ queryKey: qk.replacements.all }))

    // Assert
    expect(within(dialog).getByRole('status')).toHaveTextContent('Local qBittorrent took the release and lists it among its downloads.')
    const line = within(dialog).getByText('Replacement scheduled: 1 copy, kept until the new one is verified.')
    expect(line.closest('p')).toHaveClass('library-source-status')
    expect(line.closest('p')).not.toHaveAttribute('role')
    const copies = existingCopies(dialog)
    expect(within(copies).getByText('Replaced after verification:')).toBeInTheDocument()
    expect(within(copies).getAllByRole('listitem').map((item) => item.firstChild?.textContent)).toEqual([
      'Чужі (1986): 1 copy',
      theatricalCopy.file.appPath,
    ])
    expect(within(copies).getByText(CONDITIONS)).toBeInTheDocument()
  })

  it('names in the conditions the client whose torrent holds a ticked copy, rather than the one sent to', async () => {
    // Arrange
    const held = { ...theatricalCopy, holds: [{ clientName: 'Seedbox qBittorrent', torrentId: '0718293a4b5c6d7e8f9001122334455667788a9b', torrentName: 'Aliens.1986.Theatrical.BDRip', secondName: false }] }
    answering(replacementOptionsOf([aliensWorkOf({ copies: [held, specialCopy] })]))
    const { dialog, user } = renderDialog()
    await copiesLoaded(dialog)
    await pickReplace(dialog, user)

    // Act
    await user.click(copyBox(dialog, THEATRICAL))

    // Assert
    expect(within(existingCopies(dialog)).getByText(CONDITIONS.replace('in Local qBittorrent', 'in Seedbox qBittorrent'))).toBeInTheDocument()
  })

  it('keeps Replace picked, with Send disabled and the reason, when the next destination can\'t be used', async () => {
    // Arrange
    const problem = 'The app couldn\'t ask “Local qBittorrent” where this destination saves: it isn\'t reachable.'
    vi.mocked(replacementsApi.options).mockImplementation(async (_, destinationId) =>
      (destinationId === moviesOption.id ? replacementOptionsOf([aliensWorkOf()]) : optionsUnavailable('DestinationNotUsable', problem)))
    const { dialog, user } = renderDialog()
    await copiesLoaded(dialog)
    await pickReplace(dialog, user)
    await user.click(copyBox(dialog, THEATRICAL))

    // Act
    await user.selectOptions(within(dialog).getByRole('combobox', { name: 'Destination' }), cartoonsOption.id)

    // Assert
    expect(await within(dialog).findByText(problem)).toBeInTheDocument()
    expect(replaceRadio(dialog)).toBeChecked()
    expect(replaceRadio(dialog)).toBeEnabled()
    expect(keepBoth(dialog)).not.toBeChecked()
    expect(sendButton(dialog)).toBeDisabled()
    expect(sendReason(dialog)).toBe(problem)
    expect(within(dialog).queryByRole('region', { name: 'Your copies' })).not.toBeInTheDocument()
    expect(replacementsApi.schedule).not.toHaveBeenCalled()
    expect(downloadsApi.send).not.toHaveBeenCalled()
  })

  it('says the copies could not be checked, with Try again, when the read fails, and lists them once it answers', async () => {
    // Arrange
    vi.mocked(replacementsApi.options).mockRejectedValueOnce(apiError(500))
    const { dialog, user } = renderDialog()
    const state = await within(dialog).findByRole('alert')
    const replaceWhileFailed = { disabled: (replaceRadio(dialog) as HTMLInputElement).disabled }

    // Act
    await user.click(within(state).getByRole('button', { name: 'Try again' }))

    // Assert
    expect(state).toHaveTextContent('Could not check your copies.')
    expect(replaceWhileFailed).toEqual({ disabled: true })
    expect(await within(dialog).findByRole('group', { name: 'Чужі (1986)' })).toBeInTheDocument()
    expect(within(dialog).queryByRole('alert')).not.toBeInTheDocument()
    expect(replaceRadio(dialog)).toBeEnabled()
  })

  it('keeps the copies on screen under a banner when they fail to be read again, holds Send meanwhile, and Retry asks again', async () => {
    // Arrange
    const { client, dialog, user } = renderDialog()
    await copiesLoaded(dialog)
    await pickReplace(dialog, user)
    await user.click(copyBox(dialog, THEATRICAL))
    await user.click(within(dialog).getByRole('checkbox', { name: 'This release is Чужі (1986)' }))
    vi.mocked(replacementsApi.options).mockRejectedValueOnce(apiError(500, 'Database is locked.'))
    await act(() => client.refetchQueries({ queryKey: qk.replacements.all, type: 'active' }))
    const banner = await within(dialog).findByRole('alert')
    const whileFailed = { sendDisabled: (sendButton(dialog) as HTMLButtonElement).disabled, reason: sendReason(dialog), ticked: (copyBox(dialog, THEATRICAL) as HTMLInputElement).checked }

    // Act
    await user.click(within(banner).getByRole('button', { name: 'Retry' }))

    // Assert
    expect(banner).toHaveTextContent('Could not check your copies again, showing the last answer. Database is locked.')
    expect(whileFailed).toEqual({ sendDisabled: true, reason: 'Could not check your copies.', ticked: true })
    await vi.waitFor(() => expect(within(dialog).queryByRole('alert')).not.toBeInTheDocument())
    await vi.waitFor(() => expect(sendButton(dialog)).toBeEnabled())
  })

  it('offers no Try again when the copies can\'t be read for good, and Keep both still sends', async () => {
    // Arrange
    vi.mocked(replacementsApi.options).mockRejectedValue(apiError(404, 'This destination no longer exists.'))
    const { dialog, user } = renderDialog()
    const state = await within(dialog).findByRole('alert')
    const said = state.textContent
    const tryAgain = within(state).queryByRole('button', { name: 'Try again' })
    const replace = { disabled: (replaceRadio(dialog) as HTMLInputElement).disabled, describedBy: state.parentElement?.id }
    const readWithReplace = replaceRadio(dialog).getAttribute('aria-describedby')

    // Act
    await user.click(sendButton(dialog))

    // Assert
    expect(said).toContain('Could not check your copies.')
    expect(said).toContain('This destination no longer exists.')
    expect(tryAgain).toBeNull()
    expect(replace.disabled).toBe(true)
    expect(readWithReplace).toBe(replace.describedBy)
    await within(dialog).findByRole('status')
    expect(downloadsApi.send).toHaveBeenCalledOnce()
    expect(replacementsApi.schedule).not.toHaveBeenCalled()
  })

  it('reads the copies again when the schedule is refused, and the copy then says why it can\'t be replaced', async () => {
    // Arrange
    const refusal = '“[02.1] Aliens (1986).mkv” changed since the app inspected it, so it can\'t be replaced. Inspect the library again, or pick Keep both.'
    vi.mocked(replacementsApi.schedule).mockRejectedValueOnce(apiError(409, refusal))
    const { dialog, user } = renderDialog()
    await copiesLoaded(dialog)
    await pickReplace(dialog, user)
    await user.click(copyBox(dialog, THEATRICAL))
    await user.click(within(dialog).getByRole('checkbox', { name: 'This release is Чужі (1986)' }))
    const askedBefore = vi.mocked(replacementsApi.options).mock.calls.length
    answering(replacementOptionsOf([aliensWorkOf({ copies: [{ ...theatricalCopy, unavailable: 'ChangedSinceInspection' }, specialCopy] })]))

    // Act
    await user.click(sendButton(dialog))

    // Assert
    expect(await within(dialog).findByRole('alert')).toHaveTextContent(refusal)
    await vi.waitFor(() => expect(copyBox(dialog, THEATRICAL)).toHaveAccessibleDescription('Changed since the app inspected it.'))
    expect(vi.mocked(replacementsApi.options).mock.calls.length).toBeGreaterThan(askedBefore)
    expect(copyBox(dialog, THEATRICAL)).toBeDisabled()
    expect(copyBox(dialog, THEATRICAL)).not.toBeChecked()
    expect(sendButton(dialog)).toBeDisabled()
    expect(sendReason(dialog)).toBe(NONE_AVAILABLE)
  })

  it('keeps the form when the schedule answers a failed job, and sends the next attempt with a new request id', async () => {
    // Arrange
    const refused = 'A file is already at “/library/movies/Aliens (1986).mkv”, and a replacement never lands on a file already there, so nothing was sent and no copy will be replaced. Pick Keep both to download it.'
    vi.mocked(replacementsApi.schedule)
      .mockResolvedValueOnce(jobOf({ status: 'Failed', clientJobId: null, error: refused, replacement: replacementOf({ state: 'Failed' }) }))
      .mockResolvedValueOnce(jobOf({}))
    const { dialog, user } = renderDialog()
    await copiesLoaded(dialog)
    await pickReplace(dialog, user)
    await user.click(copyBox(dialog, THEATRICAL))
    await user.click(within(dialog).getByRole('checkbox', { name: 'This release is Чужі (1986)' }))
    await user.click(sendButton(dialog))
    const said = (await within(dialog).findByRole('alert')).textContent
    const formKept = { replace: (replaceRadio(dialog) as HTMLInputElement).checked, ticked: (copyBox(dialog, THEATRICAL) as HTMLInputElement).checked }
    await user.click(keepBoth(dialog))

    // Act
    await user.click(sendButton(dialog))

    // Assert
    await within(dialog).findByRole('status')
    expect(said).toBe(refused)
    expect(formKept).toEqual({ replace: true, ticked: true })
    const [first] = vi.mocked(replacementsApi.schedule).mock.calls.map(([request]) => request.requestId)
    const [second] = vi.mocked(downloadsApi.send).mock.calls.map(([request]) => request.requestId)
    expect(second).toMatch(UUID_V4)
    expect(second).not.toBe(first)
  })

  it('keeps the ticks across Keep both and Replace, sent only under Replace', async () => {
    // Arrange
    const { dialog, user } = renderDialog()
    await copiesLoaded(dialog)
    await pickReplace(dialog, user)
    await user.click(copyBox(dialog, THEATRICAL))
    await user.click(keepBoth(dialog))
    const underKeepBoth = {
      ticked: (copyBox(dialog, THEATRICAL) as HTMLInputElement).checked,
      disabled: (copyBox(dialog, THEATRICAL) as HTMLInputElement).disabled,
      summary: existingCopies(dialog).textContent,
    }

    // Act
    await user.click(replaceRadio(dialog))

    // Assert
    expect(underKeepBoth).toEqual({
      ticked: true,
      disabled: true,
      summary: 'Kept: nothing is replaced or removed, and a release that would write over a different file isn\'t sent.',
    })
    await vi.waitFor(() => expect(copyBox(dialog, THEATRICAL)).toBeEnabled())
    expect(copyBox(dialog, THEATRICAL)).toBeChecked()
    expect(within(existingCopies(dialog)).getByText('Чужі (1986): 1 copy')).toBeInTheDocument()
  })

  it('disables the ticks, the radios and the confirmation while the schedule is on its way', async () => {
    // Arrange
    let answer: (job: DownloadJob) => void = () => {}
    vi.mocked(replacementsApi.schedule).mockReturnValue(new Promise((resolve) => {
      answer = resolve
    }))
    const { dialog, user } = renderDialog()
    await copiesLoaded(dialog)
    await pickReplace(dialog, user)
    await user.click(copyBox(dialog, THEATRICAL))
    const confirmation = within(dialog).getByRole('checkbox', { name: 'This release is Чужі (1986)' })
    await user.click(confirmation)
    await user.click(sendButton(dialog))
    await within(dialog).findByRole('button', { name: 'Sending…' })
    const whileSending = {
      keepBoth: (keepBoth(dialog) as HTMLInputElement).disabled,
      replace: (replaceRadio(dialog) as HTMLInputElement).disabled,
      copy: (copyBox(dialog, THEATRICAL) as HTMLInputElement).disabled,
      confirmation: (confirmation as HTMLInputElement).disabled,
    }

    // Act
    await act(async () => answer(jobOf({ replacement: replacementOf() })))

    // Assert
    expect(whileSending).toEqual({ keepBoth: true, replace: true, copy: true, confirmation: true })
    expect(await within(dialog).findByRole('status')).toBeInTheDocument()
  })

  it('says in Replace mode that a replacement waits for the folder check, and holds Send, where the folder can\'t be checked', async () => {
    // Arrange
    vi.mocked(downloadsApi.overwriteCheck).mockResolvedValue(overwriteOf('NotCheckedFolderOutsideMediaFolders'))
    const { dialog, user } = renderDialog()
    await copiesLoaded(dialog)
    const underKeepBoth = within(dialog).getByText(/A release sent there goes without that check\./)

    // Act
    await pickReplace(dialog, user)
    await user.click(copyBox(dialog, THEATRICAL))
    await user.click(within(dialog).getByRole('checkbox', { name: 'This release is Чужі (1986)' }))

    // Assert
    expect(underKeepBoth).toBeInTheDocument()
    expect(within(dialog).queryByText(/goes without that check/)).not.toBeInTheDocument()
    const banner = within(dialog).getByText(NOT_CHECKED).closest('.ui-banner')
    expect(banner).toHaveAttribute('data-tone', 'warning')
    expect(sendButton(dialog)).toBeDisabled()
    expect(sendReason(dialog)).toBe(NOT_CHECKED)
    expect(sendButton(dialog)).toHaveAccessibleDescription(NOT_CHECKED)
  })

  it('says in Replace mode, for a folder the client can\'t see, that nothing is sent there, never to pick Keep both', async () => {
    // Arrange
    // MAM-30 — Local Transmission can't see the folder, which refuses any send, Keep both's included (D5).
    vi.mocked(downloadsApi.options).mockResolvedValue(optionsOf(transmissionClient))
    vi.mocked(downloadsApi.overwriteCheck).mockResolvedValue(overwriteOf('NotCheckedFolderNotSeen'))
    const { dialog, user } = renderDialog()
    await copiesLoaded(dialog)

    // Act
    await pickReplace(dialog, user)

    // Assert
    expect(within(dialog).getByText(NOT_SEEN).closest('.ui-banner')).toHaveAttribute('data-tone', 'warning')
    expect(within(dialog).queryByText(NOT_CHECKED)).not.toBeInTheDocument()
    expect(sendButton(dialog)).toBeDisabled()
    expect(sendReason(dialog)).toBe(NOT_SEEN)
    expect(sendButton(dialog)).toHaveAccessibleDescription(NOT_SEEN)
  })

  it('says in Replace mode, for a folder client\'s folder with no path in the app, what it says for any client\'s', async () => {
    // Arrange
    // MAM-30 — a folder Local Transmission sees but no mapping brings into the app: Keep both still sends it.
    vi.mocked(downloadsApi.options).mockResolvedValue(optionsOf(transmissionClient))
    vi.mocked(downloadsApi.overwriteCheck).mockResolvedValue(overwriteOf('NotCheckedFolderUnknown'))
    const { dialog, user } = renderDialog()
    await copiesLoaded(dialog)

    // Act
    await pickReplace(dialog, user)

    // Assert
    expect(within(dialog).getByText(NOT_CHECKED).closest('.ui-banner')).toHaveAttribute('data-tone', 'warning')
    expect(sendReason(dialog)).toBe(NOT_CHECKED)
  })

  it('names the client it couldn\'t ask, with its own sentence, once, under the destination, and says what that means for Replace', async () => {
    // Arrange
    // MAM-31 — the live check stopped Transmission: the warning read "has no path in the app, or its download client
    // can't say where it is" above the replacement's own "couldn't ask", which named the client and the fix.
    const unreachable = 'Couldn\'t reach Transmission at http://192.168.1.10:9091: nothing accepted the connection. Check the address and port, and that Transmission is running.'
    vi.mocked(downloadsApi.options).mockResolvedValue(optionsOf(transmissionClient))
    vi.mocked(downloadsApi.overwriteCheck).mockResolvedValue(overwriteOf('NotCheckedFolderUnknown', {
      folderUnknownCause: 'ClientNotAsked',
      clientName: 'Local Transmission',
      clientError: unreachable,
    }))
    answering(optionsUnavailable('DestinationNotUsable', `The app couldn't ask “Local Transmission” where this destination saves: ${unreachable}`))
    const { dialog } = renderDialog()

    // Act
    await copiesLoaded(dialog)

    // Assert
    const warning = `The app couldn't ask “Local Transmission” where this destination saves, so a release sent there goes without the check for files it would write over. ${unreachable}`
    expect(within(dialog).getByText(warning).closest('.ui-banner')).toHaveAttribute('data-tone', 'warning')
    expect(sendButton(dialog)).toHaveAccessibleDescription(warning)
    expect(within(dialog).queryByText(/has no path in the app, or its download client can't say where it is/)).not.toBeInTheDocument()
    expect(within(dialog).getAllByText(/nothing accepted the connection/)).toHaveLength(1)
    expect(replaceRadio(dialog)).toBeDisabled()
    expect(replaceRadio(dialog)).toHaveAccessibleDescription(NOT_CHECKED)
    expect(sendButton(dialog)).toBeEnabled()
  })

  it('keeps the server\'s own sentence for Replace where the warning names a cause without the client\'s words', async () => {
    // Arrange
    // MAM-31 — no path mapping brings the folder in: the warning names the client and the fix, the reason Replace
    // can't be picked the folder the client gave, as before.
    const problem = 'No path mapping of this integration covers “/scratch”, so automatic replacement stays off for this destination.'
    vi.mocked(downloadsApi.options).mockResolvedValue(optionsOf(transmissionClient))
    vi.mocked(downloadsApi.overwriteCheck).mockResolvedValue(overwriteOf('NotCheckedFolderUnknown', {
      folderUnknownCause: 'NoPathMapping',
      clientName: 'Local Transmission',
    }))
    answering(optionsUnavailable('DestinationNotUsable', problem))
    const { dialog } = renderDialog()

    // Act
    await copiesLoaded(dialog)

    // Assert
    expect(within(dialog).getByText(
      'No path mapping of “Local Transmission” brings this destination\'s folder into the app, so a release sent there goes without the check for files it would write over. The owner adds one under Integrations, “Local Transmission”, Path mappings.',
    ).closest('.ui-banner')).toHaveAttribute('data-tone', 'warning')
    expect(replaceRadio(dialog)).toHaveAccessibleDescription(problem)
  })

  it('warns, read with Send, of the clients whose torrents couldn\'t be read for the held marks', async () => {
    // Arrange
    // The server's sentence names only the cause: the dialog's own words before it say what wasn't checked.
    const switchedOff = '“Seedbox qBittorrent” is switched off.'
    answering(replacementOptionsOf([aliensWorkOf()]), replacementOptionsOf([aliensWorkOf()], {
      clients: [
        { clientName: 'Local qBittorrent', status: 'Partial', error: null, unplacedTorrents: 2 },
        { clientName: 'Seedbox qBittorrent', status: 'NotRead', error: switchedOff, unplacedTorrents: 0 },
        { clientName: 'NAS qBittorrent', status: 'Read', error: null, unplacedTorrents: 0 },
      ],
    }))
    const { dialog, user } = renderDialog()
    await copiesLoaded(dialog)

    // Act
    await pickReplace(dialog, user)

    // Assert
    const partial = 'Couldn\'t check 2 torrents in “Local qBittorrent”: their files couldn\'t be placed in the app\'s folders.'
    const notRead = `Couldn't check whether a torrent in “Seedbox qBittorrent” holds these copies: ${switchedOff}`
    expect(await within(dialog).findByText(partial)).toBeInTheDocument()
    expect(within(dialog).getByText(notRead)).toBeInTheDocument()
    expect(within(dialog).queryByText(/NAS qBittorrent/)).not.toBeInTheDocument()
    expect(sendButton(dialog)).toHaveAccessibleDescription(`${partial} ${notRead}`)
  })

  describe('a season pack', () => {
    beforeEach(() => {
      answering(replacementOptionsOf([yellowstoneWorkOf()], { releaseEditions: [] }))
    })

    it('lists the held episodes it claims by season, each season opening on its name with how many are ticked', async () => {
      // Arrange
      const { dialog, user } = renderDialog(yellowstonePack)
      await copiesLoaded(dialog)
      const series = within(dialog).getByRole('group', { name: 'Єллоустоун' })
      const seasonOne = within(series).getByRole('group', { name: 'Season 1' })
      const closed = within(seasonOne).queryByRole('group', { name: 'Season 1 · Episode 1 · Daybreak' })

      // Act
      await user.click(within(seasonOne).getByRole('button', { name: 'Season 1' }))

      // Assert
      expect(closed).not.toBeInTheDocument()
      expect(within(seasonOne).getByText('0 of 3 ticked')).toBeInTheDocument()
      const episode = within(seasonOne).getByRole('group', { name: 'Season 1 · Episode 1 · Daybreak' })
      // Two versions' files of one name in two folders read apart, each by its version's number.
      expect(within(episode).getAllByRole('checkbox')).toEqual([
        within(episode).getByRole('checkbox', { name: 'Season 1/Yellowstone S01E01.mkv' }),
        within(episode).getByRole('checkbox', { name: 'Season 1 Remux/Yellowstone S01E01.mkv' }),
      ])
      expect(within(episode).getByText('Version 1 · 3 GiB')).toBeInTheDocument()
      expect(within(episode).getByText('Version 2 · 3 GiB')).toBeInTheDocument()
      expect(within(series).getByText('Held by the torrent “Yellowstone.S01.1080p.WEB-DL” in “Local qBittorrent” (2 of these copies): before these copies can move you\'ll remove that torrent there without its files — which also stops seeding its other files.')).toBeInTheDocument()
      expect(copyBox(dialog, 'Season 1 Remux/Yellowstone S01E01.mkv')).toHaveAccessibleDescription('This file has another name, so moving it frees no space.')
    })

    it('ticks every copy of a season that can be replaced on one click once Replace is picked, and unticks them all on the next', async () => {
      // Arrange
      const { dialog, user } = renderDialog(yellowstonePack)
      await copiesLoaded(dialog)
      const seasonOne = () => within(dialog).getByRole('group', { name: 'Season 1' })
      const disabledUnderKeepBoth = within(seasonOne()).getByRole('button', { name: 'Tick every copy that can be replaced (3)' }).hasAttribute('disabled')
      await pickReplace(dialog, user)

      // Act
      await user.click(within(seasonOne()).getByRole('button', { name: 'Tick every copy that can be replaced (3)' }))

      // Assert
      expect(disabledUnderKeepBoth).toBe(true)
      expect(within(seasonOne()).getByText('3 of 3 ticked')).toBeInTheDocument()
      expect(within(existingCopies(dialog)).getByText('Єллоустоун, Season 1: 3 copies')).toBeInTheDocument()
      // Season 2 holds no copy that can be replaced: no button, and its copy says why once opened.
      const seasonTwo = within(dialog).getByRole('group', { name: 'Season 2' })
      expect(within(seasonTwo).queryByRole('button', { name: /Tick every copy/ })).not.toBeInTheDocument()
      await user.click(within(seasonTwo).getByRole('button', { name: 'Season 2' }))
      expect(copyBox(dialog, 'Yellowstone S02E01.mkv')).toHaveAccessibleDescription(
        'Its folder isn\'t ticked for replacement on the Media folders page.')
      await user.click(within(seasonOne()).getByRole('button', { name: 'Untick all' }))
      expect(within(seasonOne()).getByText('0 of 3 ticked')).toBeInTheDocument()
      expect(within(existingCopies(dialog)).getByText('Not picked yet')).toBeInTheDocument()
    })

    it('schedules the season\'s copies with no confirmation for a work matched by its id', async () => {
      // Arrange
      const { dialog, user } = renderDialog(yellowstonePack)
      await copiesLoaded(dialog)
      await pickReplace(dialog, user)
      await user.click(within(dialog).getByRole('button', { name: 'Tick every copy that can be replaced (3)' }))

      // Act
      await user.click(sendButton(dialog))

      // Assert
      await within(dialog).findByRole('status')
      expect(vi.mocked(replacementsApi.schedule).mock.lastCall?.[0]).toMatchObject({
        releaseId: yellowstonePack.id,
        fileIds: [...daybreak.copies.map((copy) => copy.file.id), '7a1e0000-0000-4000-8000-000000000133'],
        confirmedWorkIds: [],
      })
    })
  })

  it('lists a copy the app can\'t reach by the library\'s path, with the path mapper\'s sentence for why', async () => {
    // Arrange
    const problem = 'No path mapping of “Home Jellyfin” covers “/media/movies/Alien. Collection”.'
    const unreachable = copyOf({ ...theatricalCopy.file, appPath: null, pathStatus: 'NotCovered', pathProblem: problem }, theatricalCopy.versionId, { unavailable: 'NoAppPath' })
    answering(replacementOptionsOf([aliensWorkOf({ copies: [unreachable, specialCopy] })]))
    const { dialog } = renderDialog()

    // Act
    await copiesLoaded(dialog)

    // Assert
    expect(copyBox(dialog, THEATRICAL)).toHaveAccessibleDescription(problem)
    expect(within(dialog).getByText('/media/movies/Alien. Collection')).toBeInTheDocument()
  })

  it('says the app can\'t reach a copy outside the Media folders, which the mapper has no sentence about', async () => {
    // Arrange
    answering(replacementOptionsOf([aliensWorkOf({ copies: [{ ...theatricalCopy, unavailable: 'NoAppPath' }, specialCopy] })]))
    const { dialog } = renderDialog()

    // Act
    await copiesLoaded(dialog)

    // Assert
    expect(copyBox(dialog, THEATRICAL)).toHaveAccessibleDescription(
      'The app can\'t reach this file: it has no path in the app, or lies outside the Media folders.')
  })
})
