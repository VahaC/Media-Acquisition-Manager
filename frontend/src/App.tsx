import { lazy } from 'react'
import { QueryClientProvider } from '@tanstack/react-query'
import { BrowserRouter, Route, Routes } from 'react-router-dom'
import { AppLayout } from '@/components/AppLayout'
import { RouteErrorBoundary } from '@/components/RouteErrorBoundary'
import { ConfirmProvider } from '@/components/shared/ConfirmDialog'
import { queryClient } from '@/lib/query-client'

// Every page is its own chunk, loaded on first visit. AppLayout holds the Suspense boundary, so
// the header stays on screen while a page's code arrives.
const HomePage = lazy(() => import('@/pages/HomePage').then((m) => ({ default: m.HomePage })))
const NotFoundPage = lazy(() => import('@/pages/NotFoundPage').then((m) => ({ default: m.NotFoundPage })))

/** The providers and the route table. */
export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <RouteErrorBoundary>
          <ConfirmProvider>
            <Routes>
              <Route element={<AppLayout />}>
                <Route index element={<HomePage />} />
                <Route path="*" element={<NotFoundPage />} />
              </Route>
            </Routes>
          </ConfirmProvider>
        </RouteErrorBoundary>
      </BrowserRouter>
    </QueryClientProvider>
  )
}
