import { useState } from 'react'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { PathMappingsEditor } from '@/components/integrations/PathMappingsEditor'
import type { PathMappingDraft } from '@/lib/types'

/** The editor as the dialog holds it: its rows in state, every change also handed to `onChange`. */
function Harness({ onChange }: { onChange: (next: PathMappingDraft[]) => void }) {
  const [value, setValue] = useState<PathMappingDraft[]>([])
  return (
    <PathMappingsEditor
      serviceName="qBittorrent"
      value={value}
      onChange={(next) => {
        setValue(next)
        onChange(next)
      }}
    />
  )
}

function renderEditor() {
  const onChange = vi.fn<(next: PathMappingDraft[]) => void>()
  render(<Harness onChange={onChange} />)
  const lastPairs = () =>
    onChange.mock.lastCall?.[0].map(({ servicePrefix, appPrefix }) => [servicePrefix, appPrefix])
  return { lastPairs, user: userEvent.setup() }
}

describe('PathMappingsEditor', () => {
  it('adds and removes a mapping and keeps the order', async () => {
    // Arrange
    const { lastPairs, user } = renderEditor()
    const pairs = [['/downloads', '/media'], ['/tv', '/shows'], ['/incoming', '/media/incoming']]
    for (const [index, [servicePath, appPath]] of pairs.entries()) {
      await user.click(screen.getByRole('button', { name: 'Add mapping' }))
      await user.type(screen.getAllByLabelText('Path in qBittorrent')[index], servicePath)
      await user.type(screen.getAllByLabelText('Path in this app')[index], appPath)
    }

    // Act
    await user.click(screen.getAllByRole('button', { name: 'Remove mapping' })[1])

    // Assert
    expect(lastPairs()).toEqual([['/downloads', '/media'], ['/incoming', '/media/incoming']])
    // Each row kept its own text: the row below the removed one didn't inherit the removed inputs.
    expect(screen.getAllByLabelText('Path in qBittorrent').map((input) => (input as HTMLInputElement).value))
      .toEqual(['/downloads', '/incoming'])
    expect(screen.getAllByLabelText('Path in this app').map((input) => (input as HTMLInputElement).value))
      .toEqual(['/media', '/media/incoming'])
  })

  it('names each field after the service it belongs to', async () => {
    // Arrange
    const { user } = renderEditor()
    const group = screen.getByRole('group', { name: 'Path mappings' })
    const emptyLine = within(group).getByText('No mappings: qBittorrent sees your media at the same paths as this app.')

    // Act
    await user.click(within(group).getByRole('button', { name: 'Add mapping' }))

    // Assert
    expect(within(group).getByRole('textbox', { name: 'Path in qBittorrent' })).toBeInTheDocument()
    expect(within(group).getByRole('textbox', { name: 'Path in this app' })).toBeInTheDocument()
    expect(within(group).getByRole('button', { name: 'Remove mapping' })).toBeInTheDocument()
    expect(within(group).getByText(/If qBittorrent sees your media under other paths/)).toBeInTheDocument()
    expect(emptyLine).not.toBeInTheDocument()
  })

  it('captions each side on screen once there is a mapping: over the columns and over each field', async () => {
    // Arrange
    const { user } = renderEditor()
    const captionsWithoutMappings = screen.queryAllByText(/^Path in /)

    // Act
    await user.click(screen.getByRole('button', { name: 'Add mapping' }))
    await user.click(screen.getByRole('button', { name: 'Add mapping' }))

    // Assert
    expect(captionsWithoutMappings).toEqual([])
    // One caption row over the columns, then each field's own caption, which a phone shows instead
    // once it stacks the fields (integrations.css).
    expect(screen.getAllByText(/^Path in /).map((caption) => caption.textContent)).toEqual([
      'Path in qBittorrent', 'Path in this app',
      'Path in qBittorrent', 'Path in this app',
      'Path in qBittorrent', 'Path in this app',
    ])
    const fields = screen.getAllByRole('textbox') as HTMLInputElement[]
    expect(fields.map((field) => [...field.labels!].map((label) => label.textContent))).toEqual([
      ['Path in qBittorrent'], ['Path in this app'],
      ['Path in qBittorrent'], ['Path in this app'],
    ])
    // A wide screen hides the fields' own captions, so each keeps a name of its own as well.
    expect(fields.map((field) => field.getAttribute('aria-label'))).toEqual([
      'Path in qBittorrent', 'Path in this app',
      'Path in qBittorrent', 'Path in this app',
    ])
  })

  it('shows an example path in each field', async () => {
    // Arrange
    const { user } = renderEditor()

    // Act
    await user.click(screen.getByRole('button', { name: 'Add mapping' }))

    // Assert
    expect(screen.getByRole('textbox', { name: 'Path in qBittorrent' })).toHaveAttribute('placeholder', '/downloads')
    expect(screen.getByRole('textbox', { name: 'Path in this app' })).toHaveAttribute('placeholder', '/media')
  })
})
