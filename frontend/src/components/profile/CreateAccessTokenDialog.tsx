import { useId, useRef, useState, type FormEvent } from 'react'
import { isAxiosError } from 'axios'
import { Copy } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Banner } from '@/components/shared/Banner'
import { Field } from '@/components/shared/Field'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { copyFieldText } from '@/lib/clipboard'
import { useCreateAccessToken } from '@/lib/queries'
import type { AccessTokenAccess, AccessTokenLifetime, CreatedAccessToken } from '@/lib/types'
import { getApiErrorMessage } from '@/lib/utils'
import '@/styles/profile.css' // .profile-* — the access options and their hints, the secret, the copy row, the example
import '@/styles/settings-dialog.css' // .settings-dialog* and .settings-check — the scrolling body, the buttons, the radios

/** The access levels in the order the dialog offers them: the narrower first, and preselected. */
const ACCESS_LEVELS: readonly AccessTokenAccess[] = ['ReadOnly', 'ReadAndDownload']
const LIFETIMES: readonly AccessTokenLifetime[] = ['Days30', 'Days90', 'OneYear', 'Never']
/** MAM-5 — 90 days unless the person picks otherwise (Vasyl, 2026-10-16). */
const DEFAULT_LIFETIME: AccessTokenLifetime = 'Days90'

/**
 * MAM-5 — creates a personal access token: its name, its access and when it expires. The parent mounts
 * it only while it is open, so every opening starts empty.
 *
 * Once created, the same dialog shows the secret — the only time anyone sees it — with Copy, a warning
 * to copy it now and an example of a script sending it. The secret lives only in this component's
 * state, never in the query cache: the create mutation that answered it belongs to the form, and goes
 * from the mutation cache as the form unmounts (useCreateAccessToken). While it shows, a click outside
 * the dialog doesn't close it: one stray click would lose it for good. Escape, the close button and
 * Done still do — those are meant.
 *
 * While the create request is under way nothing closes the dialog: Cancel is disabled, and Escape, the
 * close button and a click outside are ignored. Closing would not stop the request, so the server would
 * still make a working token whose secret nobody ever sees (MAM-5, ADR 0040).
 */
export function CreateAccessTokenDialog({ onClose }: { onClose: () => void }) {
  const { t } = useTranslation('profile')
  const [created, setCreated] = useState<CreatedAccessToken | null>(null)
  // Reported by the form, which owns the mutation: lifting the mutation here would keep the secret in
  // the mutation cache for as long as it shows.
  const [creating, setCreating] = useState(false)

  return (
    // One guard for Escape, the close button and a click outside: each of them asks to close through here.
    <Dialog open onOpenChange={(open) => { if (!open && !creating) onClose() }}>
      <DialogContent
        className="settings-dialog"
        onInteractOutside={(event) => { if (created || creating) event.preventDefault() }}
      >
        <DialogHeader>
          <DialogTitle>{created ? t('accessTokens.secret.title') : t('accessTokens.dialog.title')}</DialogTitle>
          <DialogDescription>
            {created
              ? t('accessTokens.secret.description', { name: created.token.name })
              : t('accessTokens.dialog.description')}
          </DialogDescription>
        </DialogHeader>
        {created ? (
          <SecretView created={created} onDone={onClose} />
        ) : (
          <CreateForm onCreated={setCreated} onCreatingChange={setCreating} onCancel={onClose} />
        )}
      </DialogContent>
    </Dialog>
  )
}

// ── the form ──

interface CreateFormProps {
  /** The server created the token; its answer carries the secret. */
  onCreated: (created: CreatedAccessToken) => void
  /** The create request started (true) or settled (false): the dialog can't be closed in between. */
  onCreatingChange: (creating: boolean) => void
  onCancel: () => void
}

/**
 * The name, the access and the expiry. The access and the expiry always hold one of the server's
 * values, so a 400 can only be about the name (blank, or over 100 characters): its sentence goes under
 * Name, in the server's words. Anything else — the server down, too many requests — is a Banner above
 * the buttons, and the fields keep what was typed.
 */
