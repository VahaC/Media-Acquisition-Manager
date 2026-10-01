/* A library work with its versions as rows (1b, default) or as a comparison table (1c) — used by "In your library"
   on Search and by every card on Library. One per-device choice for both pages, in localStorage `mam:versionsView`. */
const LM_VIEW_KEY = 'mam:versionsView';

function lmShort(v) {
  const m = v.files[0].media;
  if (!m) return { res: null, codec: null, range: null, audio: '', subs: '' };
  const w = Number(m.resolution.split('×')[0]);
  return {
    res: w >= 3800 ? '2160p' : w >= 1900 ? '1080p' : m.resolution,
    codec: m.codec.split(' ')[0], range: m.range,
    audio: m.audio.map((a) => a.split(' · ')[0].slice(0, 3)).join(' · '),
    subs: m.subs.map((a) => a.split(' · ')[0].slice(0, 3)).join(' · '),
  };
}

function ViewToggle({ value, onChange }) {
  const { Icon } = window.MAM_DS;
  const opt = [['rows', 'Rows3', 'Rows'], ['table', 'Table2', 'Table']];
  return (
    <div role="radiogroup" aria-label="Show versions as" style={{ display: 'inline-flex', padding: 2, gap: 2, background: 'var(--secondary)', border: '1px solid var(--border)', borderRadius: 'var(--radius-md)' }}>
      {opt.map(([v, icon, label]) => { const on = value === v; return (
        <button key={v} type="button" role="radio" aria-checked={on} title={'Versions as ' + label.toLowerCase()} onClick={() => onChange(v)}
          style={{ display: 'inline-flex', alignItems: 'center', gap: 6, height: 28, padding: '0 0.625rem', fontSize: 12, fontWeight: 500, cursor: 'pointer', borderRadius: 'var(--radius-sm)',
            background: on ? 'var(--card)' : 'transparent', color: on ? 'var(--foreground)' : 'var(--muted-foreground)', boxShadow: on ? 'var(--shadow-sm)' : 'none' }}>
          <Icon name={icon} size={14} />{label}
        </button>); })}
    </div>
  );
}

function VersionRows({ work }) {
  const { Icon, ChipRow, Facts } = window.MAM_DS;
  const single = work.versions.length === 1;
  const [openState, setOpen] = React.useState(null);
  const open = single ? 0 : openState;
  return (
    <ul style={{ display: 'grid', border: '1px solid var(--border)', borderRadius: 'var(--radius-md)', overflow: 'hidden' }}>
      {work.versions.map((v, i) => { const s = lmShort(v), f = v.files[0], on = open === i; return (
        <li key={i} style={{ borderTop: i ? '1px solid var(--border)' : 0 }}>
          <button type="button" aria-expanded={single ? undefined : on} disabled={single} onClick={() => setOpen(on ? null : i)}
            style={{ width: '100%', display: 'grid', gridTemplateColumns: single ? '6rem minmax(0,1fr) auto' : '16px 6rem minmax(0,1fr) auto', gap: '0.75rem', alignItems: 'center', padding: '0.5rem 0.75rem', cursor: single ? 'default' : 'pointer', textAlign: 'left', background: 'transparent' }}>
            {single ? null : <Icon name={on ? 'ChevronDown' : 'ChevronRight'} size={16} style={{ color: 'var(--muted-foreground)' }} />}
            <strong style={{ fontWeight: 600 }}>{v.label || 'Version ' + (i + 1)}</strong>
            <span style={{ display: 'flex', flexWrap: 'wrap', gap: 4, fontSize: 12 }}>
              {f.appPath === null
                ? <span style={{ display: 'inline-flex', alignItems: 'center', gap: 4 }}><Icon name="TriangleAlert" size={12} style={{ color: 'var(--status-attention)' }} />No path in this app</span>
                : s.res ? [s.res, s.codec, s.range, s.audio].filter(Boolean).map((x) => <span key={x} className="ui-chip">{x}</span>) : <span style={{ color: 'var(--muted-foreground)' }}>Not inspected yet</span>}
            </span>
            <span style={{ fontVariantNumeric: 'tabular-nums' }}>{f.size || 'Size unknown'}</span>
          </button>
          {on ? (
            <div style={{ padding: single ? '0.25rem 0.75rem 0.75rem' : '0.25rem 0.75rem 0.75rem 2.5rem', display: 'grid', gap: 4 }}>
              <p className="mono" style={{ fontWeight: 500, overflowWrap: 'anywhere' }}>{f.name}</p>
              <p className="mono" style={{ fontSize: 12, color: 'var(--muted-foreground)', overflowWrap: 'anywhere' }}>{f.folder}</p>
              {f.appPath ? <p className="mono" style={{ fontSize: 12, color: 'var(--muted-foreground)', overflowWrap: 'anywhere' }}>In this app: {f.appPath}</p> : null}
              {f.appPath === null ? <p className="ui-status-line" data-status="warning"><Icon name="TriangleAlert" size={14} /><span>This file has no path in this app. Check the path mappings under Integrations.</span></p> : null}
              <p className="ui-facts"><span>{f.size || 'Size unknown'}</span><span>{f.location ? 'Library: ' + f.location : 'Library unknown'}</span></p>
              {f.media ? (<>
                <Facts verified items={[f.media.duration, f.media.resolution, f.media.codec, f.media.range, f.media.bitrate]} />
                <ChipRow label="Audio" chips={f.media.audio} empty="No audio tracks" />
                <ChipRow label="Subtitles" chips={f.media.subs} empty="No subtitles" />
              </>) : <p className="ui-facts"><span>{f.appPath === null ? 'Not inspected: the app has no path to this file.' : 'Not inspected yet'}</span></p>}
            </div>) : null}
        </li>); })}
    </ul>
  );
}

