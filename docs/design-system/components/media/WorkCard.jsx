import React from 'react';
import { Badge } from '../core/Badge.jsx';
import { ChipRow, Facts } from './ChipRow.jsx';
import { Icon } from '../core/Icon.jsx';

function FileBlock({ f }) {
  const m = f.media;
  return (
    <div style={{ display: 'grid', gap: '0.125rem', minWidth: 0 }}>
      <p className="mono" style={{ fontWeight: 500, overflowWrap: 'anywhere' }} title={f.folder + '/' + f.name}>{f.name}</p>
      <p className="mono" style={{ fontSize: 'var(--text-xs)', color: 'var(--muted-foreground)', overflowWrap: 'anywhere' }}>{f.folder}</p>
      {f.appPath === null
        ? <p className="ui-status-line" data-status="warning"><Icon name="TriangleAlert" size={14} /><span>This file has no path in this app.</span></p>
        : f.appPath ? <p className="mono" style={{ fontSize: 'var(--text-xs)', color: 'var(--muted-foreground)', overflowWrap: 'anywhere' }}>In this app: {f.appPath}</p> : null}
      <Facts items={[f.size || 'Size unknown', f.location ? 'Library: ' + f.location : 'Library unknown']} />
      {m ? (<>
        <Facts verified items={[m.duration || 'Duration unknown', m.resolution || 'Resolution unknown', m.codec || 'Codec unknown', m.range || 'HDR unknown', m.bitrate || 'Bit rate unknown']} />
        <ChipRow label="Audio" chips={m.audio || []} empty="No audio tracks" />
        <ChipRow label="Subtitles" chips={m.subs || []} empty="No subtitles" />
      </>) : <Facts items={['Not inspected yet']} />}
    </div>
  );
}

/** One work of the Library (LibraryWorkCard, MAM-8/9): titles, year, ids, versions with files and verified facts. */
export function WorkCard({ work, evidence, headingLevel = 2 }) {
  const w = work || {};
  const H = headingLevel === 3 ? 'h3' : 'h2';
  return (
    <article className="ui-card">
      <div className="ui-card-header" style={{ flexDirection: 'row', alignItems: 'flex-start', justifyContent: 'space-between', gap: '0.5rem 1rem', flexWrap: 'wrap' }}>
        <div style={{ display: 'grid', gap: '0.25rem', minWidth: 0 }}>
          <H className="ui-card-title" style={{ overflowWrap: 'anywhere' }}>{w.title || 'Unknown title'}</H>
          <p style={{ display: 'flex', flexWrap: 'wrap', gap: '0.25rem 0.75rem', color: 'var(--muted-foreground)' }}>
            {w.originalTitle ? <span>Original title: {w.originalTitle}</span> : null}
            <span>{w.year || 'Year unknown'}</span>
            {w.source ? <span>From {w.source}</span> : null}
          </p>
          {evidence}
        </div>
        <ul style={{ display: 'flex', flexWrap: 'wrap', justifyContent: 'flex-end', gap: '0.375rem' }}>
          {(w.ids || []).map((id) => <li key={id}><Badge variant="outline">{id}</Badge></li>)}
          {(w.missing || []).map((s) => <li key={s}><Badge variant="warning" icon="TriangleAlert">No {s} id</Badge></li>)}
        </ul>
      </div>
      <div className="ui-card-content">
        <ul style={{ display: 'grid' }}>
          {(w.versions || []).map((v, i) => (
            <li key={i} style={{ display: 'grid', gap: '0.5rem', padding: i === 0 ? '0 0 0.625rem' : '0.625rem 0', borderTop: i ? '1px solid var(--border)' : 0 }}>
              {v.label ? <p style={{ fontWeight: 600 }}>{v.label}</p> : null}
              {(v.files || []).map((f, j) => <FileBlock key={j} f={f} />)}
            </li>
          ))}
        </ul>
      </div>
    </article>
  );
}
