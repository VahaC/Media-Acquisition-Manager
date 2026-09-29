import { lazy } from 'react'
import { QueryClientProvider } from '@tanstack/react-query'
import { BrowserRouter, Route, Routes } from 'react-router-dom'
import { AppLayout } from '@/components/AppLayout'
import { ProtectedRoute } from '@/components/ProtectedRoute'
import { RouteErrorBoundary } from '@/components/RouteErrorBoundary'
import { ConfirmProvider } from '@/components/shared/ConfirmDialog'
import { queryClient } from '@/lib/query-client'

// Every page is its own chunk, loaded on first visit. AppLayout holds the Suspense boundary, so
// the header stays on screen while a page's code arrives.
const HomePage = lazy(() => import('@/pages/HomePage').then((m) => ({ default: m.HomePage })))
const IntegrationsPage = lazy(() =>
  import('@/pages/settings/IntegrationsPage').then((m) => ({ default: m.IntegrationsPage })))
const LoginPage = lazy(() => import('@/pages/LoginPage').then((m) => ({ default: m.LoginPage })))
const NotFoundPage = lazy(() => import('@/pages/NotFoundPage').then((m) => ({ default: m.NotFoundPage })))
const SetupPage = lazy(() => import('@/pages/SetupPage').then((m) => ({ default: m.SetupPage })))
const UsersPage = lazy(() => import('@/pages/settings/UsersPage').then((m) => ({ default: m.UsersPage })))

/** The providers and the route table. */
export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <RouteErrorBoundary>
          <ConfirmProvider>
            <Routes>
              <Route element={<AppLayout />}>
                <Route path="setup" element={<SetupPage />} />
                <Route path="login" element={<LoginPage />} />
                {/* MAM-4 — every other address needs a signed-in user. ProtectedRoute sends a
                    visitor without one to /login, and back to the address once signed in. */}
                <Route element={<ProtectedRoute />}>
                  <Route index element={<HomePage />} />
                  <Route path="settings/integrations" element={<IntegrationsPage />} />
                  <Route path="settings/users" element={<UsersPage />} />
                  <Route path="*" element={<NotFoundPage />} />
                </Route>
              </Route>
            </Routes>
          </ConfirmProvider>
        </RouteErrorBoundary>
      </BrowserRouter>
    </QueryClientProvider>
  )
}
