import { render, screen } from '@testing-library/react'
import { FileSizeList } from '@/components/shared/FileSizeList'
import { formatFileSize } from '@/lib/format'

// MAM-40 — the one list of paths with their sizes that the confirmations of Delete for good, Restore and Remove
// this copy share, so all of them read alike: the classes the dialogs' stylesheets lay out, in the files' order.

describe('FileSizeList', () => {
  it('lists each file\'s path with its size under it, in the order given, as the confirmations lay them out', () => {
    // Arrange
    const files = [
      { id: 'video', path: '/media/backup/Aliens (1986).mkv', sizeBytes: 14_845_708_473 },
      { id: 'subtitle', path: '/media/backup/Aliens (1986).en.srt', sizeBytes: 61_440 },
    ]

    // Act
    render(<FileSizeList files={files} />)

    // Assert
    const list = screen.getByRole('list')
    expect(list).toHaveClass('jobs-quarantine-list')
    expect(screen.getAllByRole('listitem').map((item) => [
      item.querySelector('.jobs-file-path')?.textContent,
      item.querySelector('.jobs-quarantine-size')?.textContent,
    ])).toEqual([
      ['/media/backup/Aliens (1986).mkv', formatFileSize(14_845_708_473)],
      ['/media/backup/Aliens (1986).en.srt', formatFileSize(61_440)],
    ])
  })

  it('says a size nothing measured is unknown rather than showing none', () => {
    // Arrange
    const files = [{ id: 'unmeasured', path: '/media/films/Arrival (2016).mkv', sizeBytes: null }]

    // Act
    render(<FileSizeList files={files} />)

    // Assert
    expect(screen.getByRole('listitem').querySelector('.jobs-quarantine-size')).toHaveTextContent('Size unknown')
  })
})
