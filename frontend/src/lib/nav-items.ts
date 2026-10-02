import type { ParseKeys } from 'i18next'
import { CircleUser, FolderInput, FolderOpen, HardDriveDownload, LibraryBig, Plug, Search, Users, type LucideIcon } from 'lucide-react'
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
  // MAM-16 — first in the user menu, under who is signed in: every signed-in user's own account.
  { path: '/profile', labelKey: 'nav.profile', icon: CircleUser, ownerOnly: false, placement: 'userMenu' },
  // MAM-12 — first in the header: searching the library and the release sources side by side is what the app is for.
  // MAM-16 — the header shows its search field in this link's place (AppLayout, HeaderSearch).
  { path: '/search', labelKey: 'nav.search', icon: Search, ownerOnly: false, placement: 'header' },
  { path: '/library', labelKey: 'nav.library', icon: LibraryBig, ownerOnly: false, placement: 'header' },
  // MAM-14 — last in the header: the jobs are the releases sent from Search, followed until they
  // finish. Every signed-in user sends downloads (MAM-13's decision 1) and reads their own jobs only
  // (MAM-14's decision 1), so the header offers it to everyone.
  { path: '/jobs', labelKey: 'nav.jobs', icon: HardDriveDownload, ownerOnly: false, placement: 'header' },
  { path: '/settings/integrations', labelKey: 'nav.integrations', icon: Plug, ownerOnly: true, placement: 'userMenu' },
  // MAM-11 — right after Integrations: a destination is a category of a download client set up there.
  { path: '/settings/destinations', labelKey: 'nav.destinations', icon: FolderInput, ownerOnly: true, placement: 'userMenu' },
  // MAM-9 — near Integrations (after Destinations): the folders the app may read are the other half
  // of its path mappings.
  { path: '/settings/media-folders', labelKey: 'nav.mediaFolders', icon: FolderOpen, ownerOnly: true, placement: 'userMenu' },
  { path: '/settings/users', labelKey: 'nav.users', icon: Users, ownerOnly: true, placement: 'userMenu' },
]

/** The entries of `placement` that `user` is offered, in list order. */
export function navItemsFor(placement: NavPlacement, user: CurrentUser): NavItem[] {
  return NAV_ITEMS.filter((item) => item.placement === placement && (!item.ownerOnly || user.isOwner))
}
