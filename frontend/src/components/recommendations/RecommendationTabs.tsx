import type { KeyboardEvent } from 'react'
import { Binoculars, Film, Rabbit, Tv, type LucideIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import type { RecommendationTab } from '@/lib/types'
import { cn } from '@/lib/utils'
import '@/styles/modal-tabs.css' // .modal-tab* — the app's underline tabs

const TAB_ICONS: Record<RecommendationTab, LucideIcon> = {
  Movies: Film,
  Series: Tv,
  Cartoons: Rabbit,
  Documentaries: Binoculars,
}

interface RecommendationTabsProps {
  /** The tabs the user has on, in the page's order. */
  tabs: readonly RecommendationTab[]
  current: RecommendationTab
  /** The id of the tab panel every tab controls. */
  panelId: string
  /** The id of `tab`'s button, which labels the panel while it is the current one. */
  tabId: (tab: RecommendationTab) => string
  onSelect: (tab: RecommendationTab) => void
}

/**
 * MAM-33 — the Recommendations page's lists as underline tabs (the house tab style, modal-tabs.css),
 * each with its icon. The ARIA tabs pattern, which `role="tab"` promises: one tab stop, the arrow keys
 * (and Home, End) move between the tabs and open the one they reach.
 */
export function RecommendationTabs({ tabs, current, panelId, tabId, onSelect }: RecommendationTabsProps) {
  const { t } = useTranslation('recommendations')

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
