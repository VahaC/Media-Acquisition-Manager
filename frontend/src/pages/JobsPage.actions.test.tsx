import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { ConfirmProvider } from '@/components/shared/ConfirmDialog'
import { downloadsApi } from '@/lib/downloads-api'
import { replacementsApi } from '@/lib/replacements-api'
import type { CurrentUser, DownloadJob, DownloadJobReplacement, DownloadJobStatus } from '@/lib/types'
import { JobsPage } from '@/pages/JobsPage'
import { apiError } from '@/test/api-error'
import { member, owner, signInAs } from '@/test/auth-session'
import { jobOf, jobsPage } from '@/test/downloads'
import { alienSubtitle, alienVideo, REPLACEMENT_ID, replacementDetailsOf, replacementOf } from '@/test/replacements'

vi.mock('@/lib/downloads-api')
vi.mock('@/lib/replacements-api')

// MAM-26/27 — the owner's actions on the replacement a job carries, on its Jobs row: Retry of one that
// stopped for review, and Keep both. MAM-41 — and Restore and Delete for good of one that holds old
// copies in the quarantine, so the Needs you tab settles them where it lists them; the Actions column
// holds these verbs only, in the order Retry, Keep both, Restore, Delete for good, Cancel job, under one
// busy flag per row, and a refusal shows in the page's banner.

/** The server's sentences for a replacement that stopped for review, or that the owner cancelled. */
const HELD_REASON = 'Still held by the torrent “Alien.1979.1080p” in “Local qBittorrent”: remove that torrent there without its files, then press Retry.'
const NO_ID_REASON = 'Jellyfin gave “Alien.1979.2160p.Remux.mkv” no TMDb or IMDb id within an hour, so it can\'t be compared with Alien (1979).'
const RENUMBERED_REASON = 'Jellyfin numbers “Yellowstone.S02E05.2160p.mkv” as season 2, episode 6, not the episode “Yellowstone S02E05.mkv” is.'
const DOWNLOAD_REASON = '“Local qBittorrent”\'s downloads couldn\'t be told apart for this release, so the app can\'t follow it to verify the new file.'
const KEPT_BOTH_REASON = 'You cancelled the replacement, so nothing will be replaced.'

/** Restore's confirmation, as the job's page asks it. */
const RESTORE_MESSAGE = 'Every file of this replacement in the quarantine goes back where it was, never over a file there now, and no folder is created for it. The new copy stays as it is. Jellyfin lists the old copy again and identifies it afresh, so a TMDb link you set by hand may need Identify again.'

/** Why Restore and Delete for good are held while replacement is switched off. */
const SWITCHED_OFF = 'Replacement is switched off on the Media folders page: nothing is put back or deleted while it is off.'

/** Matches a job's release title only. */
const asTitle = { selector: '.settings-name-text' }

/** The table row (or phone card) of the job whose release is `title`. */
const rowOf = (title: string) => screen.getByText(title, asTitle).closest('tr')!

/** The labels of the buttons in the Actions cell of the job's row, in order. */
const actionsOf = (title: string) =>
  [...rowOf(title).querySelectorAll<HTMLElement>('td.settings-actions-cell button')].map((button) => button.textContent)

/** The buttons of a confirmation's footer, in order. */
const footerButtons = (dialog: HTMLElement) => within(dialog.querySelector<HTMLElement>('.ui-dialog-footer')!).getAllByRole('button')

/** The server lists `items` for every page and view asked for. */
function listing(...items: DownloadJob[]) {
  vi.mocked(downloadsApi.list).mockImplementation(async (query) => jobsPage(query, items))
}

function renderPage(signedIn: CurrentUser = owner) {
  signInAs(signedIn)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={['/jobs?tab=NeedsYou']}>
        <ConfirmProvider>
          <JobsPage />
        </ConfirmProvider>
      </MemoryRouter>
    </QueryClientProvider>,
  )
  return { client, user: userEvent.setup() }
}

/** The owner's downloaded job carrying a replacement with `changes`; `status` sets the download's. */
const replacingJob = (changes: Partial<DownloadJobReplacement>, status: DownloadJobStatus = 'Downloaded') => jobOf({
  id: 'd0b10000-0000-4000-8000-000000000013',
  releaseTitle: 'Alien (1979) 2160p Remux',
  status,
  progress: status === 'Downloaded' ? 1 : 0.3,
  replacement: replacementOf(changes),
})

