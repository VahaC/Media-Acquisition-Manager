import { useId } from 'react'
import type { LucideIcon } from 'lucide-react'
import '@/styles/segmented-control.css' // .segmented-control* — the group, its pills and the one picked

interface SegmentedOption<T extends string> {
  value: T
  /** Its name: beside the icon with `showLabels`, otherwise read by a screen reader and shown on hover. */
  label: string
  /** A tooltip that says more than the label ("Versions as rows" for Rows); icons alone show the label. */
  title?: string
  /** MAM-46 — none for a pill of text only, which needs `showLabels`: without them the icon is all there is. */
  icon?: LucideIcon
  /**
   * MAM-46 — the label's own language when it isn't the page's, as for a language named in itself
   * (Українська on an English page), so a screen reader pronounces it right.
   */
  lang?: string
}

interface SegmentedControlProps<T extends string> {
  /** The group's name, read before its options. */
  label: string
  options: readonly SegmentedOption<T>[]
  value: T
  onChange: (value: T) => void
  /** Shows each label beside its icon; without it the options are icons, each with a tooltip. */
  showLabels?: boolean
}

/**
 * MAM-16 — a row of pills of which exactly one is picked, such as the theme (Light, Dark, System).
 *
 * Native radio buttons under the pills rather than buttons with `role="radio"`: the browser then
 * gives the group one tab stop and moves the choice with the arrow keys, as the ARIA radio group
 * pattern asks, where buttons would need both written by hand. A choice applies at once.
 */
export function SegmentedControl<T extends string>({
  label,
  options,
  value,
  onChange,
  showLabels = false,
}: SegmentedControlProps<T>) {
  // One group per control: two controls on screen (the menu's and the Profile page's theme) must not
  // share a radio name, or picking in one would clear the other.
  const name = useId()

  return (
    <div className="segmented-control" role="radiogroup" aria-label={label} data-icon-only={showLabels ? undefined : ''}>
      {options.map(({ value: optionValue, label: optionLabel, title, icon: Icon, lang }) => (
        <label key={optionValue} className="segmented-control-option" title={title ?? (showLabels ? undefined : optionLabel)}>
          <input
            type="radio"
            className="sr-only"
            name={name}
            value={optionValue}
            checked={optionValue === value}
            onChange={() => onChange(optionValue)}
          />
          <span className="segmented-control-pill">
            {Icon && <Icon className="h-3.5 w-3.5" aria-hidden="true" />}
            <span className={showLabels ? undefined : 'sr-only'} lang={lang}>{optionLabel}</span>
          </span>
        </label>
      ))}
    </div>
  )
}
