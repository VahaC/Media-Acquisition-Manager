import { Suspense } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, Outlet } from 'react-router-dom'
import { AppLogo } from '@/components/AppLogo'
import { RouteErrorBoundary } from '@/components/RouteErrorBoundary'
import '@/styles/app-layout.css' // .app-* — the header and content column around every page

/**
 * The shell every page renders in: a header with the app name and the running version, and the
 * page itself in `<main>`. Pages are lazy, so the Suspense boundary sits here, around the outlet
 * only — the header stays put while a page's code loads.
 *
 * A page that crashes is caught here too, so its fallback fills `<main>` and the header (the way
 * back home) stays on screen. The boundary around the whole router in App.tsx is only for a
 * failure in this shell itself.
 */
export function AppLayout() {
  const { t } = useTranslation()

  return (
    <div className="app-shell">
      <header className="app-header">
        <Link to="/" className="app-brand">
          <AppLogo />
          <span>{t('appName')}</span>
        </Link>
        <span className="app-version" title={t('versionTitle')}>
          {t('version', { version: __APP_VERSION__ })}
        </span>
      </header>
      <main className="app-main">
        <Suspense fallback={<p className="app-loading">{t('loading')}</p>}>
          <RouteErrorBoundary>
            <Outlet />
          </RouteErrorBoundary>
        </Suspense>
      </main>
    </div>
  )
}
