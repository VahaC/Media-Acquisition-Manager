import { useId, useState, type ReactNode } from 'react'
import { ChevronLeft, ChevronRight, CircleAlert, KeyRound, Trash2, UserCheck, UserPlus, UserX } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { useConfirm } from '@/components/shared/ConfirmDialog'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { CreateUserDialog } from '@/components/users/CreateUserDialog'
import { ResetPasswordDialog } from '@/components/users/ResetPasswordDialog'
import { useAuthStore } from '@/lib/auth-store'
import { formatDateTime } from '@/lib/format'
import { useDeleteUser, useDisableUser, useEnableUser, useUsers } from '@/lib/queries'
import type { UserSummary } from '@/lib/types'
import { useDebounced } from '@/lib/use-debounced'
import { getApiErrorMessage } from '@/lib/utils'
import '@/styles/users.css' // .users-* — the header, table, pager and dialogs of this page

const PAGE_SIZES: readonly number[] = [10, 25, 50, 100]
const DEFAULT_PAGE_SIZE = 25
/** Per device: a phone and a desktop want different page lengths. */
const PAGE_SIZE_STORAGE_KEY = 'mam:users:pageSize'
const SEARCH_DEBOUNCE_MS = 300
/** Date and time without the seconds, which only add noise to a sign-in date. */
const INSTANT_FORMAT: Intl.DateTimeFormatOptions = { dateStyle: 'medium', timeStyle: 'short' }

/**
 * Settings → Users, owner-only: the people who can sign in, searched and paged by the server. Add a
 * user, disable or enable one, reset a password, delete one after a confirmation. The owner's own
 * row can't be disabled or deleted, and says why.
 *
 * A non-owner who opens the address gets the API's 403 sentence in place of the list.
 */
