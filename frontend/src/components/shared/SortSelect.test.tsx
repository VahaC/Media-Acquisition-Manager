import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { SortSelect } from '@/components/shared/SortSelect'

type Order = 'name' | 'size'

const options = [
  { value: 'name', label: 'Name' },
  { value: 'size', label: 'Size' },
] as const

function renderSort(value: Order = 'name') {
  const onChange = vi.fn()
  render(<SortSelect<Order> label="Sort by" value={value} options={options} onChange={onChange} />)
  return { onChange, user: userEvent.setup() }
}

describe('SortSelect', () => {
  it('says "Sort by" beside its select, in the label that wraps it', () => {
    // Arrange
    renderSort()

    // Act
    const select = screen.getByRole('combobox', { name: 'Sort by' })

    // Assert
    expect(select.closest('label')).toHaveTextContent('Sort by')
    expect(select.closest('label')).toHaveClass('settings-sort')
  })

  it('keeps the select\'s name the label alone, without the chosen order', () => {
    // Arrange
    renderSort('size')

    // Act
    const names = screen.getAllByRole('combobox').map((select) => select.getAttribute('aria-label'))

    // Assert
    expect(names).toEqual(['Sort by'])
  })

  it('offers its options in the order given, with the value picked', () => {
    // Arrange
    renderSort('size')

    // Act
    const select = screen.getByRole('combobox', { name: 'Sort by' })

    // Assert
    expect(within(select).getAllByRole('option').map((option) => option.textContent)).toEqual(['Name', 'Size'])
    expect(select).toHaveValue('size')
  })

  it('reports the option picked by its value', async () => {
    // Arrange
    const { onChange, user } = renderSort()

    // Act
    await user.selectOptions(screen.getByRole('combobox', { name: 'Sort by' }), 'Size')

    // Assert
    expect(onChange).toHaveBeenCalledExactlyOnceWith('size')
  })
})
