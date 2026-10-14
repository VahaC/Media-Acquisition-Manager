import { useId, useState } from 'react'
import { ChevronDown, ChevronRight } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { FieldErrorLine } from '@/components/shared/FieldErrorLine'
import { sectionKey } from '@/lib/destination-sections'
import { formatNumber } from '@/lib/format'
import type { DestinationSection, DestinationSectionList, DestinationSectionSource } from '@/lib/types'
import { getApiErrorMessage } from '@/lib/utils'
import '@/styles/auth.css' // .auth-field, .auth-hint, .auth-muted — the field as every settings dialog lays it out
import '@/styles/settings-dialog.css' // .settings-check — a ticked section's box before its name
import '@/styles/destinations.css' // .destinations-source*, .destinations-sections* — the sources and their sections

interface DestinationSectionsFieldProps {
  /** The sections of every release source; undefined while they load or when they failed. */
  list: DestinationSectionList | undefined
  isLoading: boolean
  error: unknown
  /** The destination being edited; none when one is added. */
  destinationId: string | undefined
  /** The keys of the ticked sections (`sectionKey`). */
  picked: ReadonlySet<string>
  onToggle: (key: string) => void
}

/**
 * MAM-32 — the destination dialog's tracker sections (ADR 0020): the sections of each release source
 * the owner ticks, so the Download dialog opens on this destination for a release listed in one of
 * them («Мультсеріали» → Cartoons). The dialog holds the picks; its one Save sends them.
 *
 * A source is a row that opens to its sections — Toloka lists about 130 — split into its own sections
 * and the standard sections every source shares (Torznab's standard categories), as a mapped own
 * section wins over a standard one. MAM-31 — headed "Standard sections" beside "Its own sections" and
 * the field's "Tracker sections", not "Standard categories": the live check showed "categories" with
 * Transmission, whose destinations have none.
 * A section mapped to another destination names it: ticked, it moves here. A source that couldn't be
 * asked (switched off, unreachable) says why, and still lists the sections mapped of it, so a save
 * never drops one unseen. The list is whole, as the server sends it: a save replaces the
 * destination's sections, so every mapped one must be there.
 */
export function DestinationSectionsField({ list, isLoading, error, destinationId, picked, onToggle }: DestinationSectionsFieldProps) {
  const { t } = useTranslation('destinations')
  const labelId = useId()
  const hintId = useId()

  const content = (() => {
    if (isLoading) return <p className="auth-muted">{t('dialog.sectionsLoading')}</p>
    if (error && !list) return <FieldErrorLine>{getApiErrorMessage(error, t('dialog.sectionsFailed'))}</FieldErrorLine>
    if (!list) return null
    if (list.sources.length === 0) return <p className="auth-hint">{t('dialog.noSources')}</p>
    return (
      <ul className="destinations-sources">
        {list.sources.map((source) => (
          <SourceSections
            key={source.id}
            source={source}
            // One source is all there is to look at, so it opens at once; several stay closed.
            openAtFirst={list.sources.length === 1}
            destinationId={destinationId}
            picked={picked}
            onToggle={onToggle}
          />
        ))}
      </ul>
    )
  })()

  return (
    <div className="auth-field" role="group" aria-labelledby={labelId} aria-describedby={hintId}>
      <span id={labelId} className="ui-label">{t('dialog.sections')}{' '}{t('dialog.optional')}</span>
      <p id={hintId} className="auth-hint">{t('dialog.sectionsHint')}</p>
      {content}
    </div>
  )
}

// ── one release source ──

function SourceSections({ source, openAtFirst, destinationId, picked, onToggle }: {
  source: DestinationSectionSource
  openAtFirst: boolean
  destinationId: string | undefined
  picked: ReadonlySet<string>
  onToggle: (key: string) => void
}) {
  const { t } = useTranslation(['destinations', 'common'])
  const sectionsId = useId()
  const [open, setOpen] = useState(openAtFirst)
  const own = source.sections.filter((section) => section.isSourceSpecific)
  const standard = source.sections.filter((section) => !section.isSourceSpecific)
  const count = source.sections.filter((section) => picked.has(sectionKey(source.id, section))).length
  const Chevron = open ? ChevronDown : ChevronRight

  const group = (label: string, sections: DestinationSection[]) => sections.length > 0 && (
    <fieldset className="destinations-sections-group">
      <legend className="auth-muted">{label}</legend>
      <ul className="destinations-sections">
        {sections.map((section) => {
          const key = sectionKey(source.id, section)
          // Mapped to another destination: ticking it here moves it, so say where it is now.
          const elsewhere = section.destinationId !== null && section.destinationId !== destinationId
            ? section.destinationName
            : null
          return (
            <li key={key}>
              <label className="settings-check">
                <input type="checkbox" checked={picked.has(key)} onChange={() => onToggle(key)} />
                <span className="destinations-section-name">
                  {section.name}
                  {elsewhere !== null && (
                    <span className="auth-muted">{t('common:separator')}{t('dialog.mappedTo', { name: elsewhere })}</span>
                  )}
                </span>
              </label>
            </li>
          )
        })}
      </ul>
    </fieldset>
  )

  return (
    <li className="destinations-source">
      <button
        type="button"
        className="destinations-source-toggle"
        aria-expanded={open}
        aria-controls={open ? sectionsId : undefined}
        onClick={() => setOpen(!open)}
      >
        <Chevron className="h-4 w-4" aria-hidden="true" />
        <span className="destinations-source-name">{source.name}</span>
        <span className="auth-muted">
          {count === 0 ? t('dialog.nonePicked') : t('dialog.picked', { count, value: formatNumber(count) })}
        </span>
      </button>
      {source.error && <FieldErrorLine>{source.error}</FieldErrorLine>}
      {open && (
        <div id={sectionsId} className="destinations-source-sections">
          {source.sections.length === 0 ? (
            // A source that couldn't be asked has said why above; one that answered lists none.
            !source.error && <p className="auth-hint">{t('dialog.noSections', { name: source.name })}</p>
          ) : (
            <>
              {group(t('dialog.ownSections'), own)}
              {group(t('dialog.standardSections'), standard)}
            </>
          )}
        </div>
      )}
    </li>
  )
}
