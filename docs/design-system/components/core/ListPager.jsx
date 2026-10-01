import React from 'react';
import { Button } from './Button.jsx';
import { Icon } from './Icon.jsx';

/** Page numbers to show: always first and last, a window around the current page, gaps as '…'. */
function pageList(page, pages, around) {
  const set = new Set([1, pages]);
  for (let p = page - around; p <= page + around; p++) if (p >= 1 && p <= pages) set.add(p);
  const sorted = [...set].sort((a, b) => a - b);
  const out = [];
  sorted.forEach((p, i) => {
    if (i && p - sorted[i - 1] === 2) out.push(p - 1);
    else if (i && p - sorted[i - 1] > 2) out.push('…' + i);
    out.push(p);
  });
  return out;
}

export function ListPager({ page = 1, pages: pagesProp = 1, pageSize: sizeProp = 25, pageSizes = [10, 25, 50, 100], total, onPage, onPageSize, around = 1 }) {
  const [own, setOwn] = React.useState(page);
  React.useEffect(() => setOwn(page), [page]);
  const [ownSize, setOwnSize] = React.useState(sizeProp);
  React.useEffect(() => setOwnSize(sizeProp), [sizeProp]);
  const pageSize = onPageSize ? sizeProp : ownSize;
  // Without onPageSize (demos) the pager works out its own page count from total.
  const pages = !onPageSize && total != null ? Math.max(1, Math.ceil(total / pageSize)) : pagesProp;
  const cur = onPage ? page : own;
  const go = (p) => { if (p < 1 || p > pages || p === cur) return; if (onPage) onPage(p); else setOwn(p); };
  const changeSize = (s) => { if (onPageSize) onPageSize(s); else { setOwnSize(s); setOwn(1); } };
  const btn = (on) => ({
    minWidth: 32, height: 32, padding: '0 0.5rem', display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
    borderRadius: 'var(--radius-md)', fontSize: '0.8125rem', fontWeight: on ? 600 : 500, fontVariantNumeric: 'tabular-nums', cursor: on ? 'default' : 'pointer',
    border: '1px solid ' + (on ? 'var(--primary)' : 'transparent'),
    background: on ? 'var(--tint-selected)' : 'transparent', color: on ? 'var(--foreground)' : 'var(--muted-foreground)',
  });
  const from = (cur - 1) * pageSize + 1;
  const to = total != null ? Math.min(cur * pageSize, total) : cur * pageSize;
  return (
    <nav className="ui-pager" aria-label="Pages">
      <div className="ui-pager-grid">
      <span className="ui-pager-status">{total != null ? from + '–' + to + ' of ' + total : 'Page ' + cur + ' of ' + pages}</span>
      {pages >= 1 ? (
        <div className="ui-pager-buttons" style={{ alignItems: 'center', gap: 2 }}>
          <Button variant="ghost" size="icon" icon="ChevronLeft" title="Previous page" disabled={cur <= 1} onClick={() => go(cur - 1)} />
          {pageList(cur, pages, around).map((p) => typeof p === 'string'
            ? <span key={p} style={{ minWidth: 24, textAlign: 'center', color: 'var(--subtle-foreground)' }}>…</span>
            : <button key={p} type="button" aria-current={p === cur ? 'page' : undefined} aria-label={'Page ' + p} style={btn(p === cur)}
                onMouseEnter={(e) => { if (p !== cur) e.currentTarget.style.background = 'var(--accent)'; }}
                onMouseLeave={(e) => { if (p !== cur) e.currentTarget.style.background = 'transparent'; }}
                onClick={() => go(p)}>{p}</button>)}
          <Button variant="ghost" size="icon" icon="ChevronRight" title="Next page" disabled={cur >= pages} onClick={() => go(cur + 1)} />
        </div>
      ) : null}
      <div className="ui-pager-size">
        <label>Per page</label>
        <select className="ui-input" value={pageSize} onChange={(e) => changeSize(Number(e.target.value))}>
          {pageSizes.map((s) => <option key={s} value={s}>{s}</option>)}
        </select>
      </div>
      </div>
    </nav>
  );
}
