/* Proposed shell: a left sidebar replaces the header bar + user menu. */
function SideItem({ icon, label, active, onClick, badge }) {
  const { Icon } = window.MAM_DS;
  return (
    <a href="#" onClick={(e) => { e.preventDefault(); onClick(); }} aria-current={active ? 'page' : undefined}
      style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', padding: '0.4375rem 0.625rem', borderRadius: 'var(--radius-md)', fontSize: '0.875rem', fontWeight: 500, textDecoration: 'none',
        color: active ? 'var(--foreground)' : 'var(--muted-foreground)', background: active ? 'var(--tint-selected)' : 'transparent' }}>
      <Icon name={icon} size={16} style={{ color: active ? 'var(--primary)' : undefined }} />
      <span style={{ flex: 1 }}>{label}</span>
      {badge}
    </a>
  );
}
function Sidebar({ page, onNav }) {
  const { AppLogo, Icon, Button, ThemeSwitch } = window.MAM_DS;
  const d = window.MAM_DATA;
  const group = { fontSize: '0.75rem', fontWeight: 500, color: 'var(--subtle-foreground)', padding: '0 0.625rem', margin: '1rem 0 0.25rem' };
  return (
    <aside style={{ width: 232, flexShrink: 0, position: 'sticky', top: 0, height: '100dvh', display: 'flex', flexDirection: 'column', background: 'var(--card)', borderRight: '1px solid var(--border)', padding: '0.875rem 0.75rem' }}>
      <a href="#" onClick={(e) => { e.preventDefault(); onNav('search'); }} style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', padding: '0.25rem 0.375rem 0.75rem', textDecoration: 'none', color: 'var(--foreground)' }}>
        <AppLogo size={24} />
        <span style={{ fontSize: '0.875rem', fontWeight: 600, lineHeight: 1.2 }}>Media Acquisition<br />Manager</span>
      </a>
      <nav style={{ display: 'grid', gap: 2 }}>
        <SideItem icon="Search" label="Search" active={page === 'search'} onClick={() => onNav('search')} />
        <SideItem icon="LibraryBig" label="Library" active={page === 'library'} onClick={() => onNav('library')} />
        <p style={group}>Settings</p>
        <SideItem icon="Plug" label="Integrations" active={page === 'integrations'} onClick={() => onNav('integrations')} />
        <SideItem icon="FolderInput" label="Destinations" active={page === 'destinations'} onClick={() => onNav('destinations')} />
        <SideItem icon="FolderOpen" label="Media folders" active={page === 'folders'} onClick={() => onNav('integrations')} />
        <SideItem icon="Users" label="Users" active={page === 'users'} onClick={() => onNav('integrations')} />
      </nav>
      <div style={{ marginTop: 'auto', display: 'grid', gap: '0.5rem', borderTop: '1px solid var(--border)', paddingTop: '0.75rem' }}>
        <div style={{ display: 'grid', gap: 4, padding: '0 0.25rem' }}><span style={{ fontSize: '0.75rem', color: 'var(--subtle-foreground)', padding: '0 0.375rem' }}>Theme</span><ThemeSwitch /></div>
        <p style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', padding: '0 0.625rem', fontSize: '0.75rem', color: 'var(--muted-foreground)' }}>
          <span style={{ width: 8, height: 8, borderRadius: 9999, background: 'var(--status-up)' }} />Ready
          <span className="mono" style={{ marginLeft: 'auto' }}>v{d.version}</span>
        </p>
        <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', padding: '0.25rem 0.25rem 0 0.625rem' }}>
          <Icon name="CircleUser" size={16} />
          <span style={{ flex: 1, fontWeight: 500, overflow: 'hidden', textOverflow: 'ellipsis' }}>{d.user.userName}</span>
          <span style={{ fontSize: '0.75rem', color: 'var(--muted-foreground)' }}>Owner</span>
          <Button variant="ghost" size="icon" icon="LogOut" title="Sign out" />
        </div>
      </div>
    </aside>
  );
}
function PHeader({ title, lead, children }) {
  return (
    <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'flex-end', justifyContent: 'space-between', gap: '0.75rem 1.5rem' }}>
      <div style={{ display: 'grid', gap: '0.25rem', minWidth: 0, maxWidth: '44rem' }}>
        <h1 style={{ fontSize: 'var(--text-2xl)', fontWeight: 600, lineHeight: 1.25 }}>{title}</h1>
        {lead ? <p style={{ fontSize: '0.875rem', color: 'var(--muted-foreground)', textWrap: 'pretty' }}>{lead}</p> : null}
      </div>
      {children}
    </div>
  );
}
if (window.__MAM_KIT) Object.assign(window, { Sidebar, PHeader });
