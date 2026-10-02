import { useId, useState, type FormEvent } from 'react'
import { isAxiosError } from 'axios'
import { KeyRound } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Banner } from '@/components/shared/Banner'
import { Field } from '@/components/shared/Field'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { useChangePassword } from '@/lib/queries'
import { getApiErrorMessage } from '@/lib/utils'
import '@/styles/profile.css' // .profile-* — the dots, the form's column, its note and its buttons

/** The server's password rule, PasswordRules on the API: 8 to 128 characters. */
const MIN_PASSWORD_LENGTH = 8
const MAX_PASSWORD_LENGTH = 128

/**
 * MAM-16 — the Profile page's Password card: the password as dots and Change password, which opens a
 * form for the current password, the new one and the new one again. A change ends the user's other
 * sessions and keeps this one (ADR 0016); `onChanged` tells the page, which says so at its top.
 */
export function ChangePasswordCard({ onChanged }: { onChanged: () => void }) {
  const { t } = useTranslation('profile')
  const [open, setOpen] = useState(false)
  // Once the form has closed, Change password takes the focus back: the Cancel or submit button that
  // had it went with the form, which would drop a keyboard user at the top of the page. Not on the
  // page's first render, where the focus stays where it was.
  const [wasOpen, setWasOpen] = useState(false)
  const titleId = useId()

  const close = () => {
    setOpen(false)
    setWasOpen(true)
  }

  return (
    <section className="ui-card" aria-labelledby={titleId}>
      <div className="ui-card-header">
        <h2 id={titleId} className="ui-card-title">{t('password.title')}</h2>
        <p className="ui-card-description">{t('password.description')}</p>
      </div>
      <div className="ui-card-content">
        {open ? (
          // Mounted only while open, so Cancel and a success both start the next opening empty.
          <ChangePasswordForm
            onCancel={close}
            onChanged={() => {
              close()
              onChanged()
            }}
          />
        ) : (
          <div className="profile-inline">
            {/* Says only that a password is set: there is nothing in the dots for a screen reader. */}
            <span className="profile-masked" aria-hidden="true">{t('password.masked')}</span>
            <Button type="button" variant="outline" size="sm" autoFocus={wasOpen} onClick={() => setOpen(true)}>
              <KeyRound className="h-3.5 w-3.5" aria-hidden="true" />
              {t('password.change')}
            </Button>
          </div>
        )}
      </div>
    </section>
  )
}

// ── form ──

/** What each field broke at the last submit, shown under it until that field is edited. */
interface FieldErrors {
  current?: string
  next?: string
  confirm?: string
}

/**
 * The open form. MAM-16 — unlike the app's other password forms, it checks the rules itself before
 * sending, with the approved design's sentences: the blank current password, the new one's length
 * and the confirmation. What the server still refuses with a 400 after those checks pass can only be
 * the current password (wrong, or overtaken by the owner's reset meanwhile), so its sentence goes
 * under that field. Anything else — too many attempts, the server down — is the form's Banner.
 */
function ChangePasswordForm({ onCancel, onChanged }: { onCancel: () => void; onChanged: () => void }) {
  const { t } = useTranslation(['profile', 'auth', 'common'])
  const change = useChangePassword()
  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({})
  const [error, setError] = useState<string | null>(null)

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setError(null)
    // `length` counts UTF-16 units, as the server's string.Length does, so both agree on 8 and 128.
    const found: FieldErrors = {
      current: currentPassword === '' ? t('password.currentRequired') : undefined,
      next: newPassword.length < MIN_PASSWORD_LENGTH ? t('password.tooShort')
        : newPassword.length > MAX_PASSWORD_LENGTH ? t('password.tooLong')
          : undefined,
      confirm: confirmPassword !== newPassword ? t('auth:passwordsDiffer') : undefined,
    }
    setFieldErrors(found)
    if (found.current || found.next || found.confirm) return
    try {
      await change.mutateAsync({ currentPassword, newPassword })
      onChanged()
    } catch (e: unknown) {
      const sentence = getApiErrorMessage(e, t('password.failed'))
      if (isAxiosError(e) && e.response?.status === 400) setFieldErrors({ current: sentence })
      else setError(sentence)
    }
  }

  return (
    // noValidate: the checks above say what is wrong, in the app's words, under each field.
    <form className="profile-password-form" noValidate onSubmit={(event) => void submit(event)}>
      <Field label={t('password.current')} error={fieldErrors.current}>
        {(control) => (
          <Input
            {...control}
            type="password"
            value={currentPassword}
            autoComplete="current-password"
            autoFocus
            onChange={(event) => {
              setCurrentPassword(event.target.value)
              setFieldErrors({ ...fieldErrors, current: undefined })
            }}
          />
        )}
      </Field>
      <Field label={t('password.new')} hint={t('password.newHint')} error={fieldErrors.next}>
        {(control) => (
          <Input
            {...control}
            type="password"
            value={newPassword}
            autoComplete="new-password"
            onChange={(event) => {
              setNewPassword(event.target.value)
              // The confirmation is about this field too, as on the setup page.
              setFieldErrors({ ...fieldErrors, next: undefined, confirm: undefined })
            }}
          />
        )}
      </Field>
      <Field label={t('auth:confirmPassword')} error={fieldErrors.confirm}>
        {(control) => (
          <Input
            {...control}
            type="password"
            value={confirmPassword}
            autoComplete="new-password"
            onChange={(event) => {
              setConfirmPassword(event.target.value)
              setFieldErrors({ ...fieldErrors, confirm: undefined })
            }}
          />
        )}
      </Field>
      {/* What the server refused for no single field, above the buttons; the fields keep what was typed. */}
      {error && <Banner>{error}</Banner>}
      <p className="profile-note">{t('password.note')}</p>
      {/* Primary first, Cancel last — the order every form and dialog in the app uses. */}
      <div className="profile-actions">
        <Button type="submit" disabled={change.isPending}>
          {change.isPending ? t('password.changing') : t('password.change')}
        </Button>
        <Button type="button" variant="outline" onClick={onCancel}>{t('common:cancel')}</Button>
      </div>
    </form>
  )
}
