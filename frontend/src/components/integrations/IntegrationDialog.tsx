import { useId, useState, type FormEvent } from 'react'
import { Loader2, PlugZap } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { ConnectionTestResultLine, type ConnectionTestOutcome } from '@/components/integrations/ConnectionTestResultLine'
import { PathMappingsEditor } from '@/components/integrations/PathMappingsEditor'
import { SecretField } from '@/components/integrations/SecretField'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { toSecretUpsert } from '@/lib/integration-secrets'
import { newPathMappingDraft } from '@/lib/path-mapping-drafts'
import {
  useCreateIntegration,
  useIntegrationAdapterTypes,
  useTestIntegrationDraft,
  useUpdateIntegration,
} from '@/lib/queries'
import type {
  IntegrationAdapterType,
  IntegrationKind,
  IntegrationSummary,
  PathMappingDraft,
  SecretDraft,
} from '@/lib/types'
import { getApiErrorMessage } from '@/lib/utils'
import '@/styles/auth.css' // .auth-field, .auth-hint, .auth-muted — the fields as every settings dialog lays them out
import '@/styles/settings-dialog.css' // .settings-dialog*, .settings-check — the scrolling body, the error line, the switch
import '@/styles/integrations.css' // .integrations-dialog, .integrations-test — the wider dialog, the test row

const NO_SECRET: SecretDraft = { value: '', clear: false }

interface IntegrationDialogProps {
  /** The integration to edit; without it the dialog adds a new one. */
  integration?: IntegrationSummary
  onClose: () => void
}

/**
 * Adds an integration or edits one. The parent mounts it only while it is open, so every opening
 * starts from the saved state. Its one Save commits every field — credentials, the switch and the
 * path mappings included — and it closes only once the server has taken them.
 *
 * Only the fields the chosen type takes are shown, and only they are sent. A stored secret is never
 * shown: an empty field keeps it. Test connection tries the unsaved draft without saving or closing;
 * on an edit, a secret left empty is tested with the stored one (MAM-7 decision 2).
 *
 * Every rule is the server's, shown as its sentence.
 */
