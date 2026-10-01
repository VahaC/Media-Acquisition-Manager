import React from 'react';

/** A native <select> wearing .ui-input, as every filter and page-size control in the app does. */
export function Select({ options = [], className = '', ...rest }) {
  return (
    <select className={'ui-input ' + className} {...rest}>
      {options.map((o) => typeof o === 'string'
        ? <option key={o} value={o}>{o}</option>
        : <option key={o.value} value={o.value}>{o.label}</option>)}
    </select>
  );
}
