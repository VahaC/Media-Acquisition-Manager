import { Suspense } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, NavLink, Outlet } from 'react-router-dom'
import { AppLogo } from '@/components/AppLogo'
import { RouteErrorBoundary } from '@/components/RouteErrorBoundary'
import { UserMenu } from '@/components/UserMenu'
import { useAuthStore } from '@/lib/auth-store'
import { navItemsFor } from '@/lib/nav-items'
import '@/styles/app-layout.css' // .app-* — the header, its nav links and the content column around every page

/**
 * The shell every page renders in: a header with the app name, the running version and — once
 * signed in — the header links of the shared nav list (MAM-8: Library) and the user menu, and the
 * page itself in `<main>`. Pages are lazy, so the Suspense boundary sits here, around the outlet
 * only — the header stays put while a page's code loads.
 *
 * A page that crashes is caught here too, so its fallback fills `<main>` and the header (the way
 * back home) stays on screen. The boundary around the whole router in App.tsx is only for a
 * failure in this shell itself.
 */
export function AppLayout() {
  const { t } = useTranslation()
  const user = useAuthStore((state) => state.user)

  return (
    <div className="app-shell">
      <header className="app-header">
        <Link to="/" className="app-brand">
          <AppLogo />
          <span>{t('appName')}</span>
        </Link>
        {user && (
          <nav className="app-nav" aria-label={t('nav.label')}>
            {navItemsFor('header', user).map(({ path, labelKey, icon: Icon }) => (
              <NavLink key={path} to={path} className="app-nav-link">
                <Icon className="h-4 w-4" aria-hidden="true" />
                {t(labelKey)}
              </NavLink>
            ))}
          </nav>
        )}
        <div className="app-header-end">
          <span className="app-version" title={t('versionTitle')}>
            {t('version', { version: __APP_VERSION__ })}
          </span>
          {user && <UserMenu user={user} />}
        </div>
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
