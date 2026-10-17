import { useId, useState } from 'react'
import { Plus, ShieldOff } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { CreateAccessTokenDialog } from '@/components/profile/CreateAccessTokenDialog'
import { Banner } from '@/components/shared/Banner'
import { useConfirm } from '@/components/shared/ConfirmDialog'
import { ErrorState } from '@/components/shared/ErrorState'
import { RetryButton } from '@/components/shared/RetryButton'
import { Button } from '@/components/ui/button'
import { formatDate, formatDateTime } from '@/lib/format'
import { useAccessTokens, useRevokeAccessToken } from '@/lib/queries'
import type { AccessTokenSummary } from '@/lib/types'
import { useLoadRetry } from '@/lib/use-load-retry'
import { getApiErrorMessage } from '@/lib/utils'
import '@/styles/profile.css' // .profile-* — the card's column, the token cell, the dates, the muted lines and the button row
import '@/styles/settings-list.css' // .settings-table* — the table, one card per token on a phone, and the row actions

/** A day is enough for when a token was made and when it stops: the cell stays narrow in a 44rem card. */
const DATE_FORMAT: Intl.DateTimeFormatOptions = { dateStyle: 'medium' }
/** Date and time without the seconds, as the Users page shows a sign-in: "did my script just run?". */
const INSTANT_FORMAT: Intl.DateTimeFormatOptions = { dateStyle: 'medium', timeStyle: 'short' }

/**
 * MAM-5 — the Profile page's Access tokens card: the signed-in user's personal access tokens, for
 * scripts and AI assistants that use the app as them (ADR 0040), with Create token and a Revoke per
 * token behind a confirmation.
 *
 * The server lists the tokens that aren't revoked, newest first, expired ones flagged and kept until
 * revoked — the list is never sorted or filtered here. A token's secret never reaches this list: it is
 * shown once, in the create dialog.
 */
export function AccessTokensCard() {
  const { t } = useTranslation(['profile', 'common'])
  const titleId = useId()
  const confirm = useConfirm()
  const tokens = useAccessTokens()
  const { data, error, isLoading } = tokens
  const load = useLoadRetry(tokens)
  const revoke = useRevokeAccessToken()
  const [creating, setCreating] = useState(false)
  const [revokeError, setRevokeError] = useState<string | null>(null)

  const revokeToken = async (token: AccessTokenSummary) => {
    const confirmed = await confirm({
      title: t('accessTokens.revokeConfirm.title', { name: token.name }),
      message: t('accessTokens.revokeConfirm.message', { name: token.name }),
      confirmLabel: t('accessTokens.revokeConfirm.confirm'),
      destructive: true,
    })
    if (!confirmed) return
    setRevokeError(null)
    try {
      await revoke.mutateAsync(token.id)
    } catch (e: unknown) {
      setRevokeError(getApiErrorMessage(e, t('accessTokens.revokeFailed')))
    }
  }

  return (
    <section className="ui-card" aria-labelledby={titleId}>
      <div className="ui-card-header">
        <h2 id={titleId} className="ui-card-title">{t('accessTokens.title')}</h2>
        <p className="ui-card-description">{t('accessTokens.description')}</p>
      </div>
      <div className="ui-card-content profile-settings">
        {/* Outside the ternary below, so a failed refresh or a refused Revoke adds a line above the
            tokens instead of replacing them. */}
        {error && data && (
          <Banner action={<RetryButton onRetry={() => tokens.refetch()} />}>
            {t('accessTokens.refreshFailed')}{' '}
            {getApiErrorMessage(error)}
          </Banner>
        )}
        {revokeError && <Banner onDismiss={() => setRevokeError(null)}>{revokeError}</Banner>}
        {load.error ? (
          <ErrorState title={t('accessTokens.loadFailed')} onRetry={load.retry} retrying={load.retrying}>
            {getApiErrorMessage(load.error)}
          </ErrorState>
        ) : isLoading ? (
          <p className="profile-muted">{t('common:loading')}</p>
        ) : data && data.items.length === 0 ? (
          <p className="profile-muted">{t('accessTokens.empty')}</p>
        ) : data ? (
          <div className="settings-table-wrap">
            <table className="settings-table">
              <thead>
                <tr>
                  <th scope="col">{t('accessTokens.columns.token')}</th>
                  <th scope="col">{t('accessTokens.columns.created')}</th>
                  <th scope="col">{t('accessTokens.columns.lastUsed')}</th>
                  <th scope="col">{t('accessTokens.columns.expires')}</th>
                  <th scope="col">{t('accessTokens.columns.actions')}</th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((token) => (
                  <TokenRow
                    key={token.id}
                    token={token}
                    busy={revoke.isPending && revoke.variables === token.id}
                    onRevoke={() => void revokeToken(token)}
                  />
                ))}
              </tbody>
            </table>
          </div>
        ) : null}
        {/* Under the list, whatever state it is in: a token can be created while the list fails to load. */}
        <div className="profile-actions">
          <Button type="button" variant="outline" size="sm" onClick={() => setCreating(true)}>
            <Plus className="h-3.5 w-3.5" aria-hidden="true" />
            {t('accessTokens.create')}
          </Button>
        </div>
      </div>
      {creating && <CreateAccessTokenDialog onClose={() => setCreating(false)} />}
    </section>
  )
}

// ── one token ──

interface TokenRowProps {
  token: AccessTokenSummary
  /** Its Revoke is under way. */
  busy: boolean
  onRevoke: () => void
}

/**
 * A table row on a wide screen, a card on a phone: every cell carries its column name in `data-label`
 * for the stacked layout. The name, the access and the prefix share one cell, so five columns fit the
 * Profile page's 44rem column. An expired token is muted like a disabled user (`data-disabled`).
 */
function TokenRow({ token, busy, onRevoke }: TokenRowProps) {
  const { t } = useTranslation('profile')

  return (
    <tr data-disabled={token.isExpired || undefined}>
      <td data-label={t('accessTokens.columns.token')}>
        <span className="profile-token">
          <span className="settings-name">
            <span className="settings-name-text">{token.name}</span>
            <span className="ui-badge ui-badge-secondary">{t(`accessTokens.access.${token.access}`)}</span>
            {token.isExpired && <span className="ui-badge ui-badge-outline">{t('accessTokens.expired')}</span>}
          </span>
          <span className="profile-mono profile-note">{t('accessTokens.prefix', { prefix: token.prefix })}</span>
        </span>
      </td>
      <td data-label={t('accessTokens.columns.created')} className="profile-date">{formatDate(token.createdUtc, DATE_FORMAT)}</td>
      <td data-label={t('accessTokens.columns.lastUsed')} className="profile-date">
        {token.lastUsedUtc ? formatDateTime(token.lastUsedUtc, INSTANT_FORMAT) : t('accessTokens.never')}
      </td>
      <td data-label={t('accessTokens.columns.expires')} className="profile-date">
        {/* MAM-46 — a key of its own: under Expires Ukrainian says "indefinitely", not Last used's "never". */}
        {token.expiresUtc ? formatDate(token.expiresUtc, DATE_FORMAT) : t('accessTokens.expiresNever')}
      </td>
      <td className="settings-actions-cell">
        <div className="settings-actions">
          <Button
            type="button"
            variant="outline"
            size="sm"
            className="ui-button-danger"
            title={t('accessTokens.revoke')}
            disabled={busy}
            onClick={onRevoke}
          >
            <ShieldOff className="h-3.5 w-3.5" aria-hidden="true" />
            <span className="label-text">{t('accessTokens.revoke')}</span>
          </Button>
        </div>
      </td>
    </tr>
  )
}
