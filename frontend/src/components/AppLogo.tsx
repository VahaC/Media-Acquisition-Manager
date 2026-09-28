import { cn } from '@/lib/utils'
import '@/styles/app-layout.css' // .app-logo* — the tile and mark colours

/**
 * The app's logo: a clapperboard (the media) with a download arrow (acquiring it) on an indigo
 * tile. `public/favicon.svg` is the same drawing with fixed colours for the browser tab; here it
 * is inline so the tile takes the theme's `--primary`, a lighter indigo in dark mode, like every
 * other accent. AppLogo.test.tsx fails if the two drawings drift apart.
 *
 * Decorative: it always sits next to the app name, which is what a screen reader should read.
 */
export function AppLogo({ className }: { className?: string }) {
  return (
    <svg className={cn('app-logo', className)} viewBox="0 0 32 32" aria-hidden="true" focusable="false">
      <rect className="app-logo-tile" width="32" height="32" rx="7" />
      <g
        className="app-logo-mark"
        transform="translate(4 4.5)"
        fill="none"
        strokeWidth="2.2"
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <path d="M20.2 6 3 11l-.9-2.4c-.3-1.1.3-2.2 1.3-2.5l13.5-4c1.1-.3 2.2.3 2.5 1.3Z" />
        <path d="m6.2 5.3 3.1 3.9" />
        <path d="m12.4 3.4 3.1 4" />
        <path d="M3 11h18v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2Z" />
        <path d="M12 13.5v4.5" />
        <path d="m9.75 16 2.25 2.25L14.25 16" />
      </g>
    </svg>
  )
}
