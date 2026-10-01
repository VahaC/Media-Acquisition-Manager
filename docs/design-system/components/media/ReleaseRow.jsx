import React from 'react';
import { Button } from '../core/Button.jsx';
import { ChipRow, Facts } from './ChipRow.jsx';
import { MatchBadges } from './MatchBadges.jsx';

/** One release a source listed (SearchPage, MAM-12): title, listing facts, claimed chips, library match, actions. */
export function ReleaseRow({ release, onDetails, downloadEnabled = false, onDownload }) {
  const r = release || {};
  const c = r.claimed || {};
  const chips = [
    c.resolution ? c.resolution + 'p' : 'Resolution unknown',
    c.medium || 'Medium unknown',
    c.codec || 'Codec unknown',
    ...(c.ranges && c.ranges.length ? c.ranges : ['HDR unknown']),
    c.audio && c.audio.length ? 'Audio: ' + c.audio.join(' · ') : 'Audio language unknown',
    c.subs && c.subs.length ? 'Subtitles: ' + c.subs.join(' · ') : 'Subtitles unknown',
  ];
  return (
    <article className="ui-card" style={{ display: 'grid', gap: '0.5rem', padding: '0.75rem 1rem', minWidth: 0 }}>
      <div style={{ display: 'grid', gap: '0.375rem', minWidth: 0 }}>
        <h3 style={{ fontWeight: 600, overflowWrap: 'anywhere', fontSize: 13 }}>{r.title}</h3>
        <Facts items={[r.source, r.size || 'Size unknown', r.seeders != null ? r.seeders + ' seeders' : 'Seeders unknown', r.peers != null ? r.peers + ' peers' : 'Peers unknown', r.published ? 'Published ' + r.published : 'Publish date unknown']} />
        <ChipRow label="Claimed" chips={chips} />
        {r.match ? <MatchBadges evidence={r.match.evidence} conflicts={r.match.conflicts || []} inLibraryOf={r.match.work} /> : null}
      </div>
      <div style={{ display: 'flex', flexWrap: 'wrap', justifyContent: 'flex-end', gap: 6 }}>
        <Button variant="outline" size="sm" icon="Info" onClick={onDetails}>Details</Button>
        <Button variant="outline" size="sm" icon="ExternalLink">Open source</Button>
        <Button variant="outline" size="sm" icon="Download" disabled={!downloadEnabled} title={downloadEnabled ? 'Download' : 'Sending a release to a download client comes in a later update.'} onClick={onDownload}>Download</Button>
      </div>
    </article>
  );
}
