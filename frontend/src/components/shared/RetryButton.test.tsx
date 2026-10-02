import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { RetryButton } from '@/components/shared/RetryButton'

describe('RetryButton', () => {
  it('reads Retry and asks again on a click', async () => {
    // Arrange
    const user = userEvent.setup()
    const onRetry = vi.fn(async () => {})
    render(<RetryButton onRetry={onRetry} />)

    // Act
    await user.click(screen.getByRole('button', { name: 'Retry' }))

    // Assert
    expect(onRetry).toHaveBeenCalledOnce()
  })

  it('reads Retrying… and is disabled until its own retry settles', async () => {
    // Arrange
    const user = userEvent.setup()
    let settle: () => void = () => {}
    const onRetry = vi.fn(() => new Promise<void>((resolve) => { settle = resolve }))
    render(<RetryButton onRetry={onRetry} />)

    // Act
    await user.click(screen.getByRole('button', { name: 'Retry' }))

    // Assert
    expect(screen.getByRole('button', { name: 'Retrying…' })).toBeDisabled()
    settle()
    expect(await screen.findByRole('button', { name: 'Retry' })).toBeEnabled()
    expect(onRetry).toHaveBeenCalledOnce()
  })

  it('takes a label of its own', () => {
    // Arrange
    const onRetry = vi.fn(async () => {})

    // Act
    render(<RetryButton label="Retry now" onRetry={onRetry} />)

    // Assert
    expect(screen.getByRole('button', { name: 'Retry now' })).toBeInTheDocument()
  })
})
