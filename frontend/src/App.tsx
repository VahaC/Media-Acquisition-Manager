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
const DestinationsPage = lazy(() =>
  import('@/pages/settings/DestinationsPage').then((m) => ({ default: m.DestinationsPage })))
const HomePage = lazy(() => import('@/pages/HomePage').then((m) => ({ default: m.HomePage })))
const IntegrationsPage = lazy(() =>
  import('@/pages/settings/IntegrationsPage').then((m) => ({ default: m.IntegrationsPage })))
const JobDetailsPage = lazy(() => import('@/pages/JobDetailsPage').then((m) => ({ default: m.JobDetailsPage })))
const JobsPage = lazy(() => import('@/pages/JobsPage').then((m) => ({ default: m.JobsPage })))
const LibraryPage = lazy(() => import('@/pages/LibraryPage').then((m) => ({ default: m.LibraryPage })))
const LibraryTmdbPage = lazy(() => import('@/pages/LibraryTmdbPage').then((m) => ({ default: m.LibraryTmdbPage })))
const LoginPage = lazy(() => import('@/pages/LoginPage').then((m) => ({ default: m.LoginPage })))
const MediaFoldersPage = lazy(() =>
  import('@/pages/settings/MediaFoldersPage').then((m) => ({ default: m.MediaFoldersPage })))
const NotFoundPage = lazy(() => import('@/pages/NotFoundPage').then((m) => ({ default: m.NotFoundPage })))
const ProfilePage = lazy(() => import('@/pages/ProfilePage').then((m) => ({ default: m.ProfilePage })))
const RecommendationsPage = lazy(() =>
  import('@/pages/RecommendationsPage').then((m) => ({ default: m.RecommendationsPage })))
const SearchPage = lazy(() => import('@/pages/SearchPage').then((m) => ({ default: m.SearchPage })))
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
                  <Route path="search" element={<SearchPage />} />
                  <Route path="library" element={<LibraryPage />} />
                  {/* MAM-34 — the owner's TMDb links of the works without an id, reached from the
                      Library's "Missing an id" filter; the header keeps Library highlighted. */}
                  <Route path="library/tmdb" element={<LibraryTmdbPage />} />
                  {/* MAM-33 — every signed-in user's own lists of works the library doesn't have yet,
                      the tabs they switched on in their Profile; the tab is in `?tab=`. */}
                  <Route path="recommendations" element={<RecommendationsPage />} />
                  {/* MAM-14 — every signed-in user's own jobs; another user's job reads like no job. */}
                  <Route path="jobs" element={<JobsPage />} />
                  <Route path="jobs/:id" element={<JobDetailsPage />} />
                  {/* MAM-16 — every signed-in user's own account: their password, theme and session. */}
                  <Route path="profile" element={<ProfilePage />} />
                  <Route path="settings/integrations" element={<IntegrationsPage />} />
                  <Route path="settings/destinations" element={<DestinationsPage />} />
                  <Route path="settings/media-folders" element={<MediaFoldersPage />} />
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
