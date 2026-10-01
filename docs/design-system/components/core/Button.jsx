import React from 'react';
import { Icon } from './Icon.jsx';

export function Button({ variant = 'default', size = 'default', icon, iconRight, loading = false, className = '', children, type = 'button', ...rest }) {
  const cls = ['ui-button', variant === 'danger' ? 'ui-button-outline ui-button-danger' : 'ui-button-' + variant, 'ui-button-size-' + size, className].join(' ');
  const s = size === 'default' || size === 'lg' ? 16 : 14;
  return (
    <button type={type} className={cls} {...rest}>
      {loading ? <Icon name="LoaderCircle" size={s} className="spin" /> : icon ? <Icon name={icon} size={s} /> : null}
      {children != null && size !== 'icon' ? <span className="label-text">{children}</span> : null}
      {iconRight ? <Icon name={iconRight} size={s} /> : null}
    </button>
  );
}
