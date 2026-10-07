import { useState } from 'react'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Copy, Fingerprint, Layers, Link } from 'lucide-react'
import { PageTabs } from '@/components/shared/PageTabs'

type Tab = 'All' | 'SameFile' | 'ByteIdentical' | 'SameWork'

const TABS = [
  { value: 'All', label: 'All', icon: Layers },
  { value: 'SameFile', label: 'Same file', icon: Link },
  { value: 'ByteIdentical', label: 'Byte-identical', icon: Fingerprint },
  { value: 'SameWork', label: 'Same work', icon: Copy },
] as const

// jsdom has no layout: give the row a phone's width and each tab a fixed slot, so "is the current tab
// in view" has an answer.
const ROW_WIDTH = 300
const TAB_WIDTH = 100
const original = {
  clientWidth: Object.getOwnPropertyDescriptor(HTMLElement.prototype, 'clientWidth'),
  offsetWidth: Object.getOwnPropertyDescriptor(HTMLElement.prototype, 'offsetWidth'),
  offsetLeft: Object.getOwnPropertyDescriptor(HTMLElement.prototype, 'offsetLeft'),
}

function laidOutAsAPhone() {
  Object.defineProperty(HTMLElement.prototype, 'clientWidth', {
    configurable: true,
    get(this: HTMLElement) { return this.getAttribute('role') === 'tablist' ? ROW_WIDTH : 0 },
  })
  Object.defineProperty(HTMLElement.prototype, 'offsetWidth', {
    configurable: true,
    get(this: HTMLElement) { return this.getAttribute('role') === 'tab' ? TAB_WIDTH : 0 },
  })
  Object.defineProperty(HTMLElement.prototype, 'offsetLeft', {
    configurable: true,
    get(this: HTMLElement) {
      const index = this.parentElement ? [...this.parentElement.children].indexOf(this) : 0
      return this.getAttribute('role') === 'tab' ? index * TAB_WIDTH : 0
    },
  })
}

/** The tabs as a page holds them: the current one is the page's, and a pick moves it. */
function Tabs({ initial, onSelect }: { initial: Tab; onSelect?: (tab: Tab) => void }) {
  const [current, setCurrent] = useState<Tab>(initial)
  return (
    <>
      <PageTabs
        label="Kinds of duplicate"
        tabs={TABS}
        current={current}
        panelId="panel"
        tabId={(tab) => `tab-${tab}`}
        onSelect={(tab) => {
          setCurrent(tab)
          onSelect?.(tab)
        }}
      />
      <div id="panel" role="tabpanel" aria-labelledby={`tab-${current}`} />
    </>
  )
}

describe('PageTabs', () => {
  afterEach(() => {
    for (const [name, descriptor] of Object.entries(original)) {
      if (descriptor) Object.defineProperty(HTMLElement.prototype, name, descriptor)
    }
  })

  it('names the list and marks the current tab, the only one in the tab order, as the panel\'s label', () => {
    // Arrange
    const initial = 'SameFile'

    // Act
    render(<Tabs initial={initial} />)

    // Assert
    const tabs = screen.getAllByRole('tab')
    expect(screen.getByRole('tablist', { name: 'Kinds of duplicate' })).toBeInTheDocument()
    expect(tabs.map((tab) => tab.textContent)).toEqual(['All', 'Same file', 'Byte-identical', 'Same work'])
    expect(tabs.map((tab) => tab.getAttribute('aria-selected'))).toEqual(['false', 'true', 'false', 'false'])
    expect(tabs.map((tab) => tab.tabIndex)).toEqual([-1, 0, -1, -1])
    expect(screen.getByRole('tabpanel', { name: 'Same file' })).toBeInTheDocument()
  })

  it('opens a tab on a click', async () => {
    // Arrange
    const onSelect = vi.fn()
    render(<Tabs initial="All" onSelect={onSelect} />)

    // Act
    await userEvent.click(screen.getByRole('tab', { name: 'Same work' }))

    // Assert
    expect(onSelect).toHaveBeenCalledWith('SameWork')
    expect(screen.getByRole('tab', { name: 'Same work' })).toHaveAttribute('aria-selected', 'true')
  })

  it.each([
    ['→', '{ArrowRight}', 'ByteIdentical'],
    ['←', '{ArrowLeft}', 'All'],
    ['Home', '{Home}', 'All'],
    ['End', '{End}', 'SameWork'],
  ])('moves to and opens the tab %s reaches, with the focus on it', async (_, keys, expected) => {
    // Arrange
    const onSelect = vi.fn()
    render(<Tabs initial="SameFile" onSelect={onSelect} />)
    screen.getByRole('tab', { name: 'Same file' }).focus()

    // Act
    await userEvent.keyboard(keys)

    // Assert
    expect(onSelect).toHaveBeenCalledWith(expected)
    expect(document.getElementById(`tab-${expected}`)).toHaveFocus()
  })

  it('goes round from the last tab to the first with →', async () => {
    // Arrange
    render(<Tabs initial="SameWork" />)
    screen.getByRole('tab', { name: 'Same work' }).focus()

    // Act
    await userEvent.keyboard('{ArrowRight}')

    // Assert
    expect(screen.getByRole('tab', { name: 'All' })).toHaveFocus()
    expect(screen.getByRole('tab', { name: 'All' })).toHaveAttribute('aria-selected', 'true')
  })

  it('scrolls the row to a current tab that is off screen', () => {
    // Arrange
    laidOutAsAPhone()

    // Act
    render(<Tabs initial="SameWork" />)

    // Assert
    // The last tab starts at 300 and is 100 wide: centred in a 300 wide row, the row starts at 200.
    expect(screen.getByRole('tablist').scrollLeft).toBe(200)
  })

  it('leaves the row where it is when the current tab is already in view', () => {
    // Arrange
    laidOutAsAPhone()

    // Act
    render(<Tabs initial="SameFile" />)

    // Assert
    expect(screen.getByRole('tablist').scrollLeft).toBe(0)
  })
})
