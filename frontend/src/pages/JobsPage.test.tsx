import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { ConfirmProvider } from '@/components/shared/ConfirmDialog'
import { downloadsApi } from '@/lib/downloads-api'
import { formatDateTime } from '@/lib/format'
import type { CurrentUser, DownloadJob, DownloadJobStatus } from '@/lib/types'
import { JobsPage } from '@/pages/JobsPage'
import { apiError, networkError } from '@/test/api-error'
import { member, owner, signInAs } from '@/test/auth-session'
import { downloadedJob, downloadingJob, jobOf, jobsPage, sentMinutesAgo } from '@/test/downloads'

vi.mock('@/lib/downloads-api')

/** What the last request for the list asked for. */
const lastQuery = () => vi.mocked(downloadsApi.list).mock.lastCall?.[0]

/** Matches a job's release title only. */
const asTitle = { selector: '.settings-name-text' }

/** The table row (or phone card) of the job whose release is `title`. */
const rowOf = (title: string) => screen.getByText(title, asTitle).closest('tr')!

/** The server lists `items`, out of `total`, whatever page and status are asked for. */
function listing(items: DownloadJob[], total = items.length) {
  vi.mocked(downloadsApi.list).mockImplementation(async (query) => jobsPage(query, items, total))
}

function renderPage(signedIn: CurrentUser = member) {
  signInAs(signedIn)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={['/jobs']}>
        <ConfirmProvider>
          <Routes>
            <Route path="jobs" element={<JobsPage />} />
            <Route path="jobs/:id" element={<p>Job details</p>} />
            <Route path="search" element={<p>Search page</p>} />
          </Routes>
        </ConfirmProvider>
      </MemoryRouter>
    </QueryClientProvider>,
  )
  return { client, user: userEvent.setup() }
}

const blockedJob = jobOf({
  id: 'd0b10000-0000-4000-8000-000000000003',
  releaseTitle: 'Arrival (2016) 1080p WEB-DL',
  status: 'Downloading',
  progress: 0.2,
  blockReason: '“Local qBittorrent” couldn\'t be asked about this download. Ask the owner to check “Local qBittorrent” under Integrations.',
})

const failedJob = jobOf({
  id: 'd0b10000-0000-4000-8000-000000000004',
  releaseTitle: 'Dune (2021) 2160p',
  status: 'Failed',
  clientJobId: null,
  error: '“Local qBittorrent” already has this torrent, so nothing was sent.',
})

