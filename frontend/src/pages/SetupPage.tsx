import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { Navigate } from 'react-router-dom'
import { Banner } from '@/components/shared/Banner'
import { ErrorState } from '@/components/shared/ErrorState'
import { Field } from '@/components/shared/Field'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { useAuthStore } from '@/lib/auth-store'
import { useCompleteSetup, useSetupStatus } from '@/lib/queries'
import { useLoadRetry } from '@/lib/use-load-retry'
import { getApiErrorMessage } from '@/lib/utils'
import '@/styles/auth.css' // .auth-* — the centred card and the form

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
  const setupLoad = useLoadRetry(setup)
  const complete = useCompleteSetup()
  const [userName, setUserName] = useState('')
  const [password, setPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  // MAM-16 — the passwords differed at the last submit: said under Confirm password until either
  // of them is edited.
  const [mismatch, setMismatch] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // The owner created here is signed in: on to the app.
  if (status === 'authenticated') return <Navigate to="/" replace />
  if (setup.data && !setup.data.isRequired) return <Navigate to="/login" replace />

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setError(null)
    if (password !== confirmPassword) {
      setMismatch(true)
      return
    }
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
          {setupLoad.error ? (
            <ErrorState title={t('setupCheckFailed')} onRetry={setupLoad.retry} retrying={setupLoad.retrying}>
              {getApiErrorMessage(setupLoad.error)}
            </ErrorState>
          ) : setup.isLoading ? (
            <p className="auth-muted">{t('common:loading')}</p>
          ) : (
            // noValidate: the rules live on the server, and its sentence is the one to show.
            <form className="auth-form" noValidate onSubmit={(event) => void submit(event)}>
              <Field label={t('userName')}>
                {(control) => (
                  <Input
                    {...control}
                    value={userName}
                    autoComplete="username"
                    autoCapitalize="none"
                    spellCheck={false}
                    autoFocus
                    onChange={(event) => setUserName(event.target.value)}
                  />
                )}
              </Field>
              <Field label={t('password')} hint={t('setup.passwordHint')}>
                {(control) => (
                  <Input
                    {...control}
                    type="password"
                    value={password}
                    autoComplete="new-password"
                    onChange={(event) => {
                      setPassword(event.target.value)
                      setMismatch(false)
                    }}
                  />
                )}
              </Field>
              <Field label={t('confirmPassword')} error={mismatch ? t('passwordsDiffer') : null}>
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
              {/* MAM-16 — what the server refused, above the button; the fields keep what was typed. */}
              {error && <Banner>{error}</Banner>}
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
