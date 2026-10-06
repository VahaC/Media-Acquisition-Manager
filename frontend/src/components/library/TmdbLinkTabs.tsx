import type { KeyboardEvent } from 'react'
import { BadgeCheck, ListChecks, TriangleAlert, Unlink, type LucideIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { cn } from '@/lib/utils'
import '@/styles/modal-tabs.css' // .modal-tab* — the app's underline tabs

/** The TMDb page's tabs, in their order. */
export type TmdbLinkTab = 'Review' | 'Confident' | 'Conflicts' | 'Unattached'

const TAB_ICONS: Record<TmdbLinkTab, LucideIcon> = {
  Review: ListChecks,
  Confident: BadgeCheck,
  Conflicts: TriangleAlert,
  Unattached: Unlink,
}

interface TmdbLinkTabsProps {
  tabs: readonly TmdbLinkTab[]
  current: TmdbLinkTab
  /** The id of the tab panel every tab controls. */
  panelId: string
  /** The id of `tab`'s button, which labels the panel while it is the current one. */
  tabId: (tab: TmdbLinkTab) => string
  onSelect: (tab: TmdbLinkTab) => void
}

/**
 * MAM-34 — the TMDb page's underline tabs (R24): Review, Confident, Conflicts, Unattached, each with its
 * icon. The ARIA tabs pattern of the Recommendations page's tabs: one tab stop, the arrow keys (and
 * Home, End) move between the tabs and open the one they reach. The Review tab's → leaves the key to
 * this list while the focus is in it.
 */
export function TmdbLinkTabs({ tabs, current, panelId, tabId, onSelect }: TmdbLinkTabsProps) {
  const { t } = useTranslation('libraryLinks')

  const move = (event: KeyboardEvent<HTMLDivElement>) => {
    const index = tabs.indexOf(current)
    const next = event.key === 'ArrowRight' ? tabs[(index + 1) % tabs.length]
      : event.key === 'ArrowLeft' ? tabs[(index - 1 + tabs.length) % tabs.length]
        : event.key === 'Home' ? tabs[0]
          : event.key === 'End' ? tabs[tabs.length - 1]
            : undefined
    if (next === undefined) return
    event.preventDefault()
    onSelect(next)
    document.getElementById(tabId(next))?.focus()
  }

  return (
    <div className="modal-tabs" role="tablist" aria-label={t('tabs.label')} onKeyDown={move}>
      {tabs.map((tab) => {
        const Icon = TAB_ICONS[tab]
        const selected = tab === current
        return (
          <button
            key={tab}
            id={tabId(tab)}
            type="button"
            role="tab"
            className={cn('modal-tab', selected && 'modal-tab-active')}
            aria-selected={selected}
            aria-controls={panelId}
            tabIndex={selected ? 0 : -1}
            onClick={() => onSelect(tab)}
          >
            <Icon className="h-3.5 w-3.5" aria-hidden="true" />
            {t(`tabs.${tab}`)}
          </button>
        )
      })}
    </div>
  )
}
