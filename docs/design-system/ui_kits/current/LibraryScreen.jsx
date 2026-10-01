/* LibraryPage + LibrarySyncPanel (MAM-8/9) — current layout */
function LibraryScreen() {
  const { Card, Button, Input, Select, StatusLine, WorkCard, ListPager, ErrorState, Banner } = window.MAM_DS;
  const sim = useSim();
  const [retrying, retry] = useRetry('load');
  const [refreshing, refresh] = useRetry('refresh');
  const [syncError, setSyncError] = React.useState(null);
  const startSync = () => { setSyncError(null); if (sim.action || sim.offline) { setSyncError(sim.offline ? SERVER_DOWN : 'Could not start the sync. Home Jellyfin is already being synced.'); return; } setSyncing(!syncing); };
  const d = window.MAM_DATA;
  const [q, setQ] = React.useState('');
  const [syncing, setSyncing] = React.useState(false);
  const works = d.works.filter((w) => !q || (w.title + ' ' + (w.originalTitle || '') + ' ' + w.year).toLowerCase().includes(q.toLowerCase()));
  return (
    <div style={{ display: 'grid', gap: '1rem' }}>
      <PageHeader title="Library" lead="The movies in your media library, as its last sync found them: each with its versions, its files and the path this app reaches them at." />
      <Card title="Sources" description="The media libraries this list is read from. A sync reads a whole library again and replaces what was stored from it; nothing changes in the library itself.">
        {sim.load ? <ErrorState title="Could not load the media libraries." onRetry={retry} retrying={retrying}>{SERVER_DOWN}</ErrorState> : <>
        {d.sources.map((src) => (
          <div key={src.name} style={{ display: 'flex', justifyContent: 'space-between', gap: '0.5rem 1rem', alignItems: 'flex-start', paddingBottom: '0.75rem' }}>
            <div style={{ display: 'grid', gap: '0.25rem' }}>
              <span style={{ display: 'flex', gap: '0.375rem', alignItems: 'center' }}><strong style={{ fontWeight: 500 }}>{src.name}</strong><span style={{ color: 'var(--muted-foreground)' }}>{src.service}</span></span>
              {syncing ? <StatusLine status="running">Syncing… 312 of 870 movies</StatusLine> : <StatusLine status="ok">{src.line}</StatusLine>}
              <p style={{ color: 'var(--muted-foreground)' }}>Files: {src.files}</p>
              <StatusLine status="warning">{src.noPath} files have no path in this app. <a href="#">Check the path mappings under Integrations.</a></StatusLine>
              {syncError ? <StatusLine status="error">{syncError}</StatusLine> : null}
            </div>
            <Button variant="outline" size="sm" icon="RefreshCw" loading={syncing} onClick={startSync}>{syncing ? 'Syncing…' : 'Sync now'}</Button>
          </div>
        ))}
        <div style={{ display: 'flex', justifyContent: 'space-between', gap: '1rem', borderTop: '1px solid var(--border)', paddingTop: '0.75rem' }}>
          <StatusLine status="ok">{d.inspection}</StatusLine>
          <Button variant="outline" size="sm" icon="ScanSearch">Inspect files</Button>
        </div>
        </>}
      </Card>
      <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: '0.5rem 0.75rem' }}>
        <Input type="search" value={q} onChange={(e) => setQ(e.target.value)} placeholder="Title, year, IMDb or TMDb id, or path" style={{ maxWidth: '20rem' }} />
        <Select options={['All libraries', ...d.locations]} />
        <Select options={['All movies', 'Missing an IMDb or TMDb id']} />
      </div>
      {sim.load ? <ErrorState title="Could not load the library." onRetry={retry} retrying={retrying}>{SERVER_DOWN}</ErrorState> : <>
      {sim.refresh ? <Banner action={<Button variant="outline" size="sm" icon="RotateCw" loading={refreshing} onClick={refresh}>{refreshing ? 'Retrying…' : 'Retry'}</Button>}>Refresh failed, showing the last loaded movies. {SERVER_DOWN}</Banner> : null}
      <ul style={{ display: 'grid', gap: '0.75rem', opacity: sim.refresh ? 0.75 : 1 }}>{works.map((w) => <li key={w.id}><LibraryMatchCard work={w} headingLevel={2} /></li>)}</ul>
      <ListPager page={1} pages={33} pageSize={25} total={812} />
      </>}
    </div>
  );
}
if (window.__MAM_KIT) Object.assign(window, { LibraryScreen });
