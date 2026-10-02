/** A page number the pager shows, or a gap standing for the pages it leaves out between two numbers. */
export type PagerItem = number | 'gap'

/**
 * MAM-16 — the page numbers a pager shows for `page` of `pages`: always the first and the last
 * page, plus the current page and one on each side, with a gap where pages are left out
 * (`1 … 4 5 6 … 33`). A long list keeps a row of at most seven buttons instead of one per page.
 *
 * A gap that would hide a single page shows that number instead (`1 2 3 4 5 … 33`, not
 * `1 … 3 4 5 … 33`): the "…" takes nearly as much room as the number, and hides the page for no gain.
 * Only pages from 1 to `pages` are listed, so a page past the end (a list that shrank under the
 * person) lists the pages that exist without a current one.
 */
export function pageNumbers(page: number, pages: number): PagerItem[] {
  const shown = [...new Set([1, page - 1, page, page + 1, pages])]
    .filter((number) => number >= 1 && number <= pages)
    .sort((a, b) => a - b)

  const items: PagerItem[] = []
  shown.forEach((number, index) => {
    if (index > 0) {
      const hidden = number - shown[index - 1] - 1
      if (hidden === 1) items.push(number - 1)
      else if (hidden > 1) items.push('gap')
    }
    items.push(number)
  })
  return items
}
