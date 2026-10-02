import { render, screen } from '@testing-library/react'
import { Field } from '@/components/shared/Field'
import { Input } from '@/components/ui/input'

const PASSWORD_HINT = 'At least 8 characters.'
const PASSWORDS_DIFFER = 'The two passwords are different. Type the same password in both fields.'

describe('Field', () => {
  it('names each control by its own label', () => {
    // Arrange
    const labels = ['User name', 'Password']

    // Act
    render(
      <>
        {labels.map((label) => <Field key={label} label={label}>{(control) => <Input {...control} />}</Field>)}
      </>,
    )

    // Assert
    const userName = screen.getByRole('textbox', { name: 'User name' })
    expect(userName).toHaveAccessibleName('User name')
    expect(userName).not.toHaveAttribute('aria-describedby')
    expect(userName).not.toHaveAttribute('aria-invalid')
    expect(screen.getByLabelText('Password')).not.toBe(userName)
  })

  it('describes the control by its hint without adding the hint to its name', () => {
    // Arrange
    const hint = PASSWORD_HINT

    // Act
    render(<Field label="Password" hint={hint}>{(control) => <Input type="password" {...control} />}</Field>)

    // Assert
    const password = screen.getByLabelText('Password')
    expect(password).toHaveAccessibleName('Password')
    expect(password).toHaveAccessibleDescription(hint)
    expect(password.closest('.ui-field')).not.toHaveAttribute('data-invalid')
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('replaces the hint with the error, marks the field invalid and describes the control by the error', () => {
    // Arrange
    const error = PASSWORDS_DIFFER

    // Act
    render(
      <Field label="Confirm password" hint={PASSWORD_HINT} error={error}>
        {(control) => <Input type="password" {...control} />}
      </Field>,
    )

    // Assert
    const confirm = screen.getByLabelText('Confirm password')
    expect(confirm).toHaveAccessibleName('Confirm password')
    expect(confirm).toHaveAccessibleDescription(error)
    expect(confirm).toHaveAttribute('aria-invalid', 'true')
    expect(confirm.closest('.ui-field')).toHaveAttribute('data-invalid')
    expect(screen.queryByText(PASSWORD_HINT)).not.toBeInTheDocument()
  })

  it('reads the error out as an alert', () => {
    // Arrange
    const error = PASSWORDS_DIFFER

    // Act
    render(<Field label="Confirm password" error={error}>{(control) => <Input type="password" {...control} />}</Field>)

    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent(error)
  })

  it('goes back to the hint once the error is cleared', () => {
    // Arrange
    const field = (error: string | null) => (
      <Field label="Confirm password" hint={PASSWORD_HINT} error={error}>
        {(control) => <Input type="password" {...control} />}
      </Field>
    )
    const { rerender } = render(field(PASSWORDS_DIFFER))

    // Act
    rerender(field(null))

    // Assert
    const confirm = screen.getByLabelText('Confirm password')
    expect(confirm).toHaveAccessibleDescription(PASSWORD_HINT)
    expect(confirm).not.toHaveAttribute('aria-invalid')
    expect(confirm.closest('.ui-field')).not.toHaveAttribute('data-invalid')
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })
})