export function IntegrationDialog({ integration, onClose }: IntegrationDialogProps) {
  const { t } = useTranslation(['integrations', 'common'])
  const types = useIntegrationAdapterTypes()
  const create = useCreateIntegration()
  const update = useUpdateIntegration()
  const testDraft = useTestIntegrationDraft()
  const serviceId = useId()
  const serviceHintId = useId()
  const addressHintId = useId()
  const enabledHintId = useId()
  const [adapterType, setAdapterType] = useState(integration?.adapterType ?? '')
  const [name, setName] = useState(integration?.name ?? '')
  const [baseUrl, setBaseUrl] = useState(integration?.baseUrl ?? '')
  const [userName, setUserName] = useState(integration?.userName ?? '')
  const [password, setPassword] = useState(NO_SECRET)
  const [apiKey, setApiKey] = useState(NO_SECRET)
  const [isEnabled, setIsEnabled] = useState(integration?.isEnabled ?? true)
  const [mappings, setMappings] = useState<PathMappingDraft[]>(
    () => (integration?.pathMappings ?? []).map((mapping) => newPathMappingDraft(mapping)))
  const [testResult, setTestResult] = useState<ConnectionTestOutcome | null>(null)
  const [error, setError] = useState<string | null>(null)

  const adapter = types.data?.items.find((type) => type.typeId === adapterType)
  // MAM-7 — the example in the empty Address field follows the type like the hint under it does:
  // one fixed example showed Jellyfin's port to someone adding qBittorrent, against its own hint.
  const addressTexts = addressTextsFor(adapterType)
  const saving = create.isPending || update.isPending

  /** Another type takes other credentials and paths: what was typed for the last one goes. */
  const pickType = (next: string) => {
    setAdapterType(next)
    setUserName('')
    setPassword(NO_SECRET)
    setApiKey(NO_SECRET)
    setMappings([])
    setTestResult(null)
  }

  /** The credentials as the API takes them; one the type doesn't use is left out. */
  const credentials = () => ({
    userName: adapter && adapter.userName !== 'NotUsed' ? userName.trim() || undefined : undefined,
    password: adapter && adapter.password !== 'NotUsed' ? toSecretUpsert(password) : undefined,
    apiKey: adapter && adapter.apiKey !== 'NotUsed' ? toSecretUpsert(apiKey) : undefined,
  })

  const testConnection = async () => {
    setTestResult(null)
    const draft = { baseUrl: baseUrl.trim(), ...credentials() }
    try {
      const result = await testDraft.mutateAsync(
        integration ? { integrationId: integration.id, ...draft } : { adapterType, ...draft })
      setTestResult({ ok: result.status === 'Succeeded', text: result.message })
    } catch (e: unknown) {
      // A refused draft (400), a vanished integration (404) or an unusable type or secret (409).
      setTestResult({ ok: false, text: getApiErrorMessage(e, t('testFailed')) })
    }
  }

  const save = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setError(null)
    const fields = {
      name: name.trim(),
      baseUrl: baseUrl.trim(),
      ...credentials(),
      isEnabled,
      // As typed, blank rows included: the server's sentence names what is wrong with one.
      pathMappings: adapter?.reportsFilePaths
        ? mappings.map(({ servicePrefix, appPrefix }) => ({ servicePrefix, appPrefix }))
        : undefined,
    }
    try {
      if (integration) await update.mutateAsync({ id: integration.id, request: fields })
      else await create.mutateAsync({ adapterType, ...fields })
      onClose()
    } catch (e: unknown) {
      setError(getApiErrorMessage(e, integration ? t('dialog.editFailed') : t('dialog.createFailed')))
    }
  }

  return (
    <Dialog open onOpenChange={(open) => { if (!open) onClose() }}>
      <DialogContent className="settings-dialog integrations-dialog">
        <DialogHeader>
          <DialogTitle>
            {integration ? t('dialog.editTitle', { name: integration.name }) : t('dialog.createTitle')}
          </DialogTitle>
          <DialogDescription>
            {integration ? t('dialog.editDescription') : t('dialog.createDescription')}
          </DialogDescription>
        </DialogHeader>
        {/* noValidate: the rules live on the server, and its sentence is the one to show. */}
        <form className="settings-dialog-form" noValidate onSubmit={(event) => void save(event)}>
          <div className="settings-dialog-body">
            {types.isLoading && <p className="auth-muted">{t('common:loading')}</p>}
            {types.error && !types.data && (
              <p className="settings-dialog-error" role="alert">
                {getApiErrorMessage(types.error, t('dialog.typesFailed'))}
              </p>
            )}

            {integration ? (
              // The type is fixed once saved: another service takes other credentials and paths.
              <div className="auth-field">
                <label className="auth-field">
                  <span className="ui-label">{t('dialog.service')}</span>
                  <Input
                    value={integration.adapterDisplayName ?? integration.adapterType}
                    readOnly
                    aria-describedby={serviceHintId}
                  />
                </label>
                <p id={serviceHintId} className="auth-hint">{t('dialog.serviceFixed')}</p>
              </div>
            ) : (
              // MAM-4 — the label sits beside the select, tied by id: a label wrapping a select made
              // the browser read the select's own value into its name.
              <div className="auth-field">
                <label htmlFor={serviceId} className="ui-label">{t('dialog.service')}</label>
                <select
                  id={serviceId}
                  className="ui-input"
                  value={adapterType}
                  disabled={!types.data}
                  onChange={(event) => pickType(event.target.value)}
                >
                  <option value="" disabled>{t('dialog.servicePlaceholder')}</option>
                  {groupByKind(types.data?.items ?? []).map((group) => (
                    <optgroup key={group.kind} label={t(`kindFilterOptions.${group.kind}`)}>
                      {group.types.map((type) => (
                        <option key={type.typeId} value={type.typeId}>{type.displayName}</option>
                      ))}
                    </optgroup>
                  ))}
                </select>
              </div>
            )}

            <label className="auth-field">
              <span className="ui-label">{t('dialog.name')}</span>
              <Input
                value={name}
                autoComplete="off"
                autoFocus={integration !== undefined}
                onChange={(event) => setName(event.target.value)}
              />
            </label>

            <div className="auth-field">
              <label className="auth-field">
                <span className="ui-label">{t('dialog.address')}</span>
                <Input
                  value={baseUrl}
                  inputMode="url"
                  placeholder={t(`dialog.addressPlaceholder.${addressTexts}`)}
                  autoComplete="off"
                  autoCapitalize="none"
                  spellCheck={false}
                  aria-describedby={addressHintId}
                  onChange={(event) => setBaseUrl(event.target.value)}
                />
              </label>
              <p id={addressHintId} className="auth-hint">{t(`dialog.addressHelp.${addressTexts}`)}</p>
            </div>

            {adapter && adapter.userName !== 'NotUsed' && (
              <label className="auth-field">
                <span className="ui-label">
                  {t('dialog.userName')}
                  {adapter.userName === 'Optional' && <> {t('dialog.optional')}</>}
                </span>
                <Input
                  value={userName}
                  autoComplete="off"
                  autoCapitalize="none"
                  spellCheck={false}
                  onChange={(event) => setUserName(event.target.value)}
                />
              </label>
            )}
            {adapter && adapter.password !== 'NotUsed' && (
              <SecretField
                label={t('dialog.password')}
                removeLabel={t('dialog.removePassword')}
                requirement={adapter.password}
                stored={integration?.hasPassword ?? false}
                value={password}
                onChange={setPassword}
              />
            )}
            {adapter && adapter.apiKey !== 'NotUsed' && (
              <SecretField
                label={t('dialog.apiKey')}
                removeLabel={t('dialog.removeApiKey')}
                requirement={adapter.apiKey}
                stored={integration?.hasApiKey ?? false}
                value={apiKey}
                onChange={setApiKey}
              />
            )}

            <div className="auth-field">
              <label className="settings-check">
                <input
                  type="checkbox"
                  checked={isEnabled}
                  aria-describedby={enabledHintId}
                  onChange={(event) => setIsEnabled(event.target.checked)}
                />
                {t('dialog.enabled')}
              </label>
              <p id={enabledHintId} className="auth-hint">{t('dialog.enabledHint')}</p>
            </div>

            {/* Tries the draft as it stands; it saves nothing and doesn't close, so it is no second Save. */}
            <div className="integrations-test">
              <Button
                type="button"
                variant="outline"
                disabled={!types.data || testDraft.isPending}
                onClick={() => void testConnection()}
              >
                {testDraft.isPending
                  ? <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />
                  : <PlugZap className="h-3.5 w-3.5" aria-hidden="true" />}
                {testDraft.isPending ? t('dialog.testing') : t('dialog.test')}
              </Button>
              <ConnectionTestResultLine result={testResult} />
            </div>

            {adapter?.reportsFilePaths && (
              <PathMappingsEditor serviceName={adapter.displayName} value={mappings} onChange={setMappings} />
            )}
          </div>
          {error && <p className="settings-dialog-error" role="alert">{error}</p>}
          {/* Primary first, Cancel last — the order every dialog in the app uses. */}
          <DialogFooter>
            <Button type="submit" disabled={!types.data || saving}>
              {integration
                ? saving ? t('dialog.editSubmitting') : t('dialog.editSubmit')
                : saving ? t('dialog.createSubmitting') : t('dialog.createSubmit')}
            </Button>
            <Button type="button" variant="outline" onClick={onClose}>{t('common:cancel')}</Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}

// ── helpers ──

/**
 * The types in the server's order (by kind, then name), one group per kind — a qBittorrent and a
 * Jellyfin side by side would hide that they do different jobs.
 */
function groupByKind(types: IntegrationAdapterType[]) {
  const groups: { kind: IntegrationKind; types: IntegrationAdapterType[] }[] = []
  for (const type of types) {
    const last = groups.at(-1)
    if (last?.kind === type.kind) last.types.push(type)
    else groups.push({ kind: type.kind, types: [type] })
  }
  return groups
}

/**
 * Which address hint and example to show (`dialog.addressHelp.*`, `dialog.addressPlaceholder.*`):
 * the type's own for the types this build ships, the generic ones before a type is picked and for
 * any other, so a type added later needs no change here.
 */
function addressTextsFor(adapterType: string) {
  switch (adapterType) {
    case 'jellyfin': return 'jellyfin' as const
    case 'torznab': return 'torznab' as const
    case 'qbittorrent': return 'qbittorrent' as const
    default: return 'generic' as const
  }
}
