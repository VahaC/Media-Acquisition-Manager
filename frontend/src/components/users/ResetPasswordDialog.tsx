import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { Banner } from '@/components/shared/Banner'
import { Field } from '@/components/shared/Field'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { useAuthStore } from '@/lib/auth-store'
import { useResetUserPassword } from '@/lib/queries'
import type { UserSummary } from '@/lib/types'
import { getApiErrorMessage } from '@/lib/utils'
import '@/styles/settings-dialog.css' // .settings-dialog* — the scrolling body and the buttons under it

interface ResetPasswordDialogProps {
  user: UserSummary
  /** The server set the password; called just before onClose, so the page can say so. */
  onSaved: () => void
  onClose: () => void
}

/**
 * Sets a new password for a user, the owner's own included. Unlike the change on the Profile page
 * it asks for no current password: it is how the owner takes an account back from whoever knows the
 * old one. The server ends every session of that user except the one this request comes from, which
 * the description says, since it differs for the owner's own row.
 *
 * MAM-16 — it closes only once the password is set. A refusal stays here with what was typed, placed
 * as in Add user: the mismatch under Confirm password, the server's sentence in a banner above the
 * buttons.
 */
export function ResetPasswordDialog({ user, onSaved, onClose }: ResetPasswordDialogProps) {
  const { t } = useTranslation(['users', 'auth', 'common'])
  const isSelf = useAuthStore((state) => state.user?.id === user.id)
  const reset = useResetUserPassword()
  const [password, setPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  // The passwords differed at the last submit: said under Confirm password until either is edited.
  const [mismatch, setMismatch] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const save = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setError(null)
    if (password !== confirmPassword) {
      setMismatch(true)
      return
    }
    try {
      await reset.mutateAsync({ id: user.id, password })
      onSaved()
      onClose()
    } catch (e: unknown) {
      setError(getApiErrorMessage(e, t('reset.failed')))
    }
  }

  return (
    <Dialog open onOpenChange={(open) => { if (!open) onClose() }}>
      <DialogContent className="settings-dialog">
        <DialogHeader>
          <DialogTitle>{t('reset.title', { name: user.userName })}</DialogTitle>
          <DialogDescription>
            {isSelf ? t('reset.descriptionSelf') : t('reset.description', { name: user.userName })}
          </DialogDescription>
        </DialogHeader>
        {/* noValidate: the rules live on the server, and its sentence is the one to show. */}
        <form className="settings-dialog-form" noValidate onSubmit={(event) => void save(event)}>
          <div className="settings-dialog-body">
            <Field label={t('reset.newPassword')}>
              {(control) => (
                <Input
                  {...control}
                  type="password"
                  value={password}
                  autoComplete="new-password"
                  autoFocus
                  onChange={(event) => {
                    setPassword(event.target.value)
                    setMismatch(false)
                  }}
                />
              )}
            </Field>
            <Field label={t('auth:confirmPassword')} error={mismatch ? t('auth:passwordsDiffer') : null}>
              {(control) => (
                <Input
                  {...control}
                  type="password"
                  value={confirmPassword}
                  autoComplete="new-password"
                  onChange={(event) => {
                    setConfirmPassword(event.target.value)
                    setMismatch(false)
                  }}
                />
              )}
            </Field>
          </div>
          {/* Outside the scrolling body, above the buttons; the fields keep what was typed. */}
          {error && <Banner>{error}</Banner>}
          {/* Primary first, Cancel last — the order every dialog in the app uses. */}
          <DialogFooter>
            <Button type="submit" disabled={reset.isPending}>
              {reset.isPending ? t('reset.submitting') : t('reset.submit')}
            </Button>
            <Button type="button" variant="outline" onClick={onClose}>{t('common:cancel')}</Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
