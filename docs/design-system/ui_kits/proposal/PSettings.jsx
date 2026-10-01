/* Proposed Integrations: rows grouped by the job each service does, test answers inline on the row. */
function PIntegrations() {
  const { Button, Badge, Icon, Input } = window.MAM_DS;
  const d = window.MAM_DATA;
  const [tested, setTested] = React.useState({});
  const groups = [['Media library', 'Read into the Library', 'LibraryBig'], ['Release source', 'Searched on the Search page', 'Search'], ['Download client', 'Receives downloads; its categories are the destinations', 'Download']];
  return (
    <div style={{ display: 'grid', gap: '1.25rem', maxWidth: '64rem' }}>
      <PHeader title="Integrations" lead="The services this app works with. Credentials are stored encrypted and never shown again.">
        <span style={{ display: 'flex', gap: 8 }}><Input type="search" placeholder="Search by name" style={{ width: '14rem' }} /><Button icon="Plus">Add integration</Button></span>
      </PHeader>
      {groups.map(([kind, hint, icon]) => (
        <section key={kind} style={{ display: 'grid', gap: '0.5rem' }}>
          <h2 style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: '0.875rem', fontWeight: 600 }}><Icon name={icon} size={16} style={{ color: 'var(--muted-foreground)' }} />{kind}<span style={{ fontWeight: 400, color: 'var(--muted-foreground)' }}>· {hint}</span></h2>
          <div className="ui-card" style={{ overflow: 'hidden' }}>
            {d.integrations.filter((i) => i.kind === kind).map((i, idx) => (
              <div key={i.name} style={{ display: 'grid', gridTemplateColumns: 'minmax(0,1fr) minmax(0,1.3fr) auto', gap: '0.25rem 1rem', alignItems: 'center', padding: '0.75rem 0.875rem', borderTop: idx ? '1px solid var(--border)' : 0 }}>
                <span style={{ display: 'grid', gap: 2 }}>
                  <span style={{ display: 'flex', gap: 6, alignItems: 'center' }}><strong style={{ fontWeight: 500, color: i.enabled ? undefined : 'var(--muted-foreground)' }}>{i.name}</strong>{!i.enabled ? <Badge variant="outline">Disabled</Badge> : null}</span>
                  <span style={{ fontSize: 12, color: 'var(--muted-foreground)' }}>{i.service}</span></span>
                <span style={{ display: 'grid', gap: 2, minWidth: 0 }}>
                  <span className="mono" style={{ fontSize: 12, color: 'var(--muted-foreground)', overflowWrap: 'anywhere' }}>{i.url}</span>
                  {tested[i.name] ? <span className="ui-status-line" data-status="ok" style={{ fontSize: 12 }}><Icon name="CircleCheck" size={12} /><span>{tested[i.name]}</span></span> : null}</span>
                <span style={{ display: 'flex', gap: 6 }}>
                  <Button variant="outline" size="sm" icon="PlugZap" onClick={() => setTested({ ...tested, [i.name]: 'Connected just now.' })}>Test</Button>
                  <Button variant="outline" size="sm" icon="Pencil">Edit</Button>
                  <Button variant="danger" size="icon" icon="Trash2" title="Remove" /></span>
              </div>))}
          </div>
        </section>))}
    </div>
  );
}
if (window.__MAM_KIT) Object.assign(window, { PIntegrations });
