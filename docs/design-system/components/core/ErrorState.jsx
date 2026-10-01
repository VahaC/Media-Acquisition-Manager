import React from 'react';
import { Icon } from './Icon.jsx';
import { Button } from './Button.jsx';

/**
 * In place of a list or panel whose first load failed — there is nothing to show, so the block says
 * what failed (title), the server's sentence (children) and offers Retry.
 */
export function ErrorState({ title, children, onRetry, retryLabel = 'Try again', retrying = false, icon = 'CircleAlert', action }) {
  return (
    <div className="ui-error-state" role="alert">
      <div className="ui-error-state-head">
        <span className="ui-error-state-icon"><Icon name={icon} size={16} /></span>
        <div style={{ display: 'grid', gap: 2, minWidth: 0, paddingTop: 6 }}>
          <p className="ui-error-state-title">{title}</p>
          {children ? <p className="ui-error-state-body">{children}</p> : null}
        </div>
      </div>
      {onRetry || action ? (
        <div style={{ display: 'flex', gap: 8, paddingLeft: 42 }}>
          {onRetry ? <Button variant="outline" size="sm" icon="RotateCw" loading={retrying} onClick={onRetry}>{retrying ? 'Retrying…' : retryLabel}</Button> : null}
          {action}
        </div>
      ) : null}
    </div>
  );
}
