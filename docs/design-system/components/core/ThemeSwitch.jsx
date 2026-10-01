import React from 'react';
import { Icon } from './Icon.jsx';

const KEY = 'mam:theme';
const OPTIONS = [['light', 'Sun', 'Light'], ['dark', 'Moon', 'Dark'], ['system', 'Monitor', 'System']];

function apply(mode) {
  const dark = mode === 'dark' || (mode === 'system' && window.matchMedia('(prefers-color-scheme: dark)').matches);
  document.documentElement.classList.toggle('dark', dark);
}

/**
 * Light / Dark / System switch. Stores the choice in localStorage `mam:theme` — the key the app's
 * pre-paint script in frontend/index.html already reads — and toggles `.dark` on <html>.
 */
export function ThemeSwitch({ value, onChange, showLabels = false, size = 'sm' }) {
  const [own, setOwn] = React.useState(() => {
    try { return localStorage.getItem(KEY) || 'system'; } catch (e) { return 'system'; }
  });
  const mode = value || own;
  React.useEffect(() => {
    apply(mode);
    if (mode !== 'system') return undefined;
    const mq = window.matchMedia('(prefers-color-scheme: dark)');
    const on = () => apply('system');
    mq.addEventListener('change', on);
    return () => mq.removeEventListener('change', on);
  }, [mode]);
  const pick = (m) => {
    try { if (m === 'system') localStorage.removeItem(KEY); else localStorage.setItem(KEY, m); } catch (e) {}
    setOwn(m);
    if (onChange) onChange(m);
  };
  const h = size === 'sm' ? 28 : 32;
  return (
    <div role="radiogroup" aria-label="Theme" style={{ display: 'inline-flex', padding: 2, gap: 2, background: 'var(--secondary)', border: '1px solid var(--border)', borderRadius: 'var(--radius-md)' }}>
      {OPTIONS.map(([m, icon, label]) => {
        const on = mode === m;
        return (
          <button key={m} type="button" role="radio" aria-checked={on} title={label} onClick={() => pick(m)}
            style={{ display: 'inline-flex', alignItems: 'center', justifyContent: 'center', gap: 6, height: h, minWidth: h, padding: showLabels ? '0 0.625rem' : 0, flex: showLabels ? undefined : 1,
              fontSize: 12, fontWeight: 500, cursor: 'pointer', borderRadius: 'var(--radius-sm)',
              background: on ? 'var(--card)' : 'transparent', color: on ? 'var(--foreground)' : 'var(--muted-foreground)', boxShadow: on ? 'var(--shadow-sm)' : 'none' }}>
            <Icon name={icon} size={14} />
            {showLabels ? label : null}
          </button>
        );
      })}
    </div>
  );
}
