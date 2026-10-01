/* SearchPage (MAM-12) — current layout: form, "In your library" cards, then "Available releases" */
function SearchScreen({ query }) {
  const { Field, Input, Button, WorkCard, MatchBadges, StatusLine, Select, ReleaseRow, ListPager, ErrorState, Banner } = window.MAM_DS;
  const d = window.MAM_DATA, s0 = d.search;
  const sim = useSim();
  const s = { ...s0, sources: sim.source ? s0.sources : s0.sources.filter((x) => x.status === 'Succeeded') };
  const [retrying, retry] = useRetry('load');
  const [refreshing, refresh] = useRetry('refresh');
  const [formError, setFormError] = React.useState(null);
  const [titleError, setTitleError] = React.useState(null);
  const [searching, setSearching] = React.useState(false);
  const submit = (e) => {
    e.preventDefault(); setFormError(null); setTitleError(null);
    const title = e.currentTarget.querySelector('input').value.trim();
    if (!title) { setTitleError('Enter a title to search for.'); return; }
    setSearching(true);
    setTimeout(() => { setSearching(false); if (sim.action || sim.offline) setFormError(sim.offline ? SERVER_DOWN : 'Could not search. The search is already running; wait for it to finish.'); }, 600);
  };
  const [details, setDetails] = React.useState(null);
  const lbl = { fontSize: '1.125rem', fontWeight: 600, lineHeight: 1.25 };
  return (
    <div style={{ display: 'grid', gap: '1rem' }}>
      <PageHeader title="Search" lead="Look for a movie in your library and in every release source at once. What the sources list is kept for a day, so filtering and sorting never ask them again." />
      <form noValidate onSubmit={submit} style={{ display: 'grid', gridTemplateColumns: 'minmax(0,2fr) minmax(0,6rem) minmax(0,1fr) minmax(0,1fr) auto', alignItems: 'start', gap: '0.75rem' }}>
        <Field label="Title" error={titleError}><Input key={query ?? s.text} defaultValue={query || s.text} placeholder="Interstellar" /></Field>
        <Field label="Year" optional="(optional)"><Input key={'y' + (query ?? '')} defaultValue={query ? '' : s.year} placeholder="2014" /></Field>
        <Field label="IMDb id" optional="(optional)"><Input placeholder="tt0816692" /></Field>
        <Field label="TMDb id" optional="(optional)"><Input placeholder="157336" /></Field>
        <Button type="submit" icon="Search" loading={searching} style={{ marginTop: 22 }}>{searching ? 'Searching…' : 'Search'}</Button>
      </form>
      {formError ? <Banner onDismiss={() => setFormError(null)}>{formError}</Banner> : null}
      {sim.load ? <ErrorState title="Could not load this search." onRetry={retry} retrying={retrying}>{SERVER_DOWN}</ErrorState> : <>
      {sim.refresh ? <Banner action={<Button variant="outline" size="sm" icon="RotateCw" loading={refreshing} onClick={refresh}>{refreshing ? 'Retrying…' : 'Retry'}</Button>}>Refresh failed, showing the search as it was last loaded.</Banner> : null}
      <section style={{ display: 'grid', gap: '0.75rem' }}>
        <h2 style={lbl}>In your library</h2>
        {s.library.map((m) => <LibraryMatchCard key={m.workId} work={d.works.find((w) => w.id === m.workId)} match={m} />)}
      </section>
      <section style={{ display: 'grid', gap: '0.75rem' }}>
        <h2 style={lbl}>Available releases</h2>
        <ul style={{ display: 'grid', gap: '0.25rem' }}>
          {s.sources.map((x) => <StatusLine as="li" key={x.name} status={x.status === 'Succeeded' ? 'ok' : 'error'}><strong>{x.name}</strong> · {x.line}</StatusLine>)}
        </ul>
        <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: '0.5rem 0.75rem' }}>
          <Select options={['All sources', 'Toloka']} />
          <Select options={['All languages', 'Ukrainian', 'English', 'Unknown']} />
          <Select options={['All qualities', '2160p', '1080p', '720p', 'Unknown']} />
          <span style={{ display: 'inline-flex', alignItems: 'center', gap: '0.375rem', color: 'var(--muted-foreground)' }}>Size, GiB <Input placeholder="From" style={{ width: '5.5rem' }} /> <Input placeholder="To" style={{ width: '5.5rem' }} /></span>
          <Select options={['Most seeders', 'Largest', 'Smallest', 'Newest', 'Best quality']} />
        </div>
        <ul style={{ display: 'grid', gap: '0.75rem' }}>
          {s.releases.map((r) => <li key={r.id}><ReleaseRow release={r} onDetails={() => setDetails(r)} /></li>)}
        </ul>
        <ListPager page={1} pages={2} pageSize={10} total={16} />
      </section>
      </>}
      {details ? <ReleaseDetails release={details} onClose={() => setDetails(null)} /> : null}
    </div>
  );
}
function ReleaseDetails({ release, onClose }) {
  const { Dialog, WorkCard, ChipRow } = window.MAM_DS;
  const d = window.MAM_DATA, w = d.works[0];
  const facts = [['Source', release.source], ['Size', release.size || 'Unknown'], ['Seeders', release.seeders ?? 'Unknown'], ['Peers', release.peers ?? 'Unknown'], ['Published', release.published || 'Unknown'], ['Downloaded as', 'Known once it is fetched']];
  return (
    <Dialog title={release.title} description={'What ' + release.source + ' lists for this release, and the copies in your library it may be. Everything on the left is the listing\'s claim; the facts on the right were read from your files.'} onClose={onClose} maxWidth="60rem">
      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '1.5rem', alignItems: 'start' }}>
        <div style={{ display: 'grid', gap: '0.75rem' }}>
          <h3 style={{ fontSize: '0.875rem', fontWeight: 600 }}>The listing</h3>
          <dl style={{ display: 'grid', gridTemplateColumns: 'auto minmax(0,1fr)', gap: '0.25rem 0.75rem' }}>
            {facts.map(([k, v]) => <React.Fragment key={k}><dt style={{ color: 'var(--muted-foreground)' }}>{k}</dt><dd>{v}</dd></React.Fragment>)}
          </dl>
          <ChipRow label="Claimed" chips={[release.claimed.resolution ? release.claimed.resolution + 'p' : 'Resolution unknown', release.claimed.medium, release.claimed.codec || 'Codec unknown']} />
        </div>
        <div style={{ display: 'grid', gap: '0.75rem' }}>
          <h3 style={{ fontSize: '0.875rem', fontWeight: 600 }}>In your library</h3>
          <WorkCard headingLevel={3} work={w} />
        </div>
      </div>
    </Dialog>
  );
}
if (window.__MAM_KIT) Object.assign(window, { SearchScreen, ReleaseDetails });
