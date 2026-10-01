import React from 'react';

export function Card({ title, description, headerEnd, children, footer, as = 'section', className = '', style, padded = true }) {
  const Tag = as;
  const hasHeader = title || description || headerEnd;
  return (
    <Tag className={'ui-card ' + className} style={style}>
      {hasHeader ? (
        <div className="ui-card-header" style={headerEnd ? { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'flex-start', gap: '0.5rem 1rem' } : undefined}>
          <div style={{ display: 'grid', gap: '0.375rem', minWidth: 0 }}>
            {title ? <h2 className="ui-card-title">{title}</h2> : null}
            {description ? <p className="ui-card-description">{description}</p> : null}
          </div>
          {headerEnd}
        </div>
      ) : null}
      {children != null ? <div className={padded ? 'ui-card-content' : ''} style={!hasHeader && padded ? { paddingTop: '1.5rem' } : undefined}>{children}</div> : null}
      {footer ? <div className="ui-card-footer">{footer}</div> : null}
    </Tag>
  );
}
