import { useId } from 'react'
import { ChevronLeft, ChevronRight } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Button } from '@/components/ui/button'
import { pageNumbers } from '@/lib/page-numbers'
import '@/styles/list-pager.css' // .list-pager* — the range, page numbers and page-size select

interface ListPagerProps {
  /** The page on screen, from 1. */
  page: number
  /** How many pages the list has; at least 1. */
  pages: number
  pageSize: number
  pageSizes: readonly number[]
  /**
   * How many items the whole list has, from the list response. With it the status reads as the
   * range on screen ("26–50 of 812") and a list with no items shows no pager; without it the
   * status is "Page 2 of 5".
   */
  totalCount?: number
  onPage: (page: number) => void
  onPageSize: (size: number) => void
}

/**
 * The pager under a server-paged list: the range on screen on the left, ‹ the page numbers › in the
 * middle, and the page-size select on the right. It holds no state; the page asks the server for
 * what it picks.
 *
 * MAM-7 — moved out of the MAM-4 Users page so every settings list pages the same way.
 * MAM-16 — the approved layout: the range and numbered pages replace "Page 2 of 5" and
 * Previous/Next, which are now icon buttons named by aria-label and title.
 */
export function ListPager({ page, pages, pageSize, pageSizes, totalCount, onPage, onPageSize }: ListPagerProps) {
  const { t } = useTranslation('common')
  const pageSizeId = useId()

  if (totalCount === 0) return null

  const from = (page - 1) * pageSize + 1
  // A page past the end (another session deleted rows under this one; the server doesn't clamp)
  // would read "51–50 of 50", so it says which page it is instead until the list steps back.
  const status = totalCount !== undefined && from <= totalCount
    ? t('pager.range', { from, to: Math.min(page * pageSize, totalCount), total: totalCount })
    : t('pager.status', { page, pages })

  return (
    <nav className="list-pager" aria-label={t('pager.label')}>
      <span className="list-pager-status">{status}</span>
      <div className="list-pager-buttons">
        <Button
          type="button"
          variant="ghost"
          size="icon"
          aria-label={t('pager.previous')}
          title={t('pager.previous')}
          disabled={page <= 1}
          onClick={() => onPage(page - 1)}
        >
          <ChevronLeft className="h-4 w-4" aria-hidden="true" />
        </Button>
        {pageNumbers(page, pages).map((item, index) =>
          item === 'gap' ? (
            <span key={`gap-${index}`} className="list-pager-gap" aria-hidden="true">{t('pager.gap')}</span>
          ) : (
            // The current page stays a focusable button that does nothing, rather than a disabled
            // one: disabled would drop it from the tab order and from what a screen reader lists.
            <button
              key={item}
              type="button"
              className="list-pager-page"
              aria-label={t('pager.page', { page: item })}
              aria-current={item === page ? 'page' : undefined}
              onClick={() => { if (item !== page) onPage(item) }}
            >
              {item}
            </button>
          ),
        )}
        <Button
          type="button"
          variant="ghost"
          size="icon"
          aria-label={t('pager.next')}
          title={t('pager.next')}
          disabled={page >= pages}
          onClick={() => onPage(page + 1)}
        >
          <ChevronRight className="h-4 w-4" aria-hidden="true" />
        </Button>
      </div>
      {/* MAM-4 — the label sits beside the select, tied by id: a label wrapping it made the
          browser read the select's own value into its name, so it was announced as "25". */}
      <div className="list-pager-size">
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
    </nav>
  )
}
