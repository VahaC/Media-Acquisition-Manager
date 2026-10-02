import { render, screen } from '@testing-library/react'
import { FieldErrorLine } from '@/components/shared/FieldErrorLine'

describe('FieldErrorLine', () => {
  it('reads out its sentence as an alert in the field error line', () => {
    // Arrange
    const sentence = 'Enter a size in GiB, such as 4.5.'

    // Act
    render(<FieldErrorLine>{sentence}</FieldErrorLine>)

    // Assert
    const line = screen.getByRole('alert')
    expect(line).toHaveTextContent(sentence)
    expect(line).toHaveClass('ui-field-error')
    expect(line).not.toHaveAttribute('id')
  })

  it('describes the control that points at its id', () => {
    // Arrange
    const sentence = 'Enter a size in GiB, such as 4.5.'

    // Act
    render(
      <>
        <input aria-label="Min size" aria-describedby="size-line" />
        <FieldErrorLine id="size-line">{sentence}</FieldErrorLine>
      </>,
    )

    // Assert
    expect(screen.getByRole('textbox', { name: 'Min size' })).toHaveAccessibleDescription(sentence)
  })
})
