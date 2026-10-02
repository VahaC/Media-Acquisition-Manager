import { useEffect, useId, useRef, useState } from 'react'
import { ChevronDown, LogOut } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Link, NavLink } from 'react-router-dom'
import { ThemeSwitch } from '@/components/ThemeSwitch'
import { HEALTH_STATUS_TEXT } from '@/lib/health-status'
import { navItemsFor, type NavItem } from '@/lib/nav-items'
import { useReadiness, useSignOut } from '@/lib/queries'
import type { CurrentUser } from '@/lib/types'
import '@/styles/user-menu.css' // .user-menu* — the trigger, the popup's placement under it and its rows

/**
 * The signed-in user's menu in the header: who is signed in (their role, the version running and
 * whether the server is ready) and their Profile, Integrations, Destinations, Media folders and Users
 * (the installation owner only), the theme, and Sign out.
 *
 * A disclosure — a button that shows and hides a small panel — rather than an ARIA `menu`, which
 * would promise arrow-key navigation for a few entries that Tab already reaches. Escape, a click
 * anywhere outside and the focus moving out of it close it.
 */
export function UserMenu({ user }: { user: CurrentUser }) {
  const { t } = useTranslation(['auth', 'common'])
  const signOut = useSignOut()
  const [open, setOpen] = useState(false)
  const rootRef = useRef<HTMLDivElement>(null)
  const triggerRef = useRef<HTMLButtonElement>(null)
  const popupId = useId()
  const entries = navItemsFor('userMenu', user)
  // MAM-16 — the user's own pages (Profile) sit under who is signed in; the owner's settings pages
  // follow in a group of their own.
  const ownEntries = entries.filter((entry) => !entry.ownerOnly)
  const settingsEntries = entries.filter((entry) => entry.ownerOnly)

  useEffect(() => {
    if (!open) return
    const closeOnOutsidePointer = (event: PointerEvent) => {
      if (!rootRef.current?.contains(event.target as Node)) setOpen(false)
    }
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key !== 'Escape') return
      setOpen(false)
      triggerRef.current?.focus()
    }
    // MAM-4 — tabbing past the last entry moves on into the page, where the open popup would cover
    // the next focus stop (Add user, on the Users page). A document `focusin` rather than onBlur:
    // a browser that doesn't focus a clicked button (Safari) blurs to nothing, and onBlur would
    // then close the popup on mousedown and swallow the click on Sign out or Users.
    const closeOnFocusOutside = (event: FocusEvent) => {
      if (!rootRef.current?.contains(event.target as Node)) setOpen(false)
    }
    document.addEventListener('pointerdown', closeOnOutsidePointer)
    document.addEventListener('keydown', closeOnEscape)
    document.addEventListener('focusin', closeOnFocusOutside)
    return () => {
      document.removeEventListener('pointerdown', closeOnOutsidePointer)
      document.removeEventListener('keydown', closeOnEscape)
      document.removeEventListener('focusin', closeOnFocusOutside)
    }
  }, [open])

  const entryLink = ({ path, labelKey, icon: Icon }: NavItem) => (
    <Link key={path} to={path} className="ui-menu-item" onClick={() => setOpen(false)}>
      <Icon className="h-3.5 w-3.5" aria-hidden="true" />
      {t(labelKey, { ns: 'common' })}
    </Link>
  )

  const signOutNow = async () => {
    setOpen(false)
    try {
      await signOut.mutateAsync()
    } catch {
      /* useSignOut has signed this tab out anyway; there is nothing the user could do about it */
    }
  }

  return (
    <div className="user-menu" ref={rootRef}>
      {/* MAM-16 — a pill with the user's initial; a plain button rather than a ghost Button, whose
          fixed height and padding the pill would have to undo. */}
      <button
        ref={triggerRef}
        type="button"
        className="user-menu-trigger"
        aria-label={t('menu.label', { name: user.userName })}
        title={t('menu.label', { name: user.userName })}
        aria-expanded={open}
        aria-controls={open ? popupId : undefined}
        onClick={() => setOpen((wasOpen) => !wasOpen)}
      >
        <span className="user-menu-avatar" aria-hidden="true">{user.userName.charAt(0).toUpperCase()}</span>
        <span className="label-text">{user.userName}</span>
        <ChevronDown className="h-3.5 w-3.5" aria-hidden="true" />
      </button>
      {open && (
        <div id={popupId} className="ui-menu user-menu-popup">
          {/* MAM-16 — who is signed in, as a link to their Profile, tinted while it is open. Two
              lines that wrap, unlike an entry: the popup is 240px wide and a user name runs to 32
              characters. */}
          <NavLink to="/profile" className="ui-menu-item user-menu-identity" onClick={() => setOpen(false)}>
            <strong>{user.userName}</strong>
            <span className="user-menu-facts">
              {t(user.isOwner ? 'menu.role.owner' : 'menu.role.member')}
              {t('separator', { ns: 'common' })}
              <span className="user-menu-version">{t('version', { ns: 'common', version: __APP_VERSION__ })}</span>
              <ReadinessFact />
            </span>
          </NavLink>
          {ownEntries.map(entryLink)}
          {/* MAM-8 — the menu's entries of the shared nav list; the settings pages (Integrations,
              Destinations, Media folders, Users) are owner-only there, since the pages would only
              answer every other user with a 403. A user without any gets no separator either. */}
          {settingsEntries.length > 0 && <div className="ui-menu-sep" role="separator" />}
          {settingsEntries.map(entryLink)}
          <div className="ui-menu-sep" role="separator" />
          {/* Picking a theme leaves the menu open, so the other themes can be tried in turn. */}
          <div className="user-menu-theme">
            <span>{t('theme.label', { ns: 'common' })}</span>
            <ThemeSwitch />
          </div>
          <div className="ui-menu-sep" role="separator" />
          <button
            type="button"
            className="ui-menu-item"
            disabled={signOut.isPending}
            onClick={() => void signOutNow()}
          >
            <LogOut className="h-3.5 w-3.5" aria-hidden="true" />
            {t('menu.signOut')}
          </button>
        </div>
      )}
    </div>
  )
}

// ── readiness ──

/**
 * " · Ready" after the version, with a dot in the status colour: the readiness report the home page
 * shows, in its words. It mounts with the open popup only, so a closed menu asks the server nothing;
 * opening it shows the last answer at once and asks again. Until there is an answer, and when the
 * last request failed, it shows nothing rather than a status that may no longer hold.
 */
function ReadinessFact() {
  const { t } = useTranslation(['home', 'common'])
  const { data, error } = useReadiness()

  if (!data || error) return null

  return (
    <>
      {t('separator', { ns: 'common' })}
      <span className="user-menu-status" data-status={data.status}>
        <span className="user-menu-status-dot" aria-hidden="true" />
        {t(HEALTH_STATUS_TEXT[data.status])}
      </span>
    </>
  )
}
