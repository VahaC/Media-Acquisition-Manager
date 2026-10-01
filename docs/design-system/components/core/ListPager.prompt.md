Pager under every server-paged list: range ("26–50 of 812") left, numbered pages ‹ 1 … 4 5 6 … 33 › centred, Per page select right. Under 640px of its own width (container query) the pages take a row of their own, with range + Per page on the row below.

```jsx
<ListPager page={5} pages={33} pageSize={25} total={812} onPage={setPage} onPageSize={setSize} />
```

First and last page always shown, `around` pages either side of the current one, gaps collapse to "…". Current page: 12% primary tint + primary border. Arrows are icon-only ghost buttons. A single page still shows "1" with both arrows disabled.
