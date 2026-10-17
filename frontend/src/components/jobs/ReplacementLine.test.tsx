import { render, screen } from '@testing-library/react'
import { I18nextProvider } from 'react-i18next'
import { ReplacementLine } from '@/components/jobs/ReplacementLine'
import type { DownloadJobReplacement, ReplacementJobState } from '@/lib/types'
import { pluralFormProbe } from '@/test/plural-probe'
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

  // MAM-46 — Ukrainian's singular form also counts 21, 31 and 101, so a sentence that says "the old copy"
  // without its number has a key of its own for exactly one copy. The probe holds only the counted key's
  // Ukrainian forms, so one copy reads the exactly-one key in English.
  it.each<[ReplacementJobState, string, string]>([
    ['Quarantining', 'replacement.quarantining', 'Moving the old copy into the quarantine…'],
    ['Restoring', 'replacement.restoring', 'Putting the old copy back…'],
    ['Deleting', 'replacement.deleting', 'Deleting the old copy permanently…'],
    ['Restored', 'replacement.restored', 'Replacement undone: the old copy is back where it was.'],
    ['Completed', 'replacement.completed', 'Replaced: the old copy was deleted permanently.'],
  ])('reads exactly one copy\'s own sentence, and a plural form for 21, 3 and 5 copies: %s', (state, key, oneCopy) => {
    // Arrange
    const probe = pluralFormProbe('jobs', key, (form) => `form: ${form}`)

    // Act
    const lines = [1, 21, 3, 5].map((copyCount) => {
      const { container, unmount } = render(
        <I18nextProvider i18n={probe}>
          <ReplacementLine replacement={replacementOf({ state, copyCount })} client="Local Transmission" downloadFailed={false} />
        </I18nextProvider>,
      )
      const text = container.textContent
      unmount()
      return text
    })

    // Assert
    expect(lines).toEqual([oneCopy, 'form: one', 'form: few', 'form: many'])
  })
})
