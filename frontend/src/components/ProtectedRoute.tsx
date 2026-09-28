import { useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { Button } from '@/components/ui/button'
import { useAuthStore } from '@/lib/auth-store'
import '@/styles/app-layout.css' // .app-loading — the muted "Restoring your session…" line

/** MAM-4 — how often an unreachable server is asked again; a restarting container is back in seconds. */
const RETRY_INTERVAL_MS = 3_000

/**
 * The gate in front of every page that needs a signed-in user.
 *
 * A fresh tab starts `initializing`: the access token lives in memory only, so the gate asks the
 * server whether the refresh cookie still holds a session. Without a session it sends the visitor to
 * the sign-in page, remembering where they were going (not after a deliberate sign-out). While the
 * server can't be reached it keeps asking every few seconds rather than guessing either way —
 * signing the user out would lose the page, and showing it would only fill it with errors.
 */
export function ProtectedRoute() {
  const { t } = useTranslation('auth')
  const status = useAuthStore((state) => state.status)
  const userId = useAuthStore((state) => state.user?.id)
  const deliberateSignOut = useAuthStore((state) => state.deliberateSignOut)
  const initializeSession = useAuthStore((state) => state.initializeSession)
  const location = useLocation()

  useEffect(() => {
    if (status === 'initializing') void initializeSession()
  }, [status, initializeSession])

  useEffect(() => {
    if (status !== 'unavailable') return
    const timer = setInterval(() => void initializeSession(), RETRY_INTERVAL_MS)
    return () => clearInterval(timer)
  }, [status, initializeSession])

  switch (status) {
    case 'initializing':
      return <p className="app-loading">{t('restoring')}</p>
    case 'unavailable':
      return (
        <div className="auth-reconnect" role="alert">
          <p>{t('unavailable')}</p>
          <Button type="button" variant="outline" size="sm" onClick={() => void initializeSession()}>
            {t('retry')}
          </Button>
        </div>
      )
    case 'anonymous':
      // MAM-4 — after a sign-out someone asked for, the page they were on is not where the next
      // person at this device is going: it was the last user's (and may be owner-only). An expired
      // or revoked session still comes back to it after signing in again.
      return (
        <Navigate
          to="/login"
          replace
          state={deliberateSignOut ? undefined : { from: `${location.pathname}${location.search}${location.hash}` }}
        />
      )
    case 'authenticated':
      // Keyed by the user, so a different account in this tab mounts every page afresh instead of
      // keeping the last user's filters, dialogs and half-typed forms.
      return <Outlet key={userId} />
  }
}
