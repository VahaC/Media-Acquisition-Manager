/* Proposed Search: one-row query bar, library copies pinned on the left as the comparison baseline,
   releases as a dense table on the right, details in a side drawer with claimed vs verified aligned. */
const P_KEYS = (c) => ({
  Resolution: c.resolution ? c.resolution + 'p' : null,
  Codec: c.codec, HDR: c.ranges && c.ranges.length ? c.ranges.join(' · ') : null,
  Audio: c.audio && c.audio.length ? c.audio.map((a) => a.slice(0, 3)).join(' · ') : null,
  Subtitles: c.subs && c.subs.length ? c.subs.map((a) => a.slice(0, 3)).join(' · ') : null,
});
function copyKeys(file) {
  const m = file.media || {};
  const lang = (arr) => (arr || []).map((t) => t.split(' · ')[0].slice(0, 3)).join(' · ') || null;
  const h = m.resolution ? m.resolution.split('×')[1] : null;
  const res = m.resolution ? (Number(m.resolution.split('×')[0]) >= 3800 ? '2160p' : Number(m.resolution.split('×')[0]) >= 1900 ? '1080p' : h + 'p') : null;
  return { Resolution: res, Codec: m.codec ? m.codec.split(' ')[0].replace('H.264', 'H.264') : null, HDR: m.range && m.range !== 'SDR' ? m.range : null, Audio: lang(m.audio), Subtitles: lang(m.subs) };
}
function Chip({ children, differs, unknown }) {
  return <span className="ui-chip" style={{ fontSize: 12, whiteSpace: 'nowrap', color: unknown ? 'var(--muted-foreground)' : undefined, borderStyle: unknown ? 'dashed' : 'solid',
    borderColor: differs ? 'color-mix(in oklab, var(--primary) 45%, transparent)' : undefined, background: differs ? 'var(--tint-selected)' : undefined }}>{children}</span>;
}
function PSearch() {
  const { Input, Button, Badge, Icon, Select, ListPager } = window.MAM_DS;
  const d = window.MAM_DATA, s = d.search;
  const work = d.works.find((w) => w.id === s.library[0].workId);
  const copies = work.versions.map((v, i) => ({ i, label: v.label || 'Version ' + (i + 1), file: v.files[0] }));
  const [base, setBase] = React.useState(0);
  const [open, setOpen] = React.useState(null);
  const [expanded, setExpanded] = React.useState(false);
  const baseKeys = copyKeys(copies[base].file);
  const lbl = { fontSize: '0.75rem', fontWeight: 500, color: 'var(--muted-foreground)' };
  return (
    <div style={{ display: 'grid', gap: '1rem' }}>
      <form onSubmit={(e) => e.preventDefault()} className="ui-card" style={{ position: 'sticky', top: 0, zIndex: 5, display: 'grid', gridTemplateColumns: 'minmax(0,1fr) 6.5rem 11rem auto', gap: '0.5rem', alignItems: 'end', padding: '0.75rem', boxShadow: 'var(--shadow-sm)' }}>
        <label style={{ display: 'grid', gap: 4 }}><span style={lbl}>Title</span>
          <span style={{ position: 'relative' }}><Icon name="Search" size={16} style={{ position: 'absolute', left: 10, top: 10, color: 'var(--muted-foreground)' }} /><Input defaultValue={s.text} style={{ paddingLeft: 34, fontSize: 15 }} /></span></label>
        <label style={{ display: 'grid', gap: 4 }}><span style={lbl}>Year</span><Input defaultValue={s.year} placeholder="2014" /></label>
        <label style={{ display: 'grid', gap: 4 }}><span style={lbl}>IMDb or TMDb id</span><Input placeholder="tt0816692 or 157336" /></label>
        <Button type="submit">Search</Button>
      </form>

      <div className="p-split" style={{ display: 'grid', gridTemplateColumns: '320px minmax(0,1fr)', gap: '1rem', alignItems: 'start' }}>
        <aside style={{ position: 'sticky', top: 84, display: 'grid', gap: '0.5rem' }}>
          <h2 style={{ fontSize: '0.875rem', fontWeight: 600, display: 'flex', alignItems: 'center', gap: 6 }}>In your library <span style={{ color: 'var(--muted-foreground)', fontWeight: 400 }}>· 1 work</span></h2>
          <div className="ui-card" style={{ padding: '0.875rem', display: 'grid', gap: '0.625rem' }}>
            <div style={{ display: 'grid', gap: 2 }}>
              <h3 style={{ fontSize: '1rem', fontWeight: 600 }}>{work.title}</h3>
              <p style={{ color: 'var(--muted-foreground)', fontSize: 12 }}>{work.originalTitle} · {work.year} · From {work.source}</p>
            </div>
            <div style={{ display: 'flex', flexWrap: 'wrap', gap: 4 }}>
              <Badge variant="secondary" icon="CircleCheck">Matched by IMDb id</Badge>
              {work.ids.map((x) => <Badge key={x} variant="outline">{x}</Badge>)}
            </div>
            <p style={{ ...lbl, marginTop: 2 }}>Compare releases with</p>
            <ul style={{ display: 'grid', gap: 6 }}>
              {copies.map((c) => { const k = copyKeys(c.file); const on = base === c.i; return (
                <li key={c.i}><button type="button" onClick={() => setBase(c.i)} style={{ width: '100%', textAlign: 'left', cursor: 'pointer', display: 'grid', gap: 4, padding: '0.5rem 0.625rem', borderRadius: 'var(--radius-md)', border: '1px solid ' + (on ? 'var(--primary)' : 'var(--border)'), background: on ? 'var(--tint-selected)' : 'var(--card)' }}>
                  <span style={{ display: 'flex', justifyContent: 'space-between', gap: 8 }}><strong style={{ fontWeight: 600 }}>{c.label}</strong><span style={{ color: 'var(--muted-foreground)', fontSize: 12 }}>{c.file.size}</span></span>
                  <span style={{ display: 'flex', flexWrap: 'wrap', gap: 4 }}>{['Resolution', 'Codec', 'HDR', 'Audio'].map((key) => <Chip key={key} unknown={!k[key]}>{k[key] || (key === 'HDR' ? 'SDR' : key + ' unknown')}</Chip>)}</span>
                </button></li>); })}
            </ul>
            <button type="button" onClick={() => setExpanded(!expanded)} style={{ display: 'inline-flex', alignItems: 'center', gap: 4, color: 'var(--primary)', cursor: 'pointer', fontSize: 12, justifySelf: 'start' }}>
              <Icon name={expanded ? 'ChevronUp' : 'ChevronDown'} size={14} />{expanded ? 'Hide files' : 'Files and all tracks'}</button>
            {expanded ? <div style={{ display: 'grid', gap: 4, borderTop: '1px solid var(--border)', paddingTop: 8 }}>
              <p className="mono" style={{ fontWeight: 500, overflowWrap: 'anywhere', fontSize: 12 }}>{copies[base].file.name}</p>
              <p className="mono" style={{ fontSize: 11, color: 'var(--muted-foreground)', overflowWrap: 'anywhere' }}>{copies[base].file.folder}</p>
              {(copies[base].file.media.audio || []).map((a) => <p key={a} style={{ fontSize: 12 }}>{a}</p>)}
            </div> : null}
          </div>
        </aside>

        <section style={{ display: 'grid', gap: '0.5rem', minWidth: 0 }}>
          <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: '0.5rem 0.75rem' }}>
            <h2 style={{ fontSize: '0.875rem', fontWeight: 600 }}>Available releases <span style={{ color: 'var(--muted-foreground)', fontWeight: 400 }}>· {s.releases.length} of 16</span></h2>
            <span style={{ display: 'flex', gap: 6, marginLeft: 'auto' }}>
              {s.sources.map((x) => <span key={x.name} title={x.line} className="ui-badge ui-badge-outline" style={{ gap: 4, color: x.status === 'Succeeded' ? 'var(--foreground)' : 'var(--destructive)', borderColor: x.status === 'Succeeded' ? undefined : 'color-mix(in oklab, var(--destructive) 40%, transparent)' }}>
                <Icon name={x.status === 'Succeeded' ? 'CircleCheck' : 'CircleAlert'} size={12} style={{ color: x.status === 'Succeeded' ? 'var(--status-up)' : 'var(--destructive)' }} />{x.name} · {x.line}</span>)}
            </span>
          </div>
          <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: '0.5rem' }}>
            <Select options={['All sources', 'Toloka']} style={{ height: 32 }} />
            <Select options={['All languages', 'Ukrainian', 'English']} style={{ height: 32 }} />
            <Select options={['All qualities', '2160p', '1080p', '720p']} style={{ height: 32 }} />
            <span style={{ display: 'inline-flex', alignItems: 'center', gap: 6, color: 'var(--muted-foreground)' }}>GiB <Input placeholder="From" style={{ width: '4.5rem', height: 32 }} />–<Input placeholder="To" style={{ width: '4.5rem', height: 32 }} /></span>
            <span style={{ marginLeft: 'auto', display: 'inline-flex', alignItems: 'center', gap: 6, color: 'var(--muted-foreground)' }}>Sort <Select options={['Most seeders', 'Largest', 'Smallest', 'Newest', 'Best quality']} style={{ height: 32 }} /></span>
          </div>
          <p style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 12, color: 'var(--muted-foreground)' }}>
            <Chip differs>2160p</Chip> claimed value that differs from your <strong style={{ color: 'var(--foreground)', fontWeight: 500 }}>{copies[base].label}</strong> · <Chip unknown>unknown</Chip> the title doesn't say</p>
          <div className="ui-card" style={{ overflow: 'hidden' }}>
            <div style={{ display: 'grid', gridTemplateColumns: 'minmax(0,1fr) 5.5rem 4.5rem 6.5rem 6.5rem', gap: '0 0.75rem', padding: '0.5rem 0.875rem', fontSize: 12, fontWeight: 500, color: 'var(--muted-foreground)', borderBottom: '1px solid var(--border)', background: 'var(--muted)' }}>
              <span>Release · claimed</span><span style={{ textAlign: 'right' }}>Size</span><span style={{ textAlign: 'right' }}>Seeders</span><span>Published</span><span style={{ textAlign: 'right' }}>Actions</span></div>
            {s.releases.map((r, idx) => { const k = P_KEYS(r.claimed); const sel = open && open.id === r.id; return (
              <div key={r.id} onClick={() => setOpen(r)} style={{ display: 'grid', gridTemplateColumns: 'minmax(0,1fr) 5.5rem 4.5rem 6.5rem 6.5rem', gap: '0 0.75rem', alignItems: 'center', padding: '0.625rem 0.875rem', borderTop: idx ? '1px solid var(--border)' : 0, cursor: 'pointer', background: sel ? 'var(--tint-selected)' : undefined }}>
                <div style={{ display: 'grid', gap: 5, minWidth: 0 }}>
                  <span style={{ display: 'flex', gap: 6, alignItems: 'baseline', minWidth: 0 }}>
                    <span style={{ fontWeight: 600, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }} title={r.title}>{r.title}</span></span>
                  <span style={{ display: 'flex', flexWrap: 'wrap', gap: 4, alignItems: 'center' }}>
                    {r.claimed.medium ? <Chip>{r.claimed.medium}</Chip> : null}
                    {['Resolution', 'Codec', 'HDR', 'Audio', 'Subtitles'].map((key) => <Chip key={key} unknown={!k[key]} differs={k[key] && baseKeys[key] !== k[key]}>{k[key] || (key === 'HDR' ? 'HDR unknown' : key + ' unknown')}</Chip>)}
                    {r.match ? <span style={{ display: 'inline-flex', alignItems: 'center', gap: 3, fontSize: 12, marginLeft: 4, color: r.match.evidence === 'TitleOnly' ? 'var(--foreground)' : 'var(--muted-foreground)' }}>
                      <Icon name={r.match.evidence === 'TitleOnly' ? 'CircleHelp' : 'LibraryBig'} size={12} style={{ color: r.match.evidence === 'TitleOnly' ? 'var(--status-attention)' : undefined }} />{r.match.evidence === 'TitleOnly' ? 'Possible match — check it' : r.match.evidence === 'Identifier' ? 'In library · by id' : 'In library · by title and year'}</span> : null}
                  </span>
                </div>
                <span style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{r.size || '—'}</span>
                <span style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', color: r.seeders == null ? 'var(--muted-foreground)' : undefined }}>{r.seeders ?? 'Unknown'}</span>
                <span style={{ color: 'var(--muted-foreground)', fontSize: 12 }}>{r.published || 'Unknown'}</span>
                <span style={{ display: 'flex', justifyContent: 'flex-end', gap: 4 }} onClick={(e) => e.stopPropagation()}>
                  <Button variant="ghost" size="icon" icon="Info" title="Details" onClick={() => setOpen(r)} />
                  <Button variant="ghost" size="icon" icon="ExternalLink" title="Open source" />
                  <Button variant="outline" size="icon" icon="Download" title="Sending a release to a download client comes in a later update." disabled />
                </span>
              </div>); })}
          </div>
          <ListPager page={1} pages={2} pageSize={10} total={16} />
        </section>
      </div>
      {open ? <ReleaseDrawer release={open} copy={copies[base]} work={work} onClose={() => setOpen(null)} /> : null}
    </div>
  );
}
function ReleaseDrawer({ release, copy, work, onClose }) {
  const { Button, Icon } = window.MAM_DS;
  const c = release.claimed, m = copy.file.media || {};
  const rows = [
    ['Size', release.size || 'Unknown', copy.file.size],
    ['Resolution', c.resolution ? c.resolution + 'p' : 'Unknown', m.resolution],
    ['Made from', c.medium || 'Unknown', '—'],
    ['Video codec', c.codec || 'Unknown', m.codec],
    ['HDR', c.ranges.length ? c.ranges.join(', ') : 'Unknown', m.range],
    ['Audio', c.audio.length ? c.audio.join(', ') : 'Unknown', (m.audio || []).join('\n')],
    ['Subtitles', c.subs.length ? c.subs.join(', ') : 'Unknown', (m.subs || []).join('\n') || 'No subtitles'],
  ];
  return (<>
    <div onClick={onClose} style={{ position: 'fixed', inset: 0, background: 'rgb(0 0 0 / .25)', zIndex: 40 }} />
    <aside style={{ position: 'fixed', top: 0, right: 0, bottom: 0, width: 'min(40rem, 92vw)', zIndex: 41, background: 'var(--card)', borderLeft: '1px solid var(--border)', boxShadow: 'var(--shadow-lg)', display: 'flex', flexDirection: 'column' }}>
      <div style={{ padding: '1.25rem 1.5rem', borderBottom: '1px solid var(--border)', display: 'grid', gap: 6 }}>
        <div style={{ display: 'flex', gap: 12, alignItems: 'flex-start' }}>
          <h2 style={{ fontSize: '1rem', fontWeight: 600, lineHeight: 1.35, overflowWrap: 'anywhere', flex: 1 }}>{release.title}</h2>
          <Button variant="ghost" size="icon" icon="X" title="Close" onClick={onClose} />
        </div>
        <p style={{ color: 'var(--muted-foreground)', fontSize: 12 }}>{release.source} · {release.seeders ?? 'Unknown'} seeders · {release.peers ?? 'Unknown'} peers · Published {release.published || 'unknown'}</p>
      </div>
      <div style={{ flex: 1, overflowY: 'auto', padding: '1rem 1.5rem', display: 'grid', alignContent: 'start', gap: '0.75rem' }}>
        <div style={{ display: 'grid', gridTemplateColumns: '7rem 1fr 1fr', border: '1px solid var(--border)', borderRadius: 'var(--radius-lg)', overflow: 'hidden' }}>
          <span style={{ padding: '0.5rem 0.75rem', background: 'var(--muted)' }} />
          <span style={{ padding: '0.5rem 0.75rem', background: 'var(--muted)', fontWeight: 600 }}>The listing claims</span>
          <span style={{ padding: '0.5rem 0.75rem', background: 'var(--muted)', fontWeight: 600 }}>Your {copy.label} · verified</span>
          {rows.map(([k, a, b]) => (<React.Fragment key={k}>
            <span style={{ padding: '0.5rem 0.75rem', borderTop: '1px solid var(--border)', color: 'var(--muted-foreground)' }}>{k}</span>
            <span style={{ padding: '0.5rem 0.75rem', borderTop: '1px solid var(--border)', color: a === 'Unknown' ? 'var(--muted-foreground)' : undefined }}>{a}</span>
            <span style={{ padding: '0.5rem 0.75rem', borderTop: '1px solid var(--border)', whiteSpace: 'pre-line' }}>{b || 'Unknown'}</span>
          </React.Fragment>))}
        </div>
        <p style={{ fontSize: 12, color: 'var(--muted-foreground)' }}>The left column is read from the release title and isn't checked. The right column was read from your file by ffprobe.</p>
      </div>
      <div style={{ padding: '0.875rem 1.5rem', borderTop: '1px solid var(--border)', display: 'flex', gap: 8, justifyContent: 'flex-end' }}>
        <Button variant="outline" icon="ExternalLink">Open source</Button>
        <Button icon="Download" disabled title="Sending a release to a download client comes in a later update.">Download…</Button>
      </div>
    </aside>
  </>);
}
if (window.__MAM_KIT) Object.assign(window, { PSearch });
