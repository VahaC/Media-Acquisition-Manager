import { Binoculars, Film, Rabbit, Tv, type LucideIcon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { PageTabs } from '@/components/shared/PageTabs'
import type { RecommendationTab } from '@/lib/types'

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
 *
 * MAM-46 — through the shared PageTabs, which brings the current tab into view in its row: in Ukrainian
 * the four tabs need about 380px (Мультфільми, Документальні), wider than a 360–393px phone's row
 * (328–361px), so the row scrolls, and a page opened or reloaded on Documentaries showed it cut off.
 */
export function RecommendationTabs({ tabs, current, panelId, tabId, onSelect }: RecommendationTabsProps) {
  const { t } = useTranslation('recommendations')

  return (
    <PageTabs
      label={t('tabs.label')}
      tabs={tabs.map((tab) => ({ value: tab, label: t(`tabs.${tab}`), icon: TAB_ICONS[tab] }))}
      current={current}
      panelId={panelId}
      tabId={tabId}
      onSelect={onSelect}
    />
  )
}
