import React from 'react';
import { Icon } from './Icon.jsx';

export function Badge({ variant = 'secondary', icon, title, children, className = '' }) {
  return (
    <span className={'ui-badge ui-badge-' + variant + ' ' + className} title={title}>
      {icon ? <Icon name={icon} size={12} /> : null}
      {children}
    </span>
  );
}
