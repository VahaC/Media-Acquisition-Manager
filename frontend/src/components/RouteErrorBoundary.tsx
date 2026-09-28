import type { ReactNode } from 'react'
import { useLocation } from 'react-router-dom'
import { AppErrorBoundary } from '@/components/AppErrorBoundary'

interface RouteErrorBoundaryProps {
  children: ReactNode
}

/**
 * An {@link AppErrorBoundary} that forgets its error when the user navigates.
 *
 * A boundary that has caught stays caught: React keeps rendering the fallback
 * until something resets the state. Without this the first crash would pin the
 * fallback in place for the rest of the session, even after the user clicked
 * through to a page that works perfectly well.
 *
 * The pathname is the granularity we want: a new route deserves a fresh
 * attempt, a re-render of the same one does not. It is passed as `resetKey`,
 * not as `key`. This boundary wraps the whole router and every page, so
 * a `key` remounted everything on every navigation, failure or not: opening a
 * nested modal route rebuilt the page underneath it, and each tab of Settings,
 * Help or any tabbed section rebuilt the section, dropping searches, filters and
 * selected tabs.
 * Must be rendered inside a router, since it reads the current location.
 */
export function RouteErrorBoundary({ children }: RouteErrorBoundaryProps) {
  const { pathname } = useLocation()
  return <AppErrorBoundary resetKey={pathname}>{children}</AppErrorBoundary>
}
