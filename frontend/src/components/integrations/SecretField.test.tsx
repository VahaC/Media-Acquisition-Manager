import { useState } from 'react'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { SecretField } from '@/components/integrations/SecretField'
import { toSecretUpsert } from '@/lib/integration-secrets'
import type { CredentialRequirement, SecretDraft } from '@/lib/types'

/** The field as the dialog holds it: its draft in state, every change also handed to `onChange`. */
function Harness({ requirement, stored, onChange }: {
  requirement: CredentialRequirement
  stored: boolean
  onChange: (next: SecretDraft) => void
}) {
  const [value, setValue] = useState<SecretDraft>({ value: '', clear: false })
  return (
    <SecretField
      label="Password"
      removeLabel="Remove the stored password"
      requirement={requirement}
      stored={stored}
      value={value}
      onChange={(next) => {
        setValue(next)
        onChange(next)
      }}
    />
  )
}

function renderField(requirement: CredentialRequirement, stored: boolean) {
  const onChange = vi.fn<(next: SecretDraft) => void>()
  render(<Harness requirement={requirement} stored={stored} onChange={onChange} />)
  const lastDraft = () => onChange.mock.lastCall?.[0]
  return { lastDraft, user: userEvent.setup() }
}

describe('SecretField', () => {
  it('turns typing into a new value and an empty field into keep', async () => {
    // Arrange
    const { lastDraft, user } = renderField('Required', true)
    const input = screen.getByLabelText('Password')
    await user.type(input, 'hunter22')
    const typed = lastDraft()

    // Act
    await user.clear(input)

    // Assert
    expect(typed).toEqual({ value: 'hunter22', clear: false })
    expect(toSecretUpsert(typed!)).toEqual({ mode: 'Set', value: 'hunter22' })
    expect(lastDraft()).toEqual({ value: '', clear: false })
    expect(toSecretUpsert(lastDraft()!)).toBeUndefined()
    expect(input).toHaveAttribute('placeholder', 'Stored. Leave empty to keep it.')
  })

  it('offers to remove an optional stored secret and disables the field while ticked', async () => {
    // Arrange
    const { lastDraft, user } = renderField('Optional', true)
    const input = screen.getByLabelText('Password (optional)')
    await user.type(input, 'typo')
    const removal = screen.getByRole('checkbox', { name: 'Remove the stored password' })

    // Act
    await user.click(removal)

    // Assert
    expect(lastDraft()).toEqual({ value: '', clear: true })
    expect(toSecretUpsert(lastDraft()!)).toEqual({ mode: 'Clear' })
    expect(input).toHaveValue('')
    expect(input).toBeDisabled()
    // Save now removes the stored value: nothing may still say an empty field keeps it.
    expect(input).not.toHaveAttribute('placeholder')
    await user.click(removal)
    expect(lastDraft()).toEqual({ value: '', clear: false })
    expect(input).toBeEnabled()
    expect(input).toHaveAttribute('placeholder', 'Stored. Leave empty to keep it.')
  })

  it.each<[string, CredentialRequirement, boolean]>([
    ['a required secret, which the service can\'t do without', 'Required', true],
    ['an optional secret that nothing is stored for', 'Optional', false],
  ])('offers no removal for %s', (_, requirement, stored) => {
    // Arrange
    const field = { requirement, stored }

    // Act
    renderField(field.requirement, field.stored)

    // Assert
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument()
    expect(screen.getByLabelText(/^Password/)).toBeEnabled()
  })
})
