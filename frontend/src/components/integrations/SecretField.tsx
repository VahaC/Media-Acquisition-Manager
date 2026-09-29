import { useTranslation } from 'react-i18next'
import { Input } from '@/components/ui/input'
import type { CredentialRequirement, SecretDraft } from '@/lib/types'
import '@/styles/auth.css' // .auth-field — a caption and its input, like every credential field
import '@/styles/settings-dialog.css' // .settings-check — the "Remove the stored …" box

interface SecretFieldProps {
  /** The field's caption: "Password", "API key". */
  label: string
  /** The caption of the removal box: "Remove the stored password". */
  removeLabel: string
  requirement: CredentialRequirement
  /** A value is stored for this field. It is never sent here, only that it exists. */
  stored: boolean
  value: SecretDraft
  onChange: (next: SecretDraft) => void
}

/**
 * A password or API key of an integration. A stored value is never shown or prefilled — the API
 * doesn't return it — so an empty field keeps it, and its placeholder says so. An optional stored
 * secret can be removed with a box that empties and locks the field while ticked.
 *
 * MAM-7 — the placeholder goes while the box is ticked: Save then removes the stored value, and a
 * locked field still saying "Leave empty to keep it" would promise the opposite.
 *
 * MAM-7 — `autoComplete="new-password"`: with "current-password" or none, the browser offers the
 * owner's own sign-in for this app and would fill it into a qBittorrent or Jellyfin field.
 */
export function SecretField({ label, removeLabel, requirement, stored, value, onChange }: SecretFieldProps) {
  const { t } = useTranslation('integrations')
  const removable = stored && requirement === 'Optional'

  return (
    <div className="auth-field">
      <label className="auth-field">
        <span className="ui-label">
          {label}
          {requirement === 'Optional' && <> {t('dialog.optional')}</>}
        </span>
        <Input
          type="password"
          value={value.value}
          placeholder={stored && !value.clear ? t('dialog.secretStored') : undefined}
          autoComplete="new-password"
          spellCheck={false}
          disabled={value.clear}
          onChange={(event) => onChange({ value: event.target.value, clear: false })}
        />
      </label>
      {removable && (
        <label className="settings-check">
          <input
            type="checkbox"
            checked={value.clear}
            onChange={(event) => onChange({ value: '', clear: event.target.checked })}
          />
          {removeLabel}
        </label>
      )}
    </div>
  )
}
