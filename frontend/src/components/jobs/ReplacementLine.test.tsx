import { render, screen } from '@testing-library/react'
import { ReplacementLine } from '@/components/jobs/ReplacementLine'
import type { DownloadJobReplacement } from '@/lib/types'
import { replacementOf } from '@/test/replacements'

// MAM-30 — the line names the job's own download client where the owner may fix what stopped the
// check, as Transmission and qBittorrent both may be that client; the Jobs page's details pin every
// other state of the line (JobsPage.details.test.tsx).

const CHECK_REASON = 'Jellyfin identified “Alien.1979.Directors.Cut.2160p.mkv” as another work (IMDb tt0090605), not Alien (1979).'

function renderLine(replacement: DownloadJobReplacement, client: string, onJobPage = false) {
  render(<ReplacementLine replacement={replacement} client={client} downloadFailed={false} onJobPage={onJobPage} />)
}

describe('ReplacementLine', () => {
  it.each([
    ['Local Transmission'],
    ['Local qBittorrent'],
  ])('says to fix a stopped check in Jellyfin or the job\'s own client, %s', (client) => {
    // Arrange
    const replacement = replacementOf({ state: 'NeedsReview', stop: 'Check', reason: CHECK_REASON })

    // Act
    renderLine(replacement, client)

    // Assert
    expect(screen.getByText(`Replacement needs review: ${CHECK_REASON} Fix it in Jellyfin or ${client}, then press Retry — or see the job.`))
      .toBeInTheDocument()
  })

  it('names no client on the job\'s own page, whose Replacement section says what to do', () => {
    // Arrange
    const replacement = replacementOf({ state: 'NeedsReview', stop: 'Check', reason: CHECK_REASON })

    // Act
    renderLine(replacement, 'Local Transmission', true)

    // Assert
    expect(screen.getByText(`Replacement needs review: ${CHECK_REASON}`)).toBeInTheDocument()
    expect(screen.queryByText(/Local Transmission/)).not.toBeInTheDocument()
  })
})
