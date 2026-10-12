import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { ConfirmProvider } from '@/components/shared/ConfirmDialog'
import { downloadsApi } from '@/lib/downloads-api'
import { formatDateTime, formatNumber } from '@/lib/format'
import type { CurrentUser, DownloadJob, DownloadJobStatus, DownloadJobView } from '@/lib/types'
import { JobsPage } from '@/pages/JobsPage'
import { apiError, networkError } from '@/test/api-error'
import { member, owner, signInAs } from '@/test/auth-session'
import { downloadedJob, downloadingJob, jobOf, jobsPage, sentMinutesAgo } from '@/test/downloads'
import { replacementOf } from '@/test/replacements'

vi.mock('@/lib/downloads-api')
vi.mock('@/lib/replacements-api')

// MAM-14 — the Jobs page around its rows: the caller's own jobs, newest first, paged by the server, the
// states of its list, and Cancel job. MAM-41 — the tabs that sort the jobs by what they wait for (Needs
// you, In progress, Finished, All) with their counts and their place in the address, the short columns
// (Release, Download, the owner's Replacement, Actions), and each tab's empty sentence. The row's
// replacement actions are in JobsPage.actions.test.tsx, its details in JobsPage.details.test.tsx.

/** What the last request for the list asked for. */
const lastQuery = () => vi.mocked(downloadsApi.list).mock.lastCall?.[0]

/** Every request for the list so far, in order. */
const queries = () => vi.mocked(downloadsApi.list).mock.calls.map(([query]) => query)

/** Matches a job's release title only. */
const asTitle = { selector: '.settings-name-text' }

/** The table row (or phone card) of the job whose release is `title`. */
const rowOf = (title: string) => screen.getByText(title, asTitle).closest('tr')!

/** When a job was sent, as its row says it under the title. */
const sentAt = (job: DownloadJob) => formatDateTime(job.createdUtc, { dateStyle: 'medium', timeStyle: 'short' })

/** The server lists `items`, out of `total`, whatever page and view are asked for. */
function listing(items: DownloadJob[], total = items.length) {
  vi.mocked(downloadsApi.list).mockImplementation(async (query) => jobsPage(query, items, total))
}

/** The address the page is at, so a test can read what the tabs put there. */
function Address() {
  const location = useLocation()
  return <output aria-label="Address">{location.pathname + location.search}</output>
}

const address = () => screen.getByRole('status', { name: 'Address' }).textContent

function renderPage(signedIn: CurrentUser = member, path = '/jobs') {
  signInAs(signedIn)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[path]}>
        <ConfirmProvider>
          <Routes>
            <Route path="jobs" element={<JobsPage />} />
            <Route path="jobs/:id" element={<p>Job details</p>} />
            <Route path="search" element={<p>Search page</p>} />
          </Routes>
          <Address />
        </ConfirmProvider>
      </MemoryRouter>
    </QueryClientProvider>,
  )
  return { client, user: userEvent.setup() }
}

/** The tabs' names, in their order. */
const tabNames = () =>
  within(screen.getByRole('tablist', { name: 'Jobs by what they wait for' })).getAllByRole('tab').map((tab) => tab.textContent)

