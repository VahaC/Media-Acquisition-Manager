import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { useCreateUser } from '@/lib/queries'
import { getApiErrorMessage } from '@/lib/utils'
import '@/styles/auth.css' // .auth-field — the same credential fields as the sign-in and setup forms
import '@/styles/settings-dialog.css' // .settings-dialog* — the scrolling body, the error line

/**
 * Adds a user who signs in with a user name and password. The parent mounts it only while it is
 * open, so every opening starts with empty fields.
 *
 * The passwords must match before anything is sent — the owner would otherwise hand out a password
 * nobody knows — and every other rule is the server's, shown as its sentence.
 */
export function CreateUserDialog({ onClose }: { onClose: () => void }) {
  const { t } = useTranslation(['users', 'auth', 'common'])
  const create = useCreateUser()
  const [userName, setUserName] = useState('')
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
      await create.mutateAsync({ userName: userName.trim(), password })
      onClose()
    } catch (e: unknown) {
      setError(getApiErrorMessage(e, t('create.failed')))
    }
  }

  return (
    <Dialog open onOpenChange={(open) => { if (!open) onClose() }}>
      <DialogContent className="settings-dialog">
        <DialogHeader>
          <DialogTitle>{t('create.title')}</DialogTitle>
          <DialogDescription>{t('create.description')}</DialogDescription>
        </DialogHeader>
        {/* noValidate: the rules live on the server, and its sentence is the one to show. */}
        <form className="settings-dialog-form" noValidate onSubmit={(event) => void save(event)}>
          <div className="settings-dialog-body">
            <label className="auth-field">
              <span className="ui-label">{t('auth:userName')}</span>
              <Input
                value={userName}
                autoComplete="off"
                autoCapitalize="none"
                spellCheck={false}
                autoFocus
                onChange={(event) => setUserName(event.target.value)}
              />
            </label>
            <label className="auth-field">
              <span className="ui-label">{t('auth:password')}</span>
              <Input
                type="password"
                value={password}
                autoComplete="new-password"
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
            <Button type="submit" disabled={create.isPending}>
              {create.isPending ? t('create.submitting') : t('create.submit')}
            </Button>
            <Button type="button" variant="outline" onClick={onClose}>{t('common:cancel')}</Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
