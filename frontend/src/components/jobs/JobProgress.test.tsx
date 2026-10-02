import { render, screen } from '@testing-library/react'
import { JobProgress } from '@/components/jobs/JobProgress'

describe('JobProgress', () => {
  it('draws the bar at the reported progress, named, with its percentage to a tenth', () => {
    // Arrange
    const progress = 0.4567

    // Act
    render(<JobProgress progress={progress} />)

    // Assert
    const bar = screen.getByRole('progressbar', { name: 'Download progress' })
    expect(bar).toHaveAttribute('value', '0.4567')
    expect(bar).toHaveAttribute('max', '1')
    expect(screen.getByText('45.6%')).toBeInTheDocument()
  })

  it('never reads 100% before the client reports the whole download', () => {
    // Arrange
    const progress = 0.99996

    // Act
    render(<JobProgress progress={progress} />)

    // Assert
    expect(screen.getByText('99.9%')).toBeInTheDocument()
  })

  it.each([
    [1, '100%'],
    [0, '0%'],
    [0.57, '57%'],
  ])('reads %s as %s', (progress, text) => {
    // Arrange
    const value = progress

    // Act
    render(<JobProgress progress={value} />)

    // Assert
    expect(screen.getByText(text)).toBeInTheDocument()
  })

  it('says the progress isn\'t known yet, with no bar, before the client has reported any', () => {
    // Arrange
    const progress = null

    // Act
    render(<JobProgress progress={progress} />)

    // Assert
    expect(screen.getByText('Not known yet')).toBeInTheDocument()
    expect(screen.queryByRole('progressbar')).not.toBeInTheDocument()
  })
})
