import { useId, useState, type FormEvent } from 'react'
import { Plus, Trash2 } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Banner } from '@/components/shared/Banner'
import { ErrorState } from '@/components/shared/ErrorState'
import { RetryButton } from '@/components/shared/RetryButton'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { formatNumber } from '@/lib/format'
import { useAllowedRoots, useUpdateAllowedRoots } from '@/lib/queries'
import type { AllowedRoots, ReplacementFolder, UpdateAllowedRootsRequest } from '@/lib/types'
import { useLoadRetry } from '@/lib/use-load-retry'
import { getApiErrorMessage, retryCanHelp } from '@/lib/utils'
import '@/styles/settings-list.css' // .settings-* — the header and the muted line every settings page shares
import '@/styles/settings-dialog.css' // .settings-check — the replacement switch's and each library folder's box before its words
import '@/styles/media-folders.css' // .media-folders-* — the folder rows, the replacement card and the Save row

/** One folder row of the form, with a key no other row of this page load has. */
interface FolderDraft {
  key: number
  path: string
}

let lastDraftKey = 0

/**
 * A row key from one counter rather than the row's index: removing a row would otherwise hand its
 * input to the row below it, typed text included (the reason of lib/path-mapping-drafts.ts).
 */
function folderDraft(path: string): FolderDraft {
  lastDraftKey += 1
  return { key: lastDraftKey, path }
}

/** The app paths of the library folders stored as ticked for replacement, in the list's order. */
function storedTicks(roots: AllowedRoots): string[] {
  return roots.libraryFolders.filter((folder) => folder.ticked).map((folder) => folder.appPath)
}

/**
 * MAM-9 — Settings → Media folders, owner-only: the folders inside the app where it may read the
 * library's files. None by default, and then the app reads nothing. One path per row, like the path
 * mappings of an integration, and one Save that sends the whole list; the server checks it and its
 * sentence names the folder that is wrong. Saving doesn't inspect anything.
 *
 * MAM-24 — the same Save carries the replacement gates (ADR 0033): the master switch, off by
 * default, and a tick per library folder the app knows, none by default. Only a copy in a ticked
 * folder inside the media folders can be picked in the Download dialog's Replace after verification.
 * One form holds both cards, so the page keeps one Save (house rule 17) and one request.
 *
 * A non-owner who opens the address gets the API's 403 sentence in place of the form.
 */
export function MediaFoldersPage() {
  const { t } = useTranslation(['mediaFolders', 'common'])
  const roots = useAllowedRoots()
  const { data, error, isLoading } = roots
  const load = useLoadRetry(roots)
  const update = useUpdateAllowedRoots()
  const [saveError, setSaveError] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)
  const [saves, setSaves] = useState(0)

  const save = async (request: UpdateAllowedRootsRequest) => {
    setSaveError(null)
    setSaved(false)
    try {
      await update.mutateAsync(request)
      setSaved(true)
      setSaves((count) => count + 1)
    } catch (e: unknown) {
      setSaveError(getApiErrorMessage(e, t('saveFailed')))
    }
  }

  return (
    <div className="settings-page">
      <div className="settings-header">
        <div className="settings-intro">
          <h1 className="settings-title">{t('title')}</h1>
          <p className="settings-lead">{t('lead')}</p>
        </div>
      </div>

      {/* Outside the ternary below, so a failed refresh adds a line above the form instead of
          replacing it. */}
      {error && data && (
        <Banner action={<RetryButton onRetry={() => roots.refetch()} />}>
          {t('refreshFailed')}{' '}
          {getApiErrorMessage(error)}
        </Banner>
      )}

      {/* A member's 403 is the server's lasting answer: no Try again for it (retryCanHelp). */}
      {load.error ? (
        <ErrorState
          title={t('loadFailed')}
          onRetry={retryCanHelp(load.error) ? load.retry : undefined}
          retrying={load.retrying}
        >
          {getApiErrorMessage(load.error)}
        </ErrorState>
      ) : isLoading ? (
        <p className="settings-muted">{t('common:loading')}</p>
      ) : data ? (
        // Mounted afresh after each save, so the rows show the folders as stored (in canonical form)
        // rather than as typed, and when the stored folders change elsewhere. Not for every answer of
        // the server: a refetch of the same folders (the network back, the page opened again) would
        // throw away what is typed. A refused save changes nothing, and the typed rows stay to correct.
        // MAM-24 — the stored switch and ticks are part of the key for the same reason; the list of
        // library folders isn't, as the form draws it from each answer (a sync adds a folder unticked
        // without taking back what is typed).
        <MediaFoldersForm
          key={JSON.stringify([saves, data.paths, data.replacementEnabled, storedTicks(data)])}
          roots={data}
          saving={update.isPending}
          saved={saved}
          error={saveError}
          onEdit={() => setSaved(false)}
          onSave={(request) => void save(request)}
        />
      ) : null}
    </div>
  )
}