describe('JobsPage', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    localStorage.clear()
    listing([downloadingJob, downloadedJob])
  })

  // ── the rows ──

  it('lists the caller\'s jobs, newest first as the server sends them, each with where its download stands and, under its title, its client, destination and when it was sent', async () => {
    // Arrange
    const query = { view: undefined, page: 1, pageSize: 25 }

    // Act
    renderPage()

    // Assert
    expect(await screen.findByText(downloadingJob.releaseTitle, asTitle)).toBeInTheDocument()
    expect(lastQuery()).toEqual(query)
    expect(screen.getAllByRole('columnheader').map((header) => header.textContent)).toEqual(['Release', 'Download', 'Actions'])
    expect(screen.getAllByText(/./, asTitle).map((title) => title.textContent))
      .toEqual([downloadingJob.releaseTitle, downloadedJob.releaseTitle])
    const downloading = within(rowOf(downloadingJob.releaseTitle))
    expect(downloading.getByText('Downloading')).toBeInTheDocument()
    expect(downloading.getByRole('progressbar', { name: 'Download progress' })).toHaveAttribute('value', '0.4567')
    expect(downloading.getByText('45.6%')).toBeInTheDocument()
    expect(downloading.getByText(`Local qBittorrent · Movies · ${sentAt(downloadingJob)}`)).toHaveClass('ui-hint')
    const downloaded = within(rowOf(downloadedJob.releaseTitle))
    expect(downloaded.getByText('Downloaded').closest('p')).toHaveAttribute('data-status', 'Succeeded')
    expect(downloaded.queryByRole('progressbar')).not.toBeInTheDocument()
    expect(downloaded.getByText(`Seedbox qBittorrent · Films · ${sentAt(downloadedJob)}`)).toBeInTheDocument()
    expect(screen.getByText('1–2 of 2')).toBeInTheDocument()
  })

  it('names the Download cell for a phone card, and lets the title head the card', async () => {
    // Arrange
    listing([downloadingJob])

    // Act
    renderPage()

    // Assert
    await screen.findByText(downloadingJob.releaseTitle, asTitle)
    const row = rowOf(downloadingJob.releaseTitle)
    expect(row.querySelector('td[data-label="Download"]')).toHaveClass('jobs-stacked-cell')
    expect(row.querySelector('td.jobs-release-cell')).not.toHaveAttribute('data-label')
  })

  it('shows the owner a Replacement column between Download and Actions, with a dash on a job without a replacement', async () => {
    // Arrange
    const replacing = jobOf({ id: 'd0b10000-0000-4000-8000-000000000020', releaseTitle: 'Alien (1979) 2160p Remux', replacement: replacementOf({ state: 'Downloading', copyCount: 2 }) })
    listing([replacing, downloadedJob])

    // Act
    renderPage(owner)

    // Assert
    await screen.findByText(replacing.releaseTitle, asTitle)
    expect(screen.getAllByRole('columnheader').map((header) => header.textContent)).toEqual(['Release', 'Download', 'Replacement', 'Actions'])
    const replacementCell = rowOf(replacing.releaseTitle).querySelector('td[data-label="Replacement"]')!
    expect(replacementCell).toHaveTextContent('Waits for the download: 2 copies')
    expect(replacementCell).toHaveClass('jobs-stacked-cell')
    expect(rowOf(downloadedJob.releaseTitle).querySelector('td[data-label="Replacement"]')).toHaveTextContent('—No replacement')
  })

  it('shows a member no Replacement column, nor a replacement on a row, though a job carried one', async () => {
    // Arrange
    const replacing = jobOf({ id: 'd0b10000-0000-4000-8000-000000000021', releaseTitle: 'Alien (1979) 2160p Remux', replacement: replacementOf({ state: 'Quarantined', quarantinedCopyCount: 1, canRestore: true, canDeleteForGood: true }) })
    listing([replacing])

    // Act
    renderPage(member)

    // Assert
    await screen.findByText(replacing.releaseTitle, asTitle)
    expect(screen.getAllByRole('columnheader').map((header) => header.textContent)).toEqual(['Release', 'Download', 'Actions'])
    const row = within(rowOf(replacing.releaseTitle))
    expect(row.queryByText(/quarantine/)).not.toBeInTheDocument()
    expect(row.queryByRole('button', { name: 'Restore' })).not.toBeInTheDocument()
    expect(row.queryByRole('button', { name: 'Delete for good' })).not.toBeInTheDocument()
  })

  it('links each release title to its job\'s details', async () => {
    // Arrange
    const { user } = renderPage()
    const title = await screen.findByText(downloadingJob.releaseTitle, asTitle)

    // Act
    await user.click(title)

    // Assert
    expect(title).toHaveAttribute('href', `/jobs/${downloadingJob.id}`)
    expect(await screen.findByText('Job details')).toBeInTheDocument()
  })

  it('names the category sent when the job\'s destination has no name', async () => {
    // Arrange
    const job = jobOf({ destinationName: null, clientDestinationKey: 'Movies/4K' })
    listing([job])

    // Act
    renderPage()

    // Assert
    await screen.findByText(job.releaseTitle, asTitle)
    expect(within(rowOf(job.releaseTitle)).getByText(`Local qBittorrent · Movies/4K · ${sentAt(job)}`)).toBeInTheDocument()
  })

  // ── the tabs ──

  it('shows the four tabs on All, asking for every job', async () => {
    // Arrange
    const path = '/jobs'

    // Act
    renderPage(member, path)

    // Assert
    expect(tabNames()).toEqual(['Needs you', 'In progress', 'Finished', 'All'])
    expect(screen.getByRole('tab', { name: 'All' })).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByRole('tabpanel', { name: 'All' })).toContainElement(await screen.findByRole('table'))
    expect(lastQuery()).toEqual({ view: undefined, page: 1, pageSize: 25 })
  })

  it('counts in their names the jobs that need the user and those in progress, as the server counts them whatever the tab', async () => {
    // Arrange
    vi.mocked(downloadsApi.list).mockImplementation(async (query) =>
      jobsPage(query, [downloadedJob], 1, { needsYouCount: 3, inProgressCount: 1_204 }))

    // Act
    renderPage(member, '/jobs?tab=Finished')

    // Assert
    await screen.findByText(downloadedJob.releaseTitle, asTitle)
    expect(tabNames()).toEqual(['Needs you (3)', `In progress (${formatNumber(1_204)})`, 'Finished', 'All'])
    expect(screen.getByRole('tab', { name: 'Finished' })).toHaveAttribute('aria-selected', 'true')
  })

  it('shows no number on a tab that holds no job', async () => {
    // Arrange
    vi.mocked(downloadsApi.list).mockImplementation(async (query) =>
      jobsPage(query, [downloadingJob], 1, { needsYouCount: 0, inProgressCount: 1 }))

    // Act
    renderPage()

    // Assert
    await screen.findByText(downloadingJob.releaseTitle, asTitle)
    expect(tabNames()).toEqual(['Needs you', 'In progress (1)', 'Finished', 'All'])
  })

  it.each<[string, DownloadJobView]>([
    ['Needs you', 'NeedsYou'],
    ['In progress', 'InProgress'],
    ['Finished', 'Finished'],
  ])('asks the server for the %s tab\'s view alone', async (name, view) => {
    // Arrange
    listing([downloadedJob])
    const { user } = renderPage()
    await screen.findByText('1–1 of 1')

    // Act
    await user.click(screen.getByRole('tab', { name }))

    // Assert
    await vi.waitFor(() => expect(lastQuery()).toEqual({ view, page: 1, pageSize: 25 }))
    expect(await screen.findByRole('tabpanel', { name })).toBeInTheDocument()
  })

  it('puts the tab picked in the address, and leaves All out of it', async () => {
    // Arrange
    listing([downloadedJob])
    const { user } = renderPage()
    await screen.findByText('1–1 of 1')

    // Act
    await user.click(screen.getByRole('tab', { name: 'Needs you' }))
    const needsYou = address()
    await user.click(screen.getByRole('tab', { name: 'All' }))

    // Assert
    expect(needsYou).toBe('/jobs?tab=NeedsYou')
    expect(address()).toBe('/jobs')
  })

  it('opens on the tab its address names, asking for that view alone', async () => {
    // Arrange
    const path = '/jobs?tab=InProgress'

    // Act
    renderPage(member, path)

    // Assert
    expect(screen.getByRole('tab', { name: 'In progress' })).toHaveAttribute('aria-selected', 'true')
    await screen.findByText(downloadingJob.releaseTitle, asTitle)
    expect(queries()[0]).toEqual({ view: 'InProgress', page: 1, pageSize: 25 })
  })

  it('reads a tab its address doesn\'t know as All', async () => {
    // Arrange
    const path = '/jobs?tab=Downloading'

    // Act
    renderPage(member, path)

    // Assert
    expect(screen.getByRole('tab', { name: 'All' })).toHaveAttribute('aria-selected', 'true')
    await vi.waitFor(() => expect(lastQuery()).toEqual({ view: undefined, page: 1, pageSize: 25 }))
  })

  it('pages through the jobs and starts another tab at page 1', async () => {
    // Arrange
    // Downloaded: a page of ended jobs asks nothing more, so the requests are the clicks' alone.
    listing([downloadedJob], 30)
    const { user } = renderPage()
    await screen.findByText('1–25 of 30')
    await user.click(screen.getByRole('button', { name: 'Next page' }))
    await screen.findByText('26–30 of 30')

    // Act
    await user.click(screen.getByRole('tab', { name: 'Finished' }))

    // Assert
    expect(await screen.findByText('1–25 of 30')).toBeInTheDocument()
    expect(queries()).toEqual([
      { view: undefined, page: 1, pageSize: 25 },
      { view: undefined, page: 2, pageSize: 25 },
      { view: 'Finished', page: 1, pageSize: 25 },
    ])
  })

  it('says Loading… rather than show the last tab\'s jobs while another tab loads', async () => {
    // Arrange
    const { user } = renderPage()
    await screen.findByText(downloadingJob.releaseTitle, asTitle)
    vi.mocked(downloadsApi.list).mockReturnValue(new Promise(() => {}))

    // Act
    await user.click(screen.getByRole('tab', { name: 'Finished' }))

    // Assert
    expect(await screen.findByText('Loading…')).toBeInTheDocument()
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
  })

  it('keeps the page on screen while the next page of its tab loads', async () => {
    // Arrange
    listing([downloadedJob], 30)
    const { user } = renderPage(member, '/jobs?tab=Finished')
    await screen.findByText('1–25 of 30')
    vi.mocked(downloadsApi.list).mockReturnValue(new Promise(() => {}))

    // Act
    await user.click(screen.getByRole('button', { name: 'Next page' }))

    // Assert
    expect(lastQuery()).toEqual({ view: 'Finished', page: 2, pageSize: 25 })
    expect(screen.getByText(downloadedJob.releaseTitle, asTitle)).toBeInTheDocument()
    expect(screen.queryByText('Loading…')).not.toBeInTheDocument()
  })

  it.each<[DownloadJobView, string]>([
    ['NeedsYou', 'Nothing waits for you: every job moves on by itself or has ended.'],
    ['InProgress', 'Nothing is in progress.'],
    ['Finished', 'No job has finished yet.'],
  ])('says the %s tab holds no job, and Show all jobs opens All', async (view, sentence) => {
    // Arrange
    vi.mocked(downloadsApi.list).mockImplementation(async (query) =>
      jobsPage(query, query.view === undefined ? [downloadedJob] : []))
    const { user } = renderPage(member, `/jobs?tab=${view}`)
    await screen.findByText(sentence)
    const tableBefore = screen.queryByRole('table')

    // Act
    await user.click(screen.getByRole('button', { name: 'Show all jobs' }))

    // Assert
    expect(tableBefore).not.toBeInTheDocument()
    expect(await screen.findByText(downloadedJob.releaseTitle, asTitle)).toBeInTheDocument()
    expect(screen.queryByText(sentence)).not.toBeInTheDocument()
    expect(screen.getByRole('tab', { name: 'All' })).toHaveAttribute('aria-selected', 'true')
    expect(address()).toBe('/jobs')
    expect(lastQuery()).toEqual({ view: undefined, page: 1, pageSize: 25 })
  })

  it('says how to send a release from Search when there are no jobs yet', async () => {
    // Arrange
    listing([])

    // Act
    const { user } = renderPage()

    // Assert
    const empty = await screen.findByText(/No download jobs yet\./)
    expect(empty).toHaveTextContent('No download jobs yet. Send a release from Search, and it is followed here until it finishes.')
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Show all jobs' })).not.toBeInTheDocument()
    await user.click(within(empty).getByRole('link', { name: 'Search' }))
    expect(await screen.findByText('Search page')).toBeInTheDocument()
  })

  // ── paging and the list's states ──

  it('pages through the list and keeps the page size per device', async () => {
    // Arrange
    listing([downloadedJob], 60)
    const { user } = renderPage()
    await screen.findByText('1–25 of 60')

    // Act
    await user.selectOptions(screen.getByRole('combobox', { name: 'Per page' }), '50')

    // Assert
    expect(await screen.findByText('1–50 of 60')).toBeInTheDocument()
    expect(lastQuery()).toEqual({ view: undefined, page: 1, pageSize: 50 })
    expect(localStorage.getItem('mam:jobs:pageSize')).toBe('50')
  })

  it('moves to the last page there is when the tab\'s list shrank under the page on screen', async () => {
    // Arrange
    listing([downloadedJob], 30)
    const { user, client } = renderPage(owner, '/jobs?tab=NeedsYou')
    await screen.findByText('1–25 of 30')
    await user.click(screen.getByRole('button', { name: 'Next page' }))
    await screen.findByText('26–30 of 30')
    listing([downloadedJob], 20)

    // Act
    await client.refetchQueries()

    // Assert
    expect(await screen.findByText('1–20 of 20')).toBeInTheDocument()
    expect(lastQuery()).toEqual({ view: 'NeedsYou', page: 1, pageSize: 25 })
  })

  it('shows the server sentence in place of the list when the first load is refused, with no Try again', async () => {
    // Arrange
    vi.mocked(downloadsApi.list).mockRejectedValue(apiError(400, 'The page size must be between 1 and 100.'))

    // Act
    renderPage()

    // Assert
    const state = await screen.findByRole('alert')
    expect(state).toHaveTextContent('Could not load your download jobs.')
    expect(state).toHaveTextContent('The page size must be between 1 and 100.')
    expect(screen.queryByRole('button', { name: 'Try again' })).not.toBeInTheDocument()
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
  })

  it('says what failed when the first load fails and the API gives no sentence, and Try again loads the list', async () => {
    // Arrange
    vi.mocked(downloadsApi.list).mockRejectedValueOnce(apiError(500))
    const { user } = renderPage()
    const state = await screen.findByRole('alert')

    // Act
    await user.click(screen.getByRole('button', { name: 'Try again' }))

    // Assert
    expect(state).toHaveTextContent('Could not load your download jobs.')
    expect(await screen.findByText(downloadingJob.releaseTitle, asTitle)).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('keeps the same rows on screen under a banner when a refresh fails', async () => {
    // Arrange
    const { client } = renderPage()
    await screen.findByText(downloadingJob.releaseTitle, asTitle)
    vi.mocked(downloadsApi.list).mockRejectedValue(networkError())

    // Act
    await client.refetchQueries()

    // Assert
    const banner = await screen.findByRole('alert')
    expect(banner).toHaveTextContent('Refresh failed, showing the last loaded jobs.')
    expect(screen.getByRole('tabpanel', { name: 'All' })).toContainElement(banner)
    expect(screen.getByText(downloadingJob.releaseTitle, asTitle)).toBeInTheDocument()
  })

  it('asks again from Retry on the refresh banner, and drops the banner once the list is back', async () => {
    // Arrange
    const { client, user } = renderPage()
    await screen.findByText(downloadingJob.releaseTitle, asTitle)
    vi.mocked(downloadsApi.list).mockRejectedValueOnce(networkError())
    await client.refetchQueries()
    await screen.findByRole('alert')

    // Act
    await user.click(screen.getByRole('button', { name: 'Retry' }))

    // Assert
    await vi.waitFor(() => expect(screen.queryByRole('alert')).not.toBeInTheDocument())
    expect(screen.getByText(downloadingJob.releaseTitle, asTitle)).toBeInTheDocument()
  })

  // ── Cancel job ──

  it.each<[DownloadJobStatus, boolean]>([
    ['Created', true],
    ['Submitted', true],
    ['Downloading', true],
    ['Paused', true],
    ['NeedsLinking', true],
    ['Downloaded', false],
    ['Failed', false],
    ['Cancelled', false],
  ])('offers Cancel on a %s job sent 6 minutes ago: %s', async (status, offered) => {
    // Arrange
    listing([jobOf({ status, createdUtc: sentMinutesAgo(6) })])

    // Act
    renderPage()

    // Assert
    await screen.findByText(downloadingJob.releaseTitle, asTitle)
    const button = within(rowOf(downloadingJob.releaseTitle)).queryByRole('button', { name: 'Cancel job' })
    expect(button !== null).toBe(offered)
  })

  it('offers no Cancel on a Created job sent under 5 minutes ago, as it may still be being sent', async () => {
    // Arrange
    listing([jobOf({ status: 'Created', clientJobId: null, createdUtc: sentMinutesAgo(1) })])

    // Act
    renderPage()

    // Assert
    await screen.findByText(downloadingJob.releaseTitle, asTitle)
    expect(within(rowOf(downloadingJob.releaseTitle)).queryByRole('button', { name: 'Cancel job' })).not.toBeInTheDocument()
  })

  it('leaves the Actions cell empty on a row with nothing to do', async () => {
    // Arrange
    listing([downloadedJob])

    // Act
    renderPage(owner)

    // Assert
    await screen.findByText(downloadedJob.releaseTitle, asTitle)
    const actions = rowOf(downloadedJob.releaseTitle).querySelector('td.settings-actions-cell')!
    expect(actions).toBeEmptyDOMElement()
  })

  it.each([
    ['the installation owner', owner],
    ['any other user', member],
  ])('cancels a job after a confirmation that says the torrent stays in the client, for %s, and the row says how far it got', async (_, signedIn) => {
    // Arrange
    vi.mocked(downloadsApi.cancel).mockResolvedValue({ ...downloadingJob, status: 'Cancelled' })
    const { user } = renderPage(signedIn)
    await screen.findByText(downloadingJob.releaseTitle, asTitle)
    await user.click(within(rowOf(downloadingJob.releaseTitle)).getByRole('button', { name: 'Cancel job' }))
    const confirmation = await screen.findByRole('dialog', { name: `Cancel the job for ${downloadingJob.releaseTitle}?` })
    listing([{ ...downloadingJob, status: 'Cancelled' }, downloadedJob])

    // Act
    await user.click(within(confirmation).getByRole('button', { name: 'Cancel job' }))

    // Assert
    expect(confirmation).toHaveTextContent(
      'The torrent and its files stay in Local qBittorrent: nothing is paused, stopped or removed there.')
    expect(confirmation).not.toHaveClass('confirm-dialog-wide')
    expect(downloadsApi.cancel).toHaveBeenCalledExactlyOnceWith(downloadingJob.id)
    expect(await within(rowOf(downloadingJob.releaseTitle)).findByText('Cancelled at 45.6%')).toBeInTheDocument()
    expect(within(rowOf(downloadingJob.releaseTitle)).queryByRole('button', { name: 'Cancel job' })).not.toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('puts Cancel job first and Keep following last in the confirmation, and cancels nothing on Keep following', async () => {
    // Arrange
    const { user } = renderPage()
    await screen.findByText(downloadingJob.releaseTitle, asTitle)
    await user.click(within(rowOf(downloadingJob.releaseTitle)).getByRole('button', { name: 'Cancel job' }))
    const confirmation = await screen.findByRole('dialog', { name: `Cancel the job for ${downloadingJob.releaseTitle}?` })

    // Act
    const buttons = within(confirmation.querySelector<HTMLElement>('.ui-dialog-footer')!).getAllByRole('button')
    await user.click(within(confirmation).getByRole('button', { name: 'Keep following' }))

    // Assert
    expect(buttons.map((button) => button.textContent)).toEqual(['Cancel job', 'Keep following'])
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(downloadsApi.cancel).not.toHaveBeenCalled()
  })

  it('shows the server sentence above the rows when a cancel is refused, and the job as it is now', async () => {
    // Arrange
    vi.mocked(downloadsApi.cancel).mockRejectedValue(
      apiError(409, 'This download is already downloaded, so there\'s nothing to stop following.'))
    const { user } = renderPage()
    await screen.findByText(downloadingJob.releaseTitle, asTitle)
    await user.click(within(rowOf(downloadingJob.releaseTitle)).getByRole('button', { name: 'Cancel job' }))
    const confirmation = await screen.findByRole('dialog', { name: `Cancel the job for ${downloadingJob.releaseTitle}?` })
    listing([{ ...downloadingJob, status: 'Downloaded', progress: 1 }, downloadedJob])

    // Act
    await user.click(within(confirmation).getByRole('button', { name: 'Cancel job' }))

    // Assert
    expect(await screen.findByRole('alert'))
      .toHaveTextContent('This download is already downloaded, so there\'s nothing to stop following.')
    expect(await within(rowOf(downloadingJob.releaseTitle)).findByText('Downloaded')).toBeInTheDocument()
    expect(screen.getByText(downloadedJob.releaseTitle, asTitle)).toBeInTheDocument()
  })

  it('takes a refused cancel\'s banner away on Dismiss', async () => {
    // Arrange
    vi.mocked(downloadsApi.cancel).mockRejectedValue(apiError(500))
    const { user } = renderPage()
    await screen.findByText(downloadingJob.releaseTitle, asTitle)
    await user.click(within(rowOf(downloadingJob.releaseTitle)).getByRole('button', { name: 'Cancel job' }))
    const confirmation = await screen.findByRole('dialog', { name: `Cancel the job for ${downloadingJob.releaseTitle}?` })
    await user.click(within(confirmation).getByRole('button', { name: 'Cancel job' }))
    await screen.findByRole('alert')

    // Act
    await user.click(screen.getByRole('button', { name: 'Dismiss' }))

    // Assert
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(screen.getByText(downloadingJob.releaseTitle, asTitle)).toBeInTheDocument()
  })

  it('shows its own sentence when a cancel fails and the API gives none', async () => {
    // Arrange
    vi.mocked(downloadsApi.cancel).mockRejectedValue(apiError(500))
    const { user } = renderPage()
    await screen.findByText(downloadingJob.releaseTitle, asTitle)
    await user.click(within(rowOf(downloadingJob.releaseTitle)).getByRole('button', { name: 'Cancel job' }))
    const confirmation = await screen.findByRole('dialog', { name: `Cancel the job for ${downloadingJob.releaseTitle}?` })

    // Act
    await user.click(within(confirmation).getByRole('button', { name: 'Cancel job' }))

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Could not cancel this job. Reload the page and try again.')
  })

  it('names the Cancel button in its title, for the icon alone on a phone', async () => {
    // Arrange
    renderPage()

    // Act
    const button = await screen.findByRole('button', { name: 'Cancel job' })

    // Assert
    expect(button).toHaveAttribute('title', 'Cancel job')
    expect(button.querySelector('.label-text')).toHaveTextContent('Cancel job')
    expect(button.closest('.settings-actions')).not.toBeNull()
  })
})
