import { act, render, screen } from '@testing-library/react'
import { RecommendationTabs } from '@/components/recommendations/RecommendationTabs'
import { i18n } from '@/lib/i18n'
import type { RecommendationTab } from '@/lib/types'

const TABS: readonly RecommendationTab[] = ['Movies', 'Series', 'Cartoons', 'Documentaries']

// jsdom has no layout: give the row a phone's width and each tab a fixed slot, so "is the current tab
// in view" has an answer (as PageTabs.test.tsx does). Four 100px tabs in a 300px row: the Ukrainian
// labels' row on a phone, wider than the screen.
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

function renderTabs(current: RecommendationTab) {
  render(
    <>
      <RecommendationTabs tabs={TABS} current={current} panelId="panel" tabId={(tab) => `tab-${tab}`} onSelect={vi.fn()} />
      <div id="panel" role="tabpanel" aria-labelledby={`tab-${current}`} />
    </>,
  )
}

describe('RecommendationTabs', () => {
  afterEach(async () => {
    for (const [name, descriptor] of Object.entries(original)) {
      if (descriptor) Object.defineProperty(HTMLElement.prototype, name, descriptor)
    }
    // The app's one i18n instance: a test that switched it to Ukrainian must not leave it there.
    await act(() => i18n.changeLanguage('en-US'))
  })

  it('names the lists in the UI\'s language, the current one the only tab in the tab order', async () => {
    // Arrange
    await act(() => i18n.changeLanguage('uk'))

    // Act
    renderTabs('Series')

    // Assert
    const tabs = screen.getAllByRole('tab')
    expect(screen.getByRole('tablist', { name: i18n.t('recommendations:tabs.label') })).toBeInTheDocument()
    expect(tabs.map((tab) => tab.textContent)).toEqual(TABS.map((tab) => i18n.t(`recommendations:tabs.${tab}`)))
    expect(tabs.map((tab) => tab.tabIndex)).toEqual([-1, 0, -1, -1])
    expect(screen.getByRole('tabpanel', { name: i18n.t('recommendations:tabs.Series') })).toBeInTheDocument()
  })

  it('scrolls a row wider than a phone to the current tab when the page opens on the last one', () => {
    // Arrange
    laidOutAsAPhone()

    // Act
    renderTabs('Documentaries')

    // Assert
    // The last tab starts at 300 and is 100 wide: centred in a 300 wide row, the row starts at 200.
    expect(screen.getByRole('tablist').scrollLeft).toBe(200)
  })
})