// ── the form ──

interface MediaFoldersFormProps {
  /** The stored settings the form starts from: the folders, the switch and the ticks. */
  roots: AllowedRoots
  saving: boolean
  /** The last save went through; shown until the next change. */
  saved: boolean
  /** The API's refusal of the last save. */
  error: string | null
  onEdit: () => void
  onSave: (request: UpdateAllowedRootsRequest) => void
}

/**
 * MAM-24 — one form around both cards, then the refusal and the one Save row under them (MAM-16's
 * "error above Save"): a second card inside the first one's form would nest one card in another.
 */
function MediaFoldersForm({ roots, saving, saved, error, onEdit, onSave }: MediaFoldersFormProps) {
  const { t } = useTranslation('mediaFolders')
  const titleId = useId()
  const [folders, setFolders] = useState(() => roots.paths.map(folderDraft))
  const [replacementEnabled, setReplacementEnabled] = useState(roots.replacementEnabled)
  const [picked, setPicked] = useState<ReadonlySet<string>>(() => new Set(storedTicks(roots)))

  const change = (next: FolderDraft[]) => {
    setFolders(next)
    onEdit()
  }

  const switchReplacement = (on: boolean) => {
    setReplacementEnabled(on)
    onEdit()
  }

  const tick = (appPath: string, on: boolean) => {
    const next = new Set(picked)
    if (on) next.add(appPath)
    else next.delete(appPath)
    setPicked(next)
    onEdit()
  }

  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    onSave({
      // As typed, blank rows included: the server's sentence names what is wrong with one.
      paths: folders.map((folder) => folder.path.trim()),
      replacementEnabled,
      // Every tick the list shows, in its order, stale ones included until they are unticked: the
      // save replaces the stored ticks, so one left out would be dropped unseen.
      replacementFolders: roots.libraryFolders
        .filter((folder) => picked.has(folder.appPath))
        .map((folder) => folder.appPath),
    })
  }

  return (
    <form className="media-folders-form" noValidate onSubmit={submit}>
      <section className="ui-card" aria-labelledby={titleId}>
        <div className="ui-card-header">
          <h2 id={titleId} className="ui-card-title">{t('listTitle')}</h2>
          <p className="ui-card-description">{t('listDescription')}</p>
        </div>
        <div className="ui-card-content media-folders-roots">
          {folders.length === 0 ? (
            <p className="settings-muted">{t('empty')}</p>
          ) : (
            <ul className="media-folders-list">
              {folders.map((folder, index) => (
                <li key={folder.key} className="media-folders-row">
                  <Input
                    value={folder.path}
                    aria-label={t('folder', { number: index + 1 })}
                    placeholder={t('placeholder')}
                    autoComplete="off"
                    autoCapitalize="none"
                    spellCheck={false}
                    onChange={(event) => change(folders.map((other) =>
                      other.key === folder.key ? { ...other, path: event.target.value } : other))}
                  />
                  <Button
                    type="button"
                    variant="outline"
                    size="icon"
                    className="ui-button-danger"
                    title={t('remove', { number: index + 1 })}
                    aria-label={t('remove', { number: index + 1 })}
                    onClick={() => change(folders.filter((other) => other.key !== folder.key))}
                  >
                    <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />
                  </Button>
                </li>
              ))}
            </ul>
          )}
          <Button type="button" variant="outline" size="sm" onClick={() => change([...folders, folderDraft('')])}>
            <Plus className="h-3.5 w-3.5" aria-hidden="true" />
            {t('add')}
          </Button>
        </div>
      </section>
      <ReplacementCard
        roots={roots}
        replacementEnabled={replacementEnabled}
        picked={picked}
        onSwitch={switchReplacement}
        onTick={tick}
      />
      {/* MAM-16 — above Save, inside the form; the rows, the switch and the ticks keep what was
          picked to correct. */}
      {error && <Banner>{error}</Banner>}
      <div className="media-folders-save">
        <Button type="submit" disabled={saving}>{saving ? t('saving') : t('save')}</Button>
        {saved && <span className="media-folders-saved" role="status">{t('saved')}</span>}
      </div>
    </form>
  )
}

// ── replacing copies ──

