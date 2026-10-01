import { render, screen, within } from '@testing-library/react'
import { LibraryWorkCard } from '@/components/library/LibraryWorkCard'
import { aliens } from '@/test/library'

// MAM-12 — the card as the search page shows a library match: why it matched under the title and
// year, its title a level below the section's own heading.

describe('LibraryWorkCard on the search page', () => {
  it('shows what it is given as the evidence under the title and the year', () => {
    // Arrange
    const evidence = <p>Matched by IMDb id</p>

    // Act
    render(<LibraryWorkCard work={aliens} evidence={evidence} />)

    // Assert
    const heading = screen.getByRole('heading', { level: 2, name: 'Чужі' }).parentElement!
    expect(within(heading).getByText('Matched by IMDb id')).toBeInTheDocument()
    expect(within(heading).getByText('1986').closest('p')!.nextElementSibling).toHaveTextContent('Matched by IMDb id')
  })

  it('titles the card at the heading level it is given', () => {
    // Arrange
    const headingLevel = 3

    // Act
    render(<LibraryWorkCard work={aliens} headingLevel={headingLevel} />)

    // Assert
    expect(screen.getByRole('heading', { level: 3, name: 'Чужі' })).toHaveClass('ui-card-title', 'library-work-title')
    expect(screen.queryByRole('heading', { level: 2 })).not.toBeInTheDocument()
    expect(screen.getByRole('article')).toHaveAccessibleName('Чужі')
  })
})
