import { useState } from 'react'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Rows3, Table2 } from 'lucide-react'
import { SegmentedControl } from '@/components/shared/SegmentedControl'

type View = 'rows' | 'table'

const options = [
  { value: 'rows', label: 'Rows', icon: Rows3 },
  { value: 'table', label: 'Table', icon: Table2 },
] as const

/** A control whose choice lives in the test, as a page holds it in its state or a store. */
function Picker({ showLabels, onChange }: { showLabels?: boolean; onChange?: (view: View) => void }) {
  const [view, setView] = useState<View>('rows')
  return (
    <SegmentedControl
      label="Show versions as"
      options={options}
      value={view}
      onChange={(next) => {
        setView(next)
        onChange?.(next)
      }}
      showLabels={showLabels}
    />
  )
}

describe('SegmentedControl', () => {
  it('is a named group of radio buttons with the current value checked', () => {
    // Arrange
    const label = 'Show versions as'

    // Act
    render(<Picker />)

    // Assert
    const group = screen.getByRole('radiogroup', { name: label })
    expect(group).toBeInTheDocument()
    expect(screen.getByRole('radio', { name: 'Rows' })).toBeChecked()
    expect(screen.getByRole('radio', { name: 'Table' })).not.toBeChecked()
  })

  it('reports the option clicked', async () => {
    // Arrange
    const onChange = vi.fn()
    const user = userEvent.setup()
    render(<Picker onChange={onChange} />)

    // Act
    await user.click(screen.getByRole('radio', { name: 'Table' }))

    // Assert
    expect(onChange).toHaveBeenCalledExactlyOnceWith('table')
    expect(screen.getByRole('radio', { name: 'Table' })).toBeChecked()
  })

  it('takes one tab stop, on the checked option', async () => {
    // Arrange
    const user = userEvent.setup()
    render(
      <>
        <Picker />
        <button type="button">After the group</button>
      </>,
    )
    await user.tab()
    const firstStop = document.activeElement

    // Act
    await user.tab()

    // Assert
    expect(firstStop).toBe(screen.getByRole('radio', { name: 'Rows' }))
    // Past Table, which is no stop of its own.
    expect(screen.getByRole('button', { name: 'After the group' })).toHaveFocus()
  })

  it('moves the choice with the arrow keys', async () => {
    // Arrange
    const onChange = vi.fn()
    const user = userEvent.setup()
    render(<Picker onChange={onChange} />)
    await user.tab()

    // Act
    await user.keyboard('{ArrowRight}')

    // Assert
    expect(onChange).toHaveBeenCalledExactlyOnceWith('table')
    expect(screen.getByRole('radio', { name: 'Table' })).toHaveFocus()
    expect(screen.getByRole('radio', { name: 'Table' })).toBeChecked()
  })

  it('gives each option its label as a tooltip, unless the labels are shown beside the icons', () => {
    // Arrange
    const { container: iconsOnly } = render(<Picker />)

    // Act
    const { container: withLabels } = render(<Picker showLabels />)

    // Assert
    expect(within(iconsOnly).getByTitle('Rows')).toContainElement(within(iconsOnly).getByRole('radio', { name: 'Rows' }))
    expect(within(iconsOnly).getByTitle('Table')).toContainElement(within(iconsOnly).getByRole('radio', { name: 'Table' }))
    expect(within(withLabels).queryByTitle(/Rows|Table/)).not.toBeInTheDocument()
  })

  it("shows an option's own tooltip in place of its label", () => {
    // Arrange
    const titled = [
      { value: 'rows', label: 'Rows', title: 'Versions as rows', icon: Rows3 },
      { value: 'table', label: 'Table', title: 'Versions as table', icon: Table2 },
    ] as const

    // Act
    render(<SegmentedControl label="Show versions as" options={titled} value="rows" onChange={() => {}} showLabels />)

    // Assert
    expect(screen.getByTitle('Versions as rows')).toContainElement(screen.getByRole('radio', { name: 'Rows' }))
    expect(screen.getByTitle('Versions as table')).toContainElement(screen.getByRole('radio', { name: 'Table' }))
  })

  it('keeps two controls on screen apart', async () => {
    // Arrange
    const user = userEvent.setup()
    render(
      <>
        <Picker />
        <Picker />
      </>,
    )
    const [firstTable, secondTable] = screen.getAllByRole('radio', { name: 'Table' })

    // Act
    await user.click(firstTable)

    // Assert
    expect(firstTable).toBeChecked()
    expect(secondTable).not.toBeChecked()
    expect(screen.getAllByRole('radio', { name: 'Rows' })[1]).toBeChecked()
  })
})