function CreateForm({ onCreated, onCreatingChange, onCancel }: CreateFormProps) {
  const { t } = useTranslation(['profile', 'common'])
  const create = useCreateAccessToken()
  const accessName = useId()
  const hintIds = useId()
  const [name, setName] = useState('')
  const [access, setAccess] = useState<AccessTokenAccess>('ReadOnly')
  const [lifetime, setLifetime] = useState<AccessTokenLifetime>(DEFAULT_LIFETIME)
  const [nameError, setNameError] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setNameError(null)
    setError(null)
    // Told from the handler, not mirrored from create.isPending by an effect, which would leave one
    // render with the request running and the dialog still closable.
    onCreatingChange(true)
    try {
      onCreated(await create.mutateAsync({ name: name.trim(), access, lifetime }))
    } catch (e: unknown) {
      const sentence = getApiErrorMessage(e, t('accessTokens.dialog.failed'))
      if (isAxiosError(e) && e.response?.status === 400) setNameError(sentence)
      else setError(sentence)
    } finally {
      onCreatingChange(false)
    }
  }

  return (
    // noValidate: the rules live on the server, and its sentence is the one to show.
    <form className="settings-dialog-form" noValidate onSubmit={(event) => void submit(event)}>
      <div className="settings-dialog-body">
        <Field label={t('accessTokens.dialog.name')} hint={t('accessTokens.dialog.nameHint')} error={nameError}>
          {(control) => (
            <Input
              {...control}
              value={name}
              autoComplete="off"
              autoFocus
              onChange={(event) => {
                setName(event.target.value)
                setNameError(null)
              }}
            />
          )}
        </Field>
        <fieldset className="profile-settings-group">
          <legend className="ui-label">{t('accessTokens.dialog.access')}</legend>
          {ACCESS_LEVELS.map((level) => (
            <div key={level} className="profile-option">
              <label className="settings-check">
                <input
                  type="radio"
                  name={accessName}
                  value={level}
                  checked={access === level}
                  aria-describedby={`${hintIds}-${level}`}
                  onChange={() => setAccess(level)}
                />
                {t(`accessTokens.access.${level}`)}
              </label>
              {/* Outside the label, tied by aria-describedby: inside it, the hint would join the radio's name. */}
              <p id={`${hintIds}-${level}`} className="ui-hint profile-option-hint">
                {t(`accessTokens.dialog.accessHint.${level}`)}
              </p>
            </div>
          ))}
        </fieldset>
        {/* The label beside the select by id, never wrapping it: a browser names a wrapped select by its value too (MAM-4). */}
        <Field label={t('accessTokens.dialog.lifetime')} hint={t('accessTokens.dialog.lifetimeHint')}>
          {(control) => (
            <select
              {...control}
              className="ui-input"
              value={lifetime}
              onChange={(event) => setLifetime(event.target.value as AccessTokenLifetime)}
            >
              {LIFETIMES.map((option) => (
                <option key={option} value={option}>{t(`accessTokens.dialog.lifetimes.${option}`)}</option>
              ))}
            </select>
          )}
        </Field>
      </div>
      {/* Outside the scrolling body, above the buttons. */}
      {error && <Banner>{error}</Banner>}
      {/* Primary first, Cancel last — the order every dialog in the app uses. */}
      <DialogFooter>
        <Button type="submit" disabled={create.isPending}>
          {create.isPending ? t('accessTokens.dialog.submitting') : t('accessTokens.dialog.submit')}
        </Button>
        <Button type="button" variant="outline" disabled={create.isPending} onClick={onCancel}>{t('common:cancel')}</Button>
      </DialogFooter>
    </form>
  )
}

// ── the secret, once ──

/** What Copy last did: nothing yet, copied, or failed in both ways (lib/clipboard.ts). */
type CopyOutcome = 'idle' | 'copied' | 'failed'

/**
 * The new token's secret in a read-only field that selects itself on focus and wraps (51 characters
 * with no space are wider than the dialog), Copy, the warning and an example. On a plain-HTTP LAN
 * address Copy falls back to selecting the field; when that fails too, the line beside it says to
 * press Ctrl+C, with the token already selected.
 */
function SecretView({ created, onDone }: { created: CreatedAccessToken; onDone: () => void }) {
  const { t } = useTranslation('profile')
  const secretField = useRef<HTMLTextAreaElement>(null)
  const [copyOutcome, setCopyOutcome] = useState<CopyOutcome>('idle')

  const copy = async () => {
    if (!secretField.current) return
    setCopyOutcome((await copyFieldText(secretField.current)) ? 'copied' : 'failed')
  }

  return (
    <div className="settings-dialog-form">
      <div className="settings-dialog-body">
        <Banner tone="warning">{t('accessTokens.secret.warning')}</Banner>
        <Field label={t('accessTokens.secret.label')}>
          {(control) => (
            <textarea
              {...control}
              ref={secretField}
              className="ui-textarea profile-secret"
              value={created.secret}
              readOnly
              rows={2}
              spellCheck={false}
              autoFocus
              onFocus={(event) => event.currentTarget.select()}
            />
          )}
        </Field>
        <div className="profile-actions">
          <Button type="button" variant="outline" size="sm" onClick={() => void copy()}>
            <Copy className="h-3.5 w-3.5" aria-hidden="true" />
            {t('accessTokens.secret.copy')}
          </Button>
          {copyOutcome === 'copied' && <span role="status" className="profile-muted">{t('accessTokens.secret.copied')}</span>}
          {copyOutcome === 'failed' && <span role="status">{t('accessTokens.secret.copyFailed')}</span>}
        </div>
        <p className="profile-note">{t('accessTokens.secret.example')}</p>
        <code className="profile-command">
          {t('accessTokens.secret.command', {
            token: t('accessTokens.prefix', { prefix: created.token.prefix }),
            origin: window.location.origin,
          })}
        </code>
      </div>
      <DialogFooter>
        <Button type="button" onClick={onDone}>{t('accessTokens.secret.done')}</Button>
      </DialogFooter>
    </div>
  )
}
