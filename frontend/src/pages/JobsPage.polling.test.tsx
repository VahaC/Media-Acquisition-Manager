import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { ConfirmProvider } from '@/components/shared/ConfirmDialog'
import { downloadsApi } from '@/lib/downloads-api'
import type { DownloadJob, DownloadJobStatus } from '@/lib/types'
import { JobsPage } from '@/pages/JobsPage'
import { member, signInAs } from '@/test/auth-session'
import { downloadedJob, downloadingJob, jobOf, jobsPage, sentMinutesAgo } from '@/test/downloads'

vi.mock('@/lib/downloads-api')

// MAM-14 — the Jobs page asks for its list again every 10 s while it shows a job the app still
// follows (Created, Submitted, Downloading, Paused), and stops once every job on it has ended.

/** The server lists `items` for every request from now on. */
function listing(...items: DownloadJob[]) {
  vi.mocked(downloadsApi.list).mockImplementation(async (query) => jobsPage(query, items))
}

/** Lets the polling interval pass once, with the answers it brings. */
const nextPoll = () => act(() => vi.advanceTimersByTimeAsync(10_000))

/** The table row of the job whose release is `title`. */
const rowOf = (title: string) => screen.getByText(title, { selector: '.settings-name-text' }).closest('tr')!

function renderPage() {
  signInAs(member)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <ConfirmProvider>
          <JobsPage />
        </ConfirmProvider>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('JobsPage while a job is followed', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    localStorage.clear()
    vi.useFakeTimers({ shouldAdvanceTime: true })
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('follows a job from downloading through its progress to downloaded, then asks nothing more', async () => {
    // Arrange
    listing(downloadingJob, downloadedJob)
    renderPage()
    await screen.findByText('45.6%')
    listing({ ...downloadingJob, progress: 0.9 }, downloadedJob)

    // Act
    await nextPoll()
    await within(rowOf(downloadingJob.releaseTitle)).findByText('90%')
    listing({ ...downloadingJob, status: 'Downloaded', progress: 1 }, downloadedJob)
    await nextPoll()
    await within(rowOf(downloadingJob.releaseTitle)).findByText('Downloaded')
    await nextPoll()
    await nextPoll()

    // Assert
    expect(downloadsApi.list).toHaveBeenCalledTimes(3)
  })

  it.each<DownloadJobStatus>(['Created', 'Submitted', 'Downloading', 'Paused'])('asks again every 10 s while a %s job is on the page', async (status) => {
    // Arrange
    listing(jobOf({ status }), downloadedJob)
    renderPage()
    await screen.findByText(downloadedJob.releaseTitle, { selector: '.settings-name-text' })

    // Act
    await nextPoll()
    await nextPoll()

    // Assert
    expect(downloadsApi.list).toHaveBeenCalledTimes(3)
  })

  it('offers Cancel on a Created job once a poll finds it 5 minutes old, with no timer of its own', async () => {
    // Arrange
    listing(jobOf({ status: 'Created', clientJobId: null, createdUtc: sentMinutesAgo(4.95) }))
    renderPage()
    await screen.findByText(downloadingJob.releaseTitle, { selector: '.settings-name-text' })
    const before = within(rowOf(downloadingJob.releaseTitle)).queryByRole('button', { name: 'Cancel job' })

    // Act
    await nextPoll()

    // Assert
    expect(before).not.toBeInTheDocument()
    expect(await within(rowOf(downloadingJob.releaseTitle)).findByRole('button', { name: 'Cancel job' })).toBeInTheDocument()
  })

  it.each<DownloadJobStatus>(['Downloaded', 'Failed', 'NeedsLinking', 'Cancelled'])('asks nothing more while every job on the page is %s', async (status) => {
    // Arrange
    listing(jobOf({ status }))
    renderPage()
    await screen.findByText(downloadingJob.releaseTitle, { selector: '.settings-name-text' })

    // Act
    await nextPoll()
    await nextPoll()

    // Assert
    expect(downloadsApi.list).toHaveBeenCalledOnce()
  })

  it('asks nothing more while the list is empty', async () => {
    // Arrange
    listing()
    renderPage()
    await screen.findByText(/No download jobs yet\./)

    // Act
    await nextPoll()

    // Assert
    expect(downloadsApi.list).toHaveBeenCalledOnce()
  })
})
