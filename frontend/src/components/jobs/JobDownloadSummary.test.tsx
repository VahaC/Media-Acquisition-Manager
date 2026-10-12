import { render, screen } from '@testing-library/react'
import { JobDownloadSummary } from '@/components/jobs/JobDownloadSummary'
import type { DownloadJobStatus } from '@/lib/types'
import { jobOf } from '@/test/downloads'

// MAM-41 — a Jobs row's Download cell, alone: where the download stands in a word, the bar only while the
// app still follows it, how far a cancelled one got, and why one isn't moving on or failed, cut at two
// lines with the whole sentence on hover.

const BLOCK_REASON = '“Local qBittorrent” couldn\'t be asked about this download. Ask the owner to check “Local qBittorrent” under Integrations.'
const ERROR = '“Local qBittorrent” already has this torrent, so nothing was sent.'

describe('JobDownloadSummary', () => {
  it.each<[DownloadJobStatus, string]>([
    ['Created', 'Sending'],
    ['Submitted', 'Submitted'],
    ['Downloading', 'Downloading'],
    ['Paused', 'Paused'],
  ])('shows the bar under the status of a job the app still follows: %s', (status, word) => {
    // Arrange
    const job = jobOf({ status, progress: 0.4567 })

    // Act
    render(<JobDownloadSummary job={job} />)

    // Assert
    expect(screen.getByText(word)).toBeInTheDocument()
    expect(screen.getByRole('progressbar', { name: 'Download progress' })).toHaveAttribute('value', '0.4567')
    expect(screen.getByText('45.6%')).toBeInTheDocument()
  })

  it('says the progress isn\'t known yet on a followed job the client hasn\'t reported on', () => {
    // Arrange
    const job = jobOf({ status: 'Submitted', progress: null })

    // Act
    render(<JobDownloadSummary job={job} />)

    // Assert
    expect(screen.getByText('Not known yet')).toBeInTheDocument()
    expect(screen.queryByRole('progressbar')).not.toBeInTheDocument()
  })

  it.each<[DownloadJobStatus, number | null, string]>([
    ['Downloaded', 1, 'Downloaded'],
    ['Failed', null, 'Failed'],
    ['NeedsLinking', 0.3, 'Needs linking'],
    ['Cancelled', 1, 'Cancelled'],
  ])('shows no bar on a job the app no longer follows: %s', (status, progress, word) => {
    // Arrange
    const job = jobOf({ status, progress })

    // Act
    render(<JobDownloadSummary job={job} />)

    // Assert
    expect(screen.getByText(word)).toBeInTheDocument()
    expect(screen.queryByRole('progressbar')).not.toBeInTheDocument()
    expect(screen.queryByText('Not known yet')).not.toBeInTheDocument()
  })

  it.each<[number | null, string]>([
    [0.018, 'Cancelled at 1.8%'],
    [0.57, 'Cancelled at 57%'],
    [0.99996, 'Cancelled at 99.9%'],
    [0, 'Cancelled at 0%'],
    [1, 'Cancelled'],
    [null, 'Cancelled'],
  ])('says how far a cancelled job got, cut down as the bar is, when it got part of the way: %s', (progress, word) => {
    // Arrange
    const job = jobOf({ status: 'Cancelled', progress })

    // Act
    render(<JobDownloadSummary job={job} />)

    // Assert
    expect(screen.getByText(word)).toBeInTheDocument()
  })

  it.each<[DownloadJobStatus, string | null, boolean, string]>([
    ['Downloaded', 'Succeeded', false, 'library-source-status'],
    ['Failed', 'Failed', false, 'library-source-status'],
    ['Downloading', null, true, 'library-source-status'],
    ['Paused', null, false, 'library-source-status'],
    ['NeedsLinking', null, false, 'library-source-warning'],
  ])('colours a %s job\'s status as the job\'s page does, and spins only a download under way', (status, tone, spinning, line) => {
    // Arrange
    const job = jobOf({ status, progress: 0.5 })

    // Act
    render(<JobDownloadSummary job={job} />)

    // Assert
    const statusLine = screen.getByText(status === 'NeedsLinking' ? 'Needs linking' : status).closest('p')!
    expect(statusLine).toHaveClass(line)
    expect(statusLine.getAttribute('data-status')).toBe(tone)
    expect(statusLine.querySelector('svg')?.classList.contains('animate-spin')).toBe(spinning)
  })

  it('cuts a block reason at two lines, as a warning, with the whole sentence on hover, and stops the spinner', () => {
    // Arrange
    const job = jobOf({ status: 'Downloading', progress: 0.2, blockReason: BLOCK_REASON })

    // Act
    render(<JobDownloadSummary job={job} />)

    // Assert
    const reason = screen.getByText(BLOCK_REASON).closest('p')!
    expect(reason).toHaveClass('library-source-warning', 'jobs-clamp')
    expect(reason).toHaveAttribute('title', BLOCK_REASON)
    expect(reason.querySelector('svg')).toHaveAttribute('aria-hidden', 'true')
    expect(screen.getByText('Downloading').closest('p')!.querySelector('svg')).not.toHaveClass('animate-spin')
  })

  it('cuts a failed job\'s sentence at two lines, as a red line, with the whole sentence on hover', () => {
    // Arrange
    const job = jobOf({ status: 'Failed', clientJobId: null, error: ERROR })

    // Act
    render(<JobDownloadSummary job={job} />)

    // Assert
    const error = screen.getByText(ERROR).closest('p')!
    expect(error).toHaveClass('library-source-status', 'jobs-clamp')
    expect(error).toHaveAttribute('data-status', 'Failed')
    expect(error).toHaveAttribute('title', ERROR)
  })

  it('says nothing more of a job that is neither blocked nor failed: no replacement, no unchecked send', () => {
    // Arrange
    const job = jobOf({ status: 'Downloading', progress: 0.5, overwriteCheck: 'NotCheckedMagnet' })

    // Act
    const { container } = render(<JobDownloadSummary job={job} />)

    // Assert
    expect(container.querySelectorAll('.jobs-status > p')).toHaveLength(1)
    expect(screen.queryByText(/Not checked for files/)).not.toBeInTheDocument()
  })
})
