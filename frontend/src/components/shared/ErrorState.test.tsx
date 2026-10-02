import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ErrorState } from '@/components/shared/ErrorState'

const LOAD_FAILED = 'Could not load the users.'
const UNREACHABLE = 'The server could not be reached. Check that it is running, then try again.'

describe('ErrorState', () => {
  it('says what failed and the server\'s sentence in one alert', () => {
    // Arrange
    const sentence = UNREACHABLE

    // Act
    render(<ErrorState title={LOAD_FAILED} onRetry={() => {}}>{sentence}</ErrorState>)

    // Assert
    const state = screen.getByRole('alert')
    expect(within(state).getByText(LOAD_FAILED)).toBeInTheDocument()
    expect(within(state).getByText(sentence)).toBeInTheDocument()
    expect(within(state).getByRole('button', { name: 'Try again' })).toBeEnabled()
  })

  it('leaves the body out when the server sent no sentence', () => {
    // Arrange
    const sentence = undefined

    // Act
    render(<ErrorState title={LOAD_FAILED}>{sentence}</ErrorState>)

    // Assert
    expect(screen.getByRole('alert').textContent).toBe(LOAD_FAILED)
  })

  it('offers no Try again without onRetry, for a failure a second try can\'t change', () => {
    // Arrange
    const notFound = 'This search is no longer available.'

    // Act
    render(<ErrorState title={notFound} />)

    // Assert
    expect(within(screen.getByRole('alert')).queryByRole('button')).not.toBeInTheDocument()
  })

  it('asks again from Try again', async () => {
    // Arrange
    const retry = vi.fn()
    const user = userEvent.setup()
    render(<ErrorState title={LOAD_FAILED} onRetry={retry}>{UNREACHABLE}</ErrorState>)

    // Act
    await user.click(screen.getByRole('button', { name: 'Try again' }))

    // Assert
    expect(retry).toHaveBeenCalledOnce()
  })

  it('reads Retrying… and takes no second click while a retry is pending', async () => {
    // Arrange
    const retry = vi.fn()
    const user = userEvent.setup()
    render(<ErrorState title={LOAD_FAILED} onRetry={retry} retrying>{UNREACHABLE}</ErrorState>)

    // Act
    await user.click(screen.getByRole('button', { name: 'Retrying…' }))

    // Assert
    expect(screen.getByRole('button', { name: 'Retrying…' })).toBeDisabled()
    expect(screen.queryByRole('button', { name: 'Try again' })).not.toBeInTheDocument()
    expect(retry).not.toHaveBeenCalled()
    // The sentence stays while the retry runs: it is still the last thing the server said.
    expect(screen.getByRole('alert')).toHaveTextContent(UNREACHABLE)
  })
})
