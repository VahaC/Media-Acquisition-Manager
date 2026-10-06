import { render, screen, within } from '@testing-library/react'
import { FilterBar } from '@/components/shared/FilterBar'

function renderBar() {
  render(
    <FilterBar sort={<label>Sort by<select aria-label="Sort by"><option>Name</option></select></label>}>
      <select aria-label="Genre"><option>All genres</option></select>
      <select aria-label="Decade"><option>All decades</option></select>
    </FilterBar>,
  )
}

describe('FilterBar', () => {
  it('holds the filters in the row they wrap in', () => {
    // Arrange
    renderBar()

    // Act
    const toolbar = screen.getByRole('combobox', { name: 'Genre' }).parentElement

    // Assert
    expect(toolbar).toHaveClass('settings-toolbar')
    expect(within(toolbar!).getAllByRole('combobox').map((select) => select.getAttribute('aria-label'))).toEqual(['Genre', 'Decade'])
  })

  it('stands the order beside that row, never inside it, so the row can wrap without leaving it between the filters and their answer', () => {
    // Arrange
    renderBar()

    // Act
    const toolbar = screen.getByRole('combobox', { name: 'Genre' }).parentElement
    const sort = screen.getByRole('combobox', { name: 'Sort by' })

    // Assert
    expect(toolbar).not.toContainElement(sort)
    expect(toolbar?.parentElement).toHaveClass('settings-filterbar')
    expect(toolbar?.parentElement).toContainElement(sort)
    expect(toolbar?.nextElementSibling).toContainElement(sort)
  })
})
