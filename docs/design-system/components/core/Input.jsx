import React from 'react';
import { Icon } from './Icon.jsx';

export function Input({ className = '', ...rest }) {
  return <input className={'ui-input ' + className} {...rest} />;
}

export function Field({ label, optional, hint, error, children, style }) {
  return (
    <label className="ui-field" style={style} data-invalid={error ? '' : undefined}>
      <span className="ui-label">{label}{optional ? <span style={{ color: 'var(--muted-foreground)', fontWeight: 400 }}> {optional}</span> : null}</span>
      {children}
      {error
        ? <span className="ui-field-error" role="alert"><Icon name="CircleAlert" size={12} /><span>{error}</span></span>
        : hint ? <span className="ui-hint">{hint}</span> : null}
    </label>
  );
}