export function UsersPage() {
  const { t } = useTranslation(['users', 'common'])
  const isOwner = useAuthStore((state) => state.user?.isOwner === true)
  const confirm = useConfirm()
  const disable = useDisableUser()
  const enable = useEnableUser()
  const remove = useDeleteUser()
  const [search, setSearch] = useState('')
  const [pageSize, setPageSize] = useState(readPageSize)
  // The page remembers the search it belongs to, so a different search starts again at page 1
  // without an effect.
  const [paging, setPaging] = useState({ search: '', page: 1 })
  const [creating, setCreating] = useState(false)
  const [resetting, setResetting] = useState<UserSummary | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  const pageSizeId = useId()

  // Typing waits for a pause; emptying the box shows every user at once.
  const debouncedSearch = useDebounced(search.trim(), SEARCH_DEBOUNCE_MS)
  const appliedSearch = search.trim() === '' ? '' : debouncedSearch
  // MAM-4 — the stored page is reset on every change of the applied search, not only derived: kept,
  // it came back with an earlier search (clearing one returned to page 3), which by then could lie
  // past the last page. The derivation below keeps this discarded render off the stale page.
  if (paging.search !== appliedSearch) setPaging({ search: appliedSearch, page: 1 })
  const page = paging.search === appliedSearch ? paging.page : 1
  const { data, error, isLoading } = useUsers({ search: appliedSearch || undefined, page, pageSize })
  const pages = data ? Math.max(1, Math.ceil(data.totalCount / pageSize)) : 1

  const goToPage = (next: number) => setPaging({ search: appliedSearch, page: next })

  const changePageSize = (next: number) => {
    setPageSize(next)
    savePageSize(next)
    goToPage(1)
  }

  /** Runs one row action; a refusal shows as the API's sentence above the list. */
  const changeUser = async (action: () => Promise<unknown>) => {
    setActionError(null)
    try {
      await action()
    } catch (e: unknown) {
      setActionError(getApiErrorMessage(e, t('actionFailed')))
    }
  }

  const setDisabled = (user: UserSummary, disabled: boolean) =>
    void changeUser(() => (disabled ? disable : enable).mutateAsync(user.id))

  const deleteUser = async (user: UserSummary) => {
    const confirmed = await confirm({
      title: t('delete.title', { name: user.userName }),
      message: t('delete.message', { name: user.userName }),
      confirmLabel: t('delete.confirm'),
      destructive: true,
    })
    if (!confirmed) return
    await changeUser(async () => {
      await remove.mutateAsync(user.id)
      // The last row of a later page is gone: show the page before it rather than an empty one.
      if (data?.items.length === 1 && page > 1) goToPage(page - 1)
    })
  }

  const busyWith = (user: UserSummary) =>
    [disable, enable, remove].some((mutation) => mutation.isPending && mutation.variables === user.id)

  return (
    <div className="users-page">
      <div className="users-header">
        <div className="users-intro">
          <h1 className="users-title">{t('title')}</h1>
          <p className="users-lead">{t('lead')}</p>
        </div>
        {isOwner && (
          <Button type="button" onClick={() => setCreating(true)}>
            <UserPlus className="h-4 w-4" aria-hidden="true" />
            {t('add')}
          </Button>
        )}
      </div>

      {isOwner && (
        <Input
          type="search"
          className="users-search"
          value={search}
          placeholder={t('search.placeholder')}
          aria-label={t('search.label')}
          onChange={(event) => setSearch(event.target.value)}
        />
      )}

      {/* Outside the ternary below, so a failed refresh or a refused action adds a line above the
          rows instead of replacing them. */}
      {error && data && (
        <p className="users-banner" role="alert">
          <CircleAlert className="h-3.5 w-3.5 inline" aria-hidden="true" />{' '}
          {t('refreshFailed')}{' '}
          {getApiErrorMessage(error)}
        </p>
      )}
      {actionError && <p className="users-banner" role="alert">{actionError}</p>}

      {isLoading ? (
        <p className="users-muted">{t('common:loading')}</p>
      ) : error && !data ? (
        <p className="users-error" role="alert">{getApiErrorMessage(error, t('loadFailed'))}</p>
      ) : data && data.items.length === 0 && appliedSearch !== '' ? (
        <div className="users-empty">
          <p>{t('noMatch', { search: appliedSearch })}</p>
          <Button type="button" variant="outline" size="sm" onClick={() => setSearch('')}>
            {t('clearSearch')}
          </Button>
        </div>
      ) : data ? (
        <>
          <div className="users-table-wrap">
            <table className="users-table">
              <thead>
                <tr>
                  <th scope="col">{t('columns.userName')}</th>
                  <th scope="col">{t('columns.created')}</th>
                  <th scope="col">{t('columns.lastSignIn')}</th>
                  <th scope="col">{t('columns.actions')}</th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((user) => (
                  <UserRow
                    key={user.id}
                    user={user}
                    busy={busyWith(user)}
                    onSetDisabled={(disabled) => setDisabled(user, disabled)}
                    onResetPassword={() => setResetting(user)}
                    onDelete={() => void deleteUser(user)}
                  />
                ))}
              </tbody>
            </table>
          </div>

          <nav className="users-pager" aria-label={t('pager.label')}>
            <span className="users-pager-status">{t('pager.status', { page, pages })}</span>
            {/* MAM-4 — the label sits beside the select, tied by id: a label wrapping it made the
                browser read the select's own value into its name, so it was announced as "25". */}
            <div className="users-page-size">
              <label htmlFor={pageSizeId}>{t('pager.pageSize')}</label>
              <select
                id={pageSizeId}
                className="ui-input"
                value={pageSize}
                onChange={(event) => changePageSize(Number(event.target.value))}
              >
                {PAGE_SIZES.map((size) => <option key={size} value={size}>{size}</option>)}
              </select>
            </div>
            <div className="users-pager-buttons">
              <Button
                type="button"
                variant="outline"
                size="sm"
                title={t('pager.previous')}
                disabled={page <= 1}
                onClick={() => goToPage(page - 1)}
              >
                <ChevronLeft className="h-3.5 w-3.5" aria-hidden="true" />
                <span className="label-text">{t('pager.previous')}</span>
              </Button>
              <Button
                type="button"
                variant="outline"
                size="sm"
                title={t('pager.next')}
                disabled={page >= pages}
                onClick={() => goToPage(page + 1)}
              >
                <span className="label-text">{t('pager.next')}</span>
                <ChevronRight className="h-3.5 w-3.5" aria-hidden="true" />
              </Button>
            </div>
          </nav>
        </>
      ) : null}

      {creating && <CreateUserDialog onClose={() => setCreating(false)} />}
      {resetting && <ResetPasswordDialog user={resetting} onClose={() => setResetting(null)} />}
    </div>
  )
}

