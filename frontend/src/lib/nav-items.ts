import type { ParseKeys } from 'i18next'
import { LibraryBig, Plug, Users, type LucideIcon } from 'lucide-react'
import type { CurrentUser } from './types'

/** Where an entry is offered: the header bar, or the signed-in user's menu. */
export type NavPlacement = 'header' | 'userMenu'

/** One page a signed-in user can open from the shell. */
export interface NavItem {
  /** The address, as App.tsx routes it. */
  path: string
  /** Its label, in the `common` namespace. */
  labelKey: ParseKeys<'common'>
  icon: LucideIcon
  /** Offered to the installation owner only: the page answers everyone else with a 403. */
  ownerOnly: boolean
  placement: NavPlacement
}

/**
 * MAM-8 — every page the shell links to, in the order it shows them: the header's links and the user
 * menu's entries come from this one list, so a new page is added here once rather than in each.
 */
export const NAV_ITEMS: readonly NavItem[] = [
  { path: '/library', labelKey: 'nav.library', icon: LibraryBig, ownerOnly: false, placement: 'header' },
  { path: '/settings/integrations', labelKey: 'nav.integrations', icon: Plug, ownerOnly: true, placement: 'userMenu' },
  { path: '/settings/users', labelKey: 'nav.users', icon: Users, ownerOnly: true, placement: 'userMenu' },
]

/** The entries of `placement` that `user` is offered, in list order. */
export function navItemsFor(placement: NavPlacement, user: CurrentUser): NavItem[] {
  return NAV_ITEMS.filter((item) => item.placement === placement && (!item.ownerOnly || user.isOwner))
}
