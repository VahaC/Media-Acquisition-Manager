import { ArrowRight, Plus, Trash2 } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { newPathMappingDraft } from '@/lib/path-mapping-drafts'
import type { PathMappingDraft } from '@/lib/types'
import '@/styles/auth.css' // .auth-hint — the help lines and the captions, like the dialog's other hints
import '@/styles/integrations.css' // .integrations-mapping* — the captions and the rows, which stack on a phone

interface PathMappingsEditorProps {
  /** The service's name, which names its side of each mapping: "Path in qBittorrent". */
  serviceName: string
  value: PathMappingDraft[]
  onChange: (next: PathMappingDraft[]) => void
}

/**
 * The path mappings of an integration whose service reports file paths (ADR 0003): each row maps a
 * folder as the service sees it to the same folder inside this app. Controlled, and saved by the
 * dialog's one Save with the rest of the integration.
 *
 * No checks here: the server checks the whole set when it is saved, and its sentence names the
 * mapping that is wrong.
 */
export function PathMappingsEditor({ serviceName, value, onChange }: PathMappingsEditorProps) {
  const { t } = useTranslation('integrations')
  const servicePath = t('mappings.servicePath', { service: serviceName })
  const appPath = t('mappings.appPath')

  const change = (key: number, patch: Partial<Omit<PathMappingDraft, 'key'>>) =>
    onChange(value.map((mapping) => (mapping.key === key ? { ...mapping, ...patch } : mapping)))

  return (
    <fieldset className="integrations-mappings">
      <legend className="ui-label">{t('mappings.title')}</legend>
      <p className="auth-hint">{t('mappings.help', { service: serviceName })}</p>
      {value.length === 0 && <p className="auth-hint">{t('mappings.none', { service: serviceName })}</p>}
      {/* MAM-7 — each side is captioned on screen: named only for screen readers, a row was two
          boxes nobody could tell apart. A wide screen captions the columns once, over the rows; a
          phone stacks a row's fields and hides its arrow, so it captions each field instead
          (integrations.css). */}
      {value.length > 0 && (
        <div className="integrations-mapping-list">
          {/* Hidden from screen readers, which already hear each field's name. */}
          <div className="integrations-mapping-head" aria-hidden="true">
            <span className="auth-hint">{servicePath}</span>
            <span className="auth-hint">{appPath}</span>
          </div>
          {value.map((mapping) => (
            <div key={mapping.key} className="integrations-mapping-row">
              <label className="integrations-mapping-field">
                <span className="integrations-mapping-caption auth-hint">{servicePath}</span>
                <Input
                  value={mapping.servicePrefix}
                  aria-label={servicePath}
                  placeholder={t('mappings.servicePlaceholder')}
                  autoComplete="off"
                  autoCapitalize="none"
                  spellCheck={false}
                  onChange={(event) => change(mapping.key, { servicePrefix: event.target.value })}
                />
              </label>
              <ArrowRight className="integrations-mapping-arrow h-4 w-4" aria-hidden="true" />
              <label className="integrations-mapping-field">
                <span className="integrations-mapping-caption auth-hint">{appPath}</span>
                <Input
                  value={mapping.appPrefix}
                  aria-label={appPath}
                  placeholder={t('mappings.appPlaceholder')}
                  autoComplete="off"
                  autoCapitalize="none"
                  spellCheck={false}
                  onChange={(event) => change(mapping.key, { appPrefix: event.target.value })}
                />
              </label>
              <Button
                type="button"
                variant="outline"
                size="icon"
                className="ui-button-danger"
                title={t('mappings.remove')}
                onClick={() => onChange(value.filter((other) => other.key !== mapping.key))}
              >
                <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />
              </Button>
            </div>
          ))}
        </div>
      )}
      <Button type="button" variant="outline" size="sm" onClick={() => onChange([...value, newPathMappingDraft()])}>
        <Plus className="h-3.5 w-3.5" aria-hidden="true" />
        {t('mappings.add')}
      </Button>
    </fieldset>
  )
}
