/* IntegrationsPage, DestinationsPage (MAM-7/11) — settings list pages */
function IntegrationsScreen() {
  const { Button, Input, Select, Badge, Banner, Dialog, Field, ListPager, ErrorState } = window.MAM_DS;
  const d = window.MAM_DATA;
  const sim = useSim();
  const [retrying, retry] = useRetry('load');
  const [result, setResult] = React.useState(null);
  const [testing, setTesting] = React.useState(null);
  const test = (i) => { setResult(null); setTesting(i.name); setTimeout(() => { setTesting(null); const bad = sim.action || sim.offline || !i.enabled; setResult({ ok: !bad, text: sim.offline ? 'Could not test the connection. ' + SERVER_DOWN : bad ? i.name + ': Couldn\'t be reached at ' + i.url.replace(/^(https?:\/\/[^/]+).*/, '$1') + '. Check the address and that ' + i.service + ' is running.' : i.name + ': Connected to ' + i.service + '.' }); }, 700); };
  const [dialog, setDialog] = React.useState(false);
  return (
    <div style={{ display: 'grid', gap: '1rem' }}>
      <PageHeader title="Integrations" lead="The services this app works with: a media library to read, release sources to search and a download client to send releases to." action={<Button icon="Plus" onClick={() => setDialog(true)}>Add integration</Button>} />
      <div style={{ display: 'flex', flexWrap: 'wrap', gap: '0.5rem 0.75rem' }}>
        <Input type="search" placeholder="Search by name" style={{ maxWidth: '20rem' }} />
        <Select options={['All kinds', 'Media library', 'Release source', 'Download client']} />
      </div>
      {result ? <Banner tone={result.ok ? 'ok' : 'error'} onDismiss={() => setResult(null)}>{result.text}</Banner> : null}
      {sim.load ? <ErrorState title="Could not load the integrations." onRetry={retry} retrying={retrying}>{SERVER_DOWN}</ErrorState> : <>
      <div className="ui-table-wrap"><table className="ui-table">
        <thead><tr><th>Name</th><th>Service</th><th>Address</th><th>Actions</th></tr></thead>
        <tbody>{d.integrations.map((i) => (
          <tr key={i.name}>
            <td><span style={{ display: 'flex', gap: '0.375rem', alignItems: 'center', flexWrap: 'wrap' }}><span style={{ fontWeight: 500, color: i.enabled ? undefined : 'var(--muted-foreground)' }}>{i.name}</span>{!i.enabled ? <Badge variant="outline">Disabled</Badge> : null}</span></td>
            <td><span style={{ display: 'grid' }}><span>{i.service}</span><span style={{ fontSize: 12, color: 'var(--muted-foreground)' }}>{i.kind}</span></span></td>
            <td><span className="mono" style={{ fontSize: 12, color: 'var(--muted-foreground)', overflowWrap: 'anywhere' }}>{i.url}</span></td>
            <td><div style={{ display: 'flex', gap: 6, justifyContent: 'flex-end' }}>
              <Button variant="outline" size="sm" icon="PlugZap" loading={testing === i.name} onClick={() => test(i)}>{testing === i.name ? 'Testing…' : 'Test'}</Button>
              <Button variant="outline" size="sm" icon="Pencil" onClick={() => setDialog(true)}>Edit</Button>
              <Button variant="danger" size="sm" icon="Trash2">Remove</Button></div></td>
          </tr>))}</tbody>
      </table></div>
      <ListPager page={1} pages={1} pageSize={25} total={4} />
      </>}
      {dialog ? <Dialog title="Add integration" onClose={() => setDialog(false)} footer={<><Button>Save</Button><Button variant="outline" onClick={() => setDialog(false)}>Cancel</Button></>}>
        <div style={{ display: 'grid', gap: '1rem' }}>
          <Field label="Service"><Select options={['Jellyfin', 'Torznab feed', 'qBittorrent']} style={{ width: '100%' }} /></Field>
          <Field label="Name"><Input placeholder="Home Jellyfin" /></Field>
          <Field label="Address" hint="No user name, password or query in it."><Input placeholder="http://192.168.1.20:8096" /></Field>
          <Field label="API key"><Input type="password" placeholder="Stored encrypted, never shown again" /></Field>
          <div><Button variant="outline" size="sm" icon="PlugZap">Test connection</Button></div>
        </div>
      </Dialog> : null}
    </div>
  );
}
function DestinationsScreen() {
  const { Button, Input, Badge, StatusLine } = window.MAM_DS;
  const d = window.MAM_DATA;
  return (
    <div style={{ display: 'grid', gap: '1rem' }}>
      <PageHeader title="Destinations" lead="Where downloads go: each destination is a category your download client already has, which decides the folder." action={<Button icon="Plus">Add destination</Button>} />
      <Input type="search" placeholder="Search by name" style={{ maxWidth: '20rem' }} />
      <div className="ui-table-wrap"><table className="ui-table">
        <thead><tr><th>Name</th><th>Client · category</th><th>Saves to</th><th>Actions</th></tr></thead>
        <tbody>{d.destinations.map((x) => (
          <tr key={x.name}>
            <td><span style={{ display: 'flex', gap: '0.375rem', alignItems: 'center' }}><span style={{ fontWeight: 500 }}>{x.name}</span>{x.isDefault ? <Badge variant="secondary">Default</Badge> : null}</span></td>
            <td>{x.client} · <span className="mono">{x.category}</span></td>
            <td><div style={{ display: 'grid', gap: 2 }}><span className="mono" style={{ fontSize: 12 }}>{x.path}</span>
              {x.check === 'InLibrary' ? <StatusLine status="ok">In the library “{x.libs}”</StatusLine> : <StatusLine status="warning">{x.problem}</StatusLine>}</div></td>
            <td><div style={{ display: 'flex', gap: 6, justifyContent: 'flex-end' }}><Button variant="outline" size="sm" icon="Pencil">Edit</Button><Button variant="danger" size="sm" icon="Trash2">Remove</Button></div></td>
          </tr>))}</tbody>
      </table></div>
    </div>
  );
}
if (window.__MAM_KIT) Object.assign(window, { IntegrationsScreen, DestinationsScreen });
