import React from 'react';

/** Clapperboard + download arrow on a --primary tile (frontend/src/components/AppLogo.tsx). */
export function AppLogo({ size = 22, withName = false, name = 'Media Acquisition Manager' }) {
  const svg = (
    <svg width={size} height={size} viewBox="0 0 32 32" aria-hidden="true" style={{ flexShrink: 0 }}>
      <rect width="32" height="32" rx="7" style={{ fill: 'var(--primary)' }} />
      <g transform="translate(4 4.5)" fill="none" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round" style={{ stroke: 'var(--primary-foreground)' }}>
        <path d="M20.2 6 3 11l-.9-2.4c-.3-1.1.3-2.2 1.3-2.5l13.5-4c1.1-.3 2.2.3 2.5 1.3Z" />
        <path d="m6.2 5.3 3.1 3.9" /><path d="m12.4 3.4 3.1 4" />
        <path d="M3 11h18v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2Z" />
        <path d="M12 13.5v4.5" /><path d="m9.75 16 2.25 2.25L14.25 16" />
      </g>
    </svg>
  );
  if (!withName) return svg;
  return <span style={{ display: 'inline-flex', alignItems: 'center', gap: '0.5rem', fontSize: '0.9375rem', fontWeight: 600, color: 'var(--foreground)' }}>{svg}<span>{name}</span></span>;
}
