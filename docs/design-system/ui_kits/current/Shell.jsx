/* AppLayout + UserMenu — header variant 2d: search field in the header, everything else in the account menu */
function AppHeader({ page, onNav, onSignOut, onQuery }) {
  const { AppLogo, Icon, ThemeSwitch } = window.MAM_DS;
  const d = window.MAM_DATA;
  const [open, setOpen] = React.useState(false);
  const [q, setQ] = React.useState('');
  const rootRef = React.useRef(null);
  const inputRef = React.useRef(null);
  React.useEffect(() => {
    const onKey = (e) => {
      const typing = ['INPUT', 'TEXTAREA', 'SELECT'].includes(document.activeElement.tagName);
      if (e.key === '/' && !typing) { e.preventDefault(); inputRef.current && inputRef.current.focus(); }
      if (e.key === 'Escape') setOpen(false);
    };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, []);
  React.useEffect(() => {
    if (!open) return undefined;
    const out = (e) => { if (rootRef.current && !rootRef.current.contains(e.target)) setOpen(false); };
    document.addEventListener('pointerdown', out);
    return () => document.removeEventListener('pointerdown', out);
  }, [open]);
  const go = (p) => { setOpen(false); onNav(p); };
  const submit = (e) => { e.preventDefault(); if (onQuery) onQuery(q.trim()); go('search'); inputRef.current && inputRef.current.blur(); };
  const settings = [['integrations', 'Integrations', 'Plug'], ['destinations', 'Destinations', 'FolderInput'], ['integrations', 'Media folders', 'FolderOpen'], ['users', 'Users', 'Users']];
  const libOn = page === 'library';
  return (
    <header style={{ position: 'sticky', top: 0, zIndex: 30, display: 'grid', gridTemplateColumns: 'minmax(max-content,1fr) minmax(0,42rem) minmax(max-content,1fr)', alignItems: 'center', gap: '1.25rem', padding: '0.625rem 1.5rem', background: 'var(--card)', borderBottom: '1px solid var(--border)' }}>
      <a href="#" onClick={(e) => { e.preventDefault(); go('home'); }} title="The version you are running" style={{ justifySelf: 'start', display: 'inline-flex', alignItems: 'center', gap: '0.625rem', color: 'inherit', textDecoration: 'none', whiteSpace: 'nowrap' }}>
        <AppLogo size={30} />
        <span style={{ display: 'grid', gap: 1, lineHeight: 1.15 }}>
          <span style={{ fontSize: '0.9375rem', fontWeight: 600, color: 'var(--foreground)' }}>Media Acquisition Manager</span>
          <span className="mono" style={{ fontSize: '0.6875rem', color: 'var(--subtle-foreground)' }}>v{d.version}</span>
        </span>
      </a>
      <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem', minWidth: 0 }}>
      <form role="search" onSubmit={submit} style={{ flex: '1 1 auto', minWidth: 0, position: 'relative' }}>
        <Icon name="Search" size={16} style={{ position: 'absolute', left: 11, top: 10, color: page === 'search' ? 'var(--primary)' : 'var(--muted-foreground)' }} />
        <input ref={inputRef} className="ui-input" type="search" value={q} onChange={(e) => setQ(e.target.value)} aria-label="Search for a movie"
          placeholder="Search a movie — title, year or IMDb id" style={{ paddingLeft: 36, paddingRight: q === '' ? 40 : 12, background: 'var(--muted)', borderColor: 'transparent' }} />
        {q === '' ? <kbd className="mono" title="Press / to search" onMouseDown={(e) => { e.preventDefault(); inputRef.current && inputRef.current.focus(); }} style={{ position: 'absolute', right: 8, top: 8, fontSize: 11, padding: '1px 6px', border: '1px solid var(--border-strong)', borderRadius: 'var(--radius-sm)', color: 'var(--muted-foreground)', background: 'var(--card)', cursor: 'text' }}>/</kbd> : null}
      </form>
      <a href="#" onClick={(e) => { e.preventDefault(); go('library'); }} aria-current={libOn ? 'page' : undefined}
        style={{ display: 'inline-flex', alignItems: 'center', gap: 6, padding: '0.375rem 0.625rem', borderRadius: 'var(--radius-md)', fontSize: '0.875rem', fontWeight: 500, textDecoration: 'none', flexShrink: 0,
          color: libOn ? 'var(--foreground)' : 'var(--muted-foreground)', background: libOn ? 'var(--tint-selected)' : 'transparent' }}>
        <Icon name="LibraryBig" size={16} />Library
      </a>
      </div>
      <div ref={rootRef} style={{ position: 'relative', justifySelf: 'end' }}>
        <button type="button" aria-expanded={open} aria-label={'Account menu for ' + d.user.userName} onClick={() => setOpen(!open)}
          style={{ display: 'inline-flex', alignItems: 'center', gap: 8, cursor: 'pointer', padding: '2px 6px 2px 2px', borderRadius: 9999 }}>
          <span style={{ width: 28, height: 28, borderRadius: 9999, display: 'inline-grid', placeItems: 'center', background: 'var(--tint-selected)', color: 'var(--primary)', fontWeight: 600, fontSize: 12 }}>{d.user.userName[0].toUpperCase()}</span>
          <span style={{ fontWeight: 500 }}>{d.user.userName}</span>
          <Icon name="ChevronDown" size={14} style={{ color: 'var(--muted-foreground)' }} />
        </button>
        {open ? (
          <div className="ui-menu" style={{ position: 'absolute', top: 'calc(100% + 0.375rem)', right: 0, width: 240 }}>
            <button type="button" className="ui-menu-item" onClick={() => go('profile')} style={{ display: 'grid', gap: 2, padding: '6px 12px 8px', background: page === 'profile' ? 'var(--tint-selected)' : undefined }} title="Your profile">
              <strong>{d.user.userName}</strong>
              <span style={{ fontSize: 12, color: 'var(--muted-foreground)', display: 'inline-flex', alignItems: 'center', gap: 6 }}>
                {d.user.isOwner ? 'Owner' : 'Member'} · <span className="mono">v{d.version}</span> ·
                <span style={{ display: 'inline-flex', alignItems: 'center', gap: 4 }}><span style={{ width: 7, height: 7, borderRadius: 9999, background: 'var(--status-up)' }} />Ready</span>
              </span>
            </button>
            <button type="button" className="ui-menu-item" onClick={() => go('profile')}><Icon name="CircleUser" size={14} />Profile</button>
            {d.user.isOwner ? (<>
              <div className="ui-menu-sep" />
              {settings.map(([p, l, i]) => <button key={l} type="button" className="ui-menu-item" onClick={() => go(p)}><Icon name={i} size={14} />{l}</button>)}
            </>) : null}
            <div className="ui-menu-sep" />
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '4px 8px 4px 12px' }}><span>Theme</span><ThemeSwitch /></div>
            <div className="ui-menu-sep" />
            <button type="button" className="ui-menu-item" onClick={() => { setOpen(false); onSignOut(); }}><Icon name="LogOut" size={14} />Sign out</button>
          </div>
        ) : null}
      </div>
    </header>
  );
}
function PageHeader({ title, lead, action }) {
  return (
    <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'flex-start', justifyContent: 'space-between', gap: '0.75rem 1rem' }}>
      <div style={{ display: 'grid', gap: '0.375rem', minWidth: 0 }}>
        <h1 style={{ fontSize: 'var(--text-2xl)', fontWeight: 600, lineHeight: 1.25 }}>{title}</h1>
        <p style={{ fontSize: '0.875rem', color: 'var(--muted-foreground)' }}>{lead}</p>
      </div>
      {action}
    </div>
  );
}
function HomeScreen() {
  const { Card, StatusLine } = window.MAM_DS;
  return (
    <div style={{ display: 'grid', gap: '1.5rem' }}>
      <section style={{ display: 'grid', gap: '0.5rem' }}>
        <h1 style={{ fontSize: 'var(--text-2xl)', fontWeight: 600 }}>Media Acquisition Manager</h1>
        <p style={{ color: 'var(--muted-foreground)', fontSize: '0.875rem' }}>Search your library and your release sources together, then send the release you choose to your download client.</p>
      </section>
      <Card title="Service status" description="Whether the server is ready to work."><StatusLine status="ok">Ready</StatusLine></Card>
    </div>
  );
}
if (window.__MAM_KIT) Object.assign(window, { AppHeader, PageHeader, HomeScreen });
