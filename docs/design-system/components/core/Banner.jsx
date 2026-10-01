import React from 'react';
import { Icon } from './Icon.jsx';

const ICON = { error: 'CircleAlert', ok: 'CircleCheck', warning: 'TriangleAlert' };

/**
 * News about the last request, above content that stays on screen: a failed refresh (data is stale),
 * a refused action, a test result. tone: error | warning | ok. action: e.g. a Retry button.
 */
export function Banner({ tone = 'error', children, action, onDismiss }) {
  return (
    <div className="ui-banner" data-tone={tone === 'error' ? undefined : tone} role={tone === 'ok' ? 'status' : 'alert'}>
      <Icon name={ICON[tone] || ICON.error} size={14} />
      <span className="ui-banner-text">{children}</span>
      {action || onDismiss ? (
        <span className="ui-banner-actions">
          {action}
          {onDismiss ? <button type="button" className="ui-button ui-button-ghost ui-button-size-icon" style={{ width: 24, height: 24, color: 'inherit' }} title="Dismiss" onClick={onDismiss}><Icon name="X" size={14} /></button> : null}
        </span>
      ) : null}
    </div>
  );
}

/** One status sentence led by an icon: ok (green icon), error (red text), warning (amber icon), running (spinner). */
export function StatusLine({ status = 'ok', children, as = 'p' }) {
  const Tag = as;
  const icon = status === 'ok' ? 'CircleCheck' : status === 'error' ? 'CircleAlert' : status === 'warning' ? 'TriangleAlert' : 'LoaderCircle';
  return (
    <Tag className="ui-status-line" data-status={status}>
      <Icon name={icon} size={14} className={status === 'running' ? 'spin' : undefined} />
      <span>{children}</span>
    </Tag>
  );
}
