import React from 'react';

/** A labelled row of outline chips: a file's audio/subtitle tracks (verified) or a release's "Claimed" quality. */
export function ChipRow({ label, chips = [], empty }) {
  if (chips.length === 0 && empty) return <p className="ui-facts"><span>{empty}</span></p>;
  return (
    <div className="ui-chip-row">
      <span className="ui-chip-row-label">{label}</span>
      <ul className="ui-chip-list">{chips.map((c, i) => <li key={i} className="ui-chip">{c}</li>)}</ul>
    </div>
  );
}

/** A line of facts separated by gap; verified=true reads in the text colour (ffprobe facts). */
export function Facts({ items = [], verified = false }) {
  return <p className={'ui-facts' + (verified ? ' is-verified' : '')}>{items.map((x, i) => <span key={i}>{x}</span>)}</p>;
}
