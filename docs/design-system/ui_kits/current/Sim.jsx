/* Failure simulator — a reviewer tool, not product UI. Toggles make the kit's requests "fail" so each
   page shows its error states in place. Shared store: useSim() re-renders on change. */
const SIM_DEFAULT = { offline: false, load: false, refresh: false, action: false, source: true, crash: false, notFound: false };
if (window.__MAM_KIT) window.MAM_SIM = window.MAM_SIM || { ...SIM_DEFAULT };
const simSubs = new Set();
function setSim(patch) { window.MAM_SIM = { ...window.MAM_SIM, ...patch }; simSubs.forEach((f) => f(window.MAM_SIM)); }
function useSim() {
  const [s, set] = React.useState(window.MAM_SIM);
  React.useEffect(() => { simSubs.add(set); return () => simSubs.delete(set); }, []);
  return s;
}
/** Retry that "works": spins briefly, then clears the failure it was for. */
function useRetry(key) {
  const [busy, setBusy] = React.useState(false);
  return [busy, () => { setBusy(true); setTimeout(() => { setBusy(false); setSim({ [key]: false }); }, 700); }];
}
const SERVER_DOWN = 'The server could not be reached. Check that it is running, then try again.';

/** Under the header while the API can't be reached; data on screen stays, actions wait. */
function OfflineStrip() {
  const sim = useSim();
  const { Icon, Button } = window.MAM_DS;
  const [busy, retry] = useRetry('offline');
  if (!sim.offline) return null;
  return (
    <div role="alert" style={{ position: 'sticky', top: 57, zIndex: 25, display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '0.75rem', flexWrap: 'wrap', padding: '0.5rem 1.5rem', background: 'var(--tint-warning)', borderBottom: '1px solid var(--tint-warning-border)', backdropFilter: 'none' }}>
      <Icon name="WifiOff" size={16} style={{ color: 'var(--status-attention)' }} />
      <span>{SERVER_DOWN} <span style={{ color: 'var(--muted-foreground)' }}>Trying again every few seconds.</span></span>
      <Button variant="outline" size="sm" icon="RotateCw" loading={busy} onClick={retry}>{busy ? 'Retrying…' : 'Retry now'}</Button>
    </div>
  );
}

function SimPanel() {
  const sim = useSim();
  const { Icon } = window.MAM_DS;
  const [open, setOpenRaw] = React.useState(() => localStorage.getItem('mamkit:sim:open') === '1');
  const setOpen = (v) => { setOpenRaw(v); localStorage.setItem('mamkit:sim:open', v ? '1' : '0'); };
  const on = Object.entries(sim).filter(([k, v]) => v && k !== 'source').length;
  const rows = [
    ['offline', 'Server unreachable', 'Strip under the header'],
    ['load', 'First load fails', 'Error block instead of the list'],
    ['refresh', 'Refresh fails', 'Banner over stale data'],
    ['action', 'Action fails', 'Sync, Test, Delete, Add, Search'],
    ['source', 'One release source fails', 'Jackett line on Search'],
    ['crash', 'Page crashes', 'Error boundary card'],
    ['notFound', 'Unknown address', '404 page'],
  ];
  if (!open) return <button type="button" onClick={() => setOpen(true)} style={{ position: 'fixed', left: 16, bottom: 16, zIndex: 70, display: 'inline-flex', alignItems: 'center', gap: 6, padding: '0.5rem 0.75rem', borderRadius: 9999, background: 'var(--foreground)', color: 'var(--background)', fontWeight: 500, cursor: 'pointer', boxShadow: 'var(--shadow-lg)' }}><Icon name="FlaskConical" size={14} />Simulate failures{on ? <span style={{ background: 'var(--destructive)', color: '#fff', borderRadius: 9999, fontSize: 11, padding: '0 6px' }}>{on}</span> : null}</button>;
  return (
    <div style={{ position: 'fixed', left: 16, bottom: 16, zIndex: 70, width: 272, background: 'var(--popover)', border: '1px dashed var(--border-strong)', borderRadius: 'var(--radius-lg)', boxShadow: 'var(--shadow-lg)', padding: '0.75rem', display: 'grid', gap: 6 }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
        <Icon name="FlaskConical" size={14} style={{ color: 'var(--muted-foreground)' }} />
        <strong style={{ flex: 1 }}>Simulate failures</strong>
        <span style={{ fontSize: 11, color: 'var(--subtle-foreground)' }}>review only</span>
        <button type="button" title="Hide" onClick={() => setOpen(false)} style={{ cursor: 'pointer', display: 'inline-flex', color: 'var(--muted-foreground)' }}><Icon name="X" size={14} /></button>
      </div>
      {rows.map(([k, label, hint]) => (
        <label key={k} style={{ display: 'grid', gridTemplateColumns: 'auto 1fr', gap: '0 8px', alignItems: 'start', cursor: 'pointer', padding: '4px 2px' }}>
          <input type="checkbox" checked={!!sim[k]} onChange={(e) => setSim({ [k]: e.target.checked })} style={{ accentColor: 'var(--primary)', marginTop: 3 }} />
          <span style={{ display: 'grid' }}><span>{label}</span><span style={{ fontSize: 11, color: 'var(--muted-foreground)' }}>{hint}</span></span>
        </label>
      ))}
    </div>
  );
}

/** AppErrorFallback (frontend/src/components/AppErrorFallback.tsx) — copy from errors.json */
function CrashFallback() {
  const { Card, Button } = window.MAM_DS;
  return (
    <div role="alert" style={{ minHeight: '60dvh', display: 'grid', placeItems: 'center', padding: '1.5rem' }}>
      <Card title="Something went wrong" description="This page hit an unexpected error. The rest of the app is still usable." style={{ maxWidth: '32rem' }}
        footer={<Button onClick={() => setSim({ crash: false })}>Try again</Button>} />
    </div>
  );
}
/** NotFoundPage — copy from notFound.json */
function NotFound({ onHome }) {
  const { Card, Button } = window.MAM_DS;
  return (
    <div style={{ minHeight: '50dvh', display: 'grid', placeItems: 'center' }}>
      <Card title="Page not found" description="There is no page at this address." style={{ maxWidth: '28rem' }}
        footer={<Button variant="outline" icon="House" onClick={() => { setSim({ notFound: false }); onHome(); }}>Go to the home page</Button>} />
    </div>
  );
}
if (window.__MAM_KIT) Object.assign(window, { useSim, setSim, useRetry, SimPanel, OfflineStrip, CrashFallback, NotFound, SERVER_DOWN });
