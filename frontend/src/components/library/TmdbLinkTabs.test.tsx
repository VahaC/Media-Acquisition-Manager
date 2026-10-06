import { render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import { TmdbLinkTabs, type TmdbLinkTab } from '@/components/library/TmdbLinkTabs'

const TABS: readonly TmdbLinkTab[] = ['Review', 'Confident', 'NotIdentified', 'Conflicts', 'Unattached']

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

function renderTabs(current: TmdbLinkTab) {
  render(<TmdbLinkTabs tabs={TABS} current={current} panelId="panel" tabId={(tab) => `tab-${tab}`} onSelect={() => {}} />)
  return screen.getByRole('tablist')
}

describe('TmdbLinkTabs', () => {
  afterEach(() => {
    for (const [name, descriptor] of Object.entries(original)) {
      if (descriptor) Object.defineProperty(HTMLElement.prototype, name, descriptor)
    }
  })

  it('scrolls the row to a current tab that is off screen', () => {
    laidOutAsAPhone()

    const row = renderTabs('Unattached')

    // The last tab starts at 400 and is 100 wide: centred in a 300 wide row, the row starts at 300.
    expect(row.scrollLeft).toBe(300)
  })

  it('leaves the row where it is when the current tab is already in view', () => {
    laidOutAsAPhone()

    const row = renderTabs('Confident')

    expect(row.scrollLeft).toBe(0)
  })
})