// ── one row ──

interface UserRowProps {
  user: UserSummary
  /** An action on this row is under way. */
  busy: boolean
  onSetDisabled: (disabled: boolean) => void
  onResetPassword: () => void
  onDelete: () => void
}

/**
 * A table row on a wide screen, a card on a phone: every cell carries its column name in
 * `data-label` for the stacked layout. Disable/Enable is instant — it is undone by the other one.
 */
function UserRow({ user, busy, onSetDisabled, onResetPassword, onDelete }: UserRowProps) {
  const { t } = useTranslation('users')

  return (
    <tr data-disabled={user.isDisabled || undefined}>
      <td data-label={t('columns.userName')}>
        <span className="users-name">
          <span className="users-name-text">{user.userName}</span>
          {user.isOwner && <span className="ui-badge ui-badge-default">{t('owner')}</span>}
          {user.isDisabled && <span className="ui-badge ui-badge-outline">{t('disabled')}</span>}
        </span>
      </td>
      <td data-label={t('columns.created')}>{formatDateTime(user.createdUtc, INSTANT_FORMAT)}</td>
      <td data-label={t('columns.lastSignIn')}>
        {user.lastSignInUtc ? formatDateTime(user.lastSignInUtc, INSTANT_FORMAT) : t('never')}
      </td>
      <td className="users-actions-cell">
        <div className="users-actions">
          {user.isDisabled ? (
            <Button
              type="button"
              variant="outline"
              size="sm"
              title={t('actions.enable')}
              disabled={busy}
              onClick={() => onSetDisabled(false)}
            >
              <UserCheck className="h-3.5 w-3.5" aria-hidden="true" />
              <span className="label-text">{t('actions.enable')}</span>
            </Button>
          ) : (
            <DisabledReason reason={user.isOwner ? t('ownerCannotBeDisabled') : undefined}>
              <Button
                type="button"
                variant="outline"
                size="sm"
                title={t('actions.disable')}
                disabled={user.isOwner || busy}
                onClick={() => onSetDisabled(true)}
              >
                <UserX className="h-3.5 w-3.5" aria-hidden="true" />
                <span className="label-text">{t('actions.disable')}</span>
              </Button>
            </DisabledReason>
          )}
          <Button
            type="button"
            variant="outline"
            size="sm"
            title={t('actions.resetPassword')}
            disabled={busy}
            onClick={onResetPassword}
          >
            <KeyRound className="h-3.5 w-3.5" aria-hidden="true" />
            <span className="label-text">{t('actions.resetPassword')}</span>
          </Button>
          <DisabledReason reason={user.isOwner ? t('ownerCannotBeDeleted') : undefined}>
            <Button
              type="button"
              variant="outline"
              size="sm"
              className="ui-button-danger"
              title={t('actions.delete')}
              disabled={user.isOwner || busy}
              onClick={onDelete}
            >
              <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />
              <span className="label-text">{t('actions.delete')}</span>
            </Button>
          </DisabledReason>
        </div>
      </td>
    </tr>
  )
}

/** Shows `reason` on hover over a button disabled for that reason (see `.users-action-reason`). */
function DisabledReason({ reason, children }: { reason: string | undefined; children: ReactNode }) {
  if (reason === undefined) return children
  return <span className="users-action-reason" title={reason}>{children}</span>
}

// ── page size preference ──

function readPageSize(): number {
  try {
    const stored = Number(localStorage.getItem(PAGE_SIZE_STORAGE_KEY))
    if (PAGE_SIZES.includes(stored)) return stored
  } catch {
    /* storage unavailable: the default it is */
  }
  return DEFAULT_PAGE_SIZE
}

function savePageSize(size: number) {
  try {
    localStorage.setItem(PAGE_SIZE_STORAGE_KEY, String(size))
  } catch {
    /* storage unavailable: the choice lasts until the page is closed */
  }
}
