import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { useAuthStore } from '@/lib/auth-store'
import { useResetUserPassword } from '@/lib/queries'
import type { UserSummary } from '@/lib/types'
import { getApiErrorMessage } from '@/lib/utils'
import '@/styles/auth.css' // .auth-field — the same credential fields as the sign-in and setup forms
import '@/styles/settings-dialog.css' // .settings-dialog* — the scrolling body, the error line

/**
 * Sets a new password for a user, the owner's own included — there is no other way to change a
 * password yet. The server ends every session of that user except the one this request comes from,
 * which the description says, since it differs for the owner's own row.
 */
export function ResetPasswordDialog({ user, onClose }: { user: UserSummary; onClose: () => void }) {
  const { t } = useTranslation(['users', 'auth', 'common'])
  const isSelf = useAuthStore((state) => state.user?.id === user.id)
  const reset = useResetUserPassword()
  const [password, setPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [error, setError] = useState<string | null>(null)

  const save = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (password !== confirmPassword) {
      setError(t('auth:passwordsDiffer'))
      return
    }
    setError(null)
    try {
      await reset.mutateAsync({ id: user.id, password })
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
            <label className="auth-field">
              <span className="ui-label">{t('reset.newPassword')}</span>
              <Input
                type="password"
                value={password}
                autoComplete="new-password"
                autoFocus
                onChange={(event) => setPassword(event.target.value)}
              />
            </label>
            <label className="auth-field">
              <span className="ui-label">{t('auth:confirmPassword')}</span>
              <Input
                type="password"
                value={confirmPassword}
                autoComplete="new-password"
                onChange={(event) => setConfirmPassword(event.target.value)}
              />
            </label>
          </div>
          {error && <p className="settings-dialog-error" role="alert">{error}</p>}
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
