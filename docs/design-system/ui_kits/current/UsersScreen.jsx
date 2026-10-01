/* UsersPage (MAM-4) — owner-only: search, add, disable/enable, reset password, delete */
function UsersScreen() {
  const { Button, Input, Badge, Banner, Dialog, Field, ListPager, ErrorState } = window.MAM_DS;
  const sim = useSim();
  const [retrying, retry] = useRetry('load');
  const [actionError, setActionError] = React.useState(null);
  const failing = () => { if (sim.action || sim.offline) { setActionError(sim.offline ? SERVER_DOWN : 'Could not change this user. Reload the page and try again.'); setDialog(null); return true; } setActionError(null); return false; };
  const [users, setUsers] = React.useState(() => window.MAM_DATA.users.map((u) => ({ ...u })));
  const [q, setQ] = React.useState('');
  const [dialog, setDialog] = React.useState(null);
  const [news, setNews] = React.useState(null);
  const list = users.filter((u) => !q || u.userName.toLowerCase().includes(q.toLowerCase()));
  const toggle = (u) => !failing() && setUsers(users.map((x) => x.userName === u.userName ? { ...x, isDisabled: !x.isDisabled } : x));
  const remove = (u) => { if (failing()) return; setUsers(users.filter((x) => x.userName !== u.userName)); setDialog(null); };
  const add = (name) => { if (name) setUsers([...users, { userName: name, isOwner: false, isDisabled: false, created: '2 Oct 2026, 12:00', lastSignIn: null }].sort((a, b) => a.userName.localeCompare(b.userName))); setDialog(null); };
  return (
    <div style={{ display: 'grid', gap: '1rem' }}>
      <PageHeader title="Users" lead="The people who can sign in to this installation." action={<Button icon="UserPlus" onClick={() => setDialog({ kind: 'add' })}>Add user</Button>} />
      <Input type="search" value={q} onChange={(e) => setQ(e.target.value)} placeholder="Search by user name" aria-label="Search users" style={{ maxWidth: '20rem' }} />
      {news ? <Banner tone="ok" onDismiss={() => setNews(null)}>{news}</Banner> : null}
      {actionError ? <Banner onDismiss={() => setActionError(null)}>{actionError}</Banner> : null}
      {sim.load ? <ErrorState title="Could not load the users." onRetry={retry} retrying={retrying}>{SERVER_DOWN}</ErrorState> : list.length === 0 ? (
        <div style={{ display: 'grid', justifyItems: 'start', gap: '0.75rem', color: 'var(--muted-foreground)' }}>
          <p>No users match “{q}”.</p><Button variant="outline" size="sm" onClick={() => setQ('')}>Clear search</Button>
        </div>
      ) : (<>
        <div className="ui-table-wrap"><table className="ui-table">
          <thead><tr><th>User name</th><th>Created</th><th>Last sign-in</th><th>Actions</th></tr></thead>
          <tbody>{list.map((u) => (
            <tr key={u.userName}>
              <td><span style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: '0.375rem' }}>
                <span style={{ fontWeight: 500, color: u.isDisabled ? 'var(--muted-foreground)' : undefined, overflowWrap: 'anywhere' }}>{u.userName}</span>
                {u.isOwner ? <Badge variant="default">Owner</Badge> : null}
                {u.isDisabled ? <Badge variant="outline">Disabled</Badge> : null}</span></td>
              <td>{u.created}</td>
              <td style={{ color: u.lastSignIn ? undefined : 'var(--muted-foreground)' }}>{u.lastSignIn || 'Never'}</td>
              <td><div style={{ display: 'flex', flexWrap: 'wrap', gap: 6, justifyContent: 'flex-end' }}>
                {u.isDisabled
                  ? <Button variant="outline" size="sm" icon="UserCheck" onClick={() => toggle(u)}>Enable</Button>
                  : <span title={u.isOwner ? "The installation owner can't be disabled." : undefined} style={{ display: 'inline-flex', cursor: u.isOwner ? 'not-allowed' : undefined }}><Button variant="outline" size="sm" icon="UserX" disabled={u.isOwner} onClick={() => toggle(u)}>Disable</Button></span>}
                <Button variant="outline" size="sm" icon="KeyRound" onClick={() => setDialog({ kind: 'reset', user: u })}>Reset password</Button>
                <span title={u.isOwner ? "The installation owner can't be deleted." : undefined} style={{ display: 'inline-flex', cursor: u.isOwner ? 'not-allowed' : undefined }}><Button variant="danger" size="sm" icon="Trash2" disabled={u.isOwner} onClick={() => setDialog({ kind: 'delete', user: u })}>Delete</Button></span>
              </div></td>
            </tr>))}</tbody>
        </table></div>
        <ListPager page={1} pages={1} pageSize={25} total={list.length} />
      </>)}
      {dialog && dialog.kind === 'add' ? <AddUserDialog onClose={() => setDialog(null)} onAdd={add} fail={sim.action || sim.offline} taken={users.map((x) => x.userName.toLowerCase())} /> : null}
      {dialog && dialog.kind === 'reset' ? (
        <Dialog title={'Reset the password of ' + dialog.user.userName}
          description={dialog.user.isOwner ? 'Your other devices are signed out. This one stays signed in.' : dialog.user.userName + ' is signed out on every device and signs in again with the new password.'}
          onClose={() => setDialog(null)} footer={<><Button onClick={() => { setNews('The password of ' + dialog.user.userName + ' was reset.'); setDialog(null); }}>Reset password</Button><Button variant="outline" onClick={() => setDialog(null)}>Cancel</Button></>}>
          <Field label="New password" hint="8–128 characters."><Input type="password" autoComplete="new-password" /></Field>
        </Dialog>) : null}
      {dialog && dialog.kind === 'delete' ? (
        <Dialog maxWidth="26rem" title={'Delete ' + dialog.user.userName + '?'} description={dialog.user.userName + " can no longer sign in and is signed out everywhere. This can't be undone."}
          onClose={() => setDialog(null)} footer={<><Button variant="destructive" onClick={() => remove(dialog.user)}>Delete</Button><Button variant="outline" onClick={() => setDialog(null)}>Cancel</Button></>} />) : null}
    </div>
  );
}
function AddUserDialog({ onClose, onAdd, fail, taken }) {
  const { Button, Input, Dialog, Field, Banner } = window.MAM_DS;
  const [v, setV] = React.useState({ name: '', a: '', b: '' });
  const [err, setErr] = React.useState({});
  const [busy, setBusy] = React.useState(false);
  const submit = () => {
    const name = v.name.trim(), e = {};
    if (!/^[A-Za-z0-9._-]{3,32}$/.test(name)) e.name = 'A user name is 3–32 letters, digits, ".", "-" or "_".';
    else if (taken.includes(name.toLowerCase())) e.name = 'A user named ' + name + ' already exists.';
    if (v.a.length < 8) e.a = 'A password is at least 8 characters.';
    if (v.b !== v.a) e.b = 'The two passwords are different. Type the same password in both fields.';
    setErr(e);
    if (Object.keys(e).length) return;
    setBusy(true);
    setTimeout(() => { setBusy(false); if (fail) setErr({ form: 'Could not add the user. The server could not be reached. Check that it is running, then try again.' }); else onAdd(name); }, 600);
  };
  const set = (k) => (ev) => { setV({ ...v, [k]: ev.target.value }); if (err[k]) setErr({ ...err, [k]: undefined }); };
  return (
    <Dialog title="Add user" description="They sign in with this user name and password. You can reset the password later." onClose={onClose}
      footer={<><Button loading={busy} onClick={submit}>{busy ? 'Adding…' : 'Add user'}</Button><Button variant="outline" onClick={onClose}>Cancel</Button></>}>
      <div style={{ display: 'grid', gap: '1rem' }}>
        <Field label="User name" hint="3–32 letters, digits, . - and _" error={err.name}><Input value={v.name} onChange={set('name')} autoComplete="off" /></Field>
        <Field label="Password" hint="8–128 characters." error={err.a}><Input type="password" value={v.a} onChange={set('a')} autoComplete="new-password" /></Field>
        <Field label="Confirm password" error={err.b}><Input type="password" value={v.b} onChange={set('b')} autoComplete="new-password" /></Field>
        {err.form ? <Banner>{err.form}</Banner> : null}
      </div>
    </Dialog>
  );
}
if (window.__MAM_KIT) Object.assign(window, { UsersScreen });
