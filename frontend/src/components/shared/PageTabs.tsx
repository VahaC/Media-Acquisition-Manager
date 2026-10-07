import { useEffect, useRef, type KeyboardEvent } from 'react'
import type { LucideIcon } from 'lucide-react'
import { cn } from '@/lib/utils'
import '@/styles/modal-tabs.css' // .modal-tab* — the app's underline tabs

interface PageTab<T extends string> {
  value: T
  /** The tab's name, already translated. */
  label: string
  icon: LucideIcon
}

interface PageTabsProps<T extends string> {
  /** The tab list's name, read before its tabs. */
  label: string
  /** In their order. */
  tabs: readonly PageTab<T>[]
  current: T
  /** The id of the tab panel every tab controls. */
  panelId: string
  /** The id of `tab`'s button, which labels the panel while it is the current one. */
  tabId: (tab: T) => string
  onSelect: (tab: T) => void
}

/**
 * MAM-21 — a page's underline tabs (modal-tabs.css), each with its icon, for the Duplicates page: the
 * tabs of the TMDb page (TmdbLinkTabs) without their fixed list, so a third page with tabs doesn't copy
 * them a third time. The ARIA tabs pattern: one tab stop, the arrow keys (and Home, End) move between the
 * tabs and open the one they reach.
 *
 * More tabs than a phone's width scroll sideways, and a page opened on its last tab (or reloaded there)
 * would show a row with no tab underlined, so the current tab is brought into view in the row.
 */
export function PageTabs<T extends string>({ label, tabs, current, panelId, tabId, onSelect }: PageTabsProps<T>) {
  const list = useRef<HTMLDivElement>(null)

  // The row's own scroll is moved, not the page's: scrollIntoView would also drag the page to the tabs on
  // every load.
  useEffect(() => {
    const row = list.current
    const active = row?.querySelector<HTMLElement>('[aria-selected="true"]')
    if (!row || !active) return
    const hiddenLeft = active.offsetLeft < row.scrollLeft
    const hiddenRight = active.offsetLeft + active.offsetWidth > row.scrollLeft + row.clientWidth
    if (hiddenLeft || hiddenRight) row.scrollLeft = active.offsetLeft - (row.clientWidth - active.offsetWidth) / 2
  }, [current])

  const move = (event: KeyboardEvent<HTMLDivElement>) => {
    const values = tabs.map((tab) => tab.value)
    const index = values.indexOf(current)
    const next = event.key === 'ArrowRight' ? values[(index + 1) % values.length]
      : event.key === 'ArrowLeft' ? values[(index - 1 + values.length) % values.length]
        : event.key === 'Home' ? values[0]
          : event.key === 'End' ? values[values.length - 1]
            : undefined
    if (next === undefined) return
    event.preventDefault()
    onSelect(next)
    document.getElementById(tabId(next))?.focus()
  }

  return (
    <div ref={list} className="modal-tabs" role="tablist" aria-label={label} onKeyDown={move}>
      {tabs.map(({ value, label: tabLabel, icon: Icon }) => {
        const selected = value === current
        return (
          <button
            key={value}
            id={tabId(value)}
            type="button"
            role="tab"
            className={cn('modal-tab', selected && 'modal-tab-active')}
            aria-selected={selected}
            aria-controls={panelId}
            tabIndex={selected ? 0 : -1}
            onClick={() => onSelect(value)}
          >
            <Icon className="h-3.5 w-3.5" aria-hidden="true" />
            {tabLabel}
          </button>
        )
      })}
    </div>
  )
}
