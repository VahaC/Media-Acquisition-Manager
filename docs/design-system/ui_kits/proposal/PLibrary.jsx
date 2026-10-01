/* Proposed Library: one-line source strip, a dense table of works, a row expands to versions and files. */
function PLibrary() {
  const { Button, Input, Select, Badge, Icon, StatusLine, ListPager, ChipRow, Facts } = window.MAM_DS;
  const d = window.MAM_DATA, src = d.sources[0];
  const [q, setQ] = React.useState('');
  const [missing, setMissing] = React.useState(false);
  const [open, setOpen] = React.useState({ w1: true });
  const [syncing, setSyncing] = React.useState(false);
  const works = d.works.filter((w) => (!missing || w.missing.length) && (!q || (w.title + ' ' + (w.originalTitle || '') + ' ' + w.year).toLowerCase().includes(q.toLowerCase())));
  const cols = '1.75rem minmax(0,2.2fr) 3.5rem minmax(0,1.4fr) minmax(0,1.5fr) 4.5rem 5.5rem';
  const seg = (on) => ({ height: 32, padding: '0 0.75rem', fontSize: 12, fontWeight: 500, cursor: 'pointer', background: on ? 'var(--card)' : 'transparent', color: on ? 'var(--foreground)' : 'var(--muted-foreground)', borderRadius: 'var(--radius-sm)', boxShadow: on ? 'var(--shadow-sm)' : 'none' });
  return (
    <div style={{ display: 'grid', gap: '1rem' }}>
      <PHeader title="Library" lead="The movies in your media library as its last sync found them, with the facts the app read from each file." />
      <div className="ui-card" style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: '0.5rem 1.25rem', padding: '0.625rem 0.875rem' }}>
        <span style={{ display: 'flex', alignItems: 'center', gap: 6 }}><Icon name="LibraryBig" size={16} style={{ color: 'var(--muted-foreground)' }} /><strong style={{ fontWeight: 600 }}>{src.name}</strong><span style={{ color: 'var(--muted-foreground)' }}>Jellyfin</span></span>
        {syncing ? <StatusLine status="running">Syncing… 312 of 870 movies</StatusLine> : <StatusLine status="ok">Synced 1 Oct, 09:12 · 812 works</StatusLine>}
        <span style={{ color: 'var(--muted-foreground)' }}>Files: 846 inspected · 3 couldn't be read · 4 waiting</span>
        <a href="#" style={{ display: 'inline-flex', alignItems: 'center', gap: 4, color: 'var(--foreground)' }}><Icon name="TriangleAlert" size={14} style={{ color: 'var(--status-attention)' }} />2 files have no path</a>
        <span style={{ marginLeft: 'auto', display: 'flex', gap: 6 }}>
          <Button variant="outline" size="sm" icon="ScanSearch">Inspect files</Button>
          <Button variant="outline" size="sm" icon="RefreshCw" loading={syncing} onClick={() => setSyncing(!syncing)}>{syncing ? 'Syncing…' : 'Sync now'}</Button>
        </span>
      </div>
      <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: '0.5rem' }}>
        <span style={{ position: 'relative', flex: '1 1 18rem', maxWidth: '26rem' }}><Icon name="Search" size={14} style={{ position: 'absolute', left: 10, top: 11, color: 'var(--muted-foreground)' }} /><Input type="search" value={q} onChange={(e) => setQ(e.target.value)} placeholder="Title, year, IMDb or TMDb id, or path" style={{ paddingLeft: 30 }} /></span>
        <Select options={['All libraries', ...d.locations]} />
        <span style={{ display: 'inline-flex', padding: 2, gap: 2, background: 'var(--secondary)', border: '1px solid var(--border)', borderRadius: 'var(--radius-md)' }}>
          <button type="button" style={seg(!missing)} onClick={() => setMissing(false)}>All movies</button>
          <button type="button" style={seg(missing)} onClick={() => setMissing(true)}>Missing an id · 2</button>
        </span>
        <span style={{ marginLeft: 'auto', color: 'var(--muted-foreground)' }}>{missing ? 2 : 812} works</span>
      </div>
      <div className="ui-card" style={{ overflow: 'hidden' }}>
        <div style={{ display: 'grid', gridTemplateColumns: cols, gap: '0 0.75rem', padding: '0.5rem 0.875rem', fontSize: 12, fontWeight: 500, color: 'var(--muted-foreground)', background: 'var(--muted)', borderBottom: '1px solid var(--border)' }}>
          <span /><span>Title</span><span>Year</span><span>Ids</span><span>Best copy · verified</span><span>Versions</span><span style={{ textAlign: 'right' }}>Size</span></div>
        {works.map((w, idx) => { const on = !!open[w.id]; const s = w.summary; return (
          <div key={w.id} style={{ borderTop: idx ? '1px solid var(--border)' : 0 }}>
            <div onClick={() => setOpen({ ...open, [w.id]: !on })} style={{ display: 'grid', gridTemplateColumns: cols, gap: '0 0.75rem', alignItems: 'center', padding: '0.625rem 0.875rem', cursor: 'pointer', background: on ? 'var(--muted)' : undefined }}>
              <Icon name={on ? 'ChevronDown' : 'ChevronRight'} size={16} style={{ color: 'var(--muted-foreground)' }} />
              <span style={{ display: 'grid', minWidth: 0 }}><strong style={{ fontWeight: 600, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{w.title}</strong>
                {w.originalTitle ? <span style={{ fontSize: 12, color: 'var(--muted-foreground)' }}>{w.originalTitle}</span> : null}</span>
              <span style={{ fontVariantNumeric: 'tabular-nums' }}>{w.year}</span>
              <span style={{ display: 'flex', flexWrap: 'wrap', gap: 4 }}>
                {w.ids.map((x) => <Badge key={x} variant="outline">{x.split(' ')[0]}</Badge>)}
                {w.missing.map((x) => <Badge key={x} variant="warning" icon="TriangleAlert">No {x}</Badge>)}</span>
              <span style={{ display: 'flex', flexWrap: 'wrap', gap: 4, fontSize: 12 }}>
                {s.res ? [s.res, s.codec, s.range, s.audio.join(' · ')].filter(Boolean).map((x) => <span key={x} className="ui-chip">{x}</span>)
                  : <span style={{ display: 'inline-flex', alignItems: 'center', gap: 4, color: 'var(--muted-foreground)' }}>{s.note === 'No path in this app' ? <Icon name="TriangleAlert" size={12} style={{ color: 'var(--status-attention)' }} /> : null}{s.note}</span>}</span>
              <span>{w.versions.length}</span>
              <span style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{s.size}</span>
            </div>
            {on ? (
              <div style={{ padding: '0.25rem 0.875rem 0.875rem 3.375rem', display: 'grid', gap: '0.75rem', background: 'var(--muted)' }}>
                {w.versions.map((v, i) => v.files.map((f, j) => (
                  <div key={i + '-' + j} className="ui-card" style={{ display: 'grid', gridTemplateColumns: 'minmax(0,1fr) minmax(0,1fr)', gap: '0.5rem 1.5rem', padding: '0.75rem 0.875rem' }}>
                    <div style={{ display: 'grid', gap: 3, alignContent: 'start', minWidth: 0 }}>
                      <span style={{ display: 'flex', gap: 8, alignItems: 'baseline' }}><strong style={{ fontWeight: 600 }}>{v.label || 'Version ' + (i + 1)}</strong><span style={{ fontSize: 12, color: 'var(--muted-foreground)' }}>{f.size} · Library: {f.location}</span></span>
                      <p className="mono" style={{ fontWeight: 500, fontSize: 12, overflowWrap: 'anywhere' }}>{f.name}</p>
                      <p className="mono" style={{ fontSize: 11, color: 'var(--muted-foreground)', overflowWrap: 'anywhere' }}>{f.folder}</p>
                      {f.appPath === null ? <StatusLine status="warning">This file has no path in this app.</StatusLine> : null}
                    </div>
                    <div style={{ display: 'grid', gap: 6, alignContent: 'start', minWidth: 0 }}>
                      {f.media ? (<>
                        <Facts verified items={[f.media.duration, f.media.resolution, f.media.codec, f.media.range, f.media.bitrate]} />
                        <ChipRow label="Audio" chips={f.media.audio} empty="No audio tracks" />
                        <ChipRow label="Subtitles" chips={f.media.subs} empty="No subtitles" />
                      </>) : <p style={{ color: 'var(--muted-foreground)' }}>{f.appPath === null ? 'Not inspected: the app has no path to this file.' : 'Not inspected yet'}</p>}
                    </div>
                  </div>)))}
              </div>) : null}
          </div>); })}
      </div>
      <ListPager page={1} pages={33} pageSize={25} total={812} />
    </div>
  );
}
if (window.__MAM_KIT) Object.assign(window, { PLibrary });
