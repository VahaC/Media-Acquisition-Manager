import { render, screen } from '@testing-library/react'
import { ConnectionTestResultLine } from '@/components/integrations/ConnectionTestResultLine'

describe('ConnectionTestResultLine', () => {
  it('shows nothing before a test has finished', () => {
    // Arrange
    const result = null

    // Act
    const { container } = render(<ConnectionTestResultLine result={result} />)

    // Assert
    expect(container).toBeEmptyDOMElement()
  })

  it.each([
    [true, 'ok', 'Connected to qBittorrent v5.2.3.'],
    [false, 'err', 'qBittorrent refused the user name or password.'],
  ])('reads out the sentence and marks its outcome (ok: %s)', (ok, outcome, text) => {
    // Arrange
    const result = { ok, text }

    // Act
    render(<ConnectionTestResultLine result={result} />)

    // Assert
    const line = screen.getByRole('status')
    expect(line).toHaveTextContent(text)
    expect(line).toHaveAttribute('data-outcome', outcome)
  })
})
