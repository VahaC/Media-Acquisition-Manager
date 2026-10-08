import type { FormEvent } from 'react'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Banner } from '@/components/shared/Banner'
import { Button } from '@/components/ui/button'

const REFRESH_FAILED = 'Refresh failed, showing the last loaded users. The server could not be reached. Check that it is running, then try again.'

describe('Banner', () => {
  it('reads a failure out as an alert, in the error tone, with nothing to click', () => {
    // Arrange
    const sentence = REFRESH_FAILED

    // Act
    render(<Banner>{sentence}</Banner>)

    // Assert
    const banner = screen.getByRole('alert')
    expect(banner).toHaveTextContent(sentence)
    // The error tone is the banner with no data-tone: ui-primitives.css colours that one red.
    expect(banner).not.toHaveAttribute('data-tone')
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
    expect(within(banner).queryByRole('button')).not.toBeInTheDocument()
  })

  it('describes the control that points at it by its id, as the Download dialog\'s Send reads its warning', () => {
    // Arrange
    const sentence = 'The app can\'t check whether this release would write over a file already there.'

    // Act
    render(
      <>
        <Banner tone="warning" id="overwrite-warning">{sentence}</Banner>
        <Button type="button" aria-describedby="overwrite-warning">Send</Button>
      </>,
    )

    // Assert
    expect(screen.getByRole('status')).toHaveAttribute('id', 'overwrite-warning')
    expect(screen.getByRole('button', { name: 'Send' })).toHaveAccessibleDescription(sentence)
  })

  it.each([
    ['ok', 'Password reset. Give Olena the new password yourself.'],
    ['warning', 'Synced with a warning: 3 movies have no file.'],
  ] as const)('tells %s news as a status, not an alert', (tone, sentence) => {
    // Arrange
    const news = sentence

    // Act
    render(<Banner tone={tone}>{news}</Banner>)

    // Assert
    const banner = screen.getByRole('status')
    expect(banner).toHaveTextContent(news)
    expect(banner).toHaveAttribute('data-tone', tone)
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('shows the action it is given after the sentence', async () => {
    // Arrange
    const retry = vi.fn()
    const user = userEvent.setup()
    render(<Banner action={<Button type="button" variant="outline" size="sm" onClick={retry}>Retry</Button>}>{REFRESH_FAILED}</Banner>)

    // Act
    await user.click(within(screen.getByRole('alert')).getByRole('button', { name: 'Retry' }))

    // Assert
    expect(retry).toHaveBeenCalledOnce()
    expect(screen.getByText(REFRESH_FAILED).compareDocumentPosition(screen.getByRole('button', { name: 'Retry' })))
      .toBe(Node.DOCUMENT_POSITION_FOLLOWING)
    expect(screen.queryByRole('button', { name: 'Dismiss' })).not.toBeInTheDocument()
  })

  it('offers Dismiss, which calls onDismiss without submitting the form the banner sits in', async () => {
    // Arrange
    const dismiss = vi.fn()
    const submit = vi.fn((event: FormEvent) => event.preventDefault())
    const user = userEvent.setup()
    render(
      <form onSubmit={submit}>
        <Banner onDismiss={dismiss}>Could not disable olena. The owner can&apos;t be disabled.</Banner>
      </form>,
    )

    // Act
    await user.click(screen.getByRole('button', { name: 'Dismiss' }))

    // Assert
    expect(dismiss).toHaveBeenCalledOnce()
    expect(submit).not.toHaveBeenCalled()
    expect(screen.getByRole('button', { name: 'Dismiss' })).toHaveAttribute('title', 'Dismiss')
  })

  it('puts Dismiss after the action', () => {
    // Arrange
    const action = <Button type="button" variant="outline" size="sm">Retry</Button>

    // Act
    render(<Banner action={action} onDismiss={() => {}}>{REFRESH_FAILED}</Banner>)

    // Assert
    const buttons = within(screen.getByRole('alert')).getAllByRole('button')
    expect(buttons.map((button) => button.textContent || button.getAttribute('aria-label'))).toEqual(['Retry', 'Dismiss'])
  })
})
