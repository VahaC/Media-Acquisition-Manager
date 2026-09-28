import { useEffect, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { Navigate, useLocation } from 'react-router-dom'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { useAuthStore } from '@/lib/auth-store'
import { useSetupStatus, useSignIn } from '@/lib/queries'
import { getApiErrorMessage } from '@/lib/utils'
import '@/styles/auth.css' // .auth-* — the centred card, the form and its fields

/** MAM-4 — the pace ProtectedRoute asks an unreachable server at; a restarting container is back in seconds. */
const RETRY_INTERVAL_MS = 3_000

/**
 * Sign-in with a user name and password. A signed-in visitor goes on to the page they were headed
 * for (ProtectedRoute leaves it in the location state); before any owner exists, first-run setup
 * takes the place of this page.
 *
 * Every refusal shows the API's own sentence: the one identical 401 for any wrong detail, the 429
 * of the rate limit.
 */
export function LoginPage() {
  const { t } = useTranslation(['auth', 'common'])
  const status = useAuthStore((state) => state.status)
  const initializeSession = useAuthStore((state) => state.initializeSession)
  const setup = useSetupStatus()
  const signIn = useSignIn()
  const location = useLocation()
  const [userName, setUserName] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)

  // A tab opened straight on /login may still hold a session in its cookie; if so, it goes on.
  useEffect(() => {
    if (status === 'initializing') void initializeSession()
  }, [status, initializeSession])

  // MAM-4 — while the server can't be reached, whether the cookie holds a session is still unknown.
  // Asked again like ProtectedRoute does, a live session comes back by itself once the server does,
  // instead of the user signing in a second time and leaving the first session orphaned.
  useEffect(() => {
    if (status !== 'unavailable') return
    const timer = setInterval(() => void initializeSession(), RETRY_INTERVAL_MS)
    return () => clearInterval(timer)
  }, [status, initializeSession])

  if (status === 'authenticated') return <Navigate to={destinationFrom(location.state)} replace />
  if (setup.data?.isRequired) return <Navigate to="/setup" replace />

  const retry = () => {
    void setup.refetch()
    // MAM-4 — the session is as unknown as the setup answer then; ask about both.
    if (status === 'unavailable') void initializeSession()
  }

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setError(null)
    try {
      // Signed in, the store turns `authenticated` and the redirect above takes over.
      await signIn.mutateAsync({ userName: userName.trim(), password })
    } catch (e: unknown) {
      setError(getApiErrorMessage(e, t('login.failed')))
    }
  }

  return (
    <div className="auth-page">
      <section className="ui-card auth-card" aria-labelledby="login-title">
        <div className="ui-card-header">
          <h1 id="login-title" className="ui-card-title">{t('login.title')}</h1>
          <p className="ui-card-description">{t('login.description')}</p>
        </div>
        <div className="ui-card-content">
          {status === 'initializing' || setup.isLoading ? (
            <p className="auth-muted">{t('common:loading')}</p>
          ) : setup.error && !setup.data ? (
            <div className="auth-status">
              <p className="auth-error" role="alert">{getApiErrorMessage(setup.error, t('setupCheckFailed'))}</p>
              <Button type="button" variant="outline" size="sm" onClick={retry}>
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
              <label className="auth-field">
                <span className="ui-label">{t('password')}</span>
                <Input
                  type="password"
                  value={password}
                  autoComplete="current-password"
                  onChange={(event) => setPassword(event.target.value)}
                />
              </label>
              {error && <p className="auth-error" role="alert">{error}</p>}
              <Button type="submit" className="w-full" disabled={signIn.isPending}>
                {signIn.isPending ? t('login.submitting') : t('login.submit')}
              </Button>
            </form>
          )}
        </div>
      </section>
    </div>
  )
}

/**
 * Where to go once signed in: the in-app path ProtectedRoute remembered, else home. Anything that
 * isn't a same-origin path (`//host` is another origin) is ignored rather than followed.
 */
function destinationFrom(state: unknown): string {
  const from = typeof state === 'object' && state !== null && 'from' in state ? state.from : undefined
  return typeof from === 'string' && from.startsWith('/') && !from.startsWith('//') ? from : '/'
}
