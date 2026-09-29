import { useEffect, useId, useRef, useState } from 'react'
import { ChevronDown, CircleUser, LogOut, Plug, Users } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import { Button } from '@/components/ui/button'
import { useSignOut } from '@/lib/queries'
import type { CurrentUser } from '@/lib/types'
import '@/styles/user-menu.css' // .user-menu* — the trigger and the popup's placement under it

/**
 * The signed-in user's menu in the header: Integrations and Users (the installation owner only) and
 * Sign out.
 *
 * A disclosure — a button that shows and hides a small panel — rather than an ARIA `menu`, which
 * would promise arrow-key navigation for a few entries that Tab already reaches. Escape, a click
 * anywhere outside and the focus moving out of it close it.
 */
export function UserMenu({ user }: { user: CurrentUser }) {
  const { t } = useTranslation('auth')
  const signOut = useSignOut()
  const [open, setOpen] = useState(false)
  const rootRef = useRef<HTMLDivElement>(null)
  const triggerRef = useRef<HTMLButtonElement>(null)
  const popupId = useId()

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
      <Button
        ref={triggerRef}
        type="button"
        variant="ghost"
        size="sm"
        className="user-menu-trigger"
        aria-label={t('menu.label', { name: user.userName })}
        title={t('menu.label', { name: user.userName })}
        aria-expanded={open}
        aria-controls={open ? popupId : undefined}
        onClick={() => setOpen((wasOpen) => !wasOpen)}
      >
        <CircleUser className="h-4 w-4" aria-hidden="true" />
        <span className="label-text">{user.userName}</span>
        <ChevronDown className="h-3.5 w-3.5" aria-hidden="true" />
      </Button>
      {open && (
        <div id={popupId} className="ui-menu user-menu-popup">
          {/* Owner-only: the pages would only answer every other user with a 403. */}
          {user.isOwner && (
            <>
              <Link to="/settings/integrations" className="ui-menu-item" onClick={() => setOpen(false)}>
                <Plug className="h-3.5 w-3.5" aria-hidden="true" />
                {t('menu.integrations')}
              </Link>
              <Link to="/settings/users" className="ui-menu-item" onClick={() => setOpen(false)}>
                <Users className="h-3.5 w-3.5" aria-hidden="true" />
                {t('menu.users')}
              </Link>
            </>
          )}
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
