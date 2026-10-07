import { useTranslation } from 'react-i18next'
import { DuplicateHash } from '@/components/duplicates/DuplicateHash'
import { DuplicateHolders } from '@/components/duplicates/DuplicateHolders'
import { editionsText, identityText } from '@/lib/duplicates'
import type { LibraryDuplicateFile } from '@/lib/types'
import '@/styles/duplicates.css' // .duplicates-facts — the facts of a file, a label beside each

/**
 * MAM-21 — what the Duplicates page adds about one file of a group, in its open row under the Library's
 * own file lines: the file its name leads to (device, inode, links) or that the app hasn't looked yet,
 * its SHA-256 when hashed, the editions its names say it is, and who holds it. The comparison table
 * says the same in its rows of the same names.
 */
export function DuplicateFileFacts({ file }: { file: LibraryDuplicateFile }) {
  const { t } = useTranslation('duplicates')

  return (
    <dl className="duplicates-facts">
      <div>
        <dt>{t('facts.identity')}</dt>
        <dd>{identityText(file)}</dd>
      </div>
      <div>
        <dt>{t('facts.sha256')}</dt>
        <dd><DuplicateHash value={file.sha256} /></dd>
      </div>
      <div>
        <dt>{t('facts.editions')}</dt>
        <dd>{editionsText(file.editions)}</dd>
      </div>
      <div>
        <dt>{t('facts.heldBy')}</dt>
        <dd><DuplicateHolders works={file.works} /></dd>
      </div>
    </dl>
  )
}