/** A replacement the mover stopped for review: Retry is offered. */
const stopped = (changes: Partial<DownloadJobReplacement> = {}) =>
  replacingJob({ state: 'NeedsReview', stop: 'Move', reason: HELD_REASON, canRetry: true, ...changes })

/** A replacement whose old copy is in the quarantine: Restore and Delete for good are offered. */
const quarantined = (changes: Partial<DownloadJobReplacement> = {}) =>
  replacingJob({ state: 'Quarantined', quarantinedCopyCount: 1, canRestore: true, canDeleteForGood: true, ...changes })

/** The Replacement cell of the job's row. */
const replacementCellOf = (title: string) => rowOf(title).querySelector<HTMLElement>('td[data-label="Replacement"]')!

describe('JobsPage row actions', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    localStorage.clear()
  })

  it('puts the verbs in the order Retry, Keep both, Restore, Delete for good, Cancel job, and nothing else in the Actions cell', async () => {
    // Arrange
    const job = replacingJob({ state: 'NeedsReview', stop: 'Move', reason: HELD_REASON, canRetry: true, canCancel: true, canRestore: true, canDeleteForGood: true }, 'Downloading')
    listing(job)

    // Act
    renderPage()

    // Assert
    await screen.findByText(job.releaseTitle, asTitle)
    expect(actionsOf(job.releaseTitle)).toEqual(['Retry', 'Keep both', 'Restore', 'Delete for good', 'Cancel job'])
  })

  it.each<[string, DownloadJob, string[]]>([
    ['Keep both before Cancel job on a job still downloading', replacingJob({ state: 'Downloading', canCancel: true }, 'Downloading'), ['Keep both', 'Cancel job']],
    ['Retry before Keep both on a replacement the check stopped', stopped({ stop: 'Check', reason: NO_ID_REASON, canCancel: true }), ['Retry', 'Keep both']],
    ['Retry before Restore on a replacement in review that holds earlier copies in the quarantine', stopped({ canRestore: true }), ['Retry', 'Restore']],
    ['Restore before Delete for good on a replacement in the quarantine', quarantined(), ['Restore', 'Delete for good']],
  ])('puts %s', async (_, job, buttons) => {
    // Arrange
    listing(job)

    // Act
    renderPage()

    // Assert
    await screen.findByText(job.releaseTitle, asTitle)
    expect(actionsOf(job.releaseTitle)).toEqual(buttons)
  })

  // ── Retry ──

  // MAM-26 — the server's sentence ends "…then press Retry.", so the owner's Retry sits on the row.
  // MAM-27 — and on a row whose check of the new files stopped.
  describe('Retry of a replacement that stopped for review', () => {
    it.each<[string, Partial<DownloadJobReplacement>, CurrentUser, boolean]>([
      ['offers Retry to the owner on a replacement the mover stopped', {}, owner, true],
      ['offers no Retry to anyone else', {}, member, false],
      ['offers Retry to the owner on a replacement the check of its new files stopped', { stop: 'Check', reason: NO_ID_REASON }, owner, true],
      ['offers no Retry to anyone else on a replacement the check stopped', { stop: 'Check', reason: NO_ID_REASON }, member, false],
      ['offers no Retry while a copy only a drop settles is in the replacement', { stop: 'Check', reason: RENUMBERED_REASON, canRetry: false, dropOnlyCopyCount: 1 }, owner, false],
      ['offers no Retry on a replacement whose download the app can\'t follow', { stop: 'Download', reason: DOWNLOAD_REASON, canRetry: false }, owner, false],
      ['offers no Retry on a replacement in the quarantine', { state: 'Quarantined', stop: null, reason: null, canRetry: false, quarantinedCopyCount: 1 }, owner, false],
    ])('%s, on its row', async (_, changes, signedIn, offered) => {
      // Arrange
      const job = stopped(changes)
      listing(job)

      // Act
      renderPage(signedIn)

      // Assert
      await screen.findByText(job.releaseTitle, asTitle)
      expect(within(rowOf(job.releaseTitle)).queryByRole('button', { name: 'Retry' }) !== null).toBe(offered)
    })

    it.each<[string, Partial<DownloadJobReplacement>, Partial<DownloadJobReplacement>, string]>([
      ['back to be moved, and the row shows it verified', {}, { state: 'Verified' }, 'Verified, moves next'],
      ['a replacement the check stopped back to that check, and the row shows it checked again', { stop: 'Check', reason: NO_ID_REASON }, { state: 'Downloaded' }, 'Checking the new files'],
    ])('sends a replacement %s, without a confirmation', async (_, changes, after, line) => {
      // Arrange
      const job = stopped(changes)
      const retried = { ...job, replacement: replacementOf(after) }
      vi.mocked(replacementsApi.retry).mockResolvedValue(retried)
      listing(job)
      const { user } = renderPage()
      await screen.findByText(job.releaseTitle, asTitle)
      listing(retried)

      // Act
      await user.click(within(rowOf(job.releaseTitle)).getByRole('button', { name: 'Retry' }))

      // Assert
      expect(replacementsApi.retry).toHaveBeenCalledExactlyOnceWith(REPLACEMENT_ID)
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
      expect(await within(replacementCellOf(job.releaseTitle)).findByText(line)).toBeInTheDocument()
      expect(within(rowOf(job.releaseTitle)).queryByRole('button', { name: 'Retry' })).not.toBeInTheDocument()
      expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    })

    it.each<[string, Partial<DownloadJobReplacement>, string]>([
      ['the mover stopped', {}, 'Check the old copies again, and move them once nothing stands in the way'],
      ['the check of its new files stopped', { stop: 'Check', reason: NO_ID_REASON }, 'Check the new files again: Jellyfin\'s ids decide whether the old copies move'],
    ])('says in Retry\'s title what it checks again on a replacement %s', async (_, changes, title) => {
      // Arrange
      const job = stopped(changes)
      listing(job)

      // Act
      renderPage()

      // Assert
      await screen.findByText(job.releaseTitle, asTitle)
      expect(within(rowOf(job.releaseTitle)).getByRole('button', { name: 'Retry' })).toHaveAttribute('title', title)
    })

    it('shows the server sentence in the page\'s banner when a retry is refused', async () => {
      // Arrange
      const sentence = 'This replacement isn\'t waiting for review, so there\'s nothing to retry here.'
      vi.mocked(replacementsApi.retry).mockRejectedValue(apiError(409, sentence))
      const job = stopped()
      listing(job)
      const { user } = renderPage()
      await screen.findByText(job.releaseTitle, asTitle)

      // Act
      await user.click(within(rowOf(job.releaseTitle)).getByRole('button', { name: 'Retry' }))

      // Assert
      const banner = await screen.findByRole('alert')
      expect(banner).toHaveTextContent(sentence)
      expect(rowOf(job.releaseTitle)).not.toContainElement(banner)
    })

    it('says Retrying… with a spinner while its retry is on its way, and names the button in its title for a phone', async () => {
      // Arrange
      vi.mocked(replacementsApi.retry).mockReturnValue(new Promise(() => {}))
      const job = stopped()
      listing(job)
      const { user } = renderPage()
      await screen.findByText(job.releaseTitle, asTitle)
      const button = within(rowOf(job.releaseTitle)).getByRole('button', { name: 'Retry' })

      // Act
      await user.click(button)

      // Assert
      expect(button).toHaveTextContent('Retrying…')
      expect(button).toBeDisabled()
      expect(button.querySelector('svg')).toHaveClass('animate-spin')
      expect(button).toHaveAttribute('title', 'Check the old copies again, and move them once nothing stands in the way')
      expect(button.querySelector('.label-text')).toHaveTextContent('Retrying…')
      expect(button.closest('.settings-actions')).not.toBeNull()
    })
  })

  // ── Keep both ──

  // MAM-27 — the owner's Keep both cancels only the replacement: both copies stay, and the download goes on.
  describe('Keep both on a replacement', () => {
    /** The owner's downloading job whose replacement waits for it; `changes` sets the replacement. */
    const waitingJob = (changes: Partial<DownloadJobReplacement> = {}) =>
      replacingJob({ state: 'Downloading', canCancel: true, ...changes }, 'Downloading')

    /** The same job once Keep both went through: the replacement cancelled, the download going on. */
    const keptBoth = (job: DownloadJob) => ({ ...job, replacement: replacementOf({ state: 'Cancelled', reason: KEPT_BOTH_REASON }) })

    /** Opens the confirmation of Keep both on the row of `job`. */
    async function openKeepBoth(user: ReturnType<typeof userEvent.setup>, job: DownloadJob) {
      await screen.findByText(job.releaseTitle, asTitle)
      await user.click(within(rowOf(job.releaseTitle)).getByRole('button', { name: 'Keep both' }))
      return screen.findByRole('dialog', { name: `Keep both copies of ${job.releaseTitle}?` })
    }

    it.each<[string, Partial<DownloadJobReplacement>, CurrentUser, boolean]>([
      ['offers Keep both to the owner on a replacement none of whose copies has started to move', {}, owner, true],
      ['offers no Keep both to anyone else', {}, member, false],
      ['offers Keep both to the owner on a replacement that stopped for review', { state: 'NeedsReview', stop: 'Check', reason: NO_ID_REASON, canRetry: true }, owner, true],
      ['offers no Keep both on a replacement part of which is in the quarantine', { state: 'Quarantined', canCancel: false, quarantinedCopyCount: 1 }, owner, false],
      ['offers no Keep both on a replacement that ended', { state: 'Cancelled', reason: KEPT_BOTH_REASON, canCancel: false }, owner, false],
    ])('%s, on its row', async (_, changes, signedIn, offered) => {
      // Arrange
      const job = waitingJob(changes)
      listing(job)

      // Act
      renderPage(signedIn)

      // Assert
      await screen.findByText(job.releaseTitle, asTitle)
      expect(within(rowOf(job.releaseTitle)).queryByRole('button', { name: 'Keep both' }) !== null).toBe(offered)
    })

    it('cancels only the replacement after a confirmation that says the download goes on, and the row shows it cancelled', async () => {
      // Arrange
      const job = waitingJob()
      vi.mocked(replacementsApi.cancel).mockResolvedValue(keptBoth(job))
      listing(job)
      const { user } = renderPage()
      const confirmation = await openKeepBoth(user, job)
      listing(keptBoth(job))

      // Act
      await user.click(within(confirmation).getByRole('button', { name: 'Keep both' }))

      // Assert
      expect(confirmation).toHaveTextContent(
        'The replacement is cancelled: the old copy stays where it is, and nothing of it moves. The download is left as it is, and nothing is sent to Local qBittorrent. This can\'t be undone.')
      expect(confirmation).not.toHaveClass('confirm-dialog-wide')
      expect(replacementsApi.cancel).toHaveBeenCalledExactlyOnceWith(REPLACEMENT_ID)
      expect(downloadsApi.cancel).not.toHaveBeenCalled()
      expect(await within(replacementCellOf(job.releaseTitle)).findByText('Cancelled: nothing moved')).toBeInTheDocument()
      const row = within(rowOf(job.releaseTitle))
      expect(row.queryByRole('button', { name: 'Keep both' })).not.toBeInTheDocument()
      expect(row.getByRole('button', { name: 'Cancel job' })).toBeInTheDocument()
      expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    })

    it('says how many old copies stay when several were ticked', async () => {
      // Arrange
      const job = waitingJob({ copyCount: 3 })
      listing(job)
      const { user } = renderPage()

      // Act
      const confirmation = await openKeepBoth(user, job)

      // Assert
      expect(confirmation).toHaveTextContent(
        'The replacement is cancelled: the 3 old copies stay where they are, and nothing of them moves. The download is left as it is, and nothing is sent to Local qBittorrent. This can\'t be undone.')
    })

    it('puts Keep both first and Keep replacing last in its confirmation, not in red, and cancels nothing on Keep replacing', async () => {
      // Arrange
      const job = waitingJob()
      listing(job)
      const { user } = renderPage()
      const confirmation = await openKeepBoth(user, job)

      // Act
      const buttons = footerButtons(confirmation)
      await user.click(within(confirmation).getByRole('button', { name: 'Keep replacing' }))

      // Assert
      expect(buttons.map((button) => button.textContent)).toEqual(['Keep both', 'Keep replacing'])
      expect(buttons[0]).not.toHaveClass('ui-button-destructive')
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
      expect(replacementsApi.cancel).not.toHaveBeenCalled()
    })

    it('stops following the download from Cancel job, not from Keep both', async () => {
      // Arrange
      const job = waitingJob()
      vi.mocked(downloadsApi.cancel).mockReturnValue(new Promise(() => {}))
      listing(job)
      const { user } = renderPage()
      await screen.findByText(job.releaseTitle, asTitle)
      await user.click(within(rowOf(job.releaseTitle)).getByRole('button', { name: 'Cancel job' }))
      const confirmation = await screen.findByRole('dialog', { name: `Cancel the job for ${job.releaseTitle}?` })

      // Act
      await user.click(within(confirmation).getByRole('button', { name: 'Cancel job' }))

      // Assert
      expect(downloadsApi.cancel).toHaveBeenCalledExactlyOnceWith(job.id)
      expect(replacementsApi.cancel).not.toHaveBeenCalled()
    })

    it.each<[string, ReturnType<typeof apiError>, string]>([
      ['the server sentence when Keep both is refused', apiError(409, 'Part of this replacement has left its folders already, so it can\'t be cancelled: Restore puts back what is in the quarantine.'), 'Part of this replacement has left its folders already, so it can\'t be cancelled: Restore puts back what is in the quarantine.'],
      ['its own sentence when Keep both fails and the API gives none', apiError(500), 'The replacement couldn\'t be cancelled.'],
    ])('shows %s in the page\'s banner', async (_, failure, sentence) => {
      // Arrange
      vi.mocked(replacementsApi.cancel).mockRejectedValue(failure)
      const job = waitingJob()
      listing(job)
      const { user } = renderPage()
      const confirmation = await openKeepBoth(user, job)

      // Act
      await user.click(within(confirmation).getByRole('button', { name: 'Keep both' }))

      // Assert
      expect(await screen.findByRole('alert')).toHaveTextContent(sentence)
    })

    it('says Keeping both… with a spinner while it is on its way, names the button in its title for a phone, and holds Cancel job', async () => {
      // Arrange
      vi.mocked(replacementsApi.cancel).mockReturnValue(new Promise(() => {}))
      const job = waitingJob()
      listing(job)
      const { user } = renderPage()
      await screen.findByText(job.releaseTitle, asTitle)
      // Taken before the confirmation opens: the dialog hides the page behind it from queries by role.
      const button = within(rowOf(job.releaseTitle)).getByRole('button', { name: 'Keep both' })
      const confirmation = await openKeepBoth(user, job)

      // Act
      await user.click(within(confirmation).getByRole('button', { name: 'Keep both' }))

      // Assert
      expect(button).toHaveTextContent('Keeping both…')
      expect(button).toBeDisabled()
      expect(button.querySelector('svg')).toHaveClass('animate-spin')
      expect(button).toHaveAttribute('title', 'Cancel only the replacement: both copies stay, and the download goes on')
      expect(button.querySelector('.label-text')).toHaveTextContent('Keeping both…')
      expect(button.closest('.settings-actions')).not.toBeNull()
      // One busy flag for the row: Cancel job waits too.
      expect(within(rowOf(job.releaseTitle)).getByRole('button', { name: 'Cancel job' })).toBeDisabled()
    })
  })

  // ── Restore ──

  // MAM-41 — the job's page's Restore, on the row: asked for what the quarantine holds when pressed.
  describe('Restore of old copies in the quarantine', () => {
    beforeEach(() => {
      vi.mocked(replacementsApi.get).mockResolvedValue(replacementDetailsOf())
    })

    /** Opens Restore's confirmation on the row of `job`. */
    async function openRestore(user: ReturnType<typeof userEvent.setup>, job: DownloadJob) {
      await screen.findByText(job.releaseTitle, asTitle)
      await user.click(within(rowOf(job.releaseTitle)).getByRole('button', { name: 'Restore' }))
      return screen.findByRole('dialog', { name: 'Put the old copy back?' })
    }

    it.each<[string, Partial<DownloadJobReplacement>, CurrentUser, boolean]>([
      ['offers Restore to the owner while the server says it can be done', {}, owner, true],
      ['offers no Restore to anyone else', {}, member, false],
      ['offers no Restore while the server says it can\'t be done', { canRestore: false }, owner, false],
    ])('%s, on its row', async (_, changes, signedIn, offered) => {
      // Arrange
      const job = quarantined(changes)
      listing(job)

      // Act
      renderPage(signedIn)

      // Assert
      await screen.findByText(job.releaseTitle, asTitle)
      expect(within(rowOf(job.releaseTitle)).queryByRole('button', { name: 'Restore' }) !== null).toBe(offered)
    })

    it('puts the old copies back after the job page\'s plain confirmation, asked for what the quarantine holds then, and the row says they are back', async () => {
      // Arrange
      const job = quarantined()
      const restored = { ...job, replacement: replacementOf({ state: 'Restored' }) }
      vi.mocked(replacementsApi.restore).mockResolvedValue(restored)
      listing(job)
      const { user } = renderPage()
      const confirmation = await openRestore(user, job)
      const buttons = footerButtons(confirmation)
      listing(restored)

      // Act
      await user.click(within(confirmation).getByRole('button', { name: 'Restore' }))

      // Assert
      expect(replacementsApi.get).toHaveBeenCalledWith(REPLACEMENT_ID)
      expect(confirmation).toHaveTextContent(RESTORE_MESSAGE)
      expect(buttons.map((button) => button.textContent)).toEqual(['Restore', 'Cancel'])
      expect(buttons[0]).not.toHaveClass('ui-button-destructive')
      expect(confirmation).not.toHaveClass('confirm-dialog-wide')
      expect(replacementsApi.restore).toHaveBeenCalledExactlyOnceWith(REPLACEMENT_ID)
      expect(await within(replacementCellOf(job.releaseTitle)).findByText('Undone: the old copy is back')).toBeInTheDocument()
      expect(within(rowOf(job.releaseTitle)).queryByRole('button', { name: 'Restore' })).not.toBeInTheDocument()
      expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    })

    it('counts the copies in its title by the videos the quarantine holds', async () => {
      // Arrange
      vi.mocked(replacementsApi.get).mockResolvedValue(replacementDetailsOf({
        names: [alienVideo, alienSubtitle, { ...alienVideo, id: 'e1a50000-0000-4000-8000-000000000104', originalPath: '/media/movies/Alien (1979)/Alien.Directors.Cut.mkv' }],
      }))
      const job = quarantined({ quarantinedCopyCount: 2 })
      listing(job)
      const { user } = renderPage()
      await screen.findByText(job.releaseTitle, asTitle)

      // Act
      await user.click(within(rowOf(job.releaseTitle)).getByRole('button', { name: 'Restore' }))

      // Assert
      expect(await screen.findByRole('dialog', { name: 'Put the old copies back?' })).toBeInTheDocument()
    })

    it('puts nothing back on Cancel', async () => {
      // Arrange
      const job = quarantined()
      listing(job)
      const { user } = renderPage()
      const confirmation = await openRestore(user, job)

      // Act
      await user.click(within(confirmation).getByRole('button', { name: 'Cancel' }))

      // Assert
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
      expect(replacementsApi.restore).not.toHaveBeenCalled()
    })

    it('shows the server sentence in the page\'s banner when a restore stops part-way', async () => {
      // Arrange
      const sentence = '“Alien.en.srt” couldn\'t be put back: “/media/movies/Alien (1979)/Alien.en.srt” already holds a file. 1 of 2 names is back where it was.'
      vi.mocked(replacementsApi.restore).mockRejectedValue(apiError(409, sentence))
      const job = quarantined()
      listing(job)
      const { user } = renderPage()
      const confirmation = await openRestore(user, job)

      // Act
      await user.click(within(confirmation).getByRole('button', { name: 'Restore' }))

      // Assert
      const banner = await screen.findByRole('alert')
      expect(banner).toHaveTextContent(sentence)
      expect(rowOf(job.releaseTitle)).not.toContainElement(banner)
    })

    it('says so in the page\'s banner, and asks nothing, when what the quarantine holds can\'t be asked for', async () => {
      // Arrange
      vi.mocked(replacementsApi.get).mockRejectedValue(apiError(500))
      const job = quarantined()
      listing(job)
      const { user } = renderPage()
      await screen.findByText(job.releaseTitle, asTitle)

      // Act
      await user.click(within(rowOf(job.releaseTitle)).getByRole('button', { name: 'Restore' }))

      // Assert
      expect(await screen.findByRole('alert')).toHaveTextContent('Could not put the old copies back. Reload the page and try again.')
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
      expect(replacementsApi.restore).not.toHaveBeenCalled()
    })

    it('says Restoring… with a spinner while the restore is on its way, and holds Delete for good meanwhile', async () => {
      // Arrange
      vi.mocked(replacementsApi.restore).mockReturnValue(new Promise(() => {}))
      const job = quarantined()
      listing(job)
      const { user } = renderPage()
      const confirmation = await openRestore(user, job)

      // Act
      await user.click(within(confirmation).getByRole('button', { name: 'Restore' }))

      // Assert
      const restoring = await within(rowOf(job.releaseTitle)).findByRole('button', { name: 'Restoring…' })
      expect(restoring).toBeDisabled()
      expect(restoring.querySelector('svg')).toHaveClass('animate-spin')
      expect(restoring).toHaveAttribute('title', 'Put the old copies in the quarantine back where they were')
      expect(within(rowOf(job.releaseTitle)).getByRole('button', { name: 'Delete for good' })).toBeDisabled()
    })
  })

  // ── Delete for good ──

  // MAM-41 — the job's page's Delete for good, on the row: the names the quarantine holds when pressed,
  // listed in the wide confirmation, are the names sent.
  describe('Delete for good of old copies in the quarantine', () => {
    beforeEach(() => {
      vi.mocked(replacementsApi.get).mockResolvedValue(replacementDetailsOf())
    })

    /** Opens Delete for good's confirmation on the row of `job`. */
    async function openDelete(user: ReturnType<typeof userEvent.setup>, job: DownloadJob) {
      await screen.findByText(job.releaseTitle, asTitle)
      await user.click(within(rowOf(job.releaseTitle)).getByRole('button', { name: 'Delete for good' }))
      return screen.findByRole('dialog', { name: 'Delete the old copy for good?' })
    }

    it.each<[string, Partial<DownloadJobReplacement>, CurrentUser, boolean]>([
      ['offers Delete for good to the owner while the server says it can be done', {}, owner, true],
      ['offers no Delete for good to anyone else', {}, member, false],
      ['offers no Delete for good while the server says it can\'t be done', { canDeleteForGood: false }, owner, false],
    ])('%s, on its row', async (_, changes, signedIn, offered) => {
      // Arrange
      const job = quarantined(changes)
      listing(job)

      // Act
      renderPage(signedIn)

      // Assert
      await screen.findByText(job.releaseTitle, asTitle)
      expect(within(rowOf(job.releaseTitle)).queryByRole('button', { name: 'Delete for good' }) !== null).toBe(offered)
    })

    it('shows Delete for good in red outline, named in its title for a phone', async () => {
      // Arrange
      const job = quarantined()
      listing(job)

      // Act
      renderPage()

      // Assert
      await screen.findByText(job.releaseTitle, asTitle)
      const button = within(rowOf(job.releaseTitle)).getByRole('button', { name: 'Delete for good' })
      expect(button).toHaveClass('ui-button-danger')
      expect(button).toHaveAttribute('title', 'Delete the old copies in the quarantine for good')
      expect(button.querySelector('.label-text')).toHaveTextContent('Delete for good')
    })

    it('asks in the wide confirmation, listing every path in the quarantine with its size and the room it frees, then deletes exactly those, and the row says they were deleted', async () => {
      // Arrange
      const job = quarantined()
      const completed = { ...job, replacement: replacementOf({ state: 'Completed' }) }
      vi.mocked(replacementsApi.deleteForGood).mockResolvedValue(completed)
      listing(job)
      const { user } = renderPage()
      const confirmation = await openDelete(user, job)
      const listed = within(confirmation).getAllByRole('listitem').map((item) => [
        item.querySelector('.jobs-file-path')?.textContent,
        item.querySelector('.jobs-quarantine-size')?.textContent,
      ])
      const buttons = footerButtons(confirmation)
      listing(completed)

      // Act
      await user.click(within(confirmation).getByRole('button', { name: 'Delete for good' }))

      // Assert
      expect(confirmation).toHaveClass('confirm-dialog', 'confirm-dialog-wide')
      expect(confirmation).toHaveTextContent('These 2 files are deleted from the quarantine, and can\'t be restored afterwards:')
      expect(listed).toEqual([[alienVideo.originalPath, '40 GiB'], [alienSubtitle.originalPath, '50 KiB']])
      expect(confirmation).toHaveTextContent('Frees 40 GiB.')
      expect(buttons.map((button) => button.textContent)).toEqual(['Delete for good', 'Cancel'])
      expect(buttons[0]).toHaveClass('ui-button-destructive')
      expect(replacementsApi.deleteForGood).toHaveBeenCalledExactlyOnceWith(REPLACEMENT_ID, [alienVideo.id, alienSubtitle.id])
      expect(await within(replacementCellOf(job.releaseTitle)).findByText('Replaced: old copy deleted')).toBeInTheDocument()
      expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    })

    it('sends exactly the names the confirmation listed, though what the quarantine holds changed while it was open', async () => {
      // Arrange
      vi.mocked(replacementsApi.deleteForGood).mockReturnValue(new Promise(() => {}))
      const job = quarantined()
      listing(job)
      const { client, user } = renderPage()
      const confirmation = await openDelete(user, job)
      vi.mocked(replacementsApi.get).mockResolvedValue(replacementDetailsOf({ names: [alienVideo] }))
      await client.refetchQueries()

      // Act
      await user.click(within(confirmation).getByRole('button', { name: 'Delete for good' }))

      // Assert
      expect(replacementsApi.deleteForGood).toHaveBeenCalledExactlyOnceWith(REPLACEMENT_ID, [alienVideo.id, alienSubtitle.id])
    })

    it('deletes nothing on Cancel', async () => {
      // Arrange
      const job = quarantined()
      listing(job)
      const { user } = renderPage()
      const confirmation = await openDelete(user, job)

      // Act
      await user.click(within(confirmation).getByRole('button', { name: 'Cancel' }))

      // Assert
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
      expect(replacementsApi.deleteForGood).not.toHaveBeenCalled()
    })

    it.each<[string, ReturnType<typeof apiError>, string]>([
      ['the server sentence when the list changed meanwhile', apiError(409, 'What this replacement holds in the quarantine changed since you opened it. Open Delete for good again.'), 'What this replacement holds in the quarantine changed since you opened it. Open Delete for good again.'],
      ['its own sentence when a deletion fails and the API gives none', apiError(500), 'Could not delete the old copies. Reload the page and try again.'],
    ])('shows %s in the page\'s banner', async (_, failure, sentence) => {
      // Arrange
      vi.mocked(replacementsApi.deleteForGood).mockRejectedValue(failure)
      const job = quarantined()
      listing(job)
      const { user } = renderPage()
      const confirmation = await openDelete(user, job)

      // Act
      await user.click(within(confirmation).getByRole('button', { name: 'Delete for good' }))

      // Assert
      const banner = await screen.findByRole('alert')
      expect(banner).toHaveTextContent(sentence)
      expect(rowOf(job.releaseTitle)).not.toContainElement(banner)
    })

    it('takes a refusal\'s banner away on Dismiss', async () => {
      // Arrange
      vi.mocked(replacementsApi.deleteForGood).mockRejectedValue(apiError(500))
      const job = quarantined()
      listing(job)
      const { user } = renderPage()
      const confirmation = await openDelete(user, job)
      await user.click(within(confirmation).getByRole('button', { name: 'Delete for good' }))
      await screen.findByRole('alert')

      // Act
      await user.click(screen.getByRole('button', { name: 'Dismiss' }))

      // Assert
      expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    })

    it('says Deleting… with a spinner while the deletion is on its way, and holds Restore meanwhile', async () => {
      // Arrange
      vi.mocked(replacementsApi.deleteForGood).mockReturnValue(new Promise(() => {}))
      const job = quarantined()
      listing(job)
      const { user } = renderPage()
      const confirmation = await openDelete(user, job)

      // Act
      await user.click(within(confirmation).getByRole('button', { name: 'Delete for good' }))

      // Assert
      const deleting = await within(rowOf(job.releaseTitle)).findByRole('button', { name: 'Deleting…' })
      expect(deleting).toBeDisabled()
      expect(deleting.querySelector('svg')).toHaveClass('animate-spin')
      expect(within(rowOf(job.releaseTitle)).getByRole('button', { name: 'Restore' })).toBeDisabled()
    })
  })

  // MAM-41 — the server refuses both while replacement is switched off; Retry and Keep both write nothing
  // to the media, so they stay.
  it('disables Restore and Delete for good with the reason while replacement is switched off, and leaves Retry and Keep both usable', async () => {
    // Arrange
    const job = stopped({ switchedOff: true, canCancel: true, canRestore: true, canDeleteForGood: true })
    listing(job)

    // Act
    renderPage()

    // Assert
    await screen.findByText(job.releaseTitle, asTitle)
    const row = within(rowOf(job.releaseTitle))
    for (const name of ['Restore', 'Delete for good']) {
      const button = row.getByRole('button', { name })
      expect(button).toBeDisabled()
      expect(button.closest('.settings-action-reason')).toHaveAttribute('title', SWITCHED_OFF)
    }
    expect(row.getByRole('button', { name: 'Retry' })).toBeEnabled()
    expect(row.getByRole('button', { name: 'Keep both' })).toBeEnabled()
    expect(replacementsApi.get).not.toHaveBeenCalled()
  })

  it('leaves another job\'s actions alone while one row\'s action is on its way', async () => {
    // Arrange
    vi.mocked(replacementsApi.retry).mockReturnValue(new Promise(() => {}))
    const job = stopped()
    const other = jobOf({ id: 'd0b10000-0000-4000-8000-000000000016', releaseTitle: 'Arrival (2016) 2160p', status: 'Downloading', progress: 0.4 })
    listing(job, other)
    const { user } = renderPage()
    await screen.findByText(job.releaseTitle, asTitle)

    // Act
    await user.click(within(rowOf(job.releaseTitle)).getByRole('button', { name: 'Retry' }))

    // Assert
    await within(rowOf(job.releaseTitle)).findByRole('button', { name: 'Retrying…' })
    expect(within(rowOf(other.releaseTitle)).getByRole('button', { name: 'Cancel job' })).toBeEnabled()
  })
})
