repo: VahaC/Media-Acquisition-Manager
branch: main
path: frontend

## Last sync
date: 2026-10-02T13:20:53.062Z

### Updated in this project
- Approved layout: header with search, numbered pager, versions as rows or table
- Users and new Profile pages; error states for failed requests
- Jobs added to the header (MAM-14); implementation brief for Claude Code

## Screen map
| Screen | Repo files |
| --- | --- |
| ui_kits/current Shell (header, account menu) | frontend/src/components/AppLayout.tsx, HeaderSearch.tsx, UserMenu.tsx, lib/nav-items.ts, lib/health-status.ts, styles/app-layout.css, styles/user-menu.css |
| ui_kits/current Search | frontend/src/pages/SearchPage.tsx, components/search/*, lib/search-query.ts, styles/search.css |
| ui_kits/current Library + versions view | frontend/src/pages/LibraryPage.tsx, components/library/* (VersionRows.tsx, VersionTable.tsx), lib/use-versions-view.ts, styles/library.css |
| ui_kits/current Users | frontend/src/pages/settings/UsersPage.tsx, components/users/*, lib/users-api.ts |
| ui_kits/current Profile | frontend/src/pages/ProfilePage.tsx, components/profile/ChangePasswordCard.tsx, styles/profile.css, lib/auth-api.ts, lib/types/auth.ts |
| ui_kits/current Integrations | frontend/src/pages/settings/IntegrationsPage.tsx, styles/settings-list.css |
| ui_kits/current Destinations | frontend/src/pages/settings/DestinationsPage.tsx |
| components/core/ListPager | frontend/src/components/shared/ListPager.tsx, lib/page-numbers.ts, styles/list-pager.css |
| components/core/ThemeSwitch | frontend/src/components/ThemeSwitch.tsx, components/shared/SegmentedControl.tsx, lib/theme-store.ts, styles/segmented-control.css |
| components/core/Banner, ErrorState, Input (Field) | frontend/src/components/shared/Banner.tsx, ErrorState.tsx, Field.tsx, FieldErrorLine.tsx, RetryButton.tsx, styles/ui-primitives.css |
| Error states | frontend/src/components/AppErrorFallback.tsx, pages/NotFoundPage.tsx, components/OfflineStrip.tsx, components/shared/Banner.tsx, ErrorState.tsx, lib/use-load-retry.ts, lib/connection-store.ts, locales/en/errors.json |
| ui_kits/proposal/* (not adopted) | same sources, re-laid out |

## Sync history
- 2026-10-01T12:00:00Z — first build: tokens, components, current-layout kit, layout proposal
- 2026-10-02 — not a design sync: the app adopted the approved layout (MAM-16, release 0.1.0), and the screen map names the files that implement it
