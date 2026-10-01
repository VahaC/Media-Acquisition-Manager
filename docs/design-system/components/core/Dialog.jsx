import React from 'react';
import { Icon } from './Icon.jsx';

/** Modal dialog (Radix Dialog look). inline renders it in flow, without the overlay, for specimens. */
export function Dialog({ open = true, title, description, children, footer, onClose, maxWidth = '28rem', inline = false }) {
  if (!open) return null;
  const box = (
    <div role="dialog" aria-modal={!inline} className={'ui-dialog-content' + (inline ? ' is-inline' : '')} style={{ maxWidth }}>
      <div className="ui-dialog-header">
        <h2 className="ui-dialog-title" style={{ paddingRight: '1.5rem' }}>{title}</h2>
        {description ? <p className="ui-dialog-description">{description}</p> : null}
      </div>
      {children}
      {footer ? <div className="ui-dialog-footer">{footer}</div> : null}
      {onClose ? <button type="button" className="ui-dialog-close" aria-label="Close" onClick={onClose}><Icon name="X" size={16} /></button> : null}
    </div>
  );
  if (inline) return box;
  return (<>
    <div className="ui-dialog-overlay" onClick={onClose} />
    {box}
  </>);
}
