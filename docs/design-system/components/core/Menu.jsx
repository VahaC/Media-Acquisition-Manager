import React from 'react';
import { Icon } from './Icon.jsx';

/** The shared popup menu (.ui-menu). items: { label, icon, danger, disabled, separator, onClick }. */
export function Menu({ items = [], style, className = '' }) {
  return (
    <div className={'ui-menu ' + className} style={style}>
      {items.map((it, i) => it.separator
        ? <div key={i} className="ui-menu-sep" />
        : (
          <button key={i} type="button" disabled={it.disabled} className={'ui-menu-item' + (it.danger ? ' ui-menu-item--danger' : '')} onClick={it.onClick}>
            {it.icon ? <Icon name={it.icon} size={14} /> : null}
            {it.label}
          </button>
        ))}
    </div>
  );
}
