import { useTranslation } from 'react-i18next'
import { FileLines } from '@/components/library/FileLines'
import type { LibraryVersion } from '@/lib/types'
import '@/styles/settings-list.css' // .settings-muted — the "no file reported" line
import '@/styles/library.css' // .library-version* — the block of one version and its label

/**
 * MAM-8 — one version of a work as an item of a `.library-versions` list: the service's edition
 * label, when it has one, over each of its files.
 *
 * MAM-12 — moved out of LibraryWorkCard, so the search page's Details dialog lists a library copy's
 * versions exactly as the Library page does.
 *
 * MAM-16 — the cards show versions as rows or a table now (VersionRows, VersionTable); this list
 * stays the Details dialog's, which puts every copy's files beside the release at once.
 */
export function VersionItem({ version }: { version: LibraryVersion }) {
  const { t } = useTranslation('library')

  return (
    <li className="library-version">
      {/* The service's own label, when it offers a choice of versions of one movie. */}
      {version.editionLabel && <p className="library-version-label">{version.editionLabel}</p>}
      {version.files.length === 0
        ? <p className="settings-muted">{t('version.noFile')}</p>
        : version.files.map((file) => <FileLines key={file.id} file={file} />)}
    </li>
  )
}
