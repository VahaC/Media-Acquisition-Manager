import { render, screen } from '@testing-library/react'
import { LinkStateBadge } from '@/components/library/LinkStateBadge'
import type { LibraryLinkWriteState } from '@/lib/types'
import { conflictSentence } from '@/test/library-links'

// MAM-34 — the badge every card, row and item shows for a link: what the app and Jellyfin have done
// with it. "Written" and "In Jellyfin" mean the ids are there, which is not the same as the app having
// identified the item, so a link the app never identified says so and takes the warning tone.

describe('LinkStateBadge', () => {
  it.each([
    ['Written', true, 'Identified in Jellyfin', false],
    ['InLibrary', true, 'Identified in Jellyfin', false],
    ['Written', false, 'Not identified yet', true],
    ['InLibrary', false, 'Not identified yet', true],
    ['Written', undefined, 'In Jellyfin', false],
    ['NotWritten', true, 'Not identified yet', true],
    ['NotWritten', false, 'Not identified yet', true],
    ['Lost', true, 'Jellyfin lost it', true],
    ['Queued', false, 'Waiting to identify', false],
    ['Writing', false, 'Identifying…', false],
    ['KeptInApp', false, 'Kept in the app', false],
  ] as const)('shows %s, identified %s, as "%s" (warning: %s)', (state, identified, label, warning) => {
    // Arrange
    const props: { state: LibraryLinkWriteState; identified?: boolean } = { state, identified }

    // Act
    render(<LinkStateBadge {...props} />)

    // Assert
    const badge = screen.getByText(label)
    expect(badge).toHaveClass('ui-badge')
    expect(badge.classList.contains('library-badge-warning')).toBe(warning)
    expect(badge.classList.contains('ui-badge-outline')).toBe(!warning)
  })

  it('names the server\'s sentence as its tooltip, and has none without one', () => {
    // Arrange
    const { unmount } = render(<LinkStateBadge state="NotWritten" identified={false} error={conflictSentence} />)
    const refused = screen.getByText('Not identified yet')
    const tooltip = refused.getAttribute('title')
    unmount()

    // Act
    render(<LinkStateBadge state="Written" identified />)

    // Assert
    expect(tooltip).toBe(conflictSentence)
    expect(screen.getByText('Identified in Jellyfin')).not.toHaveAttribute('title')
  })

  it('spins the icon only while Jellyfin is identifying', () => {
    // Arrange
    const { unmount } = render(<LinkStateBadge state="Writing" />)
    const identifying = screen.getByText('Identifying…').querySelector('svg')
    unmount()

    // Act
    render(<LinkStateBadge state="Queued" />)

    // Assert
    expect(identifying).toHaveClass('animate-spin')
    expect(screen.getByText('Waiting to identify').querySelector('svg')).not.toHaveClass('animate-spin')
    expect(screen.getByText('Waiting to identify').querySelector('svg')).toHaveAttribute('aria-hidden', 'true')
  })

  it('is a live region that keeps its element as the state turns over, so a screen reader hears each turn', () => {
    // Arrange
    const { rerender } = render(<LinkStateBadge state="Queued" />)
    const region = screen.getByRole('status')

    // Act
    rerender(<LinkStateBadge state="Writing" />)
    const identifying = region.textContent
    rerender(<LinkStateBadge state="Written" identified />)

    // Assert
    expect(identifying).toBe('Identifying…')
    expect(screen.getByRole('status')).toBe(region)
    expect(region).toHaveTextContent('Identified in Jellyfin')
  })
})
