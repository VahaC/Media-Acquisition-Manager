import React from 'react';
import { Badge } from '../core/Badge.jsx';

const TEXT = {
  Identifier: 'Matched by id', TitleAndYear: 'Matched by title and year', TitleOnly: 'Possible match — check it',
};
const IN_LIB = {
  Identifier: 'In your library, matched by id', TitleAndYear: 'In your library, matched by title and year', TitleOnly: 'Possible match in your library',
};

/** Why a library work matched: evidence badge(s), the matched title, a warning per conflicting scheme. */
export function MatchBadges({ evidence = 'Identifier', schemes = [], matchedTitle, conflicts = [], inLibraryOf }) {
  const warn = evidence === 'TitleOnly';
  const icon = warn ? 'CircleHelp' : 'CircleCheck';
  const variant = warn ? 'warning' : 'secondary';
  const ev = inLibraryOf !== undefined
    ? [<Badge key="e" variant={variant} icon={icon} title={inLibraryOf}>{IN_LIB[evidence]}</Badge>]
    : evidence === 'Identifier' && schemes.length
      ? schemes.map((s) => <Badge key={s} variant="secondary" icon="CircleCheck">Matched by {s} id</Badge>)
      : [<Badge key="e" variant={variant} icon={icon}>{TEXT[evidence]}</Badge>];
  return (
    <ul style={{ display: 'flex', flexWrap: 'wrap', gap: '0.375rem', minWidth: 0 }}>
      {ev.map((b, i) => <li key={i}>{b}</li>)}
      {matchedTitle ? <li><Badge variant="outline">“{matchedTitle}”</Badge></li> : null}
      {conflicts.map((c) => <li key={c}><Badge variant="warning" icon="TriangleAlert">Conflicting {c} ids</Badge></li>)}
    </ul>
  );
}
