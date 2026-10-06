import type { ReactNode } from 'react'
import '@/styles/settings-list.css' // .settings-filterbar, .settings-toolbar — the filters, and the order beside them

interface FilterBarProps {
  /** The filters: the row they wrap in. */
  children: ReactNode
  /** The list's order (a `SortSelect`): beside the filters, not one of them. */
  sort: ReactNode
}

/**
 * A list's filters with its order. The filters wrap among themselves; the order stands beside them at the right
 * and never inside what wraps, so it can't land on a line of its own between the filters and what answers them —
 * the list, or its empty state with Clear filters — and the filters and that button stay one block. Shared by the
 * Library, Search and Recommendations pages.
 */
export function FilterBar({ children, sort }: FilterBarProps) {
  return (
    <div className="settings-filterbar">
      <div className="settings-toolbar">{children}</div>
      {sort}
    </div>
  )
}
