import { useState } from 'react'
import { LogOut } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { ThemeSwitch } from '@/components/ThemeSwitch'
import { AccessTokensCard } from '@/components/profile/AccessTokensCard'
import { ChangePasswordCard } from '@/components/profile/ChangePasswordCard'
import { LanguageCard } from '@/components/profile/LanguageCard'
import { RecommendationSettingsCard } from '@/components/profile/RecommendationSettingsCard'
import { Banner } from '@/components/shared/Banner'
import { ErrorState } from '@/components/shared/ErrorState'
import { RetryButton } from '@/components/shared/RetryButton'
import { Button } from '@/components/ui/button'
import { useAuthStore } from '@/lib/auth-store'
import { formatDateTime } from '@/lib/format'
import { useProfile, useSignOut } from '@/lib/queries'
import { useLoadRetry } from '@/lib/use-load-retry'
import { getApiErrorMessage } from '@/lib/utils'
import '@/styles/profile.css' // .profile-* — the head, the account rows and the cards' inner layout

/** Date and time without the seconds, as the Users page shows a sign-in. */
const INSTANT_FORMAT: Intl.DateTimeFormatOptions = { dateStyle: 'medium', timeStyle: 'short' }

/**
 * MAM-16 — every signed-in user's own page (account menu → Profile): who they are, their password,
 * the theme of this browser and this session, with Sign out.
 *
 * The head comes from the signed-in user the app already holds, so it shows at once; the account's
 * dates and the session's end come from `GET /api/auth/me`. A failed first load of those shows in
 * the Account card, and the Session card leaves its sentence out until there is a date to put in it.
 *
 * MAM-33 — and their Recommendations: the lists they want and their daily search budget, a card with
 * its own Save (RecommendationSettingsCard).
 *
 * MAM-5 — and their personal access tokens, for scripts and AI assistants, right after the password:
 * both are how the account is reached (AccessTokensCard).
 *
 * MAM-46 — and the language the app is shown in, after the theme: saved with the account rather than
 * in this browser, so a card of its own (LanguageCard), and Appearance's "Saved in this browser." stays
 * true of everything in it.
 */
export function ProfilePage() {
  const { t } = useTranslation(['profile', 'auth', 'common'])
  const user = useAuthStore((state) => state.user)
  const profile = useProfile()
  const { data, error, isLoading } = profile
  const load = useLoadRetry(profile)
  const signOut = useSignOut()
  const [passwordChanged, setPasswordChanged] = useState(false)

  // ProtectedRoute renders this page only with a signed-in user; the store's type allows none.
  if (!user) return null

  const signOutNow = async () => {
    try {
      await signOut.mutateAsync()
    } catch {
      /* useSignOut has signed this tab out anyway; there is nothing the user could do about it */
    }
  }

  return (
    <div className="profile-page">
      <div className="profile-head">
        <span className="profile-avatar" aria-hidden="true">{user.userName.charAt(0).toUpperCase()}</span>
        <div className="profile-intro">
          <h1 className="profile-name">
            {user.userName}
            {user.isOwner && <span className="ui-badge ui-badge-default">{t('auth:menu.role.owner')}</span>}
          </h1>
          <p className="profile-lead">{t(user.isOwner ? 'lead.owner' : 'lead.member')}</p>
        </div>
      </div>

      {passwordChanged && (
        <Banner tone="ok" onDismiss={() => setPasswordChanged(false)}>{t('password.changed')}</Banner>
      )}
      {/* Outside the Account card's ternary, so a failed refresh adds a line above the rows instead
          of replacing them. */}
      {error && data && (
        <Banner action={<RetryButton onRetry={() => profile.refetch()} />}>
          {t('refreshFailed')}{' '}
          {getApiErrorMessage(error)}
        </Banner>
      )}

      <section className="ui-card" aria-labelledby="profile-account-title">
        <div className="ui-card-header">
          <h2 id="profile-account-title" className="ui-card-title">{t('account.title')}</h2>
        </div>
        <div className="ui-card-content">
          {load.error ? (
            <ErrorState title={t('loadFailed')} onRetry={load.retry} retrying={load.retrying}>
              {getApiErrorMessage(load.error)}
            </ErrorState>
          ) : isLoading ? (
            <p className="profile-muted">{t('common:loading')}</p>
          ) : data ? (
            <dl>
              <div className="profile-row">
                <dt>{t('account.userName')}</dt>
                <dd className="profile-mono">{data.userName}</dd>
              </div>
              <div className="profile-row">
                <dt>{t('account.role')}</dt>
                <dd>{t(data.isOwner ? 'auth:menu.role.owner' : 'auth:menu.role.member')}</dd>
              </div>
              <div className="profile-row">
                <dt>{t('account.created')}</dt>
                <dd>{formatDateTime(data.createdUtc, INSTANT_FORMAT)}</dd>
              </div>
              <div className="profile-row">
                <dt>{t('account.lastSignIn')}</dt>
                <dd>{data.lastSignInUtc ? formatDateTime(data.lastSignInUtc, INSTANT_FORMAT) : t('account.never')}</dd>
              </div>
            </dl>
          ) : null}
        </div>
      </section>

      <ChangePasswordCard onChanged={() => setPasswordChanged(true)} />

      <AccessTokensCard />

      <section className="ui-card" aria-labelledby="profile-appearance-title">
        <div className="ui-card-header">
          <h2 id="profile-appearance-title" className="ui-card-title">{t('appearance.title')}</h2>
          <p className="ui-card-description">{t('appearance.description')}</p>
        </div>
        <div className="ui-card-content profile-inline">
          <span>{t('common:theme.label')}</span>
          <ThemeSwitch showLabels />
        </div>
      </section>

      <LanguageCard />

      <RecommendationSettingsCard />

      <section className="ui-card" aria-labelledby="profile-session-title">
        <div className="ui-card-header">
          <h2 id="profile-session-title" className="ui-card-title">{t('session.title')}</h2>
          {data && (
            <p className="ui-card-description">
              {t('session.description', { date: formatDateTime(data.sessionExpiresUtc, INSTANT_FORMAT) })}
            </p>
          )}
        </div>
        <div className="ui-card-content">
          <Button type="button" variant="outline" disabled={signOut.isPending} onClick={() => void signOutNow()}>
            <LogOut className="h-4 w-4" aria-hidden="true" />
            {t('auth:menu.signOut')}
          </Button>
        </div>
      </section>
    </div>
  )
}
