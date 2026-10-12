import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { VersionRows } from '@/components/library/VersionRows'
import type { ShownVersion } from '@/lib/types'
import { aliens } from '@/test/library'

// MAM-40 — what a page offers for a version (the Duplicates page's Remove this copy) sits on the row's line,
// beside the button that opens the row, so it shows with every row closed; a page that offers nothing (the
// Library and Search pages) gets its rows as they were.

const [theatricalCut, specialEdition] = aliens.versions

/** The row of the version labelled `label`, which opens it. */
const rowOf = (label: string) => screen.getByRole('button', { name: new RegExp(`^${label}`) })

/** An action named after the version it is for. */
const actOn = (version: ShownVersion) => <button type="button">Act on {version.editionLabel}</button>

describe('VersionRows actions', () => {
  it('lays out the rows as they were when the page offers nothing beside them', () => {
    // Arrange
    const versions = aliens.versions

    // Act
    render(<VersionRows versions={versions} />)

    // Assert
    const items = screen.getAllByRole('listitem')
    expect(items.map((item) => item.firstElementChild)).toEqual([rowOf('Theatrical Cut'), rowOf('Special Edition')])
    expect(document.querySelector('.library-version-line, .library-version-actions')).toBeNull()
  })

  it('puts what the page offers beside each closed row, outside the button that opens it', async () => {
    // Arrange
    const user = userEvent.setup()
    render(<VersionRows versions={aliens.versions} actions={actOn} />)

    // Act
    await user.click(screen.getByRole('button', { name: 'Act on Special Edition' }))

    // Assert
    for (const label of ['Theatrical Cut', 'Special Edition']) {
      const action = screen.getByRole('button', { name: `Act on ${label}` })
      expect(rowOf(label)).toHaveAttribute('aria-expanded', 'false')
      expect(rowOf(label)).not.toContainElement(action)
      expect(rowOf(label).parentElement).toHaveClass('library-version-line')
      expect(action.parentElement).toHaveClass('library-version-actions')
    }
  })

  it('leaves a row alone when the page offers nothing for its version', () => {
    // Arrange
    const actions = (version: ShownVersion) => (version.id === specialEdition.id ? actOn(version) : null)

    // Act
    render(<VersionRows versions={[theatricalCut, specialEdition]} actions={actions} />)

    // Assert
    expect(screen.getAllByRole('listitem')[0].firstElementChild).toBe(rowOf('Theatrical Cut'))
    expect(screen.getByRole('button', { name: 'Act on Special Edition' })).toBeInTheDocument()
    expect(document.querySelectorAll('.library-version-actions')).toHaveLength(1)
  })

  it('puts the action beside a single version\'s row, which doesn\'t open', () => {
    // Arrange
    const versions = [theatricalCut]

    // Act
    render(<VersionRows versions={versions} actions={actOn} />)

    // Assert
    const action = screen.getByRole('button', { name: 'Act on Theatrical Cut' })
    expect(screen.getAllByRole('button')).toEqual([action])
    expect(action.closest('.library-version-line')!.firstElementChild).toHaveClass('library-version-row')
  })
})
