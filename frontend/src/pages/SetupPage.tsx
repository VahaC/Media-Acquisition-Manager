import { useId, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { Navigate } from 'react-router-dom'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { useAuthStore } from '@/lib/auth-store'
import { useCompleteSetup, useSetupStatus } from '@/lib/queries'
import { getApiErrorMessage } from '@/lib/utils'
import '@/styles/auth.css' // .auth-* — the centred card, the form and its fields

/**
 * First-run setup: creates the installation owner, who is signed in at once. Only while no owner
 * exists; afterwards the page sends visitors to sign-in (and the API refuses a second setup).
 *
 * The one check made here is that the two passwords match: the rules themselves are the server's,
 * but a typo in the owner's password would lock the whole installation, with no email reset to
 * recover it.
 */
export function SetupPage() {
  const { t } = useTranslation(['auth', 'common'])
  const status = useAuthStore((state) => state.status)
  const setup = useSetupStatus()
  const complete = useCompleteSetup()
  const [userName, setUserName] = useState('')
  const [password, setPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const passwordHintId = useId()

  // The owner created here is signed in: on to the app.
  if (status === 'authenticated') return <Navigate to="/" replace />
  if (setup.data && !setup.data.isRequired) return <Navigate to="/login" replace />

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (password !== confirmPassword) {
      setError(t('passwordsDiffer'))
      return
    }
    setError(null)
    try {
      await complete.mutateAsync({ userName: userName.trim(), password })
    } catch (e: unknown) {
      setError(getApiErrorMessage(e, t('setup.failed')))
    }
  }

  return (
    <div className="auth-page">
      <section className="ui-card auth-card" aria-labelledby="setup-title">
        <div className="ui-card-header">
          <h1 id="setup-title" className="ui-card-title">{t('setup.title')}</h1>
          <p className="ui-card-description">{t('setup.description')}</p>
        </div>
        <div className="ui-card-content">
          {setup.isLoading ? (
            <p className="auth-muted">{t('common:loading')}</p>
          ) : setup.error && !setup.data ? (
            <div className="auth-status">
              <p className="auth-error" role="alert">{getApiErrorMessage(setup.error, t('setupCheckFailed'))}</p>
              <Button type="button" variant="outline" size="sm" onClick={() => void setup.refetch()}>
                {t('retry')}
              </Button>
            </div>
          ) : (
            // noValidate: the rules live on the server, and its sentence is the one to show.
            <form className="auth-form" noValidate onSubmit={(event) => void submit(event)}>
              <label className="auth-field">
                <span className="ui-label">{t('userName')}</span>
                <Input
                  value={userName}
                  autoComplete="username"
                  autoCapitalize="none"
                  spellCheck={false}
                  autoFocus
                  onChange={(event) => setUserName(event.target.value)}
                />
              </label>
              {/* The hint sits outside the label so it describes the field instead of joining its name. */}
              <div className="auth-field">
                <label className="auth-field">
                  <span className="ui-label">{t('password')}</span>
                  <Input
                    type="password"
                    value={password}
                    autoComplete="new-password"
                    aria-describedby={passwordHintId}
                    onChange={(event) => setPassword(event.target.value)}
                  />
                </label>
                <p id={passwordHintId} className="auth-hint">{t('setup.passwordHint')}</p>
              </div>
              <label className="auth-field">
                <span className="ui-label">{t('confirmPassword')}</span>
                <Input
                  type="password"
                  value={confirmPassword}
                  autoComplete="new-password"
                  onChange={(event) => setConfirmPassword(event.target.value)}
                />
              </label>
              {error && <p className="auth-error" role="alert">{error}</p>}
              <Button type="submit" className="w-full" disabled={complete.isPending}>
                {complete.isPending ? t('setup.submitting') : t('setup.submit')}
              </Button>
            </form>
          )}
        </div>
      </section>
    </div>
  )
}
