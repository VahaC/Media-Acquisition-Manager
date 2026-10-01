export interface ListPagerProps {
  /** Current page, from 1 */
  page: number;
  pages: number;
  pageSize: number;
  pageSizes?: number[];
  /** Total item count; shows "26–50 of 812" instead of "Page 2 of 33" */
  total?: number;
  /** Pages shown either side of the current one before collapsing to … (default 1) */
  around?: number;
  /** Omit to let the pager keep its own page (demos) */
  onPage?: (p: number) => void;
  onPageSize?: (s: number) => void;
}
export declare function ListPager(props: ListPagerProps): any;
