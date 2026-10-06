import { ArrowUpDown } from 'lucide-react'
import '@/styles/settings-list.css' // .settings-sort, .settings-filter — the order's label and select

interface SortOption<TSort extends string> {
  value: TSort
  label: string
}

interface SortSelectProps<TSort extends string> {
  /** "Sort by", as the page words it: shown before the select and its accessible name. */
  label: string
  value: TSort
  options: readonly SortOption<TSort>[]
  onChange: (value: TSort) => void
}

/**
 * The order of a list, beside its filters (in a FilterBar), under a visible "Sort by" with a sort icon: the
 * filter selects name themselves by their first option ("All reasons"), which a list of orders can't. Shared by
 * the Library, Search and Recommendations pages, so every list says it the same way. The select keeps its own
 * aria-label, so its name stays the label alone rather than the label with the chosen order tacked on, as a
 * wrapping label's text would make it.
 */
export function SortSelect<TSort extends string>({ label, value, options, onChange }: SortSelectProps<TSort>) {
  return (
    <label className="settings-sort">
      <ArrowUpDown className="h-3.5 w-3.5" aria-hidden="true" />
      {label}
      <select
        className="ui-input settings-filter"
        value={value}
        aria-label={label}
        onChange={(event) => onChange(event.target.value as TSort)}
      >
        {options.map((option) => (
          <option key={option.value} value={option.value}>{option.label}</option>
        ))}
      </select>
    </label>
  )
}
