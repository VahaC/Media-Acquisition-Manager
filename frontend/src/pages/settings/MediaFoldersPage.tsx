import { useId, useState, type FormEvent } from 'react'
import { CircleAlert, Plus, Trash2 } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { useAllowedRoots, useUpdateAllowedRoots } from '@/lib/queries'
import { getApiErrorMessage } from '@/lib/utils'
import '@/styles/settings-list.css' // .settings-* — the header, banner, muted and error lines every settings page shares
import '@/styles/media-folders.css' // .media-folders-* — the folder rows and the Save row

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

/**
 * MAM-9 — Settings → Media folders, owner-only: the folders inside the app where it may read the
 * library's files. None by default, and then the app reads nothing. One path per row, like the path
 * mappings of an integration, and one Save that sends the whole list; the server checks it and its
 * sentence names the folder that is wrong. Saving doesn't inspect anything.
 *
 * A non-owner who opens the address gets the API's 403 sentence in place of the form.
 */
export function MediaFoldersPage() {
  const { t } = useTranslation(['mediaFolders', 'common'])
  const { data, error, isLoading } = useAllowedRoots()
  const update = useUpdateAllowedRoots()
  const [saveError, setSaveError] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)
  const [saves, setSaves] = useState(0)

  const save = async (paths: string[]) => {
    setSaveError(null)
    setSaved(false)
    try {
      await update.mutateAsync(paths)
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
        <p className="settings-banner" role="alert">
          <CircleAlert className="h-3.5 w-3.5 inline" aria-hidden="true" />{' '}
          {t('refreshFailed')}{' '}
          {getApiErrorMessage(error)}
        </p>
      )}

      {isLoading ? (
        <p className="settings-muted">{t('common:loading')}</p>
      ) : error && !data ? (
        <p className="settings-error" role="alert">{getApiErrorMessage(error, t('loadFailed'))}</p>
      ) : data ? (
        // Mounted afresh after each save, so the rows show the folders as stored (in canonical form)
        // rather than as typed, and when the stored folders change elsewhere. Not for every answer of
        // the server: a refetch of the same folders (the network back, the page opened again) would
        // throw away what is typed. A refused save changes nothing, and the typed rows stay to correct.
        <FoldersForm
          key={`${saves}\n${data.paths.join('\n')}`}
          paths={data.paths}
          saving={update.isPending}
          saved={saved}
          error={saveError}
          onEdit={() => setSaved(false)}
          onSave={(paths) => void save(paths)}
        />
      ) : null}
    </div>
  )
}

// ── the form ──

interface FoldersFormProps {
  /** The stored folders the rows start from. */
  paths: string[]
  saving: boolean
  /** The last save went through; shown until the next change. */
  saved: boolean
  /** The API's refusal of the last save. */
  error: string | null
  onEdit: () => void
  onSave: (paths: string[]) => void
}

function FoldersForm({ paths, saving, saved, error, onEdit, onSave }: FoldersFormProps) {
  const { t } = useTranslation('mediaFolders')
  const titleId = useId()
  const [folders, setFolders] = useState(() => paths.map(folderDraft))

  const change = (next: FolderDraft[]) => {
    setFolders(next)
    onEdit()
  }

  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    // As typed, blank rows included: the server's sentence names what is wrong with one.
    onSave(folders.map((folder) => folder.path.trim()))
  }

  return (
    <section className="ui-card" aria-labelledby={titleId}>
      <div className="ui-card-header">
        <h2 id={titleId} className="ui-card-title">{t('listTitle')}</h2>
        <p className="ui-card-description">{t('listDescription')}</p>
      </div>
      <form className="ui-card-content media-folders-form" noValidate onSubmit={submit}>
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
        {error && <p className="settings-error" role="alert">{error}</p>}
        <div className="media-folders-save">
          <Button type="submit" disabled={saving}>{saving ? t('saving') : t('save')}</Button>
          {saved && <span className="media-folders-saved" role="status">{t('saved')}</span>}
        </div>
      </form>
    </section>
  )
}
