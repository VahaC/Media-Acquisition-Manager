import { useId } from 'react'
import { ChevronLeft, ChevronRight } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Button } from '@/components/ui/button'
import '@/styles/list-pager.css' // .list-pager* — the status, page-size select and buttons

interface ListPagerProps {
  /** The page on screen, from 1. */
  page: number
  /** How many pages the list has; at least 1. */
  pages: number
  pageSize: number
  pageSizes: readonly number[]
  onPage: (page: number) => void
  onPageSize: (size: number) => void
}

/**
 * The pager under a server-paged list: "Page 2 of 5", the page-size select, and Previous/Next,
 * which collapse to icons on a phone. It holds no state; the page asks the server for what it picks.
 *
 * MAM-7 — moved out of the MAM-4 Users page so every settings list pages the same way.
 */
export function ListPager({ page, pages, pageSize, pageSizes, onPage, onPageSize }: ListPagerProps) {
  const { t } = useTranslation('common')
  const pageSizeId = useId()

  return (
    <nav className="list-pager" aria-label={t('pager.label')}>
      <span className="list-pager-status">{t('pager.status', { page, pages })}</span>
      {/* MAM-4 — the label sits beside the select, tied by id: a label wrapping it made the
          browser read the select's own value into its name, so it was announced as "25". */}
      <div className="list-page-size">
        <label htmlFor={pageSizeId}>{t('pager.pageSize')}</label>
        <select
          id={pageSizeId}
          className="ui-input"
          value={pageSize}
          onChange={(event) => onPageSize(Number(event.target.value))}
        >
          {pageSizes.map((size) => <option key={size} value={size}>{size}</option>)}
        </select>
      </div>
      <div className="list-pager-buttons">
        <Button
          type="button"
          variant="outline"
          size="sm"
          title={t('pager.previous')}
          disabled={page <= 1}
          onClick={() => onPage(page - 1)}
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
          onClick={() => onPage(page + 1)}
        >
          <span className="label-text">{t('pager.next')}</span>
          <ChevronRight className="h-3.5 w-3.5" aria-hidden="true" />
        </Button>
      </div>
    </nav>
  )
}
