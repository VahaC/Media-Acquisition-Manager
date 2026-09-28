import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ConfirmProvider, useConfirm, type ConfirmOptions } from '@/components/shared/ConfirmDialog'

/** A button that asks for confirmation and reports the answer through `onAnswer`. */
function Asker({ options, onAnswer }: { options: ConfirmOptions; onAnswer: (value: boolean) => void }) {
  const confirm = useConfirm()
  return <button type="button" onClick={async () => onAnswer(await confirm(options))}>Ask</button>
}

function renderAsker(options: ConfirmOptions) {
  const onAnswer = vi.fn()
  render(
    <ConfirmProvider>
      <Asker options={options} onAnswer={onAnswer} />
    </ConfirmProvider>,
  )
  return { onAnswer, user: userEvent.setup() }
}

const deleteOptions: ConfirmOptions = {
  title: 'Delete the library?',
  message: 'This removes it from the app.',
  confirmLabel: 'Delete',
  destructive: true,
}

describe('ConfirmDialog', () => {
  it('resolves true when the primary button is clicked', async () => {
    // Arrange
    const { onAnswer, user } = renderAsker(deleteOptions)
    await user.click(screen.getByRole('button', { name: 'Ask' }))

    // Act
    await user.click(screen.getByRole('button', { name: 'Delete' }))

    // Assert
    expect(onAnswer).toHaveBeenCalledWith(true)
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('resolves false when Cancel is clicked', async () => {
    // Arrange
    const { onAnswer, user } = renderAsker(deleteOptions)
    await user.click(screen.getByRole('button', { name: 'Ask' }))

    // Act
    await user.click(screen.getByRole('button', { name: 'Cancel' }))

    // Assert
    expect(onAnswer).toHaveBeenCalledWith(false)
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('resolves false when Escape closes it', async () => {
    // Arrange
    const { onAnswer, user } = renderAsker(deleteOptions)
    await user.click(screen.getByRole('button', { name: 'Ask' }))

    // Act
    await user.keyboard('{Escape}')

    // Assert
    expect(onAnswer).toHaveBeenCalledWith(false)
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('puts the primary action first and Cancel last', async () => {
    // Arrange
    const { user } = renderAsker(deleteOptions)

    // Act
    await user.click(screen.getByRole('button', { name: 'Ask' }))

    // Assert
    const dialog = screen.getByRole('dialog', { name: 'Delete the library?' })
    const footer = within(dialog).getByRole('button', { name: 'Delete' }).parentElement!
    const labels = within(footer).getAllByRole('button').map((button) => button.textContent)
    expect(labels).toEqual(['Delete', 'Cancel'])
  })

  it('keeps the confirm button disabled until the required text is typed', async () => {
    // Arrange
    const { onAnswer, user } = renderAsker({ ...deleteOptions, requireTyped: 'Movies 4K' })
    await user.click(screen.getByRole('button', { name: 'Ask' }))
    const confirmButton = screen.getByRole('button', { name: 'Delete' })
    const disabledBefore = (confirmButton as HTMLButtonElement).disabled

    // Act
    await user.type(screen.getByRole('textbox', { name: 'Type Movies 4K to confirm' }), '  movies 4k ')
    await user.click(confirmButton)

    // Assert
    expect(disabledBefore).toBe(true)
    expect(onAnswer).toHaveBeenCalledWith(true)
  })

  it('does not accept a partly typed name', async () => {
    // Arrange
    const { user } = renderAsker({ ...deleteOptions, requireTyped: 'Movies 4K' })
    await user.click(screen.getByRole('button', { name: 'Ask' }))

    // Act
    await user.type(screen.getByRole('textbox', { name: 'Type Movies 4K to confirm' }), 'Movies')

    // Assert
    expect(screen.getByRole('button', { name: 'Delete' })).toBeDisabled()
  })
})