function VersionTable({ work }) {
  const vs = work.versions.map((v, i) => ({ label: v.label || 'Version ' + (i + 1), f: v.files[0], m: v.files[0].media || {} }));
  const u = (x, fallback) => x || fallback;
  const rows = [
    ['Resolution', (x) => u(x.m.resolution, 'Unknown')], ['Video', (x) => u(x.m.codec, 'Unknown')], ['HDR', (x) => u(x.m.range, 'Unknown')],
    ['Bit rate', (x) => u(x.m.bitrate, 'Unknown')], ['Duration', (x) => u(x.m.duration, 'Unknown')],
    ['Audio', (x) => (x.m.audio || []).join('\n') || 'No audio tracks'], ['Subtitles', (x) => (x.m.subs || []).join('\n') || 'No subtitles'],
    ['Size', (x) => u(x.f.size, 'Unknown')], ['File', (x) => x.f.name],
  ];
  const cell = { padding: '0.5rem 0.75rem', borderTop: '1px solid var(--border)', overflowWrap: 'anywhere', whiteSpace: 'pre-line' };
  return (<>
    <div style={{ display: 'grid', gridTemplateColumns: `7rem repeat(${vs.length}, minmax(0,1fr))`, border: '1px solid var(--border)', borderRadius: 'var(--radius-md)', overflow: 'hidden' }}>
      <span style={{ padding: '0.5rem 0.75rem', background: 'var(--muted)' }} />
      {vs.map((x) => <span key={x.label} style={{ padding: '0.5rem 0.75rem', background: 'var(--muted)', fontWeight: 600 }}>{x.label}</span>)}
      {rows.map(([k, fn]) => <React.Fragment key={k}>
        <span style={{ ...cell, color: 'var(--muted-foreground)' }}>{k}</span>
        {vs.map((x) => <span key={x.label} className={k === 'File' ? 'mono' : undefined} style={{ ...cell, fontSize: k === 'File' ? 12 : undefined }}>{fn(x)}</span>)}
      </React.Fragment>)}
    </div>
    <p style={{ fontSize: 12, color: 'var(--muted-foreground)' }}>Every value was read from the file by ffprobe.</p>
  </>);
}

function LibraryMatchCard({ work, match, headingLevel = 3 }) {
  const H = headingLevel === 2 ? 'h2' : 'h3';
  const { Badge, MatchBadges } = window.MAM_DS;
  const [view, setView] = React.useState(() => { try { return localStorage.getItem(LM_VIEW_KEY) || 'rows'; } catch (e) { return 'rows'; } });
  const pick = (v) => { setView(v); try { localStorage.setItem(LM_VIEW_KEY, v); } catch (e) {} };
  return (
    <article className="ui-card" style={{ padding: '1.25rem 1.5rem', display: 'grid', gap: '0.875rem' }}>
      <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'flex-start', justifyContent: 'space-between', gap: '0.5rem 1rem' }}>
        <div style={{ display: 'grid', gap: 4, minWidth: 0 }}>
          <H style={{ fontSize: '1.125rem', fontWeight: 600, lineHeight: 1.25, overflowWrap: 'anywhere' }}>{work.title || 'Unknown title'}</H>
          <p style={{ display: 'flex', flexWrap: 'wrap', gap: '0.25rem 0.75rem', color: 'var(--muted-foreground)' }}>
            {work.originalTitle ? <span>Original title: {work.originalTitle}</span> : null}<span>{work.year || 'Year unknown'}</span>{work.source ? <span>From {work.source}</span> : null}</p>
          {match ? <MatchBadges evidence={match.evidence} schemes={match.schemes} matchedTitle={match.matchedTitle} /> : null}
        </div>
        <div style={{ display: 'grid', justifyItems: 'end', gap: 8 }}>
          <ul style={{ display: 'flex', flexWrap: 'wrap', justifyContent: 'flex-end', gap: 6 }}>
            {work.ids.map((x) => <li key={x}><Badge variant="outline">{x}</Badge></li>)}
            {work.missing.map((x) => <li key={x}><Badge variant="warning" icon="TriangleAlert">No {x} id</Badge></li>)}
          </ul>
          {work.versions.length > 1 ? <ViewToggle value={view} onChange={pick} /> : null}
        </div>
      </div>
      {view === 'table' && work.versions.length > 1 ? <VersionTable work={work} /> : <VersionRows work={work} />}
    </article>
  );
}
if (window.__MAM_KIT) Object.assign(window, { LibraryMatchCard });
