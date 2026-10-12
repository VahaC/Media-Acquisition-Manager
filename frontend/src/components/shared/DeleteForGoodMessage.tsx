import { useTranslation } from 'react-i18next'
import { FileSizeList } from '@/components/shared/FileSizeList'
import { formatFileSize, formatNumber } from '@/lib/format'
import type { ReplacementName } from '@/lib/types'

interface DeleteForGoodMessageProps {
  /** The names it deletes: what the quarantine holds of it now. */
  names: ReplacementName[]
  /** The room it frees, as the server worked it out. */
  freesBytes: number
}

/**
 * The message of Delete for good's confirmation: every name it deletes, where it was and its size, then the
 * room it frees as the server worked it out — a name with another name (a hardlink) frees nothing, and the
 * message says how many have one. The list is left-aligned and its paths break anywhere: the dialog is
 * narrow, and centres its text on a phone.
 *
 * MAM-40 — moved out of MAM-26's ReplacementSection, so deleting a copy removed on the Duplicates page asks
 * as deleting a replacement's old copies does. MAM-41 — every caller asks in the wide confirmation
 * (ConfirmOptions.wide), half as wide again as a plain one, so a path fits on fewer lines.
 */
export function DeleteForGoodMessage({ names, freesBytes }: DeleteForGoodMessageProps) {
  const { t } = useTranslation('jobs')
  const otherNames = names.filter((name) => name.otherNames).length

  return (
    <>
      <p>{t('oldCopies.deleteForGood.lead', { count: names.length, value: formatNumber(names.length) })}</p>
      <FileSizeList files={names.map((name) => ({ id: name.id, path: name.originalPath, sizeBytes: name.sizeBytes }))} />
      <p>{t('oldCopies.deleteForGood.frees', { size: formatFileSize(freesBytes) })}</p>
      {otherNames > 0 && (
        <p>{t('oldCopies.deleteForGood.otherNames', { count: otherNames, value: formatNumber(otherNames) })}</p>
      )}
    </>
  )
}