describe('JobsPage', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    localStorage.clear()
    listing([downloadingJob, downloadedJob])
  })

  it('lists the caller\'s jobs, newest first as the server sends them, with their status, progress, client and destination, and when they were sent', async () => {
    // Arrange
    const query = { status: undefined, page: 1, pageSize: 25 }

    // Act
    renderPage()

    // Assert
    expect(await screen.findByText(downloadingJob.releaseTitle, asTitle)).toBeInTheDocument()
    expect(lastQuery()).toEqual(query)
    expect(screen.getAllByRole('columnheader').map((header) => header.textContent))
      .toEqual(['Release', 'Status', 'Progress', 'Sent to', 'Sent', 'Actions'])
    expect(screen.getAllByText(/./, asTitle).map((title) => title.textContent))
      .toEqual([downloadingJob.releaseTitle, downloadedJob.releaseTitle])
    const downloading = within(rowOf(downloadingJob.releaseTitle))
    expect(downloading.getByText('Downloading')).toBeInTheDocument()
    expect(downloading.getByRole('progressbar', { name: 'Download progress' })).toHaveAttribute('value', '0.4567')
    expect(downloading.getByText('45.6%')).toBeInTheDocument()
    expect(downloading.getByText('Local qBittorrent · Movies')).toBeInTheDocument()
    expect(downloading.getByText(formatDateTime(downloadingJob.createdUtc, { dateStyle: 'medium', timeStyle: 'short' }))).toBeInTheDocument()
    const downloaded = within(rowOf(downloadedJob.releaseTitle))
    expect(downloaded.getByText('Downloaded').closest('p')).toHaveAttribute('data-status', 'Succeeded')
    expect(downloaded.getByText('100%')).toBeInTheDocument()
    expect(downloaded.getByText('Seedbox qBittorrent · Films')).toBeInTheDocument()
    expect(screen.getByText('Page 1 of 1')).toBeInTheDocument()
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

  it('shows a block reason as a warning line under the status, which the job keeps', async () => {
    // Arrange
    listing([blockedJob])

    // Act
    renderPage()

    // Assert
    const row = within(await screen.findByText(blockedJob.releaseTitle, asTitle).then((title) => title.closest('tr')!))
    expect(row.getByText('Downloading')).toBeInTheDocument()
    const reason = row.getByText(blockedJob.blockReason!).closest('p')
    expect(reason).toHaveClass('library-source-warning')
    expect(rowOf(blockedJob.releaseTitle).querySelector('td[data-label="Status"]')).toHaveClass('jobs-stacked-cell')
  })

  it('shows a failed job\'s sentence as a red line under its status, with no Cancel', async () => {
    // Arrange
    listing([failedJob])

    // Act
    renderPage()

    // Assert
    await screen.findByText(failedJob.releaseTitle, asTitle)
    const row = within(rowOf(failedJob.releaseTitle))
    expect(row.getByText('Failed').closest('p')).toHaveAttribute('data-status', 'Failed')
    expect(row.getByText(failedJob.error!).closest('p')).toHaveAttribute('data-status', 'Failed')
    expect(row.getByText('Not known yet')).toBeInTheDocument()
    expect(row.queryByRole('button', { name: 'Cancel job' })).not.toBeInTheDocument()
  })

  it('names the category sent when the job\'s destination has no name', async () => {
    // Arrange
    listing([jobOf({ destinationName: null, clientDestinationKey: 'Movies/4K' })])

    // Act
    renderPage()

    // Assert
    await screen.findByText(downloadingJob.releaseTitle, asTitle)
    expect(within(rowOf(downloadingJob.releaseTitle)).getByText('Local qBittorrent · Movies/4K')).toBeInTheDocument()
  })

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

  it('offers every status in the filter, in the API\'s order', async () => {
    // Arrange
    renderPage()

    // Act
    const select = await screen.findByRole('combobox', { name: 'Status' })

    // Assert
    expect([...select.querySelectorAll('option')].map((option) => option.textContent)).toEqual([
      'All statuses', 'Sending', 'Submitted', 'Downloading', 'Paused', 'Downloaded', 'Failed', 'Needs linking', 'Cancelled',
    ])
  })

  it('asks the server for the picked status, from page 1 again', async () => {
    // Arrange
    listing([downloadingJob, downloadedJob], 30)
    const { user } = renderPage()
    await screen.findByText('Page 1 of 2')
    await user.click(screen.getByRole('button', { name: 'Next' }))
    await screen.findByText('Page 2 of 2')

    // Act
    await user.selectOptions(screen.getByRole('combobox', { name: 'Status' }), 'Downloading')

    // Assert
    expect(await screen.findByText('Page 1 of 2')).toBeInTheDocument()
    expect(lastQuery()).toEqual({ status: 'Downloading', page: 1, pageSize: 25 })
  })

  it('pages through the list and keeps the page size per device', async () => {
    // Arrange
    listing([downloadingJob], 60)
    const { user } = renderPage()
    await screen.findByText('Page 1 of 3')

    // Act
    await user.selectOptions(screen.getByRole('combobox', { name: 'Per page' }), '50')

    // Assert
    expect(await screen.findByText('Page 1 of 2')).toBeInTheDocument()
    expect(lastQuery()).toEqual({ status: undefined, page: 1, pageSize: 50 })
    expect(localStorage.getItem('mam:jobs:pageSize')).toBe('50')
  })

  it('moves to the last page there is when the list shrank under the page on screen', async () => {
    // Arrange
    listing([downloadingJob], 30)
    const { user, client } = renderPage()
    await screen.findByText('Page 1 of 2')
    await user.click(screen.getByRole('button', { name: 'Next' }))
    await screen.findByText('Page 2 of 2')
    listing([downloadingJob], 20)

    // Act
    await client.refetchQueries()

    // Assert
    expect(await screen.findByText('Page 1 of 1')).toBeInTheDocument()
    expect(lastQuery()).toEqual({ status: undefined, page: 1, pageSize: 25 })
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
    await user.click(within(empty).getByRole('link', { name: 'Search' }))
    expect(await screen.findByText('Search page')).toBeInTheDocument()
  })

  it('says nothing matches the status, and clears the filter on request', async () => {
    // Arrange
    vi.mocked(downloadsApi.list).mockImplementation(async (query) =>
      jobsPage(query, query.status === undefined ? [downloadingJob] : [], query.status === undefined ? 1 : 0))
    const { user } = renderPage()
    await screen.findByText(downloadingJob.releaseTitle, asTitle)
    await user.selectOptions(screen.getByRole('combobox', { name: 'Status' }), 'Paused')
    await screen.findByText('None of your download jobs has this status.')

    // Act
    await user.click(screen.getByRole('button', { name: 'Clear filters' }))

    // Assert
    expect(await screen.findByText(downloadingJob.releaseTitle, asTitle)).toBeInTheDocument()
    expect(screen.getByRole('combobox', { name: 'Status' })).toHaveValue('')
    expect(lastQuery()).toEqual({ status: undefined, page: 1, pageSize: 25 })
  })

  it('shows the server sentence in place of the list when the first load fails', async () => {
    // Arrange
    vi.mocked(downloadsApi.list).mockRejectedValue(apiError(400, 'The page size must be between 1 and 100.'))

    // Act
    renderPage()

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('The page size must be between 1 and 100.')
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
  })

  it('shows its own sentence when the first load fails and the API gives none', async () => {
    // Arrange
    vi.mocked(downloadsApi.list).mockRejectedValue(apiError(500))

    // Act
    renderPage()

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Could not load your download jobs.')
  })

  it('keeps the same rows on screen under a banner when a refresh fails', async () => {
    // Arrange
    const { client } = renderPage()
    await screen.findByText(downloadingJob.releaseTitle, asTitle)
    vi.mocked(downloadsApi.list).mockRejectedValue(networkError())

    // Act
    await client.refetchQueries()

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Refresh failed, showing the last loaded jobs.')
    expect(screen.getByText(downloadingJob.releaseTitle, asTitle)).toBeInTheDocument()
  })

  it.each([
    ['the installation owner', owner],
    ['any other user', member],
  ])('cancels a job after a confirmation that says the torrent stays in the client, for %s', async (_, signedIn) => {
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
    expect(downloadsApi.cancel).toHaveBeenCalledExactlyOnceWith(downloadingJob.id)
    expect(await within(rowOf(downloadingJob.releaseTitle)).findByText('Cancelled')).toBeInTheDocument()
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
