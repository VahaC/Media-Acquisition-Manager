/* LoginPage (MAM-4) */
function SignInScreen({ onSignIn }) {
  const { Card, Field, Input, Button, Banner } = window.MAM_DS;
  const [err, setErr] = React.useState(null);
  const [busy, setBusy] = React.useState(false);
  const submit = (e) => { e.preventDefault(); setErr(null); const f = e.currentTarget; const ok = f.querySelector('[type=password]').value === 'password123'; setBusy(true); setTimeout(() => { setBusy(false); if (window.MAM_SIM.offline) setErr(SERVER_DOWN); else if (!ok) setErr('The user name or password is incorrect.'); else onSignIn(); }, 500); };
  return (
    <div style={{ display: 'grid', justifyItems: 'center', paddingTop: '2rem' }}>
      <Card title="Sign in" description="Use the user name and password the owner gave you." style={{ width: '100%', maxWidth: '24rem' }}>
        <form onSubmit={submit} style={{ display: 'grid', gap: '1rem' }}>
          {err ? <Banner>{err}</Banner> : null}
          <Field label="User name"><Input defaultValue="vasyl" autoComplete="username" /></Field>
          <Field label="Password"><Input type="password" defaultValue="password123" /></Field>
          <Button type="submit" loading={busy}>{busy ? 'Signing in…' : 'Sign in'}</Button>
          <p style={{ fontSize: 12, color: 'var(--muted-foreground)' }}>Demo: any password other than password123 fails.</p>
        </form>
      </Card>
    </div>
  );
}
if (window.__MAM_KIT) Object.assign(window, { SignInScreen });
