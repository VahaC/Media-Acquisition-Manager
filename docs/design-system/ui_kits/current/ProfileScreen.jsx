/* Profile — NEW page, approved 2 Oct 2026. Name, role, created, last sign-in; change your own password
   (every user — needs a new API endpoint, see design_handoff_approved_layout); theme (mam:theme); sign-out. */
function ProfileRow({ label, children, last }) {
  return (
    <div style={{ display: 'grid', gridTemplateColumns: '11rem minmax(0,1fr)', gap: '0.25rem 1.5rem', alignItems: 'center', padding: '0.875rem 0', borderBottom: last ? 0 : '1px solid var(--border)' }}>
      <span style={{ color: 'var(--muted-foreground)' }}>{label}</span>
      <div style={{ minWidth: 0 }}>{children}</div>
    </div>
  );
}
function ProfileScreen({ onSignOut }) {
  const { Card, Button, Input, Field, Badge, Banner, ThemeSwitch } = window.MAM_DS;
  const d = window.MAM_DATA, u = d.user, p = d.profile;
  const [pwOpen, setPwOpen] = React.useState(false);
  const [pw, setPw] = React.useState({ c: '', a: '', b: '' });
  const [pwErr, setPwErr] = React.useState({});
  const [busy, setBusy] = React.useState(false);
  const [news, setNews] = React.useState(null);
  const sim = useSim();
  const set = (k) => (e) => { setPw({ ...pw, [k]: e.target.value }); if (pwErr[k] || pwErr.form) setPwErr({ ...pwErr, [k]: undefined, form: undefined }); };
  const reset = () => { setPwOpen(false); setPw({ c: '', a: '', b: '' }); setPwErr({}); };
  const save = () => {
    const e = {};
    if (!pw.c) e.c = 'Enter your current password.';
    if (pw.a.length < 8) e.a = 'A password is at least 8 characters.';
    else if (pw.a.length > 128) e.a = 'A password is at most 128 characters.';
    if (pw.b !== pw.a) e.b = 'The two passwords are different. Type the same password in both fields.';
    setPwErr(e);
    if (Object.keys(e).length) return;
    setBusy(true);
    setTimeout(() => {
      setBusy(false);
      if (sim.offline || sim.action) { setPwErr({ form: 'Could not change the password. ' + SERVER_DOWN }); return; }
      if (pw.c !== 'password123') { setPwErr({ c: 'The current password is incorrect.' }); return; }
      reset(); setNews('Password changed. Your other devices are signed out; this one stays signed in.');
    }, 600);
  };
  return (
    <div style={{ display: 'grid', gap: '1.25rem', maxWidth: '44rem' }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: '1rem' }}>
        <span style={{ width: 56, height: 56, borderRadius: 9999, display: 'inline-grid', placeItems: 'center', background: 'var(--tint-selected)', color: 'var(--primary)', fontWeight: 600, fontSize: 22, flexShrink: 0 }}>{u.userName[0].toUpperCase()}</span>
        <div style={{ display: 'grid', gap: 4 }}>
          <h1 style={{ fontSize: 'var(--text-2xl)', fontWeight: 600, lineHeight: 1.2, display: 'flex', alignItems: 'center', gap: 8 }}>{u.userName}{u.isOwner ? <Badge variant="default">Owner</Badge> : null}</h1>
          <p style={{ fontSize: '0.875rem', color: 'var(--muted-foreground)' }}>{u.isOwner ? 'The installation owner: manages users, integrations and settings.' : 'Can search, browse the library and send downloads.'}</p>
        </div>
      </div>
      {news ? <Banner tone="ok" onDismiss={() => setNews(null)}>{news}</Banner> : null}

      <Card title="Account">
        <ProfileRow label="User name"><span className="mono">{u.userName}</span></ProfileRow>
        <ProfileRow label="Role">{u.isOwner ? 'Owner' : 'Member'}</ProfileRow>
        <ProfileRow label="Created">{p.created}</ProfileRow>
        <ProfileRow label="Last sign-in" last>{p.lastSignIn}</ProfileRow>
      </Card>

      <Card title="Password" description="There is no email reset, so keep it somewhere safe.">
        {pwOpen ? (
          <form noValidate onSubmit={(e) => { e.preventDefault(); save(); }} style={{ display: 'grid', gap: '1rem', maxWidth: '22rem' }}>
            <Field label="Current password" error={pwErr.c}><Input type="password" value={pw.c} onChange={set('c')} autoComplete="current-password" /></Field>
            <Field label="New password" hint="8–128 characters." error={pwErr.a}><Input type="password" value={pw.a} onChange={set('a')} autoComplete="new-password" /></Field>
            <Field label="Confirm password" error={pwErr.b}><Input type="password" value={pw.b} onChange={set('b')} autoComplete="new-password" /></Field>
            {pwErr.form ? <Banner>{pwErr.form}</Banner> : null}
            <p style={{ fontSize: 12, color: 'var(--muted-foreground)' }}>Your other devices are signed out. This one stays signed in.</p>
            <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 8 }}>
              <Button type="submit" loading={busy}>{busy ? 'Changing…' : 'Change password'}</Button>
              <Button variant="outline" onClick={reset}>Cancel</Button>
              <span style={{ fontSize: 11, color: 'var(--subtle-foreground)' }}>Demo: the current password is password123.</span>
            </div>
          </form>
        ) : (
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '1rem', flexWrap: 'wrap' }}>
            <span className="mono" style={{ letterSpacing: 2, color: 'var(--muted-foreground)' }}>••••••••••</span>
            <Button variant="outline" size="sm" icon="KeyRound" onClick={() => setPwOpen(true)}>Change password</Button>
          </div>
        )}
      </Card>

      <Card title="Appearance" description="Saved in this browser.">
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '1rem', flexWrap: 'wrap' }}>
          <span>Theme</span><ThemeSwitch showLabels size="default" />
        </div>
      </Card>

      <Card title="Session" description={'A session lasts 30 days from its last use. This one ends ' + p.sessionEnds + ' unless you use the app again.'}>
        <Button variant="outline" icon="LogOut" onClick={onSignOut}>Sign out</Button>
      </Card>
    </div>
  );
}
if (window.__MAM_KIT) Object.assign(window, { ProfileScreen });