interface ReplacementCardProps {
  roots: AllowedRoots
  replacementEnabled: boolean
  /** The app paths ticked in the form, stored or not. */
  picked: ReadonlySet<string>
  onSwitch: (on: boolean) => void
  onTick: (appPath: string, on: boolean) => void
}

/**
 * MAM-24 — the replacement gates of ADR 0033: the master switch and the library folders it may touch.
 * The ticks stay editable while the switch is off, as both save together, so the owner can pick the
 * folders before switching it on. Each row's status comes from the server, against the stored media
 * folders rather than the ones being typed.
 */
function ReplacementCard({ roots, replacementEnabled, picked, onSwitch, onTick }: ReplacementCardProps) {
  const { t } = useTranslation('mediaFolders')
  const titleId = useId()
  const switchHintId = useId()
  const foldersHintId = useId()
  const unmapped = roots.unmappedLibraryFolderCount

  return (
    <section className="ui-card" aria-labelledby={titleId}>
      <div className="ui-card-header">
        <h2 id={titleId} className="ui-card-title">{t('replace.title')}</h2>
        <p className="ui-card-description">{t('replace.description')}</p>
      </div>
      <div className="ui-card-content media-folders-replace">
        <div className="ui-field">
          <label className="settings-check">
            <input
              type="checkbox"
              checked={replacementEnabled}
              aria-describedby={switchHintId}
              onChange={(event) => onSwitch(event.target.checked)}
            />
            {t('replace.switch')}
          </label>
          <p id={switchHintId} className="ui-hint">{t('replace.switchHint')}</p>
        </div>
        <fieldset className="media-folders-library" aria-describedby={foldersHintId}>
          <legend className="ui-label">{t('replace.folders')}</legend>
          <p id={foldersHintId} className="ui-hint">{t('replace.foldersHint')}</p>
          {roots.libraryFolders.length > 0 && (
            <ul className="media-folders-library-list">
              {roots.libraryFolders.map((folder) => (
                <LibraryFolderRow
                  key={folder.appPath}
                  folder={folder}
                  ticked={picked.has(folder.appPath)}
                  onTick={(on) => onTick(folder.appPath, on)}
                />
              ))}
            </ul>
          )}
          {/* MAM-24 — "sync a library first" only when nothing is stored at all: folders the mappings
              can't place say so instead, as syncing again wouldn't list them. */}
          {roots.libraryFolders.length === 0 && unmapped === 0 && (
            <p className="settings-muted">{t('replace.noFolders')}</p>
          )}
          {unmapped > 0 && (
            <p className="settings-muted">{t('replace.unmapped', { count: unmapped, value: formatNumber(unmapped) })}</p>
          )}
        </fieldset>
      </div>
    </section>
  )
}

/**
 * One library folder with its tick. MAM-24 — a folder stored as ticked is never disabled, so a tick that
 * lost its effect (outside the media folders, no longer a library folder) can always be taken back;
 * one stored unticked can be ticked only where a tick counts. The reason is a line of its own under
 * the path, not a tooltip: a phone has none.
 */
function LibraryFolderRow({ folder, ticked, onTick }: {
  folder: ReplacementFolder
  /** As ticked in the form. */
  ticked: boolean
  onTick: (on: boolean) => void
}) {
  const { t } = useTranslation('mediaFolders')
  const id = useId()
  const disabled = !folder.ticked && folder.status !== 'Available'
  // Worded for the box as it stands, so unticking an outside folder doesn't leave "this tick" behind.
  const note = folder.status === 'OutsideMediaFolders'
    ? t(ticked ? 'replace.outsideTicked' : 'replace.outside')
    : folder.status === 'NoLongerALibraryFolder'
      ? t('replace.noLonger')
      : null
  const describedBy = [
    folder.libraryNames.length > 0 ? `${id}-names` : null,
    note === null ? null : `${id}-note`,
  ].filter((part) => part !== null)

  return (
    <li>
      <label className="settings-check">
        <input
          type="checkbox"
          checked={ticked}
          disabled={disabled}
          aria-labelledby={`${id}-path`}
          aria-describedby={describedBy.length === 0 ? undefined : describedBy.join(' ')}
          onChange={(event) => onTick(event.target.checked)}
        />
        <span className="media-folders-library-body">
          <span id={`${id}-path`} className="media-folders-path">{folder.appPath}</span>
          {folder.libraryNames.length > 0 && (
            <span id={`${id}-names`} className="ui-hint">
              {t('replace.libraries', { count: folder.libraryNames.length, names: folder.libraryNames })}
            </span>
          )}
          {note !== null && <span id={`${id}-note`} className="ui-hint">{note}</span>}
        </span>
      </label>
    </li>
  )
}
