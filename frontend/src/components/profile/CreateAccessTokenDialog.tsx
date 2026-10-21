import { useEffect, useId, useState, type FormEvent } from 'react'
import { isAxiosError } from 'axios'
import { useTranslation } from 'react-i18next'
import { CopyableField } from '@/components/profile/CopyableField'
import { Banner } from '@/components/shared/Banner'
import { Field } from '@/components/shared/Field'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { holdLanguage } from '@/lib/auth-store'
import { useCreateAccessToken } from '@/lib/queries'
import type { AccessTokenAccess, AccessTokenLifetime, CreatedAccessToken } from '@/lib/types'
import { cn, getApiErrorMessage } from '@/lib/utils'
import '@/styles/profile.css' // .profile-* — the access options and their hints, the secret, the example, the wider secret step, the assistant's lines
import '@/styles/search.css' // .search-details-heading — the heading of Connect an AI assistant, as the Download dialog's sections have
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
 * MAM-45 — the same step gives the lines that connect Claude Code and Claude Desktop to the app's MCP
 * endpoint, each with the whole secret and a Copy of its own; it is wider than the form for them.
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
        // MAM-45 — wider once the secret shows, for the Claude Desktop block; the form keeps a settings dialog's width.
        className={cn('settings-dialog', created && 'profile-secret-dialog')}
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

/**
 * The new token's secret in a read-only field that selects itself on focus and wraps (51 characters
 * with no space are wider than the dialog), Copy, the warning and an example. On a plain-HTTP LAN
 * address Copy falls back to selecting the field; when that fails too, the line beside it says to
 * press Ctrl+C, with the token already selected (CopyableField).
 *
 * MAM-45 — then Connect an AI assistant: the Claude Code command and the Claude Desktop configuration,
 * each copied on its own. Unlike the curl example they carry the whole secret, so either is pasted as it
 * is (Vasyl, 2026-10-16). Both point at this page's own address, as the example does: the address the
 * person reached the app at. A Read only token's assistant gets no tool that writes (ADR 0040), which a
 * line says here rather than the assistant later.
 *
 * MAM-48 — a line above both says the assistant must reach that address from its own computer: a page
 * opened through a proxy that asks for a sign-in in the browser (Cloudflare Access) gives an address the
 * assistant can't pass, so the person opens the app at the home-network one and creates a token there,
 * where both lines are built for it, `--allow-http` included. The lines keep this page's address: the
 * app knows no other. Each line's hint says what the live check met (MAM-48): Claude Code takes up the
 * server from its next session, and Claude Desktop is quit before its configuration is edited, as it
 * writes the file while it runs.
 *
 * MAM-46 — while it shows, a language this tab follows from elsewhere (another tab's Profile, a refresh
 * carrying one saved on another device) waits until it closes: the signed-in pages mount afresh in a new
 * language, which would take the secret with them (auth-store `holdLanguage`).
 */
function SecretView({ created, onDone }: { created: CreatedAccessToken; onDone: () => void }) {
  const { t } = useTranslation('profile')
  const assistantTitle = useId()
  useEffect(() => holdLanguage(), [])
  const origin = window.location.origin
  const desktopConfig = claudeDesktopConfig(origin, created.secret, window.location.protocol === 'http:')

  return (
    <div className="settings-dialog-form">
      <div className="settings-dialog-body">
        <Banner tone="warning">{t('accessTokens.secret.warning')}</Banner>
        <CopyableField
          label={t('accessTokens.secret.label')}
          value={created.secret}
          rows={2}
          className="profile-secret"
          copyLabel={t('accessTokens.secret.copyToken')}
          copyFailed={t('accessTokens.secret.copyFailed')}
          autoFocus
        />
        <p className="profile-note">{t('accessTokens.secret.example')}</p>
        <code className="profile-command">
          {t('accessTokens.secret.command', {
            token: t('accessTokens.prefix', { prefix: created.token.prefix }),
            origin,
          })}
        </code>
        <section className="profile-assistant" aria-labelledby={assistantTitle}>
          <h3 id={assistantTitle} className="search-details-heading">{t('accessTokens.secret.assistant.title')}</h3>
          {created.token.access === 'ReadOnly' && (
            <p className="profile-note">{t('accessTokens.secret.assistant.readOnly')}</p>
          )}
          <p className="profile-note">{t('accessTokens.secret.assistant.address')}</p>
          <CopyableField
            label={t('accessTokens.secret.assistant.claudeCode.label')}
            hint={t('accessTokens.secret.assistant.claudeCode.hint')}
            value={t('accessTokens.secret.assistant.claudeCode.command', { origin, token: created.secret })}
            rows={3}
            className="profile-command-field"
            copyLabel={t('accessTokens.secret.assistant.claudeCode.copy')}
            copyFailed={t('accessTokens.secret.assistant.claudeCode.copyFailed')}
          />
          <CopyableField
            label={t('accessTokens.secret.assistant.claudeDesktop.label')}
            hint={t('accessTokens.secret.assistant.claudeDesktop.hint')}
            value={desktopConfig}
            // Every line of the block in view, without scrolling the field: it is read before it is pasted.
            rows={desktopConfig.split('\n').length}
            className="profile-command-field profile-config-field"
            copyLabel={t('accessTokens.secret.assistant.claudeDesktop.copy')}
            copyFailed={t('accessTokens.secret.assistant.claudeDesktop.copyFailed')}
          />
        </section>
      </div>
      <DialogFooter>
        <Button type="button" onClick={onDone}>{t('accessTokens.secret.done')}</Button>
      </DialogFooter>
    </div>
  )
}

/**
 * MAM-48 — the mcp-remote release the Claude Desktop block runs, pinned: the token passes through it, so
 * a new release on npm must not start running with it unasked. 0.15.2 is the one checked against the
 * live installation on 2026-10-21. Raise it here, on purpose, once a newer release has been tried with
 * the app the same way; the dialog's tests spell the pin out, so they change with it.
 */
const MCP_REMOTE = 'mcp-remote@0.15.2'

/**
 * MAM-45 — the Claude Desktop entry for claude_desktop_config.json. Claude Desktop starts local servers
 * only, so `npx mcp-remote` bridges it to the app's /mcp (`-y`: npx doesn't stop to ask before fetching
 * it; the release is pinned, MCP_REMOTE). The header goes as "Authorization:${AUTH_HEADER}" with
 * "Bearer …" in `env`, the form mcp-remote's README gives: Claude Desktop on Windows doesn't escape the
 * spaces inside `args`, so "Authorization: Bearer …" would reach mcp-remote broken up. `http-only`: the
 * app speaks Streamable HTTP alone, with no SSE endpoint for mcp-remote's default http-first to fall back
 * to. `--allow-http` only on a plain-HTTP address, which mcp-remote connects to only with it. Built as an
 * object and serialised, so no secret or address can break the JSON.
 */
function claudeDesktopConfig(origin: string, secret: string, plainHttp: boolean): string {
  const args = ['-y', MCP_REMOTE, `${origin}/mcp`, '--transport', 'http-only']
  if (plainHttp) args.push('--allow-http')
  // A plain string, not a template literal: mcp-remote itself puts the env value in place of ${AUTH_HEADER}.
  args.push('--header', 'Authorization:${AUTH_HEADER}')
  return JSON.stringify({ mcpServers: { mam: { command: 'npx', args, env: { AUTH_HEADER: `Bearer ${secret}` } } } }, null, 2)
}
