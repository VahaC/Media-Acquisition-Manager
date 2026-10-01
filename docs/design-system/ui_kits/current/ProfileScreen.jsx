/* Profile — NEW page (no counterpart in the app yet). Uses only what the API already knows about the
   signed-in user: name, role, created, last sign-in; the password reset the owner can do on themself;
   the theme stored in mam:theme; sign-out. */
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
  const [pw, setPw] = React.useState({ a: '', b: '' });
  const [news, setNews] = React.useState(null);
  const differs = pw.b !== '' && pw.a !== pw.b;
  const short = pw.a !== '' && pw.a.length < 8;
  const save = () => { if (!pw.a || differs || short) return; setPwOpen(false); setPw({ a: '', b: '' }); setNews('Password changed. Your other devices are signed out; this one stays signed in.'); };
  return (
    <div style={{ display: 'grid', gap: '1.25rem', maxWidth: '44rem' }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: '1rem' }}>
        <span style={{ width: 56, height: 56, borderRadius: 9999, display: 'inline-grid', placeItems: 'center', background: 'var(--tint-selected)', color: 'var(--primary)', fontWeight: 600, fontSize: 22, flexShrink: 0 }}>{u.userName[0].toUpperCase()}</span>
        <div style={{ display: 'grid', gap: 4 }}>
          <h1 style={{ fontSize: 'var(--text-2xl)', fontWeight: 600, lineHeight: 1.2, display: 'flex', alignItems: 'center', gap: 8 }}>{u.userName}{u.isOwner ? <Badge variant="default">Owner</Badge> : null}</h1>
          <p style={{ fontSize: '0.875rem', color: 'var(--muted-foreground)' }}>{u.isOwner ? 'The installation owner: manages users, integrations and settings.' : 'Can search and browse the library.'}</p>
        </div>
      </div>
      {news ? <Banner tone="ok">{news}</Banner> : null}

      <Card title="Account">
        <ProfileRow label="User name"><span className="mono">{u.userName}</span></ProfileRow>
        <ProfileRow label="Role">{u.isOwner ? 'Owner' : 'Member'}</ProfileRow>
        <ProfileRow label="Created">{p.created}</ProfileRow>
        <ProfileRow label="Last sign-in" last>{p.lastSignIn}</ProfileRow>
      </Card>

      <Card title="Password" description="There is no email reset, so keep it somewhere safe.">
        {u.isOwner ? (pwOpen ? (
          <form onSubmit={(e) => { e.preventDefault(); save(); }} style={{ display: 'grid', gap: '1rem', maxWidth: '22rem' }}>
            <Field label="New password" hint={short ? undefined : '8–128 characters.'}><Input type="password" value={pw.a} onChange={(e) => setPw({ ...pw, a: e.target.value })} autoComplete="new-password" /></Field>
            {short ? <p style={{ color: 'var(--destructive)', fontSize: 12, marginTop: '-0.625rem' }}>At least 8 characters.</p> : null}
            <Field label="Confirm password"><Input type="password" value={pw.b} onChange={(e) => setPw({ ...pw, b: e.target.value })} autoComplete="new-password" /></Field>
            {differs ? <p style={{ color: 'var(--destructive)', fontSize: 12, marginTop: '-0.625rem' }}>The two passwords are different. Type the same password in both fields.</p> : null}
            <p style={{ fontSize: 12, color: 'var(--muted-foreground)' }}>Your other devices are signed out. This one stays signed in.</p>
            <div style={{ display: 'flex', gap: 8 }}><Button type="submit" disabled={!pw.a || differs || short}>Change password</Button><Button variant="outline" onClick={() => { setPwOpen(false); setPw({ a: '', b: '' }); }}>Cancel</Button></div>
          </form>
        ) : (
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '1rem', flexWrap: 'wrap' }}>
            <span className="mono" style={{ letterSpacing: 2, color: 'var(--muted-foreground)' }}>••••••••••</span>
            <Button variant="outline" size="sm" icon="KeyRound" onClick={() => setPwOpen(true)}>Change password</Button>
          </div>
        )) : (
          <p style={{ color: 'var(--muted-foreground)' }}>The owner resets your password under Users.</p>
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
