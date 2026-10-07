import { useTranslation } from 'react-i18next'
import '@/styles/duplicates.css' // .duplicates-hash — a SHA-256 in monospace that breaks anywhere

/**
 * MAM-21 — a file's SHA-256 as the app hashed it, or that it isn't hashed: the app hashes only the files a
 * byte-identical copy could hide among (ADR 0022). Sixty-four characters without a space are wider than a
 * phone, so it breaks anywhere rather than widening the card.
 */
export function DuplicateHash({ value }: { value: string | null }) {
  const { t } = useTranslation('duplicates')

  if (value === null) return t('facts.notHashed')
  return <code className="duplicates-hash">{value}</code>
}
