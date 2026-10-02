import { Suspense } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, NavLink, Outlet } from 'react-router-dom'
import { AppLogo } from '@/components/AppLogo'
import { HeaderSearch } from '@/components/HeaderSearch'
import { OfflineStrip } from '@/components/OfflineStrip'
import { RouteErrorBoundary } from '@/components/RouteErrorBoundary'
import { UserMenu } from '@/components/UserMenu'
import { useAuthStore } from '@/lib/auth-store'
import { navItemsFor } from '@/lib/nav-items'
import '@/styles/app-layout.css' // .app-* — the header, its nav links and the content column around every page

/**
 * The shell every page renders in: a header that stays at the top of the window, with the app name
 * and the running version and — once signed in — the search field, the header links of the shared
 * nav list (MAM-8: Library, then MAM-14: Jobs) and the user menu, and the page itself in `<main>`.
 * Pages are lazy, so the Suspense boundary sits here, around the outlet only — the header stays put
 * while a page's code loads. MAM-16 — while the server can't be reached, a strip under the header
 * says so, and stays at the top with it.
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
      {/* MAM-16 — one sticky block, so the strip stays directly under the header whatever height
          the header takes (a second row on a phone). */}
      <div className="app-top">
        <header className="app-header">
          <Link to="/" className="app-brand" title={t('versionTitle')}>
            <AppLogo />
            {/* MAM-16 — the version under the name. Blocks rather than spans, so the link is named
                "Media Acquisition Manager v0.1.0" with the space a screen reader needs between them. */}
            <div className="app-brand-text">
              <div className="app-brand-name">{t('appName')}</div>
              <div className="app-brand-version">{t('version', { version: __APP_VERSION__ })}</div>
            </div>
          </Link>
          {user && (
            <>
              <div className="app-header-center">
                <HeaderSearch />
                <nav className="app-nav" aria-label={t('nav.label')}>
                  {/* MAM-16 — the field stands in for MAM-12's Search link, which the list keeps as
                      the page's entry. */}
                  {navItemsFor('header', user).filter(({ path }) => path !== '/search').map(({ path, labelKey, icon: Icon }) => (
                    // The title names the link once a narrow screen hides its label.
                    <NavLink key={path} to={path} className="app-nav-link" title={t(labelKey)}>
                      <Icon className="h-4 w-4" aria-hidden="true" />
                      <span className="label-text">{t(labelKey)}</span>
                    </NavLink>
                  ))}
                </nav>
              </div>
              <div className="app-header-end">
                <UserMenu user={user} />
              </div>
            </>
          )}
        </header>
        <OfflineStrip />
      </div>
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
