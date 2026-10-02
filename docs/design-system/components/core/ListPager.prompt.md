Pager under every server-paged list: range ("26–50 of 812") left, numbered pages ‹ 1 … 4 5 6 … 33 › centred, Per page select right. Under 640px of its own width (container query) the pages take a row of their own, with range + Per page on the row below.

```jsx
<ListPager page={5} pages={33} pageSize={25} total={812} onPage={setPage} onPageSize={setSize} />
```

First and last page always shown, `around` pages either side of the current one, gaps collapse to "…". Current page: 12% primary tint + primary border. Arrows are icon-only ghost buttons. A single page still shows "1" with both arrows disabled.

In the app (`frontend/src/components/shared/ListPager.tsx`, MAM-16) the prop is `totalCount`, which every list passes from its response; without it the status reads "Page 2 of 5", and with 0 the pager isn't shown. The two-row switch is `@media (max-width: 43rem)` on the window rather than a container query — every pager sits in the 64rem column with 1.5rem padding, so 43rem is where the pager is 640px wide — and on the narrowest phones the numbers wrap.
