import React from 'react';

/** Renders a Lucide icon by PascalCase name from the lucide UMD global (window.lucide). */
export function Icon({ name, size = 14, strokeWidth = 2, className, style, title, ...rest }) {
  const lib = typeof window !== 'undefined' ? window.lucide : null;
  const node = lib ? ((lib.icons && lib.icons[name]) || lib[name]) : null;
  let children = [];
  if (Array.isArray(node)) children = node.length === 3 && node[0] === 'svg' ? node[2] : node;
  return (
    <svg xmlns="http://www.w3.org/2000/svg" width={size} height={size} viewBox="0 0 24 24" fill="none"
      stroke="currentColor" strokeWidth={strokeWidth} strokeLinecap="round" strokeLinejoin="round"
      className={className} style={{ flexShrink: 0, ...style }} aria-hidden={title ? undefined : 'true'} {...rest}>
      {title ? <title>{title}</title> : null}
      {children.map(([tag, attrs], i) => React.createElement(tag, { key: i, ...attrs }))}
    </svg>
  );
}
