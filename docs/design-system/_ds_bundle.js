/* @ds-bundle: {"format":4,"namespace":"MediaAcquisitionManagerDS_29af91","components":[{"name":"AppLogo","sourcePath":"components/core/AppLogo.jsx"},{"name":"Badge","sourcePath":"components/core/Badge.jsx"},{"name":"Banner","sourcePath":"components/core/Banner.jsx"},{"name":"StatusLine","sourcePath":"components/core/Banner.jsx"},{"name":"Button","sourcePath":"components/core/Button.jsx"},{"name":"Card","sourcePath":"components/core/Card.jsx"},{"name":"Dialog","sourcePath":"components/core/Dialog.jsx"},{"name":"ErrorState","sourcePath":"components/core/ErrorState.jsx"},{"name":"Icon","sourcePath":"components/core/Icon.jsx"},{"name":"Input","sourcePath":"components/core/Input.jsx"},{"name":"Field","sourcePath":"components/core/Input.jsx"},{"name":"ListPager","sourcePath":"components/core/ListPager.jsx"},{"name":"Menu","sourcePath":"components/core/Menu.jsx"},{"name":"Select","sourcePath":"components/core/Select.jsx"},{"name":"ThemeSwitch","sourcePath":"components/core/ThemeSwitch.jsx"},{"name":"ChipRow","sourcePath":"components/media/ChipRow.jsx"},{"name":"Facts","sourcePath":"components/media/ChipRow.jsx"},{"name":"MatchBadges","sourcePath":"components/media/MatchBadges.jsx"},{"name":"ReleaseRow","sourcePath":"components/media/ReleaseRow.jsx"},{"name":"WorkCard","sourcePath":"components/media/WorkCard.jsx"}],"sourceHashes":{"components/core/AppLogo.jsx":"349271c2647d","components/core/Badge.jsx":"e113ae8aa103","components/core/Banner.jsx":"c219666c2c7c","components/core/Button.jsx":"ff2185f8aa19","components/core/Card.jsx":"2bdf1b6efafd","components/core/Dialog.jsx":"2e8d5e60f91b","components/core/ErrorState.jsx":"a8720dcf3630","components/core/Icon.jsx":"03b67ff0fd20","components/core/Input.jsx":"5b5452a62d10","components/core/ListPager.jsx":"e17f5f400dec","components/core/Menu.jsx":"d67fe8692f2e","components/core/Select.jsx":"0de1af206394","components/core/ThemeSwitch.jsx":"6008218dce59","components/media/ChipRow.jsx":"7b74fc93f385","components/media/MatchBadges.jsx":"f7fdba85cb60","components/media/ReleaseRow.jsx":"9be90a5b72de","components/media/WorkCard.jsx":"2b8c2b4e3a45","ui_kits/_shared/data.js":"cbff5490e85d","ui_kits/_shared/ds-fallback.js":"7985ebd8ff1a","ui_kits/current/LibraryMatch.jsx":"f775df32274d","ui_kits/current/LibraryScreen.jsx":"25d7d57a18f1","ui_kits/current/ProfileScreen.jsx":"2b72033ed317","ui_kits/current/SearchScreen.jsx":"5e631fbbc2a1","ui_kits/current/SettingsScreens.jsx":"8043e479e9b3","ui_kits/current/Shell.jsx":"77c2d91ed736","ui_kits/current/SignIn.jsx":"30203de92854","ui_kits/current/Sim.jsx":"eec00315893d","ui_kits/current/UsersScreen.jsx":"6db7475b74fa","ui_kits/proposal/Notes.jsx":"6a487c586583","ui_kits/proposal/PLibrary.jsx":"a3d6e3cd1405","ui_kits/proposal/PSearch.jsx":"3d532a4e3e4c","ui_kits/proposal/PSettings.jsx":"229842979347","ui_kits/proposal/PShell.jsx":"9a7f0814aec2"},"inlinedExternals":[],"unexposedExports":[]} */

(() => {

const __ds_ns = (window.MediaAcquisitionManagerDS_29af91 = window.MediaAcquisitionManagerDS_29af91 || {});

const __ds_scope = {};

(__ds_ns.__errors = __ds_ns.__errors || []);

// components/core/AppLogo.jsx
try { (() => {
/** Clapperboard + download arrow on a --primary tile (frontend/src/components/AppLogo.tsx). */
function AppLogo({
  size = 22,
  withName = false,
  name = 'Media Acquisition Manager'
}) {
  const svg = /*#__PURE__*/React.createElement("svg", {
    width: size,
    height: size,
    viewBox: "0 0 32 32",
    "aria-hidden": "true",
    style: {
      flexShrink: 0
    }
  }, /*#__PURE__*/React.createElement("rect", {
    width: "32",
    height: "32",
    rx: "7",
    style: {
      fill: 'var(--primary)'
    }
  }), /*#__PURE__*/React.createElement("g", {
    transform: "translate(4 4.5)",
    fill: "none",
    strokeWidth: "2.2",
    strokeLinecap: "round",
    strokeLinejoin: "round",
    style: {
      stroke: 'var(--primary-foreground)'
    }
  }, /*#__PURE__*/React.createElement("path", {
    d: "M20.2 6 3 11l-.9-2.4c-.3-1.1.3-2.2 1.3-2.5l13.5-4c1.1-.3 2.2.3 2.5 1.3Z"
  }), /*#__PURE__*/React.createElement("path", {
    d: "m6.2 5.3 3.1 3.9"
  }), /*#__PURE__*/React.createElement("path", {
    d: "m12.4 3.4 3.1 4"
  }), /*#__PURE__*/React.createElement("path", {
    d: "M3 11h18v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2Z"
  }), /*#__PURE__*/React.createElement("path", {
    d: "M12 13.5v4.5"
  }), /*#__PURE__*/React.createElement("path", {
    d: "m9.75 16 2.25 2.25L14.25 16"
  })));
  if (!withName) return svg;
  return /*#__PURE__*/React.createElement("span", {
    style: {
      display: 'inline-flex',
      alignItems: 'center',
      gap: '0.5rem',
      fontSize: '0.9375rem',
      fontWeight: 600,
      color: 'var(--foreground)'
    }
  }, svg, /*#__PURE__*/React.createElement("span", null, name));
}
Object.assign(__ds_scope, { AppLogo });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/core/AppLogo.jsx", error: String((e && e.message) || e) }); }

// components/core/Card.jsx
try { (() => {
function Card({
  title,
  description,
  headerEnd,
  children,
  footer,
  as = 'section',
  className = '',
  style,
  padded = true
}) {
  const Tag = as;
  const hasHeader = title || description || headerEnd;
  return /*#__PURE__*/React.createElement(Tag, {
    className: 'ui-card ' + className,
    style: style
  }, hasHeader ? /*#__PURE__*/React.createElement("div", {
    className: "ui-card-header",
    style: headerEnd ? {
      flexDirection: 'row',
      justifyContent: 'space-between',
      alignItems: 'flex-start',
      gap: '0.5rem 1rem'
    } : undefined
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gap: '0.375rem',
      minWidth: 0
    }
  }, title ? /*#__PURE__*/React.createElement("h2", {
    className: "ui-card-title"
  }, title) : null, description ? /*#__PURE__*/React.createElement("p", {
    className: "ui-card-description"
  }, description) : null), headerEnd) : null, children != null ? /*#__PURE__*/React.createElement("div", {
    className: padded ? 'ui-card-content' : '',
    style: !hasHeader && padded ? {
      paddingTop: '1.5rem'
    } : undefined
  }, children) : null, footer ? /*#__PURE__*/React.createElement("div", {
    className: "ui-card-footer"
  }, footer) : null);
}
Object.assign(__ds_scope, { Card });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/core/Card.jsx", error: String((e && e.message) || e) }); }

// components/core/Icon.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/** Renders a Lucide icon by PascalCase name from the lucide UMD global (window.lucide). */
function Icon({
  name,
  size = 14,
  strokeWidth = 2,
  className,
  style,
  title,
  ...rest
}) {
  const lib = typeof window !== 'undefined' ? window.lucide : null;
  const node = lib ? lib.icons && lib.icons[name] || lib[name] : null;
  let children = [];
  if (Array.isArray(node)) children = node.length === 3 && node[0] === 'svg' ? node[2] : node;
  return /*#__PURE__*/React.createElement("svg", _extends({
    xmlns: "http://www.w3.org/2000/svg",
    width: size,
    height: size,
    viewBox: "0 0 24 24",
    fill: "none",
    stroke: "currentColor",
    strokeWidth: strokeWidth,
    strokeLinecap: "round",
    strokeLinejoin: "round",
    className: className,
    style: {
      flexShrink: 0,
      ...style
    },
    "aria-hidden": title ? undefined : 'true'
  }, rest), title ? /*#__PURE__*/React.createElement("title", null, title) : null, children.map(([tag, attrs], i) => React.createElement(tag, {
    key: i,
    ...attrs
  })));
}
Object.assign(__ds_scope, { Icon });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/core/Icon.jsx", error: String((e && e.message) || e) }); }

// components/core/Badge.jsx
try { (() => {
function Badge({
  variant = 'secondary',
  icon,
  title,
  children,
  className = ''
}) {
  return /*#__PURE__*/React.createElement("span", {
    className: 'ui-badge ui-badge-' + variant + ' ' + className,
    title: title
  }, icon ? /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: icon,
    size: 12
  }) : null, children);
}
Object.assign(__ds_scope, { Badge });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/core/Badge.jsx", error: String((e && e.message) || e) }); }

// components/core/Banner.jsx
try { (() => {
const ICON = {
  error: 'CircleAlert',
  ok: 'CircleCheck',
  warning: 'TriangleAlert'
};

/**
 * News about the last request, above content that stays on screen: a failed refresh (data is stale),
 * a refused action, a test result. tone: error | warning | ok. action: e.g. a Retry button.
 */
function Banner({
  tone = 'error',
  children,
  action,
  onDismiss
}) {
  return /*#__PURE__*/React.createElement("div", {
    className: "ui-banner",
    "data-tone": tone === 'error' ? undefined : tone,
    role: tone === 'ok' ? 'status' : 'alert'
  }, /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: ICON[tone] || ICON.error,
    size: 14
  }), /*#__PURE__*/React.createElement("span", {
    className: "ui-banner-text"
  }, children), action || onDismiss ? /*#__PURE__*/React.createElement("span", {
    className: "ui-banner-actions"
  }, action, onDismiss ? /*#__PURE__*/React.createElement("button", {
    type: "button",
    className: "ui-button ui-button-ghost ui-button-size-icon",
    style: {
      width: 24,
      height: 24,
      color: 'inherit'
    },
    title: "Dismiss",
    onClick: onDismiss
  }, /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: "X",
    size: 14
  })) : null) : null);
}

/** One status sentence led by an icon: ok (green icon), error (red text), warning (amber icon), running (spinner). */
function StatusLine({
  status = 'ok',
  children,
  as = 'p'
}) {
  const Tag = as;
  const icon = status === 'ok' ? 'CircleCheck' : status === 'error' ? 'CircleAlert' : status === 'warning' ? 'TriangleAlert' : 'LoaderCircle';
  return /*#__PURE__*/React.createElement(Tag, {
    className: "ui-status-line",
    "data-status": status
  }, /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: icon,
    size: 14,
    className: status === 'running' ? 'spin' : undefined
  }), /*#__PURE__*/React.createElement("span", null, children));
}
Object.assign(__ds_scope, { Banner, StatusLine });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/core/Banner.jsx", error: String((e && e.message) || e) }); }

// components/core/Button.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
function Button({
  variant = 'default',
  size = 'default',
  icon,
  iconRight,
  loading = false,
  className = '',
  children,
  type = 'button',
  ...rest
}) {
  const cls = ['ui-button', variant === 'danger' ? 'ui-button-outline ui-button-danger' : 'ui-button-' + variant, 'ui-button-size-' + size, className].join(' ');
  const s = size === 'default' || size === 'lg' ? 16 : 14;
  return /*#__PURE__*/React.createElement("button", _extends({
    type: type,
    className: cls
  }, rest), loading ? /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: "LoaderCircle",
    size: s,
    className: "spin"
  }) : icon ? /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: icon,
    size: s
  }) : null, children != null && size !== 'icon' ? /*#__PURE__*/React.createElement("span", {
    className: "label-text"
  }, children) : null, iconRight ? /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: iconRight,
    size: s
  }) : null);
}
Object.assign(__ds_scope, { Button });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/core/Button.jsx", error: String((e && e.message) || e) }); }

// components/core/Dialog.jsx
try { (() => {
/** Modal dialog (Radix Dialog look). inline renders it in flow, without the overlay, for specimens. */
function Dialog({
  open = true,
  title,
  description,
  children,
  footer,
  onClose,
  maxWidth = '28rem',
  inline = false
}) {
  if (!open) return null;
  const box = /*#__PURE__*/React.createElement("div", {
    role: "dialog",
    "aria-modal": !inline,
    className: 'ui-dialog-content' + (inline ? ' is-inline' : ''),
    style: {
      maxWidth
    }
  }, /*#__PURE__*/React.createElement("div", {
    className: "ui-dialog-header"
  }, /*#__PURE__*/React.createElement("h2", {
    className: "ui-dialog-title",
    style: {
      paddingRight: '1.5rem'
    }
  }, title), description ? /*#__PURE__*/React.createElement("p", {
    className: "ui-dialog-description"
  }, description) : null), children, footer ? /*#__PURE__*/React.createElement("div", {
    className: "ui-dialog-footer"
  }, footer) : null, onClose ? /*#__PURE__*/React.createElement("button", {
    type: "button",
    className: "ui-dialog-close",
    "aria-label": "Close",
    onClick: onClose
  }, /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: "X",
    size: 16
  })) : null);
  if (inline) return box;
  return /*#__PURE__*/React.createElement(React.Fragment, null, /*#__PURE__*/React.createElement("div", {
    className: "ui-dialog-overlay",
    onClick: onClose
  }), box);
}
Object.assign(__ds_scope, { Dialog });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/core/Dialog.jsx", error: String((e && e.message) || e) }); }

// components/core/ErrorState.jsx
try { (() => {
/**
 * In place of a list or panel whose first load failed — there is nothing to show, so the block says
 * what failed (title), the server's sentence (children) and offers Retry.
 */
function ErrorState({
  title,
  children,
  onRetry,
  retryLabel = 'Try again',
  retrying = false,
  icon = 'CircleAlert',
  action
}) {
  return /*#__PURE__*/React.createElement("div", {
    className: "ui-error-state",
    role: "alert"
  }, /*#__PURE__*/React.createElement("div", {
    className: "ui-error-state-head"
  }, /*#__PURE__*/React.createElement("span", {
    className: "ui-error-state-icon"
  }, /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: icon,
    size: 16
  })), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gap: 2,
      minWidth: 0,
      paddingTop: 6
    }
  }, /*#__PURE__*/React.createElement("p", {
    className: "ui-error-state-title"
  }, title), children ? /*#__PURE__*/React.createElement("p", {
    className: "ui-error-state-body"
  }, children) : null)), onRetry || action ? /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      gap: 8,
      paddingLeft: 42
    }
  }, onRetry ? /*#__PURE__*/React.createElement(__ds_scope.Button, {
    variant: "outline",
    size: "sm",
    icon: "RotateCw",
    loading: retrying,
    onClick: onRetry
  }, retrying ? 'Retrying…' : retryLabel) : null, action) : null);
}
Object.assign(__ds_scope, { ErrorState });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/core/ErrorState.jsx", error: String((e && e.message) || e) }); }

// components/core/Input.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
function Input({
  className = '',
  ...rest
}) {
  return /*#__PURE__*/React.createElement("input", _extends({
    className: 'ui-input ' + className
  }, rest));
}
function Field({
  label,
  optional,
  hint,
  error,
  children,
  style
}) {
  return /*#__PURE__*/React.createElement("label", {
    className: "ui-field",
    style: style,
    "data-invalid": error ? '' : undefined
  }, /*#__PURE__*/React.createElement("span", {
    className: "ui-label"
  }, label, optional ? /*#__PURE__*/React.createElement("span", {
    style: {
      color: 'var(--muted-foreground)',
      fontWeight: 400
    }
  }, " ", optional) : null), children, error ? /*#__PURE__*/React.createElement("span", {
    className: "ui-field-error",
    role: "alert"
  }, /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: "CircleAlert",
    size: 12
  }), /*#__PURE__*/React.createElement("span", null, error)) : hint ? /*#__PURE__*/React.createElement("span", {
    className: "ui-hint"
  }, hint) : null);
}
Object.assign(__ds_scope, { Input, Field });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/core/Input.jsx", error: String((e && e.message) || e) }); }

// components/core/ListPager.jsx
try { (() => {
/** Page numbers to show: always first and last, a window around the current page, gaps as '…'. */
function pageList(page, pages, around) {
  const set = new Set([1, pages]);
  for (let p = page - around; p <= page + around; p++) if (p >= 1 && p <= pages) set.add(p);
  const sorted = [...set].sort((a, b) => a - b);
  const out = [];
  sorted.forEach((p, i) => {
    if (i && p - sorted[i - 1] === 2) out.push(p - 1);else if (i && p - sorted[i - 1] > 2) out.push('…' + i);
    out.push(p);
  });
  return out;
}
function ListPager({
  page = 1,
  pages: pagesProp = 1,
  pageSize: sizeProp = 25,
  pageSizes = [10, 25, 50, 100],
  total,
  onPage,
  onPageSize,
  around = 1
}) {
  const [own, setOwn] = React.useState(page);
  React.useEffect(() => setOwn(page), [page]);
  const [ownSize, setOwnSize] = React.useState(sizeProp);
  React.useEffect(() => setOwnSize(sizeProp), [sizeProp]);
  const pageSize = onPageSize ? sizeProp : ownSize;
  // Without onPageSize (demos) the pager works out its own page count from total.
  const pages = !onPageSize && total != null ? Math.max(1, Math.ceil(total / pageSize)) : pagesProp;
  const cur = onPage ? page : own;
  const go = p => {
    if (p < 1 || p > pages || p === cur) return;
    if (onPage) onPage(p);else setOwn(p);
  };
  const changeSize = s => {
    if (onPageSize) onPageSize(s);else {
      setOwnSize(s);
      setOwn(1);
    }
  };
  const btn = on => ({
    minWidth: 32,
    height: 32,
    padding: '0 0.5rem',
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    borderRadius: 'var(--radius-md)',
    fontSize: '0.8125rem',
    fontWeight: on ? 600 : 500,
    fontVariantNumeric: 'tabular-nums',
    cursor: on ? 'default' : 'pointer',
    border: '1px solid ' + (on ? 'var(--primary)' : 'transparent'),
    background: on ? 'var(--tint-selected)' : 'transparent',
    color: on ? 'var(--foreground)' : 'var(--muted-foreground)'
  });
  const from = (cur - 1) * pageSize + 1;
  const to = total != null ? Math.min(cur * pageSize, total) : cur * pageSize;
  return /*#__PURE__*/React.createElement("nav", {
    className: "ui-pager",
    "aria-label": "Pages"
  }, /*#__PURE__*/React.createElement("div", {
    className: "ui-pager-grid"
  }, /*#__PURE__*/React.createElement("span", {
    className: "ui-pager-status"
  }, total != null ? from + '–' + to + ' of ' + total : 'Page ' + cur + ' of ' + pages), pages >= 1 ? /*#__PURE__*/React.createElement("div", {
    className: "ui-pager-buttons",
    style: {
      alignItems: 'center',
      gap: 2
    }
  }, /*#__PURE__*/React.createElement(__ds_scope.Button, {
    variant: "ghost",
    size: "icon",
    icon: "ChevronLeft",
    title: "Previous page",
    disabled: cur <= 1,
    onClick: () => go(cur - 1)
  }), pageList(cur, pages, around).map(p => typeof p === 'string' ? /*#__PURE__*/React.createElement("span", {
    key: p,
    style: {
      minWidth: 24,
      textAlign: 'center',
      color: 'var(--subtle-foreground)'
    }
  }, "\u2026") : /*#__PURE__*/React.createElement("button", {
    key: p,
    type: "button",
    "aria-current": p === cur ? 'page' : undefined,
    "aria-label": 'Page ' + p,
    style: btn(p === cur),
    onMouseEnter: e => {
      if (p !== cur) e.currentTarget.style.background = 'var(--accent)';
    },
    onMouseLeave: e => {
      if (p !== cur) e.currentTarget.style.background = 'transparent';
    },
    onClick: () => go(p)
  }, p)), /*#__PURE__*/React.createElement(__ds_scope.Button, {
    variant: "ghost",
    size: "icon",
    icon: "ChevronRight",
    title: "Next page",
    disabled: cur >= pages,
    onClick: () => go(cur + 1)
  })) : null, /*#__PURE__*/React.createElement("div", {
    className: "ui-pager-size"
  }, /*#__PURE__*/React.createElement("label", null, "Per page"), /*#__PURE__*/React.createElement("select", {
    className: "ui-input",
    value: pageSize,
    onChange: e => changeSize(Number(e.target.value))
  }, pageSizes.map(s => /*#__PURE__*/React.createElement("option", {
    key: s,
    value: s
  }, s))))));
}
Object.assign(__ds_scope, { ListPager });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/core/ListPager.jsx", error: String((e && e.message) || e) }); }

// components/core/Menu.jsx
try { (() => {
/** The shared popup menu (.ui-menu). items: { label, icon, danger, disabled, separator, onClick }. */
function Menu({
  items = [],
  style,
  className = ''
}) {
  return /*#__PURE__*/React.createElement("div", {
    className: 'ui-menu ' + className,
    style: style
  }, items.map((it, i) => it.separator ? /*#__PURE__*/React.createElement("div", {
    key: i,
    className: "ui-menu-sep"
  }) : /*#__PURE__*/React.createElement("button", {
    key: i,
    type: "button",
    disabled: it.disabled,
    className: 'ui-menu-item' + (it.danger ? ' ui-menu-item--danger' : ''),
    onClick: it.onClick
  }, it.icon ? /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: it.icon,
    size: 14
  }) : null, it.label)));
}
Object.assign(__ds_scope, { Menu });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/core/Menu.jsx", error: String((e && e.message) || e) }); }

// components/core/Select.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/** A native <select> wearing .ui-input, as every filter and page-size control in the app does. */
function Select({
  options = [],
  className = '',
  ...rest
}) {
  return /*#__PURE__*/React.createElement("select", _extends({
    className: 'ui-input ' + className
  }, rest), options.map(o => typeof o === 'string' ? /*#__PURE__*/React.createElement("option", {
    key: o,
    value: o
  }, o) : /*#__PURE__*/React.createElement("option", {
    key: o.value,
    value: o.value
  }, o.label)));
}
Object.assign(__ds_scope, { Select });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/core/Select.jsx", error: String((e && e.message) || e) }); }

// components/core/ThemeSwitch.jsx
try { (() => {
const KEY = 'mam:theme';
const OPTIONS = [['light', 'Sun', 'Light'], ['dark', 'Moon', 'Dark'], ['system', 'Monitor', 'System']];
function apply(mode) {
  const dark = mode === 'dark' || mode === 'system' && window.matchMedia('(prefers-color-scheme: dark)').matches;
  document.documentElement.classList.toggle('dark', dark);
}

/**
 * Light / Dark / System switch. Stores the choice in localStorage `mam:theme` — the key the app's
 * pre-paint script in frontend/index.html already reads — and toggles `.dark` on <html>.
 */
function ThemeSwitch({
  value,
  onChange,
  showLabels = false,
  size = 'sm'
}) {
  const [own, setOwn] = React.useState(() => {
    try {
      return localStorage.getItem(KEY) || 'system';
    } catch (e) {
      return 'system';
    }
  });
  const mode = value || own;
  React.useEffect(() => {
    apply(mode);
    if (mode !== 'system') return undefined;
    const mq = window.matchMedia('(prefers-color-scheme: dark)');
    const on = () => apply('system');
    mq.addEventListener('change', on);
    return () => mq.removeEventListener('change', on);
  }, [mode]);
  const pick = m => {
    try {
      if (m === 'system') localStorage.removeItem(KEY);else localStorage.setItem(KEY, m);
    } catch (e) {}
    setOwn(m);
    if (onChange) onChange(m);
  };
  const h = size === 'sm' ? 28 : 32;
  return /*#__PURE__*/React.createElement("div", {
    role: "radiogroup",
    "aria-label": "Theme",
    style: {
      display: 'inline-flex',
      padding: 2,
      gap: 2,
      background: 'var(--secondary)',
      border: '1px solid var(--border)',
      borderRadius: 'var(--radius-md)'
    }
  }, OPTIONS.map(([m, icon, label]) => {
    const on = mode === m;
    return /*#__PURE__*/React.createElement("button", {
      key: m,
      type: "button",
      role: "radio",
      "aria-checked": on,
      title: label,
      onClick: () => pick(m),
      style: {
        display: 'inline-flex',
        alignItems: 'center',
        justifyContent: 'center',
        gap: 6,
        height: h,
        minWidth: h,
        padding: showLabels ? '0 0.625rem' : 0,
        flex: showLabels ? undefined : 1,
        fontSize: 12,
        fontWeight: 500,
        cursor: 'pointer',
        borderRadius: 'var(--radius-sm)',
        background: on ? 'var(--card)' : 'transparent',
        color: on ? 'var(--foreground)' : 'var(--muted-foreground)',
        boxShadow: on ? 'var(--shadow-sm)' : 'none'
      }
    }, /*#__PURE__*/React.createElement(__ds_scope.Icon, {
      name: icon,
      size: 14
    }), showLabels ? label : null);
  }));
}
Object.assign(__ds_scope, { ThemeSwitch });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/core/ThemeSwitch.jsx", error: String((e && e.message) || e) }); }

// components/media/ChipRow.jsx
try { (() => {
/** A labelled row of outline chips: a file's audio/subtitle tracks (verified) or a release's "Claimed" quality. */
function ChipRow({
  label,
  chips = [],
  empty
}) {
  if (chips.length === 0 && empty) return /*#__PURE__*/React.createElement("p", {
    className: "ui-facts"
  }, /*#__PURE__*/React.createElement("span", null, empty));
  return /*#__PURE__*/React.createElement("div", {
    className: "ui-chip-row"
  }, /*#__PURE__*/React.createElement("span", {
    className: "ui-chip-row-label"
  }, label), /*#__PURE__*/React.createElement("ul", {
    className: "ui-chip-list"
  }, chips.map((c, i) => /*#__PURE__*/React.createElement("li", {
    key: i,
    className: "ui-chip"
  }, c))));
}

/** A line of facts separated by gap; verified=true reads in the text colour (ffprobe facts). */
function Facts({
  items = [],
  verified = false
}) {
  return /*#__PURE__*/React.createElement("p", {
    className: 'ui-facts' + (verified ? ' is-verified' : '')
  }, items.map((x, i) => /*#__PURE__*/React.createElement("span", {
    key: i
  }, x)));
}
Object.assign(__ds_scope, { ChipRow, Facts });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/media/ChipRow.jsx", error: String((e && e.message) || e) }); }

// components/media/MatchBadges.jsx
try { (() => {
const TEXT = {
  Identifier: 'Matched by id',
  TitleAndYear: 'Matched by title and year',
  TitleOnly: 'Possible match — check it'
};
const IN_LIB = {
  Identifier: 'In your library, matched by id',
  TitleAndYear: 'In your library, matched by title and year',
  TitleOnly: 'Possible match in your library'
};

/** Why a library work matched: evidence badge(s), the matched title, a warning per conflicting scheme. */
function MatchBadges({
  evidence = 'Identifier',
  schemes = [],
  matchedTitle,
  conflicts = [],
  inLibraryOf
}) {
  const warn = evidence === 'TitleOnly';
  const icon = warn ? 'CircleHelp' : 'CircleCheck';
  const variant = warn ? 'warning' : 'secondary';
  const ev = inLibraryOf !== undefined ? [/*#__PURE__*/React.createElement(__ds_scope.Badge, {
    key: "e",
    variant: variant,
    icon: icon,
    title: inLibraryOf
  }, IN_LIB[evidence])] : evidence === 'Identifier' && schemes.length ? schemes.map(s => /*#__PURE__*/React.createElement(__ds_scope.Badge, {
    key: s,
    variant: "secondary",
    icon: "CircleCheck"
  }, "Matched by ", s, " id")) : [/*#__PURE__*/React.createElement(__ds_scope.Badge, {
    key: "e",
    variant: variant,
    icon: icon
  }, TEXT[evidence])];
  return /*#__PURE__*/React.createElement("ul", {
    style: {
      display: 'flex',
      flexWrap: 'wrap',
      gap: '0.375rem',
      minWidth: 0
    }
  }, ev.map((b, i) => /*#__PURE__*/React.createElement("li", {
    key: i
  }, b)), matchedTitle ? /*#__PURE__*/React.createElement("li", null, /*#__PURE__*/React.createElement(__ds_scope.Badge, {
    variant: "outline"
  }, "\u201C", matchedTitle, "\u201D")) : null, conflicts.map(c => /*#__PURE__*/React.createElement("li", {
    key: c
  }, /*#__PURE__*/React.createElement(__ds_scope.Badge, {
    variant: "warning",
    icon: "TriangleAlert"
  }, "Conflicting ", c, " ids"))));
}
Object.assign(__ds_scope, { MatchBadges });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/media/MatchBadges.jsx", error: String((e && e.message) || e) }); }

// components/media/ReleaseRow.jsx
try { (() => {
/** One release a source listed (SearchPage, MAM-12): title, listing facts, claimed chips, library match, actions. */
function ReleaseRow({
  release,
  onDetails,
  downloadEnabled = false,
  onDownload
}) {
  const r = release || {};
  const c = r.claimed || {};
  const chips = [c.resolution ? c.resolution + 'p' : 'Resolution unknown', c.medium || 'Medium unknown', c.codec || 'Codec unknown', ...(c.ranges && c.ranges.length ? c.ranges : ['HDR unknown']), c.audio && c.audio.length ? 'Audio: ' + c.audio.join(' · ') : 'Audio language unknown', c.subs && c.subs.length ? 'Subtitles: ' + c.subs.join(' · ') : 'Subtitles unknown'];
  return /*#__PURE__*/React.createElement("article", {
    className: "ui-card",
    style: {
      display: 'grid',
      gap: '0.5rem',
      padding: '0.75rem 1rem',
      minWidth: 0
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gap: '0.375rem',
      minWidth: 0
    }
  }, /*#__PURE__*/React.createElement("h3", {
    style: {
      fontWeight: 600,
      overflowWrap: 'anywhere',
      fontSize: 13
    }
  }, r.title), /*#__PURE__*/React.createElement(__ds_scope.Facts, {
    items: [r.source, r.size || 'Size unknown', r.seeders != null ? r.seeders + ' seeders' : 'Seeders unknown', r.peers != null ? r.peers + ' peers' : 'Peers unknown', r.published ? 'Published ' + r.published : 'Publish date unknown']
  }), /*#__PURE__*/React.createElement(__ds_scope.ChipRow, {
    label: "Claimed",
    chips: chips
  }), r.match ? /*#__PURE__*/React.createElement(__ds_scope.MatchBadges, {
    evidence: r.match.evidence,
    conflicts: r.match.conflicts || [],
    inLibraryOf: r.match.work
  }) : null), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      flexWrap: 'wrap',
      justifyContent: 'flex-end',
      gap: 6
    }
  }, /*#__PURE__*/React.createElement(__ds_scope.Button, {
    variant: "outline",
    size: "sm",
    icon: "Info",
    onClick: onDetails
  }, "Details"), /*#__PURE__*/React.createElement(__ds_scope.Button, {
    variant: "outline",
    size: "sm",
    icon: "ExternalLink"
  }, "Open source"), /*#__PURE__*/React.createElement(__ds_scope.Button, {
    variant: "outline",
    size: "sm",
    icon: "Download",
    disabled: !downloadEnabled,
    title: downloadEnabled ? 'Download' : 'Sending a release to a download client comes in a later update.',
    onClick: onDownload
  }, "Download")));
}
Object.assign(__ds_scope, { ReleaseRow });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/media/ReleaseRow.jsx", error: String((e && e.message) || e) }); }

// components/media/WorkCard.jsx
try { (() => {
function FileBlock({
  f
}) {
  const m = f.media;
  return /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gap: '0.125rem',
      minWidth: 0
    }
  }, /*#__PURE__*/React.createElement("p", {
    className: "mono",
    style: {
      fontWeight: 500,
      overflowWrap: 'anywhere'
    },
    title: f.folder + '/' + f.name
  }, f.name), /*#__PURE__*/React.createElement("p", {
    className: "mono",
    style: {
      fontSize: 'var(--text-xs)',
      color: 'var(--muted-foreground)',
      overflowWrap: 'anywhere'
    }
  }, f.folder), f.appPath === null ? /*#__PURE__*/React.createElement("p", {
    className: "ui-status-line",
    "data-status": "warning"
  }, /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: "TriangleAlert",
    size: 14
  }), /*#__PURE__*/React.createElement("span", null, "This file has no path in this app.")) : f.appPath ? /*#__PURE__*/React.createElement("p", {
    className: "mono",
    style: {
      fontSize: 'var(--text-xs)',
      color: 'var(--muted-foreground)',
      overflowWrap: 'anywhere'
    }
  }, "In this app: ", f.appPath) : null, /*#__PURE__*/React.createElement(__ds_scope.Facts, {
    items: [f.size || 'Size unknown', f.location ? 'Library: ' + f.location : 'Library unknown']
  }), m ? /*#__PURE__*/React.createElement(React.Fragment, null, /*#__PURE__*/React.createElement(__ds_scope.Facts, {
    verified: true,
    items: [m.duration || 'Duration unknown', m.resolution || 'Resolution unknown', m.codec || 'Codec unknown', m.range || 'HDR unknown', m.bitrate || 'Bit rate unknown']
  }), /*#__PURE__*/React.createElement(__ds_scope.ChipRow, {
    label: "Audio",
    chips: m.audio || [],
    empty: "No audio tracks"
  }), /*#__PURE__*/React.createElement(__ds_scope.ChipRow, {
    label: "Subtitles",
    chips: m.subs || [],
    empty: "No subtitles"
  })) : /*#__PURE__*/React.createElement(__ds_scope.Facts, {
    items: ['Not inspected yet']
  }));
}

/** One work of the Library (LibraryWorkCard, MAM-8/9): titles, year, ids, versions with files and verified facts. */
function WorkCard({
  work,
  evidence,
  headingLevel = 2
}) {
  const w = work || {};
  const H = headingLevel === 3 ? 'h3' : 'h2';
  return /*#__PURE__*/React.createElement("article", {
    className: "ui-card"
  }, /*#__PURE__*/React.createElement("div", {
    className: "ui-card-header",
    style: {
      flexDirection: 'row',
      alignItems: 'flex-start',
      justifyContent: 'space-between',
      gap: '0.5rem 1rem',
      flexWrap: 'wrap'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gap: '0.25rem',
      minWidth: 0
    }
  }, /*#__PURE__*/React.createElement(H, {
    className: "ui-card-title",
    style: {
      overflowWrap: 'anywhere'
    }
  }, w.title || 'Unknown title'), /*#__PURE__*/React.createElement("p", {
    style: {
      display: 'flex',
      flexWrap: 'wrap',
      gap: '0.25rem 0.75rem',
      color: 'var(--muted-foreground)'
    }
  }, w.originalTitle ? /*#__PURE__*/React.createElement("span", null, "Original title: ", w.originalTitle) : null, /*#__PURE__*/React.createElement("span", null, w.year || 'Year unknown'), w.source ? /*#__PURE__*/React.createElement("span", null, "From ", w.source) : null), evidence), /*#__PURE__*/React.createElement("ul", {
    style: {
      display: 'flex',
      flexWrap: 'wrap',
      justifyContent: 'flex-end',
      gap: '0.375rem'
    }
  }, (w.ids || []).map(id => /*#__PURE__*/React.createElement("li", {
    key: id
  }, /*#__PURE__*/React.createElement(__ds_scope.Badge, {
    variant: "outline"
  }, id))), (w.missing || []).map(s => /*#__PURE__*/React.createElement("li", {
    key: s
  }, /*#__PURE__*/React.createElement(__ds_scope.Badge, {
    variant: "warning",
    icon: "TriangleAlert"
  }, "No ", s, " id"))))), /*#__PURE__*/React.createElement("div", {
    className: "ui-card-content"
  }, /*#__PURE__*/React.createElement("ul", {
    style: {
      display: 'grid'
    }
  }, (w.versions || []).map((v, i) => /*#__PURE__*/React.createElement("li", {
    key: i,
    style: {
      display: 'grid',
      gap: '0.5rem',
      padding: i === 0 ? '0 0 0.625rem' : '0.625rem 0',
      borderTop: i ? '1px solid var(--border)' : 0
    }
  }, v.label ? /*#__PURE__*/React.createElement("p", {
    style: {
      fontWeight: 600
    }
  }, v.label) : null, (v.files || []).map((f, j) => /*#__PURE__*/React.createElement(FileBlock, {
    key: j,
    f: f
  })))))));
}
Object.assign(__ds_scope, { WorkCard });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/media/WorkCard.jsx", error: String((e && e.message) || e) }); }

// ui_kits/_shared/data.js
try { (() => {
if (window.__MAM_KIT) window.MAM_DATA = {
  user: {
    userName: 'vasyl',
    isOwner: true
  },
  version: '0.1.0',
  sources: [{
    name: 'Home Jellyfin',
    service: 'Jellyfin',
    status: 'Succeeded',
    line: 'Synced 1 Oct 2026, 09:12: read 870 movies into 812 works, 851 versions, 853 files.',
    files: '846 inspected · 3 couldn\'t be read · 4 not inspected yet',
    noPath: 2
  }],
  inspection: 'Files last inspected 1 Oct 2026, 09:20: 853 checked, 849 read by ffprobe.',
  locations: ['Фільми', 'Мультфільми', 'Документальні'],
  works: [{
    id: 'w1',
    title: 'Інтерстеллар',
    originalTitle: 'Interstellar',
    year: 2014,
    source: 'Home Jellyfin',
    ids: ['IMDb tt0816692', 'TMDb 157336'],
    missing: [],
    versions: [{
      label: '4K Remux',
      files: [{
        name: 'Interstellar.2014.2160p.UHD.BDRemux.HDR.mkv',
        folder: '/media/movies/Collection/Nolan/Interstellar (2014)',
        appPath: '/media/movies/Collection/Nolan/Interstellar (2014)/Interstellar.2014.2160p.UHD.BDRemux.HDR.mkv',
        size: '61.2 GiB',
        location: 'Фільми',
        media: {
          duration: '2:49:03',
          resolution: '3840×2160',
          codec: 'HEVC Main 10',
          range: 'HDR10',
          bitrate: '51.8 Mb/s',
          audio: ['Ukrainian · AC-3 · 5.1 · default', 'English · TrueHD · 7.1'],
          subs: ['Ukrainian · SRT', 'English · PGS · SDH']
        }
      }]
    }, {
      label: '1080p',
      files: [{
        name: 'Interstellar.2014.1080p.BluRay.x264.mkv',
        folder: '/media/movies/Collection/Nolan/Interstellar (2014)',
        appPath: '/media/movies/Collection/Nolan/Interstellar (2014)/Interstellar.2014.1080p.BluRay.x264.mkv',
        size: '14.6 GiB',
        location: 'Фільми',
        media: {
          duration: '2:49:03',
          resolution: '1920×800',
          codec: 'H.264 High',
          range: 'SDR',
          bitrate: '12.3 Mb/s',
          audio: ['Ukrainian · AC-3 · 5.1 · default'],
          subs: ['Ukrainian · SRT']
        }
      }]
    }],
    summary: {
      res: '2160p',
      codec: 'HEVC',
      range: 'HDR10',
      audio: ['Ukr', 'Eng'],
      size: '75.8 GiB'
    }
  }, {
    id: 'w2',
    title: 'Чужі',
    originalTitle: 'Aliens',
    year: 1986,
    source: 'Home Jellyfin',
    ids: ['IMDb tt0090605'],
    missing: ['TMDb'],
    versions: [{
      files: [{
        name: 'Aliens.1986.Special.Edition.1080p.BluRay.mkv',
        folder: '/media/movies/Alien Collection',
        appPath: '/media/movies/Alien Collection/Aliens.1986.Special.Edition.1080p.BluRay.mkv',
        size: '18.9 GiB',
        location: 'Фільми',
        media: {
          duration: '2:34:31',
          resolution: '1920×1040',
          codec: 'H.264 High',
          range: 'SDR',
          bitrate: '16.1 Mb/s',
          audio: ['Ukrainian · AC-3 · 5.1 · default', 'English · DTS · 5.1'],
          subs: ['Ukrainian · SRT']
        }
      }]
    }],
    summary: {
      res: '1080p',
      codec: 'H.264',
      range: 'SDR',
      audio: ['Ukr', 'Eng'],
      size: '18.9 GiB'
    }
  }, {
    id: 'w3',
    title: 'Дюна: Частина друга',
    originalTitle: 'Dune: Part Two',
    year: 2024,
    source: 'Home Jellyfin',
    ids: ['IMDb tt15239678', 'TMDb 693134'],
    missing: [],
    versions: [{
      files: [{
        name: 'Dune.Part.Two.2024.2160p.WEB-DL.DV.HDR.mkv',
        folder: '/downloads/movies-4k',
        appPath: null,
        size: '24.3 GiB',
        location: 'Фільми 4K'
      }]
    }],
    summary: {
      res: null,
      codec: null,
      range: null,
      audio: [],
      size: '24.3 GiB',
      note: 'No path in this app'
    }
  }, {
    id: 'w4',
    title: 'Темний лицар',
    originalTitle: 'The Dark Knight',
    year: 2008,
    source: 'Home Jellyfin',
    ids: ['IMDb tt0468569', 'TMDb 155'],
    missing: [],
    versions: [{
      files: [{
        name: 'The.Dark.Knight.2008.1080p.BluRay.DTS.mkv',
        folder: '/media/movies/Collection/Nolan/Batman Trilogy/The Dark Knight (2008)',
        appPath: '/media/movies/Collection/Nolan/Batman Trilogy/The Dark Knight (2008)/The.Dark.Knight.2008.1080p.BluRay.DTS.mkv',
        size: '21.4 GiB',
        location: 'Фільми',
        media: {
          duration: '2:32:11',
          resolution: '1920×1080',
          codec: 'H.264 High',
          range: 'SDR',
          bitrate: '19.6 Mb/s',
          audio: ['Ukrainian · AC-3 · 5.1 · default', 'English · DTS · 5.1'],
          subs: []
        }
      }]
    }],
    summary: {
      res: '1080p',
      codec: 'H.264',
      range: 'SDR',
      audio: ['Ukr', 'Eng'],
      size: '21.4 GiB'
    }
  }, {
    id: 'w5',
    title: 'Тіні забутих предків',
    originalTitle: null,
    year: 1965,
    source: 'Home Jellyfin',
    ids: [],
    missing: ['IMDb', 'TMDb'],
    versions: [{
      files: [{
        name: 'Tini.zabutykh.predkiv.1965.DVDRip.avi',
        folder: '/media/movies/Класика',
        appPath: '/media/movies/Класика/Tini.zabutykh.predkiv.1965.DVDRip.avi',
        size: '1.4 GiB',
        location: 'Фільми'
      }]
    }],
    summary: {
      res: null,
      codec: null,
      range: null,
      audio: [],
      size: '1.4 GiB',
      note: 'Not inspected yet'
    }
  }],
  search: {
    text: 'Interstellar',
    year: '2014',
    imdbId: '',
    tmdbId: '',
    sources: [{
      name: 'Toloka',
      status: 'Succeeded',
      line: '16 releases'
    }, {
      name: 'Jackett',
      status: 'Failed',
      line: 'Couldn\'t be reached.'
    }],
    library: [{
      workId: 'w1',
      evidence: 'Identifier',
      schemes: ['IMDb'],
      matchedTitle: 'Interstellar, 2014'
    }],
    releases: [{
      id: 'r1',
      title: 'Інтерстеллар / Interstellar (2014) UHD BDRemux 2160p HDR10 Ukr/Eng | Sub Ukr/Eng',
      source: 'Toloka',
      size: '78.4 GiB',
      seeders: 12,
      peers: 3,
      published: '12 Mar 2024',
      claimed: {
        resolution: 2160,
        medium: 'BDRemux',
        codec: 'HEVC',
        ranges: ['HDR10'],
        audio: ['Ukrainian', 'English'],
        subs: ['Ukrainian', 'English']
      },
      match: {
        evidence: 'Identifier',
        work: 'Інтерстеллар (2014)'
      }
    }, {
      id: 'r2',
      title: 'Інтерстеллар / Interstellar (2014) WEB-DL 2160p DV HDR Ukr/Eng',
      source: 'Toloka',
      size: '27.1 GiB',
      seeders: 41,
      peers: 6,
      published: '3 Feb 2025',
      claimed: {
        resolution: 2160,
        medium: 'WEB-DL',
        codec: 'HEVC',
        ranges: ['Dolby Vision', 'HDR10'],
        audio: ['Ukrainian', 'English'],
        subs: ['Ukrainian']
      },
      match: {
        evidence: 'Identifier',
        work: 'Інтерстеллар (2014)'
      }
    }, {
      id: 'r3',
      title: 'Інтерстеллар / Interstellar (2014) BDRip 1080p Ukr/Eng',
      source: 'Toloka',
      size: '16.2 GiB',
      seeders: 88,
      peers: 9,
      published: '20 Nov 2019',
      claimed: {
        resolution: 1080,
        medium: 'BDRip',
        codec: 'H.264',
        ranges: [],
        audio: ['Ukrainian', 'English'],
        subs: ['Ukrainian']
      },
      match: {
        evidence: 'TitleAndYear',
        work: 'Інтерстеллар (2014)'
      }
    }, {
      id: 'r4',
      title: 'Інтерстеллар / Interstellar (2014) BDRip 720p Ukr',
      source: 'Toloka',
      size: '4.4 GiB',
      seeders: 23,
      peers: 1,
      published: '2 Jun 2016',
      claimed: {
        resolution: 720,
        medium: 'BDRip',
        codec: 'H.264',
        ranges: [],
        audio: ['Ukrainian'],
        subs: []
      },
      match: {
        evidence: 'TitleAndYear',
        work: 'Інтерстеллар (2014)'
      }
    }, {
      id: 'r5',
      title: 'Interstellar.2014.IMAX.2160p.UHD.BluRay.x265.10bit.HDR.TrueHD.7.1',
      source: 'Toloka',
      size: '45.0 GiB',
      seeders: 7,
      peers: 2,
      published: '9 Sep 2023',
      claimed: {
        resolution: 2160,
        medium: 'BDRip',
        codec: 'HEVC',
        ranges: ['HDR10'],
        audio: [],
        subs: []
      },
      match: null
    }, {
      id: 'r6',
      title: 'Інтерстеллар (2014) DVDRip Ukr',
      source: 'Toloka',
      size: '2.2 GiB',
      seeders: null,
      peers: null,
      published: null,
      claimed: {
        resolution: null,
        medium: 'DVDRip',
        codec: null,
        ranges: [],
        audio: ['Ukrainian'],
        subs: []
      },
      match: {
        evidence: 'TitleOnly',
        work: 'Інтерстеллар (2014)'
      }
    }]
  },
  users: [{
    userName: 'vasyl',
    isOwner: true,
    isDisabled: false,
    created: '27 Sep 2026, 21:04',
    lastSignIn: '2 Oct 2026, 08:41'
  }, {
    userName: 'olena',
    isOwner: false,
    isDisabled: false,
    created: '28 Sep 2026, 10:15',
    lastSignIn: '1 Oct 2026, 22:30'
  }, {
    userName: 'dmytro.k',
    isOwner: false,
    isDisabled: false,
    created: '29 Sep 2026, 18:52',
    lastSignIn: null
  }, {
    userName: 'guest_tv',
    isOwner: false,
    isDisabled: true,
    created: '29 Sep 2026, 19:01',
    lastSignIn: '30 Sep 2026, 20:12'
  }],
  profile: {
    created: '27 Sep 2026, 21:04',
    lastSignIn: '2 Oct 2026, 08:41',
    sessionEnds: '1 Nov 2026'
  },
  integrations: [{
    name: 'Home Jellyfin',
    service: 'Jellyfin',
    kind: 'Media library',
    url: 'http://192.168.1.20:8096',
    enabled: true
  }, {
    name: 'Prowlarr · Toloka',
    service: 'Torznab',
    kind: 'Release source',
    url: 'http://192.168.1.20:9696/1/api',
    enabled: true
  }, {
    name: 'Jackett',
    service: 'Torznab',
    kind: 'Release source',
    url: 'http://192.168.1.21:9117/api/v2.0/indexers/all',
    enabled: false
  }, {
    name: 'qBittorrent',
    service: 'qBittorrent',
    kind: 'Download client',
    url: 'http://192.168.1.20:8080',
    enabled: true
  }],
  destinations: [{
    name: 'Movies',
    isDefault: true,
    client: 'qBittorrent',
    category: 'movies',
    check: 'InLibrary',
    path: '/media/movies',
    libs: 'Фільми'
  }, {
    name: 'Movies 4K',
    isDefault: false,
    client: 'qBittorrent',
    category: 'movies-4k',
    check: 'OutsideLibraries',
    path: '/media/movies-4k',
    problem: 'This folder isn\'t inside any Jellyfin library folder.'
  }]
};
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/_shared/data.js", error: String((e && e.message) || e) }); }

// ui_kits/_shared/ds-fallback.js
try { (() => {
/* Kit-only loader: transpiles components/*.jsx in the browser so kit pages always render the current
   sources, then fires 'ds-ready' once. Inert anywhere else (e.g. when the compiler bundles it). */
(function () {
  var cs = document.currentScript;
  if (!cs || !/ds-fallback\.js(\?|$)/.test(cs.src || '')) return; // inside _ds_bundle.js or inline: do nothing
  if (window.__dsFallbackInit) return;
  window.__dsFallbackInit = true;
  var NS = 'MediaAcquisitionManagerDS_29af91';
  var root = cs.getAttribute('data-root') || '../../';
  var files = ['core/Icon', 'core/Button', 'core/Badge', 'core/Banner', 'core/Input', 'core/Select', 'core/Card', 'core/Dialog', 'core/Menu', 'core/ListPager', 'core/AppLogo', 'core/ThemeSwitch', 'core/ErrorState', 'media/ChipRow', 'media/MatchBadges', 'media/ReleaseRow', 'media/WorkCard'];
  var names = ['Icon', 'Button', 'Badge', 'Banner', 'StatusLine', 'Input', 'Field', 'Select', 'Card', 'Dialog', 'Menu', 'ListPager', 'AppLogo', 'ThemeSwitch', 'ErrorState', 'ChipRow', 'Facts', 'MatchBadges', 'ReleaseRow', 'WorkCard'];
  function ready() {
    window.dispatchEvent(new Event('ds-ready'));
    window.__dsReady = true;
  }
  window.addEventListener('load', function () {
    Promise.all(files.map(function (f) {
      return fetch(root + 'components/' + f + '.jsx').then(function (r) {
        return r.text();
      });
    })).then(function (srcs) {
      var src = srcs.map(function (t) {
        return t.replace(/^import .*$/mg, '').replace(/^export /mg, '');
      }).join('\n');
      src += '\nwindow["' + NS + '"]={' + names.join(',') + '};';
      (0, eval)(Babel.transform(src, {
        presets: ['react']
      }).code);
      ready();
    });
  });
})();
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/_shared/ds-fallback.js", error: String((e && e.message) || e) }); }

// ui_kits/current/LibraryMatch.jsx
try { (() => {
/* A library work with its versions as rows (1b, default) or as a comparison table (1c) — used by "In your library"
   on Search and by every card on Library. One per-device choice for both pages, in localStorage `mam:versionsView`. */
const LM_VIEW_KEY = 'mam:versionsView';
function lmShort(v) {
  const m = v.files[0].media;
  if (!m) return {
    res: null,
    codec: null,
    range: null,
    audio: '',
    subs: ''
  };
  const w = Number(m.resolution.split('×')[0]);
  return {
    res: w >= 3800 ? '2160p' : w >= 1900 ? '1080p' : m.resolution,
    codec: m.codec.split(' ')[0],
    range: m.range,
    audio: m.audio.map(a => a.split(' · ')[0].slice(0, 3)).join(' · '),
    subs: m.subs.map(a => a.split(' · ')[0].slice(0, 3)).join(' · ')
  };
}
function ViewToggle({
  value,
  onChange
}) {
  const {
    Icon
  } = window.MAM_DS;
  const opt = [['rows', 'Rows3', 'Rows'], ['table', 'Table2', 'Table']];
  return /*#__PURE__*/React.createElement("div", {
    role: "radiogroup",
    "aria-label": "Show versions as",
    style: {
      display: 'inline-flex',
      padding: 2,
      gap: 2,
      background: 'var(--secondary)',
      border: '1px solid var(--border)',
      borderRadius: 'var(--radius-md)'
    }
  }, opt.map(([v, icon, label]) => {
    const on = value === v;
    return /*#__PURE__*/React.createElement("button", {
      key: v,
      type: "button",
      role: "radio",
      "aria-checked": on,
      title: 'Versions as ' + label.toLowerCase(),
      onClick: () => onChange(v),
      style: {
        display: 'inline-flex',
        alignItems: 'center',
        gap: 6,
        height: 28,
        padding: '0 0.625rem',
        fontSize: 12,
        fontWeight: 500,
        cursor: 'pointer',
        borderRadius: 'var(--radius-sm)',
        background: on ? 'var(--card)' : 'transparent',
        color: on ? 'var(--foreground)' : 'var(--muted-foreground)',
        boxShadow: on ? 'var(--shadow-sm)' : 'none'
      }
    }, /*#__PURE__*/React.createElement(Icon, {
      name: icon,
      size: 14
    }), label);
  }));
}
function VersionRows({
  work
}) {
  const {
    Icon,
    ChipRow,
    Facts
  } = window.MAM_DS;
  const single = work.versions.length === 1;
  const [openState, setOpen] = React.useState(null);
  const open = single ? 0 : openState;
  return /*#__PURE__*/React.createElement("ul", {
    style: {
      display: 'grid',
      border: '1px solid var(--border)',
      borderRadius: 'var(--radius-md)',
      overflow: 'hidden'
    }
  }, work.versions.map((v, i) => {
    const s = lmShort(v),
      f = v.files[0],
      on = open === i;
    return /*#__PURE__*/React.createElement("li", {
      key: i,
      style: {
        borderTop: i ? '1px solid var(--border)' : 0
      }
    }, /*#__PURE__*/React.createElement("button", {
      type: "button",
      "aria-expanded": single ? undefined : on,
      disabled: single,
      onClick: () => setOpen(on ? null : i),
      style: {
        width: '100%',
        display: 'grid',
        gridTemplateColumns: single ? '6rem minmax(0,1fr) auto' : '16px 6rem minmax(0,1fr) auto',
        gap: '0.75rem',
        alignItems: 'center',
        padding: '0.5rem 0.75rem',
        cursor: single ? 'default' : 'pointer',
        textAlign: 'left',
        background: 'transparent'
      }
    }, single ? null : /*#__PURE__*/React.createElement(Icon, {
      name: on ? 'ChevronDown' : 'ChevronRight',
      size: 16,
      style: {
        color: 'var(--muted-foreground)'
      }
    }), /*#__PURE__*/React.createElement("strong", {
      style: {
        fontWeight: 600
      }
    }, v.label || 'Version ' + (i + 1)), /*#__PURE__*/React.createElement("span", {
      style: {
        display: 'flex',
        flexWrap: 'wrap',
        gap: 4,
        fontSize: 12
      }
    }, f.appPath === null ? /*#__PURE__*/React.createElement("span", {
      style: {
        display: 'inline-flex',
        alignItems: 'center',
        gap: 4
      }
    }, /*#__PURE__*/React.createElement(Icon, {
      name: "TriangleAlert",
      size: 12,
      style: {
        color: 'var(--status-attention)'
      }
    }), "No path in this app") : s.res ? [s.res, s.codec, s.range, s.audio].filter(Boolean).map(x => /*#__PURE__*/React.createElement("span", {
      key: x,
      className: "ui-chip"
    }, x)) : /*#__PURE__*/React.createElement("span", {
      style: {
        color: 'var(--muted-foreground)'
      }
    }, "Not inspected yet")), /*#__PURE__*/React.createElement("span", {
      style: {
        fontVariantNumeric: 'tabular-nums'
      }
    }, f.size || 'Size unknown')), on ? /*#__PURE__*/React.createElement("div", {
      style: {
        padding: single ? '0.25rem 0.75rem 0.75rem' : '0.25rem 0.75rem 0.75rem 2.5rem',
        display: 'grid',
        gap: 4
      }
    }, /*#__PURE__*/React.createElement("p", {
      className: "mono",
      style: {
        fontWeight: 500,
        overflowWrap: 'anywhere'
      }
    }, f.name), /*#__PURE__*/React.createElement("p", {
      className: "mono",
      style: {
        fontSize: 12,
        color: 'var(--muted-foreground)',
        overflowWrap: 'anywhere'
      }
    }, f.folder), f.appPath ? /*#__PURE__*/React.createElement("p", {
      className: "mono",
      style: {
        fontSize: 12,
        color: 'var(--muted-foreground)',
        overflowWrap: 'anywhere'
      }
    }, "In this app: ", f.appPath) : null, f.appPath === null ? /*#__PURE__*/React.createElement("p", {
      className: "ui-status-line",
      "data-status": "warning"
    }, /*#__PURE__*/React.createElement(Icon, {
      name: "TriangleAlert",
      size: 14
    }), /*#__PURE__*/React.createElement("span", null, "This file has no path in this app. Check the path mappings under Integrations.")) : null, /*#__PURE__*/React.createElement("p", {
      className: "ui-facts"
    }, /*#__PURE__*/React.createElement("span", null, f.size || 'Size unknown'), /*#__PURE__*/React.createElement("span", null, f.location ? 'Library: ' + f.location : 'Library unknown')), f.media ? /*#__PURE__*/React.createElement(React.Fragment, null, /*#__PURE__*/React.createElement(Facts, {
      verified: true,
      items: [f.media.duration, f.media.resolution, f.media.codec, f.media.range, f.media.bitrate]
    }), /*#__PURE__*/React.createElement(ChipRow, {
      label: "Audio",
      chips: f.media.audio,
      empty: "No audio tracks"
    }), /*#__PURE__*/React.createElement(ChipRow, {
      label: "Subtitles",
      chips: f.media.subs,
      empty: "No subtitles"
    })) : /*#__PURE__*/React.createElement("p", {
      className: "ui-facts"
    }, /*#__PURE__*/React.createElement("span", null, f.appPath === null ? 'Not inspected: the app has no path to this file.' : 'Not inspected yet'))) : null);
  }));
}
function VersionTable({
  work
}) {
  const vs = work.versions.map((v, i) => ({
    label: v.label || 'Version ' + (i + 1),
    f: v.files[0],
    m: v.files[0].media || {}
  }));
  const u = (x, fallback) => x || fallback;
  const rows = [['Resolution', x => u(x.m.resolution, 'Unknown')], ['Video', x => u(x.m.codec, 'Unknown')], ['HDR', x => u(x.m.range, 'Unknown')], ['Bit rate', x => u(x.m.bitrate, 'Unknown')], ['Duration', x => u(x.m.duration, 'Unknown')], ['Audio', x => (x.m.audio || []).join('\n') || 'No audio tracks'], ['Subtitles', x => (x.m.subs || []).join('\n') || 'No subtitles'], ['Size', x => u(x.f.size, 'Unknown')], ['File', x => x.f.name]];
  const cell = {
    padding: '0.5rem 0.75rem',
    borderTop: '1px solid var(--border)',
    overflowWrap: 'anywhere',
    whiteSpace: 'pre-line'
  };
  return /*#__PURE__*/React.createElement(React.Fragment, null, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gridTemplateColumns: `7rem repeat(${vs.length}, minmax(0,1fr))`,
      border: '1px solid var(--border)',
      borderRadius: 'var(--radius-md)',
      overflow: 'hidden'
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      padding: '0.5rem 0.75rem',
      background: 'var(--muted)'
    }
  }), vs.map(x => /*#__PURE__*/React.createElement("span", {
    key: x.label,
    style: {
      padding: '0.5rem 0.75rem',
      background: 'var(--muted)',
      fontWeight: 600
    }
  }, x.label)), rows.map(([k, fn]) => /*#__PURE__*/React.createElement(React.Fragment, {
    key: k
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      ...cell,
      color: 'var(--muted-foreground)'
    }
  }, k), vs.map(x => /*#__PURE__*/React.createElement("span", {
    key: x.label,
    className: k === 'File' ? 'mono' : undefined,
    style: {
      ...cell,
      fontSize: k === 'File' ? 12 : undefined
    }
  }, fn(x)))))), /*#__PURE__*/React.createElement("p", {
    style: {
      fontSize: 12,
      color: 'var(--muted-foreground)'
    }
  }, "Every value was read from the file by ffprobe."));
}
function LibraryMatchCard({
  work,
  match,
  headingLevel = 3
}) {
  const H = headingLevel === 2 ? 'h2' : 'h3';
  const {
    Badge,
    MatchBadges
  } = window.MAM_DS;
  const [view, setView] = React.useState(() => {
    try {
      return localStorage.getItem(LM_VIEW_KEY) || 'rows';
    } catch (e) {
      return 'rows';
    }
  });
  const pick = v => {
    setView(v);
    try {
      localStorage.setItem(LM_VIEW_KEY, v);
    } catch (e) {}
  };
  return /*#__PURE__*/React.createElement("article", {
    className: "ui-card",
    style: {
      padding: '1.25rem 1.5rem',
      display: 'grid',
      gap: '0.875rem'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      flexWrap: 'wrap',
      alignItems: 'flex-start',
      justifyContent: 'space-between',
      gap: '0.5rem 1rem'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gap: 4,
      minWidth: 0
    }
  }, /*#__PURE__*/React.createElement(H, {
    style: {
      fontSize: '1.125rem',
      fontWeight: 600,
      lineHeight: 1.25,
      overflowWrap: 'anywhere'
    }
  }, work.title || 'Unknown title'), /*#__PURE__*/React.createElement("p", {
    style: {
      display: 'flex',
      flexWrap: 'wrap',
      gap: '0.25rem 0.75rem',
      color: 'var(--muted-foreground)'
    }
  }, work.originalTitle ? /*#__PURE__*/React.createElement("span", null, "Original title: ", work.originalTitle) : null, /*#__PURE__*/React.createElement("span", null, work.year || 'Year unknown'), work.source ? /*#__PURE__*/React.createElement("span", null, "From ", work.source) : null), match ? /*#__PURE__*/React.createElement(MatchBadges, {
    evidence: match.evidence,
    schemes: match.schemes,
    matchedTitle: match.matchedTitle
  }) : null), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      justifyItems: 'end',
      gap: 8
    }
  }, /*#__PURE__*/React.createElement("ul", {
    style: {
      display: 'flex',
      flexWrap: 'wrap',
      justifyContent: 'flex-end',
      gap: 6
    }
  }, work.ids.map(x => /*#__PURE__*/React.createElement("li", {
    key: x
  }, /*#__PURE__*/React.createElement(Badge, {
    variant: "outline"
  }, x))), work.missing.map(x => /*#__PURE__*/React.createElement("li", {
    key: x
  }, /*#__PURE__*/React.createElement(Badge, {
    variant: "warning",
    icon: "TriangleAlert"
  }, "No ", x, " id")))), work.versions.length > 1 ? /*#__PURE__*/React.createElement(ViewToggle, {
    value: view,
    onChange: pick
  }) : null)), view === 'table' && work.versions.length > 1 ? /*#__PURE__*/React.createElement(VersionTable, {
    work: work
  }) : /*#__PURE__*/React.createElement(VersionRows, {
    work: work
  }));
}
if (window.__MAM_KIT) Object.assign(window, {
  LibraryMatchCard
});
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/current/LibraryMatch.jsx", error: String((e && e.message) || e) }); }

// ui_kits/current/LibraryScreen.jsx
try { (() => {
/* LibraryPage + LibrarySyncPanel (MAM-8/9) — current layout */
function LibraryScreen() {
  const {
    Card,
    Button,
    Input,
    Select,
    StatusLine,
    WorkCard,
    ListPager,
    ErrorState,
    Banner
  } = window.MAM_DS;
  const sim = useSim();
  const [retrying, retry] = useRetry('load');
  const [refreshing, refresh] = useRetry('refresh');
  const [syncError, setSyncError] = React.useState(null);
  const startSync = () => {
    setSyncError(null);
    if (sim.action || sim.offline) {
      setSyncError(sim.offline ? SERVER_DOWN : 'Could not start the sync. Home Jellyfin is already being synced.');
      return;
    }
    setSyncing(!syncing);
  };
  const d = window.MAM_DATA;
  const [q, setQ] = React.useState('');
  const [syncing, setSyncing] = React.useState(false);
  const works = d.works.filter(w => !q || (w.title + ' ' + (w.originalTitle || '') + ' ' + w.year).toLowerCase().includes(q.toLowerCase()));
  return /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gap: '1rem'
    }
  }, /*#__PURE__*/React.createElement(PageHeader, {
    title: "Library",
    lead: "The movies in your media library, as its last sync found them: each with its versions, its files and the path this app reaches them at."
  }), /*#__PURE__*/React.createElement(Card, {
    title: "Sources",
    description: "The media libraries this list is read from. A sync reads a whole library again and replaces what was stored from it; nothing changes in the library itself."
  }, sim.load ? /*#__PURE__*/React.createElement(ErrorState, {
    title: "Could not load the media libraries.",
    onRetry: retry,
    retrying: retrying
  }, SERVER_DOWN) : /*#__PURE__*/React.createElement(React.Fragment, null, d.sources.map(src => /*#__PURE__*/React.createElement("div", {
    key: src.name,
    style: {
      display: 'flex',
      justifyContent: 'space-between',
      gap: '0.5rem 1rem',
      alignItems: 'flex-start',
      paddingBottom: '0.75rem'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gap: '0.25rem'
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      display: 'flex',
      gap: '0.375rem',
      alignItems: 'center'
    }
  }, /*#__PURE__*/React.createElement("strong", {
    style: {
      fontWeight: 500
    }
  }, src.name), /*#__PURE__*/React.createElement("span", {
    style: {
      color: 'var(--muted-foreground)'
    }
  }, src.service)), syncing ? /*#__PURE__*/React.createElement(StatusLine, {
    status: "running"
  }, "Syncing\u2026 312 of 870 movies") : /*#__PURE__*/React.createElement(StatusLine, {
    status: "ok"
  }, src.line), /*#__PURE__*/React.createElement("p", {
    style: {
      color: 'var(--muted-foreground)'
    }
  }, "Files: ", src.files), /*#__PURE__*/React.createElement(StatusLine, {
    status: "warning"
  }, src.noPath, " files have no path in this app. ", /*#__PURE__*/React.createElement("a", {
    href: "#"
  }, "Check the path mappings under Integrations.")), syncError ? /*#__PURE__*/React.createElement(StatusLine, {
    status: "error"
  }, syncError) : null), /*#__PURE__*/React.createElement(Button, {
    variant: "outline",
    size: "sm",
    icon: "RefreshCw",
    loading: syncing,
    onClick: startSync
  }, syncing ? 'Syncing…' : 'Sync now'))), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      justifyContent: 'space-between',
      gap: '1rem',
      borderTop: '1px solid var(--border)',
      paddingTop: '0.75rem'
    }
  }, /*#__PURE__*/React.createElement(StatusLine, {
    status: "ok"
  }, d.inspection), /*#__PURE__*/React.createElement(Button, {
    variant: "outline",
    size: "sm",
    icon: "ScanSearch"
  }, "Inspect files")))), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      flexWrap: 'wrap',
      alignItems: 'center',
      gap: '0.5rem 0.75rem'
    }
  }, /*#__PURE__*/React.createElement(Input, {
    type: "search",
    value: q,
    onChange: e => setQ(e.target.value),
    placeholder: "Title, year, IMDb or TMDb id, or path",
    style: {
      maxWidth: '20rem'
    }
  }), /*#__PURE__*/React.createElement(Select, {
    options: ['All libraries', ...d.locations]
  }), /*#__PURE__*/React.createElement(Select, {
    options: ['All movies', 'Missing an IMDb or TMDb id']
  })), sim.load ? /*#__PURE__*/React.createElement(ErrorState, {
    title: "Could not load the library.",
    onRetry: retry,
    retrying: retrying
  }, SERVER_DOWN) : /*#__PURE__*/React.createElement(React.Fragment, null, sim.refresh ? /*#__PURE__*/React.createElement(Banner, {
    action: /*#__PURE__*/React.createElement(Button, {
      variant: "outline",
      size: "sm",
      icon: "RotateCw",
      loading: refreshing,
      onClick: refresh
    }, refreshing ? 'Retrying…' : 'Retry')
  }, "Refresh failed, showing the last loaded movies. ", SERVER_DOWN) : null, /*#__PURE__*/React.createElement("ul", {
    style: {
      display: 'grid',
      gap: '0.75rem',
      opacity: sim.refresh ? 0.75 : 1
    }
  }, works.map(w => /*#__PURE__*/React.createElement("li", {
    key: w.id
  }, /*#__PURE__*/React.createElement(LibraryMatchCard, {
    work: w,
    headingLevel: 2
  })))), /*#__PURE__*/React.createElement(ListPager, {
    page: 1,
    pages: 33,
    pageSize: 25,
    total: 812
  })));
}
if (window.__MAM_KIT) Object.assign(window, {
  LibraryScreen
});
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/current/LibraryScreen.jsx", error: String((e && e.message) || e) }); }

// ui_kits/current/ProfileScreen.jsx
try { (() => {
/* Profile — NEW page (no counterpart in the app yet). Uses only what the API already knows about the
   signed-in user: name, role, created, last sign-in; the password reset the owner can do on themself;
   the theme stored in mam:theme; sign-out. */
function ProfileRow({
  label,
  children,
  last
}) {
  return /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gridTemplateColumns: '11rem minmax(0,1fr)',
      gap: '0.25rem 1.5rem',
      alignItems: 'center',
      padding: '0.875rem 0',
      borderBottom: last ? 0 : '1px solid var(--border)'
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      color: 'var(--muted-foreground)'
    }
  }, label), /*#__PURE__*/React.createElement("div", {
    style: {
      minWidth: 0
    }
  }, children));
}
function ProfileScreen({
  onSignOut
}) {
  const {
    Card,
    Button,
    Input,
    Field,
    Badge,
    Banner,
    ThemeSwitch
  } = window.MAM_DS;
  const d = window.MAM_DATA,
    u = d.user,
    p = d.profile;
  const [pwOpen, setPwOpen] = React.useState(false);
  const [pw, setPw] = React.useState({
    a: '',
    b: ''
  });
  const [news, setNews] = React.useState(null);
  const differs = pw.b !== '' && pw.a !== pw.b;
  const short = pw.a !== '' && pw.a.length < 8;
  const save = () => {
    if (!pw.a || differs || short) return;
    setPwOpen(false);
    setPw({
      a: '',
      b: ''
    });
    setNews('Password changed. Your other devices are signed out; this one stays signed in.');
  };
  return /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gap: '1.25rem',
      maxWidth: '44rem'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      alignItems: 'center',
      gap: '1rem'
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      width: 56,
      height: 56,
      borderRadius: 9999,
      display: 'inline-grid',
      placeItems: 'center',
      background: 'var(--tint-selected)',
      color: 'var(--primary)',
      fontWeight: 600,
      fontSize: 22,
      flexShrink: 0
    }
  }, u.userName[0].toUpperCase()), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gap: 4
    }
  }, /*#__PURE__*/React.createElement("h1", {
    style: {
      fontSize: 'var(--text-2xl)',
      fontWeight: 600,
      lineHeight: 1.2,
      display: 'flex',
      alignItems: 'center',
      gap: 8
    }
  }, u.userName, u.isOwner ? /*#__PURE__*/React.createElement(Badge, {
    variant: "default"
  }, "Owner") : null), /*#__PURE__*/React.createElement("p", {
    style: {
      fontSize: '0.875rem',
      color: 'var(--muted-foreground)'
    }
  }, u.isOwner ? 'The installation owner: manages users, integrations and settings.' : 'Can search and browse the library.'))), news ? /*#__PURE__*/React.createElement(Banner, {
    tone: "ok"
  }, news) : null, /*#__PURE__*/React.createElement(Card, {
    title: "Account"
  }, /*#__PURE__*/React.createElement(ProfileRow, {
    label: "User name"
  }, /*#__PURE__*/React.createElement("span", {
    className: "mono"
  }, u.userName)), /*#__PURE__*/React.createElement(ProfileRow, {
    label: "Role"
  }, u.isOwner ? 'Owner' : 'Member'), /*#__PURE__*/React.createElement(ProfileRow, {
    label: "Created"
  }, p.created), /*#__PURE__*/React.createElement(ProfileRow, {
    label: "Last sign-in",
    last: true
  }, p.lastSignIn)), /*#__PURE__*/React.createElement(Card, {
    title: "Password",
    description: "There is no email reset, so keep it somewhere safe."
  }, u.isOwner ? pwOpen ? /*#__PURE__*/React.createElement("form", {
    onSubmit: e => {
      e.preventDefault();
      save();
    },
    style: {
      display: 'grid',
      gap: '1rem',
      maxWidth: '22rem'
    }
  }, /*#__PURE__*/React.createElement(Field, {
    label: "New password",
    hint: short ? undefined : '8–128 characters.'
  }, /*#__PURE__*/React.createElement(Input, {
    type: "password",
    value: pw.a,
    onChange: e => setPw({
      ...pw,
      a: e.target.value
    }),
    autoComplete: "new-password"
  })), short ? /*#__PURE__*/React.createElement("p", {
    style: {
      color: 'var(--destructive)',
      fontSize: 12,
      marginTop: '-0.625rem'
    }
  }, "At least 8 characters.") : null, /*#__PURE__*/React.createElement(Field, {
    label: "Confirm password"
  }, /*#__PURE__*/React.createElement(Input, {
    type: "password",
    value: pw.b,
    onChange: e => setPw({
      ...pw,
      b: e.target.value
    }),
    autoComplete: "new-password"
  })), differs ? /*#__PURE__*/React.createElement("p", {
    style: {
      color: 'var(--destructive)',
      fontSize: 12,
      marginTop: '-0.625rem'
    }
  }, "The two passwords are different. Type the same password in both fields.") : null, /*#__PURE__*/React.createElement("p", {
    style: {
      fontSize: 12,
      color: 'var(--muted-foreground)'
    }
  }, "Your other devices are signed out. This one stays signed in."), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      gap: 8
    }
  }, /*#__PURE__*/React.createElement(Button, {
    type: "submit",
    disabled: !pw.a || differs || short
  }, "Change password"), /*#__PURE__*/React.createElement(Button, {
    variant: "outline",
    onClick: () => {
      setPwOpen(false);
      setPw({
        a: '',
        b: ''
      });
    }
  }, "Cancel"))) : /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'space-between',
      gap: '1rem',
      flexWrap: 'wrap'
    }
  }, /*#__PURE__*/React.createElement("span", {
    className: "mono",
    style: {
      letterSpacing: 2,
      color: 'var(--muted-foreground)'
    }
  }, "\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022"), /*#__PURE__*/React.createElement(Button, {
    variant: "outline",
    size: "sm",
    icon: "KeyRound",
    onClick: () => setPwOpen(true)
  }, "Change password")) : /*#__PURE__*/React.createElement("p", {
    style: {
      color: 'var(--muted-foreground)'
    }
  }, "The owner resets your password under Users.")), /*#__PURE__*/React.createElement(Card, {
    title: "Appearance",
    description: "Saved in this browser."
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'space-between',
      gap: '1rem',
      flexWrap: 'wrap'
    }
  }, /*#__PURE__*/React.createElement("span", null, "Theme"), /*#__PURE__*/React.createElement(ThemeSwitch, {
    showLabels: true,
    size: "default"
  }))), /*#__PURE__*/React.createElement(Card, {
    title: "Session",
    description: 'A session lasts 30 days from its last use. This one ends ' + p.sessionEnds + ' unless you use the app again.'
  }, /*#__PURE__*/React.createElement(Button, {
    variant: "outline",
    icon: "LogOut",
    onClick: onSignOut
  }, "Sign out")));
}
if (window.__MAM_KIT) Object.assign(window, {
  ProfileScreen
});
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/current/ProfileScreen.jsx", error: String((e && e.message) || e) }); }

// ui_kits/current/SearchScreen.jsx
try { (() => {
/* SearchPage (MAM-12) — current layout: form, "In your library" cards, then "Available releases" */
function SearchScreen({
  query
}) {
  const {
    Field,
    Input,
    Button,
    WorkCard,
    MatchBadges,
    StatusLine,
    Select,
    ReleaseRow,
    ListPager,
    ErrorState,
    Banner
  } = window.MAM_DS;
  const d = window.MAM_DATA,
    s0 = d.search;
  const sim = useSim();
  const s = {
    ...s0,
    sources: sim.source ? s0.sources : s0.sources.filter(x => x.status === 'Succeeded')
  };
  const [retrying, retry] = useRetry('load');
  const [refreshing, refresh] = useRetry('refresh');
  const [formError, setFormError] = React.useState(null);
  const [titleError, setTitleError] = React.useState(null);
  const [searching, setSearching] = React.useState(false);
  const submit = e => {
    e.preventDefault();
    setFormError(null);
    setTitleError(null);
    const title = e.currentTarget.querySelector('input').value.trim();
    if (!title) {
      setTitleError('Enter a title to search for.');
      return;
    }
    setSearching(true);
    setTimeout(() => {
      setSearching(false);
      if (sim.action || sim.offline) setFormError(sim.offline ? SERVER_DOWN : 'Could not search. The search is already running; wait for it to finish.');
    }, 600);
  };
  const [details, setDetails] = React.useState(null);
  const lbl = {
    fontSize: '1.125rem',
    fontWeight: 600,
    lineHeight: 1.25
  };
  return /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gap: '1rem'
    }
  }, /*#__PURE__*/React.createElement(PageHeader, {
    title: "Search",
    lead: "Look for a movie in your library and in every release source at once. What the sources list is kept for a day, so filtering and sorting never ask them again."
  }), /*#__PURE__*/React.createElement("form", {
    noValidate: true,
    onSubmit: submit,
    style: {
      display: 'grid',
      gridTemplateColumns: 'minmax(0,2fr) minmax(0,6rem) minmax(0,1fr) minmax(0,1fr) auto',
      alignItems: 'start',
      gap: '0.75rem'
    }
  }, /*#__PURE__*/React.createElement(Field, {
    label: "Title",
    error: titleError
  }, /*#__PURE__*/React.createElement(Input, {
    key: query ?? s.text,
    defaultValue: query || s.text,
    placeholder: "Interstellar"
  })), /*#__PURE__*/React.createElement(Field, {
    label: "Year",
    optional: "(optional)"
  }, /*#__PURE__*/React.createElement(Input, {
    key: 'y' + (query ?? ''),
    defaultValue: query ? '' : s.year,
    placeholder: "2014"
  })), /*#__PURE__*/React.createElement(Field, {
    label: "IMDb id",
    optional: "(optional)"
  }, /*#__PURE__*/React.createElement(Input, {
    placeholder: "tt0816692"
  })), /*#__PURE__*/React.createElement(Field, {
    label: "TMDb id",
    optional: "(optional)"
  }, /*#__PURE__*/React.createElement(Input, {
    placeholder: "157336"
  })), /*#__PURE__*/React.createElement(Button, {
    type: "submit",
    icon: "Search",
    loading: searching,
    style: {
      marginTop: 22
    }
  }, searching ? 'Searching…' : 'Search')), formError ? /*#__PURE__*/React.createElement(Banner, {
    onDismiss: () => setFormError(null)
  }, formError) : null, sim.load ? /*#__PURE__*/React.createElement(ErrorState, {
    title: "Could not load this search.",
    onRetry: retry,
    retrying: retrying
  }, SERVER_DOWN) : /*#__PURE__*/React.createElement(React.Fragment, null, sim.refresh ? /*#__PURE__*/React.createElement(Banner, {
    action: /*#__PURE__*/React.createElement(Button, {
      variant: "outline",
      size: "sm",
      icon: "RotateCw",
      loading: refreshing,
      onClick: refresh
    }, refreshing ? 'Retrying…' : 'Retry')
  }, "Refresh failed, showing the search as it was last loaded.") : null, /*#__PURE__*/React.createElement("section", {
    style: {
      display: 'grid',
      gap: '0.75rem'
    }
  }, /*#__PURE__*/React.createElement("h2", {
    style: lbl
  }, "In your library"), s.library.map(m => /*#__PURE__*/React.createElement(LibraryMatchCard, {
    key: m.workId,
    work: d.works.find(w => w.id === m.workId),
    match: m
  }))), /*#__PURE__*/React.createElement("section", {
    style: {
      display: 'grid',
      gap: '0.75rem'
    }
  }, /*#__PURE__*/React.createElement("h2", {
    style: lbl
  }, "Available releases"), /*#__PURE__*/React.createElement("ul", {
    style: {
      display: 'grid',
      gap: '0.25rem'
    }
  }, s.sources.map(x => /*#__PURE__*/React.createElement(StatusLine, {
    as: "li",
    key: x.name,
    status: x.status === 'Succeeded' ? 'ok' : 'error'
  }, /*#__PURE__*/React.createElement("strong", null, x.name), " \xB7 ", x.line))), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      flexWrap: 'wrap',
      alignItems: 'center',
      gap: '0.5rem 0.75rem'
    }
  }, /*#__PURE__*/React.createElement(Select, {
    options: ['All sources', 'Toloka']
  }), /*#__PURE__*/React.createElement(Select, {
    options: ['All languages', 'Ukrainian', 'English', 'Unknown']
  }), /*#__PURE__*/React.createElement(Select, {
    options: ['All qualities', '2160p', '1080p', '720p', 'Unknown']
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      display: 'inline-flex',
      alignItems: 'center',
      gap: '0.375rem',
      color: 'var(--muted-foreground)'
    }
  }, "Size, GiB ", /*#__PURE__*/React.createElement(Input, {
    placeholder: "From",
    style: {
      width: '5.5rem'
    }
  }), " ", /*#__PURE__*/React.createElement(Input, {
    placeholder: "To",
    style: {
      width: '5.5rem'
    }
  })), /*#__PURE__*/React.createElement(Select, {
    options: ['Most seeders', 'Largest', 'Smallest', 'Newest', 'Best quality']
  })), /*#__PURE__*/React.createElement("ul", {
    style: {
      display: 'grid',
      gap: '0.75rem'
    }
  }, s.releases.map(r => /*#__PURE__*/React.createElement("li", {
    key: r.id
  }, /*#__PURE__*/React.createElement(ReleaseRow, {
    release: r,
    onDetails: () => setDetails(r)
  })))), /*#__PURE__*/React.createElement(ListPager, {
    page: 1,
    pages: 2,
    pageSize: 10,
    total: 16
  }))), details ? /*#__PURE__*/React.createElement(ReleaseDetails, {
    release: details,
    onClose: () => setDetails(null)
  }) : null);
}
function ReleaseDetails({
  release,
  onClose
}) {
  const {
    Dialog,
    WorkCard,
    ChipRow
  } = window.MAM_DS;
  const d = window.MAM_DATA,
    w = d.works[0];
  const facts = [['Source', release.source], ['Size', release.size || 'Unknown'], ['Seeders', release.seeders ?? 'Unknown'], ['Peers', release.peers ?? 'Unknown'], ['Published', release.published || 'Unknown'], ['Downloaded as', 'Known once it is fetched']];
  return /*#__PURE__*/React.createElement(Dialog, {
    title: release.title,
    description: 'What ' + release.source + ' lists for this release, and the copies in your library it may be. Everything on the left is the listing\'s claim; the facts on the right were read from your files.',
    onClose: onClose,
    maxWidth: "60rem"
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gridTemplateColumns: '1fr 1fr',
      gap: '1.5rem',
      alignItems: 'start'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gap: '0.75rem'
    }
  }, /*#__PURE__*/React.createElement("h3", {
    style: {
      fontSize: '0.875rem',
      fontWeight: 600
    }
  }, "The listing"), /*#__PURE__*/React.createElement("dl", {
    style: {
      display: 'grid',
      gridTemplateColumns: 'auto minmax(0,1fr)',
      gap: '0.25rem 0.75rem'
    }
  }, facts.map(([k, v]) => /*#__PURE__*/React.createElement(React.Fragment, {
    key: k
  }, /*#__PURE__*/React.createElement("dt", {
    style: {
      color: 'var(--muted-foreground)'
    }
  }, k), /*#__PURE__*/React.createElement("dd", null, v)))), /*#__PURE__*/React.createElement(ChipRow, {
    label: "Claimed",
    chips: [release.claimed.resolution ? release.claimed.resolution + 'p' : 'Resolution unknown', release.claimed.medium, release.claimed.codec || 'Codec unknown']
  })), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gap: '0.75rem'
    }
  }, /*#__PURE__*/React.createElement("h3", {
    style: {
      fontSize: '0.875rem',
      fontWeight: 600
    }
  }, "In your library"), /*#__PURE__*/React.createElement(WorkCard, {
    headingLevel: 3,
    work: w
  }))));
}
if (window.__MAM_KIT) Object.assign(window, {
  SearchScreen,
  ReleaseDetails
});
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/current/SearchScreen.jsx", error: String((e && e.message) || e) }); }

// ui_kits/current/SettingsScreens.jsx
try { (() => {
/* IntegrationsPage, DestinationsPage (MAM-7/11) — settings list pages */
function IntegrationsScreen() {
  const {
    Button,
    Input,
    Select,
    Badge,
    Banner,
    Dialog,
    Field,
    ListPager,
    ErrorState
  } = window.MAM_DS;
  const d = window.MAM_DATA;
  const sim = useSim();
  const [retrying, retry] = useRetry('load');
  const [result, setResult] = React.useState(null);
  const [testing, setTesting] = React.useState(null);
  const test = i => {
    setResult(null);
    setTesting(i.name);
    setTimeout(() => {
      setTesting(null);
      const bad = sim.action || sim.offline || !i.enabled;
      setResult({
        ok: !bad,
        text: sim.offline ? 'Could not test the connection. ' + SERVER_DOWN : bad ? i.name + ': Couldn\'t be reached at ' + i.url.replace(/^(https?:\/\/[^/]+).*/, '$1') + '. Check the address and that ' + i.service + ' is running.' : i.name + ': Connected to ' + i.service + '.'
      });
    }, 700);
  };
  const [dialog, setDialog] = React.useState(false);
  return /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gap: '1rem'
    }
  }, /*#__PURE__*/React.createElement(PageHeader, {
    title: "Integrations",
    lead: "The services this app works with: a media library to read, release sources to search and a download client to send releases to.",
    action: /*#__PURE__*/React.createElement(Button, {
      icon: "Plus",
      onClick: () => setDialog(true)
    }, "Add integration")
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      flexWrap: 'wrap',
      gap: '0.5rem 0.75rem'
    }
  }, /*#__PURE__*/React.createElement(Input, {
    type: "search",
    placeholder: "Search by name",
    style: {
      maxWidth: '20rem'
    }
  }), /*#__PURE__*/React.createElement(Select, {
    options: ['All kinds', 'Media library', 'Release source', 'Download client']
  })), result ? /*#__PURE__*/React.createElement(Banner, {
    tone: result.ok ? 'ok' : 'error',
    onDismiss: () => setResult(null)
  }, result.text) : null, sim.load ? /*#__PURE__*/React.createElement(ErrorState, {
    title: "Could not load the integrations.",
    onRetry: retry,
    retrying: retrying
  }, SERVER_DOWN) : /*#__PURE__*/React.createElement(React.Fragment, null, /*#__PURE__*/React.createElement("div", {
    className: "ui-table-wrap"
  }, /*#__PURE__*/React.createElement("table", {
    className: "ui-table"
  }, /*#__PURE__*/React.createElement("thead", null, /*#__PURE__*/React.createElement("tr", null, /*#__PURE__*/React.createElement("th", null, "Name"), /*#__PURE__*/React.createElement("th", null, "Service"), /*#__PURE__*/React.createElement("th", null, "Address"), /*#__PURE__*/React.createElement("th", null, "Actions"))), /*#__PURE__*/React.createElement("tbody", null, d.integrations.map(i => /*#__PURE__*/React.createElement("tr", {
    key: i.name
  }, /*#__PURE__*/React.createElement("td", null, /*#__PURE__*/React.createElement("span", {
    style: {
      display: 'flex',
      gap: '0.375rem',
      alignItems: 'center',
      flexWrap: 'wrap'
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      fontWeight: 500,
      color: i.enabled ? undefined : 'var(--muted-foreground)'
    }
  }, i.name), !i.enabled ? /*#__PURE__*/React.createElement(Badge, {
    variant: "outline"
  }, "Disabled") : null)), /*#__PURE__*/React.createElement("td", null, /*#__PURE__*/React.createElement("span", {
    style: {
      display: 'grid'
    }
  }, /*#__PURE__*/React.createElement("span", null, i.service), /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: 12,
      color: 'var(--muted-foreground)'
    }
  }, i.kind))), /*#__PURE__*/React.createElement("td", null, /*#__PURE__*/React.createElement("span", {
    className: "mono",
    style: {
      fontSize: 12,
      color: 'var(--muted-foreground)',
      overflowWrap: 'anywhere'
    }
  }, i.url)), /*#__PURE__*/React.createElement("td", null, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      gap: 6,
      justifyContent: 'flex-end'
    }
  }, /*#__PURE__*/React.createElement(Button, {
    variant: "outline",
    size: "sm",
    icon: "PlugZap",
    loading: testing === i.name,
    onClick: () => test(i)
  }, testing === i.name ? 'Testing…' : 'Test'), /*#__PURE__*/React.createElement(Button, {
    variant: "outline",
    size: "sm",
    icon: "Pencil",
    onClick: () => setDialog(true)
  }, "Edit"), /*#__PURE__*/React.createElement(Button, {
    variant: "danger",
    size: "sm",
    icon: "Trash2"
  }, "Remove")))))))), /*#__PURE__*/React.createElement(ListPager, {
    page: 1,
    pages: 1,
    pageSize: 25,
    total: 4
  })), dialog ? /*#__PURE__*/React.createElement(Dialog, {
    title: "Add integration",
    onClose: () => setDialog(false),
    footer: /*#__PURE__*/React.createElement(React.Fragment, null, /*#__PURE__*/React.createElement(Button, null, "Save"), /*#__PURE__*/React.createElement(Button, {
      variant: "outline",
      onClick: () => setDialog(false)
    }, "Cancel"))
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gap: '1rem'
    }
  }, /*#__PURE__*/React.createElement(Field, {
    label: "Service"
  }, /*#__PURE__*/React.createElement(Select, {
    options: ['Jellyfin', 'Torznab feed', 'qBittorrent'],
    style: {
      width: '100%'
    }
  })), /*#__PURE__*/React.createElement(Field, {
    label: "Name"
  }, /*#__PURE__*/React.createElement(Input, {
    placeholder: "Home Jellyfin"
  })), /*#__PURE__*/React.createElement(Field, {
    label: "Address",
    hint: "No user name, password or query in it."
  }, /*#__PURE__*/React.createElement(Input, {
    placeholder: "http://192.168.1.20:8096"
  })), /*#__PURE__*/React.createElement(Field, {
    label: "API key"
  }, /*#__PURE__*/React.createElement(Input, {
    type: "password",
    placeholder: "Stored encrypted, never shown again"
  })), /*#__PURE__*/React.createElement("div", null, /*#__PURE__*/React.createElement(Button, {
    variant: "outline",
    size: "sm",
    icon: "PlugZap"
  }, "Test connection")))) : null);
}
function DestinationsScreen() {
  const {
    Button,
    Input,
    Badge,
    StatusLine
  } = window.MAM_DS;
  const d = window.MAM_DATA;
  return /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gap: '1rem'
    }
  }, /*#__PURE__*/React.createElement(PageHeader, {
    title: "Destinations",
    lead: "Where downloads go: each destination is a category your download client already has, which decides the folder.",
    action: /*#__PURE__*/React.createElement(Button, {
      icon: "Plus"
    }, "Add destination")
  }), /*#__PURE__*/React.createElement(Input, {
    type: "search",
    placeholder: "Search by name",
    style: {
      maxWidth: '20rem'
    }
  }), /*#__PURE__*/React.createElement("div", {
    className: "ui-table-wrap"
  }, /*#__PURE__*/React.createElement("table", {
    className: "ui-table"
  }, /*#__PURE__*/React.createElement("thead", null, /*#__PURE__*/React.createElement("tr", null, /*#__PURE__*/React.createElement("th", null, "Name"), /*#__PURE__*/React.createElement("th", null, "Client \xB7 category"), /*#__PURE__*/React.createElement("th", null, "Saves to"), /*#__PURE__*/React.createElement("th", null, "Actions"))), /*#__PURE__*/React.createElement("tbody", null, d.destinations.map(x => /*#__PURE__*/React.createElement("tr", {
    key: x.name
  }, /*#__PURE__*/React.createElement("td", null, /*#__PURE__*/React.createElement("span", {
    style: {
      display: 'flex',
      gap: '0.375rem',
      alignItems: 'center'
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      fontWeight: 500
    }
  }, x.name), x.isDefault ? /*#__PURE__*/React.createElement(Badge, {
    variant: "secondary"
  }, "Default") : null)), /*#__PURE__*/React.createElement("td", null, x.client, " \xB7 ", /*#__PURE__*/React.createElement("span", {
    className: "mono"
  }, x.category)), /*#__PURE__*/React.createElement("td", null, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gap: 2
    }
  }, /*#__PURE__*/React.createElement("span", {
    className: "mono",
    style: {
      fontSize: 12
    }
  }, x.path), x.check === 'InLibrary' ? /*#__PURE__*/React.createElement(StatusLine, {
    status: "ok"
  }, "In the library \u201C", x.libs, "\u201D") : /*#__PURE__*/React.createElement(StatusLine, {
    status: "warning"
  }, x.problem))), /*#__PURE__*/React.createElement("td", null, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      gap: 6,
      justifyContent: 'flex-end'
    }
  }, /*#__PURE__*/React.createElement(Button, {
    variant: "outline",
    size: "sm",
    icon: "Pencil"
  }, "Edit"), /*#__PURE__*/React.createElement(Button, {
    variant: "danger",
    size: "sm",
    icon: "Trash2"
  }, "Remove")))))))));
}
if (window.__MAM_KIT) Object.assign(window, {
  IntegrationsScreen,
  DestinationsScreen
});
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/current/SettingsScreens.jsx", error: String((e && e.message) || e) }); }

// ui_kits/current/Shell.jsx
try { (() => {
/* AppLayout + UserMenu — header variant 2d: search field in the header, everything else in the account menu */
function AppHeader({
  page,
  onNav,
  onSignOut,
  onQuery
}) {
  const {
    AppLogo,
    Icon,
    ThemeSwitch
  } = window.MAM_DS;
  const d = window.MAM_DATA;
  const [open, setOpen] = React.useState(false);
  const [q, setQ] = React.useState('');
  const rootRef = React.useRef(null);
  const inputRef = React.useRef(null);
  React.useEffect(() => {
    const onKey = e => {
      const typing = ['INPUT', 'TEXTAREA', 'SELECT'].includes(document.activeElement.tagName);
      if (e.key === '/' && !typing) {
        e.preventDefault();
        inputRef.current && inputRef.current.focus();
      }
      if (e.key === 'Escape') setOpen(false);
    };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, []);
  React.useEffect(() => {
    if (!open) return undefined;
    const out = e => {
      if (rootRef.current && !rootRef.current.contains(e.target)) setOpen(false);
    };
    document.addEventListener('pointerdown', out);
    return () => document.removeEventListener('pointerdown', out);
  }, [open]);
  const go = p => {
    setOpen(false);
    onNav(p);
  };
  const submit = e => {
    e.preventDefault();
    if (onQuery) onQuery(q.trim());
    go('search');
    inputRef.current && inputRef.current.blur();
  };
  const settings = [['integrations', 'Integrations', 'Plug'], ['destinations', 'Destinations', 'FolderInput'], ['integrations', 'Media folders', 'FolderOpen'], ['users', 'Users', 'Users']];
  const libOn = page === 'library';
  return /*#__PURE__*/React.createElement("header", {
    style: {
      position: 'sticky',
      top: 0,
      zIndex: 30,
      display: 'grid',
      gridTemplateColumns: 'minmax(max-content,1fr) minmax(0,42rem) minmax(max-content,1fr)',
      alignItems: 'center',
      gap: '1.25rem',
      padding: '0.625rem 1.5rem',
      background: 'var(--card)',
      borderBottom: '1px solid var(--border)'
    }
  }, /*#__PURE__*/React.createElement("a", {
    href: "#",
    onClick: e => {
      e.preventDefault();
      go('home');
    },
    title: "The version you are running",
    style: {
      justifySelf: 'start',
      display: 'inline-flex',
      alignItems: 'center',
      gap: '0.625rem',
      color: 'inherit',
      textDecoration: 'none',
      whiteSpace: 'nowrap'
    }
  }, /*#__PURE__*/React.createElement(AppLogo, {
    size: 30
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      display: 'grid',
      gap: 1,
      lineHeight: 1.15
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: '0.9375rem',
      fontWeight: 600,
      color: 'var(--foreground)'
    }
  }, "Media Acquisition Manager"), /*#__PURE__*/React.createElement("span", {
    className: "mono",
    style: {
      fontSize: '0.6875rem',
      color: 'var(--subtle-foreground)'
    }
  }, "v", d.version))), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      alignItems: 'center',
      gap: '0.75rem',
      minWidth: 0
    }
  }, /*#__PURE__*/React.createElement("form", {
    role: "search",
    onSubmit: submit,
    style: {
      flex: '1 1 auto',
      minWidth: 0,
      position: 'relative'
    }
  }, /*#__PURE__*/React.createElement(Icon, {
    name: "Search",
    size: 16,
    style: {
      position: 'absolute',
      left: 11,
      top: 10,
      color: page === 'search' ? 'var(--primary)' : 'var(--muted-foreground)'
    }
  }), /*#__PURE__*/React.createElement("input", {
    ref: inputRef,
    className: "ui-input",
    type: "search",
    value: q,
    onChange: e => setQ(e.target.value),
    "aria-label": "Search for a movie",
    placeholder: "Search a movie \u2014 title, year or IMDb id",
    style: {
      paddingLeft: 36,
      paddingRight: q === '' ? 40 : 12,
      background: 'var(--muted)',
      borderColor: 'transparent'
    }
  }), q === '' ? /*#__PURE__*/React.createElement("kbd", {
    className: "mono",
    title: "Press / to search",
    onMouseDown: e => {
      e.preventDefault();
      inputRef.current && inputRef.current.focus();
    },
    style: {
      position: 'absolute',
      right: 8,
      top: 8,
      fontSize: 11,
      padding: '1px 6px',
      border: '1px solid var(--border-strong)',
      borderRadius: 'var(--radius-sm)',
      color: 'var(--muted-foreground)',
      background: 'var(--card)',
      cursor: 'text'
    }
  }, "/") : null), /*#__PURE__*/React.createElement("a", {
    href: "#",
    onClick: e => {
      e.preventDefault();
      go('library');
    },
    "aria-current": libOn ? 'page' : undefined,
    style: {
      display: 'inline-flex',
      alignItems: 'center',
      gap: 6,
      padding: '0.375rem 0.625rem',
      borderRadius: 'var(--radius-md)',
      fontSize: '0.875rem',
      fontWeight: 500,
      textDecoration: 'none',
      flexShrink: 0,
      color: libOn ? 'var(--foreground)' : 'var(--muted-foreground)',
      background: libOn ? 'var(--tint-selected)' : 'transparent'
    }
  }, /*#__PURE__*/React.createElement(Icon, {
    name: "LibraryBig",
    size: 16
  }), "Library")), /*#__PURE__*/React.createElement("div", {
    ref: rootRef,
    style: {
      position: 'relative',
      justifySelf: 'end'
    }
  }, /*#__PURE__*/React.createElement("button", {
    type: "button",
    "aria-expanded": open,
    "aria-label": 'Account menu for ' + d.user.userName,
    onClick: () => setOpen(!open),
    style: {
      display: 'inline-flex',
      alignItems: 'center',
      gap: 8,
      cursor: 'pointer',
      padding: '2px 6px 2px 2px',
      borderRadius: 9999
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      width: 28,
      height: 28,
      borderRadius: 9999,
      display: 'inline-grid',
      placeItems: 'center',
      background: 'var(--tint-selected)',
      color: 'var(--primary)',
      fontWeight: 600,
      fontSize: 12
    }
  }, d.user.userName[0].toUpperCase()), /*#__PURE__*/React.createElement("span", {
    style: {
      fontWeight: 500
    }
  }, d.user.userName), /*#__PURE__*/React.createElement(Icon, {
    name: "ChevronDown",
    size: 14,
    style: {
      color: 'var(--muted-foreground)'
    }
  })), open ? /*#__PURE__*/React.createElement("div", {
    className: "ui-menu",
    style: {
      position: 'absolute',
      top: 'calc(100% + 0.375rem)',
      right: 0,
      width: 240
    }
  }, /*#__PURE__*/React.createElement("button", {
    type: "button",
    className: "ui-menu-item",
    onClick: () => go('profile'),
    style: {
      display: 'grid',
      gap: 2,
      padding: '6px 12px 8px',
      background: page === 'profile' ? 'var(--tint-selected)' : undefined
    },
    title: "Your profile"
  }, /*#__PURE__*/React.createElement("strong", null, d.user.userName), /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: 12,
      color: 'var(--muted-foreground)',
      display: 'inline-flex',
      alignItems: 'center',
      gap: 6
    }
  }, d.user.isOwner ? 'Owner' : 'Member', " \xB7 ", /*#__PURE__*/React.createElement("span", {
    className: "mono"
  }, "v", d.version), " \xB7", /*#__PURE__*/React.createElement("span", {
    style: {
      display: 'inline-flex',
      alignItems: 'center',
      gap: 4
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      width: 7,
      height: 7,
      borderRadius: 9999,
      background: 'var(--status-up)'
    }
  }), "Ready"))), /*#__PURE__*/React.createElement("button", {
    type: "button",
    className: "ui-menu-item",
    onClick: () => go('profile')
  }, /*#__PURE__*/React.createElement(Icon, {
    name: "CircleUser",
    size: 14
  }), "Profile"), d.user.isOwner ? /*#__PURE__*/React.createElement(React.Fragment, null, /*#__PURE__*/React.createElement("div", {
    className: "ui-menu-sep"
  }), settings.map(([p, l, i]) => /*#__PURE__*/React.createElement("button", {
    key: l,
    type: "button",
    className: "ui-menu-item",
    onClick: () => go(p)
  }, /*#__PURE__*/React.createElement(Icon, {
    name: i,
    size: 14
  }), l))) : null, /*#__PURE__*/React.createElement("div", {
    className: "ui-menu-sep"
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'space-between',
      padding: '4px 8px 4px 12px'
    }
  }, /*#__PURE__*/React.createElement("span", null, "Theme"), /*#__PURE__*/React.createElement(ThemeSwitch, null)), /*#__PURE__*/React.createElement("div", {
    className: "ui-menu-sep"
  }), /*#__PURE__*/React.createElement("button", {
    type: "button",
    className: "ui-menu-item",
    onClick: () => {
      setOpen(false);
      onSignOut();
    }
  }, /*#__PURE__*/React.createElement(Icon, {
    name: "LogOut",
    size: 14
  }), "Sign out")) : null));
}
function PageHeader({
  title,
  lead,
  action
}) {
  return /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      flexWrap: 'wrap',
      alignItems: 'flex-start',
      justifyContent: 'space-between',
      gap: '0.75rem 1rem'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gap: '0.375rem',
      minWidth: 0
    }
  }, /*#__PURE__*/React.createElement("h1", {
    style: {
      fontSize: 'var(--text-2xl)',
      fontWeight: 600,
      lineHeight: 1.25
    }
  }, title), /*#__PURE__*/React.createElement("p", {
    style: {
      fontSize: '0.875rem',
      color: 'var(--muted-foreground)'
    }
  }, lead)), action);
}
function HomeScreen() {
  const {
    Card,
    StatusLine
  } = window.MAM_DS;
  return /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gap: '1.5rem'
    }
  }, /*#__PURE__*/React.createElement("section", {
    style: {
      display: 'grid',
      gap: '0.5rem'
    }
  }, /*#__PURE__*/React.createElement("h1", {
    style: {
      fontSize: 'var(--text-2xl)',
      fontWeight: 600
    }
  }, "Media Acquisition Manager"), /*#__PURE__*/React.createElement("p", {
    style: {
      color: 'var(--muted-foreground)',
      fontSize: '0.875rem'
    }
  }, "Search your library and your release sources together, then send the release you choose to your download client.")), /*#__PURE__*/React.createElement(Card, {
    title: "Service status",
    description: "Whether the server is ready to work."
  }, /*#__PURE__*/React.createElement(StatusLine, {
    status: "ok"
  }, "Ready")));
}
if (window.__MAM_KIT) Object.assign(window, {
  AppHeader,
  PageHeader,
  HomeScreen
});
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/current/Shell.jsx", error: String((e && e.message) || e) }); }

// ui_kits/current/SignIn.jsx
try { (() => {
/* LoginPage (MAM-4) */
function SignInScreen({
  onSignIn
}) {
  const {
    Card,
    Field,
    Input,
    Button,
    Banner
  } = window.MAM_DS;
  const [err, setErr] = React.useState(null);
  const [busy, setBusy] = React.useState(false);
  const submit = e => {
    e.preventDefault();
    setErr(null);
    const f = e.currentTarget;
    const ok = f.querySelector('[type=password]').value === 'password123';
    setBusy(true);
    setTimeout(() => {
      setBusy(false);
      if (window.MAM_SIM.offline) setErr(SERVER_DOWN);else if (!ok) setErr('The user name or password is incorrect.');else onSignIn();
    }, 500);
  };
  return /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      justifyItems: 'center',
      paddingTop: '2rem'
    }
  }, /*#__PURE__*/React.createElement(Card, {
    title: "Sign in",
    description: "Use the user name and password the owner gave you.",
    style: {
      width: '100%',
      maxWidth: '24rem'
    }
  }, /*#__PURE__*/React.createElement("form", {
    onSubmit: submit,
    style: {
      display: 'grid',
      gap: '1rem'
    }
  }, err ? /*#__PURE__*/React.createElement(Banner, null, err) : null, /*#__PURE__*/React.createElement(Field, {
    label: "User name"
  }, /*#__PURE__*/React.createElement(Input, {
    defaultValue: "vasyl",
    autoComplete: "username"
  })), /*#__PURE__*/React.createElement(Field, {
    label: "Password"
  }, /*#__PURE__*/React.createElement(Input, {
    type: "password",
    defaultValue: "password123"
  })), /*#__PURE__*/React.createElement(Button, {
    type: "submit",
    loading: busy
  }, busy ? 'Signing in…' : 'Sign in'), /*#__PURE__*/React.createElement("p", {
    style: {
      fontSize: 12,
      color: 'var(--muted-foreground)'
    }
  }, "Demo: any password other than password123 fails."))));
}
if (window.__MAM_KIT) Object.assign(window, {
  SignInScreen
});
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/current/SignIn.jsx", error: String((e && e.message) || e) }); }

// ui_kits/current/Sim.jsx
try { (() => {
/* Failure simulator — a reviewer tool, not product UI. Toggles make the kit's requests "fail" so each
   page shows its error states in place. Shared store: useSim() re-renders on change. */
const SIM_DEFAULT = {
  offline: false,
  load: false,
  refresh: false,
  action: false,
  source: true,
  crash: false,
  notFound: false
};
if (window.__MAM_KIT) window.MAM_SIM = window.MAM_SIM || {
  ...SIM_DEFAULT
};
const simSubs = new Set();
function setSim(patch) {
  window.MAM_SIM = {
    ...window.MAM_SIM,
    ...patch
  };
  simSubs.forEach(f => f(window.MAM_SIM));
}
function useSim() {
  const [s, set] = React.useState(window.MAM_SIM);
  React.useEffect(() => {
    simSubs.add(set);
    return () => simSubs.delete(set);
  }, []);
  return s;
}
/** Retry that "works": spins briefly, then clears the failure it was for. */
function useRetry(key) {
  const [busy, setBusy] = React.useState(false);
  return [busy, () => {
    setBusy(true);
    setTimeout(() => {
      setBusy(false);
      setSim({
        [key]: false
      });
    }, 700);
  }];
}
const SERVER_DOWN = 'The server could not be reached. Check that it is running, then try again.';

/** Under the header while the API can't be reached; data on screen stays, actions wait. */
function OfflineStrip() {
  const sim = useSim();
  const {
    Icon,
    Button
  } = window.MAM_DS;
  const [busy, retry] = useRetry('offline');
  if (!sim.offline) return null;
  return /*#__PURE__*/React.createElement("div", {
    role: "alert",
    style: {
      position: 'sticky',
      top: 57,
      zIndex: 25,
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      gap: '0.75rem',
      flexWrap: 'wrap',
      padding: '0.5rem 1.5rem',
      background: 'var(--tint-warning)',
      borderBottom: '1px solid var(--tint-warning-border)',
      backdropFilter: 'none'
    }
  }, /*#__PURE__*/React.createElement(Icon, {
    name: "WifiOff",
    size: 16,
    style: {
      color: 'var(--status-attention)'
    }
  }), /*#__PURE__*/React.createElement("span", null, SERVER_DOWN, " ", /*#__PURE__*/React.createElement("span", {
    style: {
      color: 'var(--muted-foreground)'
    }
  }, "Trying again every few seconds.")), /*#__PURE__*/React.createElement(Button, {
    variant: "outline",
    size: "sm",
    icon: "RotateCw",
    loading: busy,
    onClick: retry
  }, busy ? 'Retrying…' : 'Retry now'));
}
function SimPanel() {
  const sim = useSim();
  const {
    Icon
  } = window.MAM_DS;
  const [open, setOpenRaw] = React.useState(() => localStorage.getItem('mamkit:sim:open') === '1');
  const setOpen = v => {
    setOpenRaw(v);
    localStorage.setItem('mamkit:sim:open', v ? '1' : '0');
  };
  const on = Object.entries(sim).filter(([k, v]) => v && k !== 'source').length;
  const rows = [['offline', 'Server unreachable', 'Strip under the header'], ['load', 'First load fails', 'Error block instead of the list'], ['refresh', 'Refresh fails', 'Banner over stale data'], ['action', 'Action fails', 'Sync, Test, Delete, Add, Search'], ['source', 'One release source fails', 'Jackett line on Search'], ['crash', 'Page crashes', 'Error boundary card'], ['notFound', 'Unknown address', '404 page']];
  if (!open) return /*#__PURE__*/React.createElement("button", {
    type: "button",
    onClick: () => setOpen(true),
    style: {
      position: 'fixed',
      left: 16,
      bottom: 16,
      zIndex: 70,
      display: 'inline-flex',
      alignItems: 'center',
      gap: 6,
      padding: '0.5rem 0.75rem',
      borderRadius: 9999,
      background: 'var(--foreground)',
      color: 'var(--background)',
      fontWeight: 500,
      cursor: 'pointer',
      boxShadow: 'var(--shadow-lg)'
    }
  }, /*#__PURE__*/React.createElement(Icon, {
    name: "FlaskConical",
    size: 14
  }), "Simulate failures", on ? /*#__PURE__*/React.createElement("span", {
    style: {
      background: 'var(--destructive)',
      color: '#fff',
      borderRadius: 9999,
      fontSize: 11,
      padding: '0 6px'
    }
  }, on) : null);
  return /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'fixed',
      left: 16,
      bottom: 16,
      zIndex: 70,
      width: 272,
      background: 'var(--popover)',
      border: '1px dashed var(--border-strong)',
      borderRadius: 'var(--radius-lg)',
      boxShadow: 'var(--shadow-lg)',
      padding: '0.75rem',
      display: 'grid',
      gap: 6
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      alignItems: 'center',
      gap: 6
    }
  }, /*#__PURE__*/React.createElement(Icon, {
    name: "FlaskConical",
    size: 14,
    style: {
      color: 'var(--muted-foreground)'
    }
  }), /*#__PURE__*/React.createElement("strong", {
    style: {
      flex: 1
    }
  }, "Simulate failures"), /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: 11,
      color: 'var(--subtle-foreground)'
    }
  }, "review only"), /*#__PURE__*/React.createElement("button", {
    type: "button",
    title: "Hide",
    onClick: () => setOpen(false),
    style: {
      cursor: 'pointer',
      display: 'inline-flex',
      color: 'var(--muted-foreground)'
    }
  }, /*#__PURE__*/React.createElement(Icon, {
    name: "X",
    size: 14
  }))), rows.map(([k, label, hint]) => /*#__PURE__*/React.createElement("label", {
    key: k,
    style: {
      display: 'grid',
      gridTemplateColumns: 'auto 1fr',
      gap: '0 8px',
      alignItems: 'start',
      cursor: 'pointer',
      padding: '4px 2px'
    }
  }, /*#__PURE__*/React.createElement("input", {
    type: "checkbox",
    checked: !!sim[k],
    onChange: e => setSim({
      [k]: e.target.checked
    }),
    style: {
      accentColor: 'var(--primary)',
      marginTop: 3
    }
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      display: 'grid'
    }
  }, /*#__PURE__*/React.createElement("span", null, label), /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: 11,
      color: 'var(--muted-foreground)'
    }
  }, hint)))));
}

/** AppErrorFallback (frontend/src/components/AppErrorFallback.tsx) — copy from errors.json */
function CrashFallback() {
  const {
    Card,
    Button
  } = window.MAM_DS;
  return /*#__PURE__*/React.createElement("div", {
    role: "alert",
    style: {
      minHeight: '60dvh',
      display: 'grid',
      placeItems: 'center',
      padding: '1.5rem'
    }
  }, /*#__PURE__*/React.createElement(Card, {
    title: "Something went wrong",
    description: "This page hit an unexpected error. The rest of the app is still usable.",
    style: {
      maxWidth: '32rem'
    },
    footer: /*#__PURE__*/React.createElement(Button, {
      onClick: () => setSim({
        crash: false
      })
    }, "Try again")
  }));
}
/** NotFoundPage — copy from notFound.json */
function NotFound({
  onHome
}) {
  const {
    Card,
    Button
  } = window.MAM_DS;
  return /*#__PURE__*/React.createElement("div", {
    style: {
      minHeight: '50dvh',
      display: 'grid',
      placeItems: 'center'
    }
  }, /*#__PURE__*/React.createElement(Card, {
    title: "Page not found",
    description: "There is no page at this address.",
    style: {
      maxWidth: '28rem'
    },
    footer: /*#__PURE__*/React.createElement(Button, {
      variant: "outline",
      icon: "House",
      onClick: () => {
        setSim({
          notFound: false
        });
        onHome();
      }
    }, "Go to the home page")
  }));
}
if (window.__MAM_KIT) Object.assign(window, {
  useSim,
  setSim,
  useRetry,
  SimPanel,
  OfflineStrip,
  CrashFallback,
  NotFound,
  SERVER_DOWN
});
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/current/Sim.jsx", error: String((e && e.message) || e) }); }

// ui_kits/current/UsersScreen.jsx
try { (() => {
/* UsersPage (MAM-4) — owner-only: search, add, disable/enable, reset password, delete */
function UsersScreen() {
  const {
    Button,
    Input,
    Badge,
    Banner,
    Dialog,
    Field,
    ListPager,
    ErrorState
  } = window.MAM_DS;
  const sim = useSim();
  const [retrying, retry] = useRetry('load');
  const [actionError, setActionError] = React.useState(null);
  const failing = () => {
    if (sim.action || sim.offline) {
      setActionError(sim.offline ? SERVER_DOWN : 'Could not change this user. Reload the page and try again.');
      setDialog(null);
      return true;
    }
    setActionError(null);
    return false;
  };
  const [users, setUsers] = React.useState(() => window.MAM_DATA.users.map(u => ({
    ...u
  })));
  const [q, setQ] = React.useState('');
  const [dialog, setDialog] = React.useState(null);
  const [news, setNews] = React.useState(null);
  const list = users.filter(u => !q || u.userName.toLowerCase().includes(q.toLowerCase()));
  const toggle = u => !failing() && setUsers(users.map(x => x.userName === u.userName ? {
    ...x,
    isDisabled: !x.isDisabled
  } : x));
  const remove = u => {
    if (failing()) return;
    setUsers(users.filter(x => x.userName !== u.userName));
    setDialog(null);
  };
  const add = name => {
    if (name) setUsers([...users, {
      userName: name,
      isOwner: false,
      isDisabled: false,
      created: '2 Oct 2026, 12:00',
      lastSignIn: null
    }].sort((a, b) => a.userName.localeCompare(b.userName)));
    setDialog(null);
  };
  return /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gap: '1rem'
    }
  }, /*#__PURE__*/React.createElement(PageHeader, {
    title: "Users",
    lead: "The people who can sign in to this installation.",
    action: /*#__PURE__*/React.createElement(Button, {
      icon: "UserPlus",
      onClick: () => setDialog({
        kind: 'add'
      })
    }, "Add user")
  }), /*#__PURE__*/React.createElement(Input, {
    type: "search",
    value: q,
    onChange: e => setQ(e.target.value),
    placeholder: "Search by user name",
    "aria-label": "Search users",
    style: {
      maxWidth: '20rem'
    }
  }), news ? /*#__PURE__*/React.createElement(Banner, {
    tone: "ok",
    onDismiss: () => setNews(null)
  }, news) : null, actionError ? /*#__PURE__*/React.createElement(Banner, {
    onDismiss: () => setActionError(null)
  }, actionError) : null, sim.load ? /*#__PURE__*/React.createElement(ErrorState, {
    title: "Could not load the users.",
    onRetry: retry,
    retrying: retrying
  }, SERVER_DOWN) : list.length === 0 ? /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      justifyItems: 'start',
      gap: '0.75rem',
      color: 'var(--muted-foreground)'
    }
  }, /*#__PURE__*/React.createElement("p", null, "No users match \u201C", q, "\u201D."), /*#__PURE__*/React.createElement(Button, {
    variant: "outline",
    size: "sm",
    onClick: () => setQ('')
  }, "Clear search")) : /*#__PURE__*/React.createElement(React.Fragment, null, /*#__PURE__*/React.createElement("div", {
    className: "ui-table-wrap"
  }, /*#__PURE__*/React.createElement("table", {
    className: "ui-table"
  }, /*#__PURE__*/React.createElement("thead", null, /*#__PURE__*/React.createElement("tr", null, /*#__PURE__*/React.createElement("th", null, "User name"), /*#__PURE__*/React.createElement("th", null, "Created"), /*#__PURE__*/React.createElement("th", null, "Last sign-in"), /*#__PURE__*/React.createElement("th", null, "Actions"))), /*#__PURE__*/React.createElement("tbody", null, list.map(u => /*#__PURE__*/React.createElement("tr", {
    key: u.userName
  }, /*#__PURE__*/React.createElement("td", null, /*#__PURE__*/React.createElement("span", {
    style: {
      display: 'flex',
      flexWrap: 'wrap',
      alignItems: 'center',
      gap: '0.375rem'
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      fontWeight: 500,
      color: u.isDisabled ? 'var(--muted-foreground)' : undefined,
      overflowWrap: 'anywhere'
    }
  }, u.userName), u.isOwner ? /*#__PURE__*/React.createElement(Badge, {
    variant: "default"
  }, "Owner") : null, u.isDisabled ? /*#__PURE__*/React.createElement(Badge, {
    variant: "outline"
  }, "Disabled") : null)), /*#__PURE__*/React.createElement("td", null, u.created), /*#__PURE__*/React.createElement("td", {
    style: {
      color: u.lastSignIn ? undefined : 'var(--muted-foreground)'
    }
  }, u.lastSignIn || 'Never'), /*#__PURE__*/React.createElement("td", null, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      flexWrap: 'wrap',
      gap: 6,
      justifyContent: 'flex-end'
    }
  }, u.isDisabled ? /*#__PURE__*/React.createElement(Button, {
    variant: "outline",
    size: "sm",
    icon: "UserCheck",
    onClick: () => toggle(u)
  }, "Enable") : /*#__PURE__*/React.createElement("span", {
    title: u.isOwner ? "The installation owner can't be disabled." : undefined,
    style: {
      display: 'inline-flex',
      cursor: u.isOwner ? 'not-allowed' : undefined
    }
  }, /*#__PURE__*/React.createElement(Button, {
    variant: "outline",
    size: "sm",
    icon: "UserX",
    disabled: u.isOwner,
    onClick: () => toggle(u)
  }, "Disable")), /*#__PURE__*/React.createElement(Button, {
    variant: "outline",
    size: "sm",
    icon: "KeyRound",
    onClick: () => setDialog({
      kind: 'reset',
      user: u
    })
  }, "Reset password"), /*#__PURE__*/React.createElement("span", {
    title: u.isOwner ? "The installation owner can't be deleted." : undefined,
    style: {
      display: 'inline-flex',
      cursor: u.isOwner ? 'not-allowed' : undefined
    }
  }, /*#__PURE__*/React.createElement(Button, {
    variant: "danger",
    size: "sm",
    icon: "Trash2",
    disabled: u.isOwner,
    onClick: () => setDialog({
      kind: 'delete',
      user: u
    })
  }, "Delete"))))))))), /*#__PURE__*/React.createElement(ListPager, {
    page: 1,
    pages: 1,
    pageSize: 25,
    total: list.length
  })), dialog && dialog.kind === 'add' ? /*#__PURE__*/React.createElement(AddUserDialog, {
    onClose: () => setDialog(null),
    onAdd: add,
    fail: sim.action || sim.offline,
    taken: users.map(x => x.userName.toLowerCase())
  }) : null, dialog && dialog.kind === 'reset' ? /*#__PURE__*/React.createElement(Dialog, {
    title: 'Reset the password of ' + dialog.user.userName,
    description: dialog.user.isOwner ? 'Your other devices are signed out. This one stays signed in.' : dialog.user.userName + ' is signed out on every device and signs in again with the new password.',
    onClose: () => setDialog(null),
    footer: /*#__PURE__*/React.createElement(React.Fragment, null, /*#__PURE__*/React.createElement(Button, {
      onClick: () => {
        setNews('The password of ' + dialog.user.userName + ' was reset.');
        setDialog(null);
      }
    }, "Reset password"), /*#__PURE__*/React.createElement(Button, {
      variant: "outline",
      onClick: () => setDialog(null)
    }, "Cancel"))
  }, /*#__PURE__*/React.createElement(Field, {
    label: "New password",
    hint: "8\u2013128 characters."
  }, /*#__PURE__*/React.createElement(Input, {
    type: "password",
    autoComplete: "new-password"
  }))) : null, dialog && dialog.kind === 'delete' ? /*#__PURE__*/React.createElement(Dialog, {
    maxWidth: "26rem",
    title: 'Delete ' + dialog.user.userName + '?',
    description: dialog.user.userName + " can no longer sign in and is signed out everywhere. This can't be undone.",
    onClose: () => setDialog(null),
    footer: /*#__PURE__*/React.createElement(React.Fragment, null, /*#__PURE__*/React.createElement(Button, {
      variant: "destructive",
      onClick: () => remove(dialog.user)
    }, "Delete"), /*#__PURE__*/React.createElement(Button, {
      variant: "outline",
      onClick: () => setDialog(null)
    }, "Cancel"))
  }) : null);
}
function AddUserDialog({
  onClose,
  onAdd,
  fail,
  taken
}) {
  const {
    Button,
    Input,
    Dialog,
    Field,
    Banner
  } = window.MAM_DS;
  const [v, setV] = React.useState({
    name: '',
    a: '',
    b: ''
  });
  const [err, setErr] = React.useState({});
  const [busy, setBusy] = React.useState(false);
  const submit = () => {
    const name = v.name.trim(),
      e = {};
    if (!/^[A-Za-z0-9._-]{3,32}$/.test(name)) e.name = 'A user name is 3–32 letters, digits, ".", "-" or "_".';else if (taken.includes(name.toLowerCase())) e.name = 'A user named ' + name + ' already exists.';
    if (v.a.length < 8) e.a = 'A password is at least 8 characters.';
    if (v.b !== v.a) e.b = 'The two passwords are different. Type the same password in both fields.';
    setErr(e);
    if (Object.keys(e).length) return;
    setBusy(true);
    setTimeout(() => {
      setBusy(false);
      if (fail) setErr({
        form: 'Could not add the user. The server could not be reached. Check that it is running, then try again.'
      });else onAdd(name);
    }, 600);
  };
  const set = k => ev => {
    setV({
      ...v,
      [k]: ev.target.value
    });
    if (err[k]) setErr({
      ...err,
      [k]: undefined
    });
  };
  return /*#__PURE__*/React.createElement(Dialog, {
    title: "Add user",
    description: "They sign in with this user name and password. You can reset the password later.",
    onClose: onClose,
    footer: /*#__PURE__*/React.createElement(React.Fragment, null, /*#__PURE__*/React.createElement(Button, {
      loading: busy,
      onClick: submit
    }, busy ? 'Adding…' : 'Add user'), /*#__PURE__*/React.createElement(Button, {
      variant: "outline",
      onClick: onClose
    }, "Cancel"))
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gap: '1rem'
    }
  }, /*#__PURE__*/React.createElement(Field, {
    label: "User name",
    hint: "3\u201332 letters, digits, . - and _",
    error: err.name
  }, /*#__PURE__*/React.createElement(Input, {
    value: v.name,
    onChange: set('name'),
    autoComplete: "off"
  })), /*#__PURE__*/React.createElement(Field, {
    label: "Password",
    hint: "8\u2013128 characters.",
    error: err.a
  }, /*#__PURE__*/React.createElement(Input, {
    type: "password",
    value: v.a,
    onChange: set('a'),
    autoComplete: "new-password"
  })), /*#__PURE__*/React.createElement(Field, {
    label: "Confirm password",
    error: err.b
  }, /*#__PURE__*/React.createElement(Input, {
    type: "password",
    value: v.b,
    onChange: set('b'),
    autoComplete: "new-password"
  })), err.form ? /*#__PURE__*/React.createElement(Banner, null, err.form) : null));
}
if (window.__MAM_KIT) Object.assign(window, {
  UsersScreen
});
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/current/UsersScreen.jsx", error: String((e && e.message) || e) }); }

// ui_kits/proposal/Notes.jsx
try { (() => {
/* Reviewer notes (outside the product UI): what changed and why, per screen. Ukrainian, for the owner. */
const NOTES = {
  search: ['Пошук — головна сторінка (так у nav-items.ts), тож форма зведена в один липкий рядок; IMDb і TMDb — одне поле (tt… = IMDb, цифри = TMDb).', 'Ваша копія закріплена ліворуч і стає точкою порівняння — це цінність №2 зі специфікації (verified vs claimed).', 'Релізи — щільна таблиця замість карток по 5–6 рядків: на екрані ~10 релізів замість 3. Розмір і сідери вирівняні праворуч.', 'Чипи, що відрізняються від вибраної копії, підсвічені тінтом primary — без оцінки «краще/гірше», рішення за користувачем.', 'Статус джерел — компактні бейджі в заголовку секції; помилка одного джерела не ховає інші.', 'Details відкривається боковою панеллю з таблицею «claimed | verified» рядок у рядок; список лишається видимим.'],
  library: ['Панель Sources (≈180px) стала однорядковою смугою з тими самими фактами й діями.', 'Роботи — таблиця з колонкою «Best copy»; файли й треки розгортаються в рядку, а не займають кожну картку повністю.', 'Фільтр «Missing an id» — сегментований перемикач з кількістю, бо це часта перевірка.'],
  integrations: ['Налаштування винесено з меню користувача в бічну панель: 4 сторінки власника більше не сховані.', 'Інтеграції згруповані за роллю (бібліотека → джерела → клієнт), що повторює конвеєр застосунку.', 'Результат Test з’являється в рядку, який тестували, а не банером над таблицею.'],
  shell: ['Бічна панель 232px замість хедера: місце під майбутні Jobs і Duplicates (фази A–B) без переповнення.', 'Статус сервісу й версія — внизу панелі (зараз лише на Home).', 'Перемикач теми Light / Dark / System внизу панелі. Пише той самий ключ mam:theme, який уже читає скрипт у index.html, тож вибір зберігається без білого спалаху.', 'Контент ширший (до 80rem) — Search і Library отримують дві колонки/таблиці.']
};
function NotesPanel({
  page
}) {
  const {
    Button
  } = window.MAM_DS;
  const [open, setOpen] = React.useState(true);
  const list = [...(NOTES[page] || NOTES.integrations), ...NOTES.shell];
  if (!open) return /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'fixed',
      right: 16,
      bottom: 16,
      zIndex: 60
    }
  }, /*#__PURE__*/React.createElement(Button, {
    icon: "MessageSquareText",
    onClick: () => setOpen(true)
  }, "\u0429\u043E \u0437\u043C\u0456\u043D\u0435\u043D\u043E"));
  return /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'fixed',
      right: 16,
      bottom: 16,
      zIndex: 60,
      width: 340,
      maxHeight: '70vh',
      overflowY: 'auto',
      background: 'var(--popover)',
      border: '1px solid var(--border)',
      borderRadius: 'var(--radius-lg)',
      boxShadow: 'var(--shadow-lg)',
      padding: '0.875rem 1rem',
      display: 'grid',
      gap: '0.625rem'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      alignItems: 'center',
      gap: 8
    }
  }, /*#__PURE__*/React.createElement("strong", {
    style: {
      flex: 1
    }
  }, "\u0429\u043E \u0437\u043C\u0456\u043D\u0435\u043D\u043E \u0432 layout"), /*#__PURE__*/React.createElement(Button, {
    variant: "ghost",
    size: "icon",
    icon: "X",
    title: "Hide",
    onClick: () => setOpen(false)
  })), /*#__PURE__*/React.createElement("ol", {
    style: {
      display: 'grid',
      gap: 8,
      listStyle: 'decimal',
      paddingLeft: '1.1rem'
    }
  }, list.map((n, i) => /*#__PURE__*/React.createElement("li", {
    key: i,
    style: {
      textWrap: 'pretty'
    }
  }, n))), /*#__PURE__*/React.createElement("p", {
    style: {
      fontSize: 12,
      color: 'var(--muted-foreground)'
    }
  }, "\u041A\u043E\u043B\u044C\u043E\u0440\u0438, \u0448\u0440\u0438\u0444\u0442\u0438, \u0440\u0430\u0434\u0456\u0443\u0441\u0438 \u0439 \u043A\u043E\u043C\u043F\u043E\u043D\u0435\u043D\u0442\u0438 \u2014 \u0431\u0435\u0437 \u0437\u043C\u0456\u043D. ", /*#__PURE__*/React.createElement("a", {
    href: "../current/index.html"
  }, "\u041F\u043E\u0442\u043E\u0447\u043D\u0430 \u0432\u0435\u0440\u0441\u0456\u044F \u2192")));
}
if (window.__MAM_KIT) Object.assign(window, {
  NotesPanel
});
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/proposal/Notes.jsx", error: String((e && e.message) || e) }); }

// ui_kits/proposal/PLibrary.jsx
try { (() => {
/* Proposed Library: one-line source strip, a dense table of works, a row expands to versions and files. */
function PLibrary() {
  const {
    Button,
    Input,
    Select,
    Badge,
    Icon,
    StatusLine,
    ListPager,
    ChipRow,
    Facts
  } = window.MAM_DS;
  const d = window.MAM_DATA,
    src = d.sources[0];
  const [q, setQ] = React.useState('');
  const [missing, setMissing] = React.useState(false);
  const [open, setOpen] = React.useState({
    w1: true
  });
  const [syncing, setSyncing] = React.useState(false);
  const works = d.works.filter(w => (!missing || w.missing.length) && (!q || (w.title + ' ' + (w.originalTitle || '') + ' ' + w.year).toLowerCase().includes(q.toLowerCase())));
  const cols = '1.75rem minmax(0,2.2fr) 3.5rem minmax(0,1.4fr) minmax(0,1.5fr) 4.5rem 5.5rem';
  const seg = on => ({
    height: 32,
    padding: '0 0.75rem',
    fontSize: 12,
    fontWeight: 500,
    cursor: 'pointer',
    background: on ? 'var(--card)' : 'transparent',
    color: on ? 'var(--foreground)' : 'var(--muted-foreground)',
    borderRadius: 'var(--radius-sm)',
    boxShadow: on ? 'var(--shadow-sm)' : 'none'
  });
  return /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gap: '1rem'
    }
  }, /*#__PURE__*/React.createElement(PHeader, {
    title: "Library",
    lead: "The movies in your media library as its last sync found them, with the facts the app read from each file."
  }), /*#__PURE__*/React.createElement("div", {
    className: "ui-card",
    style: {
      display: 'flex',
      flexWrap: 'wrap',
      alignItems: 'center',
      gap: '0.5rem 1.25rem',
      padding: '0.625rem 0.875rem'
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      display: 'flex',
      alignItems: 'center',
      gap: 6
    }
  }, /*#__PURE__*/React.createElement(Icon, {
    name: "LibraryBig",
    size: 16,
    style: {
      color: 'var(--muted-foreground)'
    }
  }), /*#__PURE__*/React.createElement("strong", {
    style: {
      fontWeight: 600
    }
  }, src.name), /*#__PURE__*/React.createElement("span", {
    style: {
      color: 'var(--muted-foreground)'
    }
  }, "Jellyfin")), syncing ? /*#__PURE__*/React.createElement(StatusLine, {
    status: "running"
  }, "Syncing\u2026 312 of 870 movies") : /*#__PURE__*/React.createElement(StatusLine, {
    status: "ok"
  }, "Synced 1 Oct, 09:12 \xB7 812 works"), /*#__PURE__*/React.createElement("span", {
    style: {
      color: 'var(--muted-foreground)'
    }
  }, "Files: 846 inspected \xB7 3 couldn't be read \xB7 4 waiting"), /*#__PURE__*/React.createElement("a", {
    href: "#",
    style: {
      display: 'inline-flex',
      alignItems: 'center',
      gap: 4,
      color: 'var(--foreground)'
    }
  }, /*#__PURE__*/React.createElement(Icon, {
    name: "TriangleAlert",
    size: 14,
    style: {
      color: 'var(--status-attention)'
    }
  }), "2 files have no path"), /*#__PURE__*/React.createElement("span", {
    style: {
      marginLeft: 'auto',
      display: 'flex',
      gap: 6
    }
  }, /*#__PURE__*/React.createElement(Button, {
    variant: "outline",
    size: "sm",
    icon: "ScanSearch"
  }, "Inspect files"), /*#__PURE__*/React.createElement(Button, {
    variant: "outline",
    size: "sm",
    icon: "RefreshCw",
    loading: syncing,
    onClick: () => setSyncing(!syncing)
  }, syncing ? 'Syncing…' : 'Sync now'))), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      flexWrap: 'wrap',
      alignItems: 'center',
      gap: '0.5rem'
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      position: 'relative',
      flex: '1 1 18rem',
      maxWidth: '26rem'
    }
  }, /*#__PURE__*/React.createElement(Icon, {
    name: "Search",
    size: 14,
    style: {
      position: 'absolute',
      left: 10,
      top: 11,
      color: 'var(--muted-foreground)'
    }
  }), /*#__PURE__*/React.createElement(Input, {
    type: "search",
    value: q,
    onChange: e => setQ(e.target.value),
    placeholder: "Title, year, IMDb or TMDb id, or path",
    style: {
      paddingLeft: 30
    }
  })), /*#__PURE__*/React.createElement(Select, {
    options: ['All libraries', ...d.locations]
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      display: 'inline-flex',
      padding: 2,
      gap: 2,
      background: 'var(--secondary)',
      border: '1px solid var(--border)',
      borderRadius: 'var(--radius-md)'
    }
  }, /*#__PURE__*/React.createElement("button", {
    type: "button",
    style: seg(!missing),
    onClick: () => setMissing(false)
  }, "All movies"), /*#__PURE__*/React.createElement("button", {
    type: "button",
    style: seg(missing),
    onClick: () => setMissing(true)
  }, "Missing an id \xB7 2")), /*#__PURE__*/React.createElement("span", {
    style: {
      marginLeft: 'auto',
      color: 'var(--muted-foreground)'
    }
  }, missing ? 2 : 812, " works")), /*#__PURE__*/React.createElement("div", {
    className: "ui-card",
    style: {
      overflow: 'hidden'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gridTemplateColumns: cols,
      gap: '0 0.75rem',
      padding: '0.5rem 0.875rem',
      fontSize: 12,
      fontWeight: 500,
      color: 'var(--muted-foreground)',
      background: 'var(--muted)',
      borderBottom: '1px solid var(--border)'
    }
  }, /*#__PURE__*/React.createElement("span", null), /*#__PURE__*/React.createElement("span", null, "Title"), /*#__PURE__*/React.createElement("span", null, "Year"), /*#__PURE__*/React.createElement("span", null, "Ids"), /*#__PURE__*/React.createElement("span", null, "Best copy \xB7 verified"), /*#__PURE__*/React.createElement("span", null, "Versions"), /*#__PURE__*/React.createElement("span", {
    style: {
      textAlign: 'right'
    }
  }, "Size")), works.map((w, idx) => {
    const on = !!open[w.id];
    const s = w.summary;
    return /*#__PURE__*/React.createElement("div", {
      key: w.id,
      style: {
        borderTop: idx ? '1px solid var(--border)' : 0
      }
    }, /*#__PURE__*/React.createElement("div", {
      onClick: () => setOpen({
        ...open,
        [w.id]: !on
      }),
      style: {
        display: 'grid',
        gridTemplateColumns: cols,
        gap: '0 0.75rem',
        alignItems: 'center',
        padding: '0.625rem 0.875rem',
        cursor: 'pointer',
        background: on ? 'var(--muted)' : undefined
      }
    }, /*#__PURE__*/React.createElement(Icon, {
      name: on ? 'ChevronDown' : 'ChevronRight',
      size: 16,
      style: {
        color: 'var(--muted-foreground)'
      }
    }), /*#__PURE__*/React.createElement("span", {
      style: {
        display: 'grid',
        minWidth: 0
      }
    }, /*#__PURE__*/React.createElement("strong", {
      style: {
        fontWeight: 600,
        overflow: 'hidden',
        textOverflow: 'ellipsis',
        whiteSpace: 'nowrap'
      }
    }, w.title), w.originalTitle ? /*#__PURE__*/React.createElement("span", {
      style: {
        fontSize: 12,
        color: 'var(--muted-foreground)'
      }
    }, w.originalTitle) : null), /*#__PURE__*/React.createElement("span", {
      style: {
        fontVariantNumeric: 'tabular-nums'
      }
    }, w.year), /*#__PURE__*/React.createElement("span", {
      style: {
        display: 'flex',
        flexWrap: 'wrap',
        gap: 4
      }
    }, w.ids.map(x => /*#__PURE__*/React.createElement(Badge, {
      key: x,
      variant: "outline"
    }, x.split(' ')[0])), w.missing.map(x => /*#__PURE__*/React.createElement(Badge, {
      key: x,
      variant: "warning",
      icon: "TriangleAlert"
    }, "No ", x))), /*#__PURE__*/React.createElement("span", {
      style: {
        display: 'flex',
        flexWrap: 'wrap',
        gap: 4,
        fontSize: 12
      }
    }, s.res ? [s.res, s.codec, s.range, s.audio.join(' · ')].filter(Boolean).map(x => /*#__PURE__*/React.createElement("span", {
      key: x,
      className: "ui-chip"
    }, x)) : /*#__PURE__*/React.createElement("span", {
      style: {
        display: 'inline-flex',
        alignItems: 'center',
        gap: 4,
        color: 'var(--muted-foreground)'
      }
    }, s.note === 'No path in this app' ? /*#__PURE__*/React.createElement(Icon, {
      name: "TriangleAlert",
      size: 12,
      style: {
        color: 'var(--status-attention)'
      }
    }) : null, s.note)), /*#__PURE__*/React.createElement("span", null, w.versions.length), /*#__PURE__*/React.createElement("span", {
      style: {
        textAlign: 'right',
        fontVariantNumeric: 'tabular-nums'
      }
    }, s.size)), on ? /*#__PURE__*/React.createElement("div", {
      style: {
        padding: '0.25rem 0.875rem 0.875rem 3.375rem',
        display: 'grid',
        gap: '0.75rem',
        background: 'var(--muted)'
      }
    }, w.versions.map((v, i) => v.files.map((f, j) => /*#__PURE__*/React.createElement("div", {
      key: i + '-' + j,
      className: "ui-card",
      style: {
        display: 'grid',
        gridTemplateColumns: 'minmax(0,1fr) minmax(0,1fr)',
        gap: '0.5rem 1.5rem',
        padding: '0.75rem 0.875rem'
      }
    }, /*#__PURE__*/React.createElement("div", {
      style: {
        display: 'grid',
        gap: 3,
        alignContent: 'start',
        minWidth: 0
      }
    }, /*#__PURE__*/React.createElement("span", {
      style: {
        display: 'flex',
        gap: 8,
        alignItems: 'baseline'
      }
    }, /*#__PURE__*/React.createElement("strong", {
      style: {
        fontWeight: 600
      }
    }, v.label || 'Version ' + (i + 1)), /*#__PURE__*/React.createElement("span", {
      style: {
        fontSize: 12,
        color: 'var(--muted-foreground)'
      }
    }, f.size, " \xB7 Library: ", f.location)), /*#__PURE__*/React.createElement("p", {
      className: "mono",
      style: {
        fontWeight: 500,
        fontSize: 12,
        overflowWrap: 'anywhere'
      }
    }, f.name), /*#__PURE__*/React.createElement("p", {
      className: "mono",
      style: {
        fontSize: 11,
        color: 'var(--muted-foreground)',
        overflowWrap: 'anywhere'
      }
    }, f.folder), f.appPath === null ? /*#__PURE__*/React.createElement(StatusLine, {
      status: "warning"
    }, "This file has no path in this app.") : null), /*#__PURE__*/React.createElement("div", {
      style: {
        display: 'grid',
        gap: 6,
        alignContent: 'start',
        minWidth: 0
      }
    }, f.media ? /*#__PURE__*/React.createElement(React.Fragment, null, /*#__PURE__*/React.createElement(Facts, {
      verified: true,
      items: [f.media.duration, f.media.resolution, f.media.codec, f.media.range, f.media.bitrate]
    }), /*#__PURE__*/React.createElement(ChipRow, {
      label: "Audio",
      chips: f.media.audio,
      empty: "No audio tracks"
    }), /*#__PURE__*/React.createElement(ChipRow, {
      label: "Subtitles",
      chips: f.media.subs,
      empty: "No subtitles"
    })) : /*#__PURE__*/React.createElement("p", {
      style: {
        color: 'var(--muted-foreground)'
      }
    }, f.appPath === null ? 'Not inspected: the app has no path to this file.' : 'Not inspected yet')))))) : null);
  })), /*#__PURE__*/React.createElement(ListPager, {
    page: 1,
    pages: 33,
    pageSize: 25,
    total: 812
  }));
}
if (window.__MAM_KIT) Object.assign(window, {
  PLibrary
});
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/proposal/PLibrary.jsx", error: String((e && e.message) || e) }); }

// ui_kits/proposal/PSearch.jsx
try { (() => {
/* Proposed Search: one-row query bar, library copies pinned on the left as the comparison baseline,
   releases as a dense table on the right, details in a side drawer with claimed vs verified aligned. */
const P_KEYS = c => ({
  Resolution: c.resolution ? c.resolution + 'p' : null,
  Codec: c.codec,
  HDR: c.ranges && c.ranges.length ? c.ranges.join(' · ') : null,
  Audio: c.audio && c.audio.length ? c.audio.map(a => a.slice(0, 3)).join(' · ') : null,
  Subtitles: c.subs && c.subs.length ? c.subs.map(a => a.slice(0, 3)).join(' · ') : null
});
function copyKeys(file) {
  const m = file.media || {};
  const lang = arr => (arr || []).map(t => t.split(' · ')[0].slice(0, 3)).join(' · ') || null;
  const h = m.resolution ? m.resolution.split('×')[1] : null;
  const res = m.resolution ? Number(m.resolution.split('×')[0]) >= 3800 ? '2160p' : Number(m.resolution.split('×')[0]) >= 1900 ? '1080p' : h + 'p' : null;
  return {
    Resolution: res,
    Codec: m.codec ? m.codec.split(' ')[0].replace('H.264', 'H.264') : null,
    HDR: m.range && m.range !== 'SDR' ? m.range : null,
    Audio: lang(m.audio),
    Subtitles: lang(m.subs)
  };
}
function Chip({
  children,
  differs,
  unknown
}) {
  return /*#__PURE__*/React.createElement("span", {
    className: "ui-chip",
    style: {
      fontSize: 12,
      whiteSpace: 'nowrap',
      color: unknown ? 'var(--muted-foreground)' : undefined,
      borderStyle: unknown ? 'dashed' : 'solid',
      borderColor: differs ? 'color-mix(in oklab, var(--primary) 45%, transparent)' : undefined,
      background: differs ? 'var(--tint-selected)' : undefined
    }
  }, children);
}
function PSearch() {
  const {
    Input,
    Button,
    Badge,
    Icon,
    Select,
    ListPager
  } = window.MAM_DS;
  const d = window.MAM_DATA,
    s = d.search;
  const work = d.works.find(w => w.id === s.library[0].workId);
  const copies = work.versions.map((v, i) => ({
    i,
    label: v.label || 'Version ' + (i + 1),
    file: v.files[0]
  }));
  const [base, setBase] = React.useState(0);
  const [open, setOpen] = React.useState(null);
  const [expanded, setExpanded] = React.useState(false);
  const baseKeys = copyKeys(copies[base].file);
  const lbl = {
    fontSize: '0.75rem',
    fontWeight: 500,
    color: 'var(--muted-foreground)'
  };
  return /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gap: '1rem'
    }
  }, /*#__PURE__*/React.createElement("form", {
    onSubmit: e => e.preventDefault(),
    className: "ui-card",
    style: {
      position: 'sticky',
      top: 0,
      zIndex: 5,
      display: 'grid',
      gridTemplateColumns: 'minmax(0,1fr) 6.5rem 11rem auto',
      gap: '0.5rem',
      alignItems: 'end',
      padding: '0.75rem',
      boxShadow: 'var(--shadow-sm)'
    }
  }, /*#__PURE__*/React.createElement("label", {
    style: {
      display: 'grid',
      gap: 4
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: lbl
  }, "Title"), /*#__PURE__*/React.createElement("span", {
    style: {
      position: 'relative'
    }
  }, /*#__PURE__*/React.createElement(Icon, {
    name: "Search",
    size: 16,
    style: {
      position: 'absolute',
      left: 10,
      top: 10,
      color: 'var(--muted-foreground)'
    }
  }), /*#__PURE__*/React.createElement(Input, {
    defaultValue: s.text,
    style: {
      paddingLeft: 34,
      fontSize: 15
    }
  }))), /*#__PURE__*/React.createElement("label", {
    style: {
      display: 'grid',
      gap: 4
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: lbl
  }, "Year"), /*#__PURE__*/React.createElement(Input, {
    defaultValue: s.year,
    placeholder: "2014"
  })), /*#__PURE__*/React.createElement("label", {
    style: {
      display: 'grid',
      gap: 4
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: lbl
  }, "IMDb or TMDb id"), /*#__PURE__*/React.createElement(Input, {
    placeholder: "tt0816692 or 157336"
  })), /*#__PURE__*/React.createElement(Button, {
    type: "submit"
  }, "Search")), /*#__PURE__*/React.createElement("div", {
    className: "p-split",
    style: {
      display: 'grid',
      gridTemplateColumns: '320px minmax(0,1fr)',
      gap: '1rem',
      alignItems: 'start'
    }
  }, /*#__PURE__*/React.createElement("aside", {
    style: {
      position: 'sticky',
      top: 84,
      display: 'grid',
      gap: '0.5rem'
    }
  }, /*#__PURE__*/React.createElement("h2", {
    style: {
      fontSize: '0.875rem',
      fontWeight: 600,
      display: 'flex',
      alignItems: 'center',
      gap: 6
    }
  }, "In your library ", /*#__PURE__*/React.createElement("span", {
    style: {
      color: 'var(--muted-foreground)',
      fontWeight: 400
    }
  }, "\xB7 1 work")), /*#__PURE__*/React.createElement("div", {
    className: "ui-card",
    style: {
      padding: '0.875rem',
      display: 'grid',
      gap: '0.625rem'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gap: 2
    }
  }, /*#__PURE__*/React.createElement("h3", {
    style: {
      fontSize: '1rem',
      fontWeight: 600
    }
  }, work.title), /*#__PURE__*/React.createElement("p", {
    style: {
      color: 'var(--muted-foreground)',
      fontSize: 12
    }
  }, work.originalTitle, " \xB7 ", work.year, " \xB7 From ", work.source)), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      flexWrap: 'wrap',
      gap: 4
    }
  }, /*#__PURE__*/React.createElement(Badge, {
    variant: "secondary",
    icon: "CircleCheck"
  }, "Matched by IMDb id"), work.ids.map(x => /*#__PURE__*/React.createElement(Badge, {
    key: x,
    variant: "outline"
  }, x))), /*#__PURE__*/React.createElement("p", {
    style: {
      ...lbl,
      marginTop: 2
    }
  }, "Compare releases with"), /*#__PURE__*/React.createElement("ul", {
    style: {
      display: 'grid',
      gap: 6
    }
  }, copies.map(c => {
    const k = copyKeys(c.file);
    const on = base === c.i;
    return /*#__PURE__*/React.createElement("li", {
      key: c.i
    }, /*#__PURE__*/React.createElement("button", {
      type: "button",
      onClick: () => setBase(c.i),
      style: {
        width: '100%',
        textAlign: 'left',
        cursor: 'pointer',
        display: 'grid',
        gap: 4,
        padding: '0.5rem 0.625rem',
        borderRadius: 'var(--radius-md)',
        border: '1px solid ' + (on ? 'var(--primary)' : 'var(--border)'),
        background: on ? 'var(--tint-selected)' : 'var(--card)'
      }
    }, /*#__PURE__*/React.createElement("span", {
      style: {
        display: 'flex',
        justifyContent: 'space-between',
        gap: 8
      }
    }, /*#__PURE__*/React.createElement("strong", {
      style: {
        fontWeight: 600
      }
    }, c.label), /*#__PURE__*/React.createElement("span", {
      style: {
        color: 'var(--muted-foreground)',
        fontSize: 12
      }
    }, c.file.size)), /*#__PURE__*/React.createElement("span", {
      style: {
        display: 'flex',
        flexWrap: 'wrap',
        gap: 4
      }
    }, ['Resolution', 'Codec', 'HDR', 'Audio'].map(key => /*#__PURE__*/React.createElement(Chip, {
      key: key,
      unknown: !k[key]
    }, k[key] || (key === 'HDR' ? 'SDR' : key + ' unknown'))))));
  })), /*#__PURE__*/React.createElement("button", {
    type: "button",
    onClick: () => setExpanded(!expanded),
    style: {
      display: 'inline-flex',
      alignItems: 'center',
      gap: 4,
      color: 'var(--primary)',
      cursor: 'pointer',
      fontSize: 12,
      justifySelf: 'start'
    }
  }, /*#__PURE__*/React.createElement(Icon, {
    name: expanded ? 'ChevronUp' : 'ChevronDown',
    size: 14
  }), expanded ? 'Hide files' : 'Files and all tracks'), expanded ? /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gap: 4,
      borderTop: '1px solid var(--border)',
      paddingTop: 8
    }
  }, /*#__PURE__*/React.createElement("p", {
    className: "mono",
    style: {
      fontWeight: 500,
      overflowWrap: 'anywhere',
      fontSize: 12
    }
  }, copies[base].file.name), /*#__PURE__*/React.createElement("p", {
    className: "mono",
    style: {
      fontSize: 11,
      color: 'var(--muted-foreground)',
      overflowWrap: 'anywhere'
    }
  }, copies[base].file.folder), (copies[base].file.media.audio || []).map(a => /*#__PURE__*/React.createElement("p", {
    key: a,
    style: {
      fontSize: 12
    }
  }, a))) : null)), /*#__PURE__*/React.createElement("section", {
    style: {
      display: 'grid',
      gap: '0.5rem',
      minWidth: 0
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      flexWrap: 'wrap',
      alignItems: 'center',
      gap: '0.5rem 0.75rem'
    }
  }, /*#__PURE__*/React.createElement("h2", {
    style: {
      fontSize: '0.875rem',
      fontWeight: 600
    }
  }, "Available releases ", /*#__PURE__*/React.createElement("span", {
    style: {
      color: 'var(--muted-foreground)',
      fontWeight: 400
    }
  }, "\xB7 ", s.releases.length, " of 16")), /*#__PURE__*/React.createElement("span", {
    style: {
      display: 'flex',
      gap: 6,
      marginLeft: 'auto'
    }
  }, s.sources.map(x => /*#__PURE__*/React.createElement("span", {
    key: x.name,
    title: x.line,
    className: "ui-badge ui-badge-outline",
    style: {
      gap: 4,
      color: x.status === 'Succeeded' ? 'var(--foreground)' : 'var(--destructive)',
      borderColor: x.status === 'Succeeded' ? undefined : 'color-mix(in oklab, var(--destructive) 40%, transparent)'
    }
  }, /*#__PURE__*/React.createElement(Icon, {
    name: x.status === 'Succeeded' ? 'CircleCheck' : 'CircleAlert',
    size: 12,
    style: {
      color: x.status === 'Succeeded' ? 'var(--status-up)' : 'var(--destructive)'
    }
  }), x.name, " \xB7 ", x.line)))), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      flexWrap: 'wrap',
      alignItems: 'center',
      gap: '0.5rem'
    }
  }, /*#__PURE__*/React.createElement(Select, {
    options: ['All sources', 'Toloka'],
    style: {
      height: 32
    }
  }), /*#__PURE__*/React.createElement(Select, {
    options: ['All languages', 'Ukrainian', 'English'],
    style: {
      height: 32
    }
  }), /*#__PURE__*/React.createElement(Select, {
    options: ['All qualities', '2160p', '1080p', '720p'],
    style: {
      height: 32
    }
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      display: 'inline-flex',
      alignItems: 'center',
      gap: 6,
      color: 'var(--muted-foreground)'
    }
  }, "GiB ", /*#__PURE__*/React.createElement(Input, {
    placeholder: "From",
    style: {
      width: '4.5rem',
      height: 32
    }
  }), "\u2013", /*#__PURE__*/React.createElement(Input, {
    placeholder: "To",
    style: {
      width: '4.5rem',
      height: 32
    }
  })), /*#__PURE__*/React.createElement("span", {
    style: {
      marginLeft: 'auto',
      display: 'inline-flex',
      alignItems: 'center',
      gap: 6,
      color: 'var(--muted-foreground)'
    }
  }, "Sort ", /*#__PURE__*/React.createElement(Select, {
    options: ['Most seeders', 'Largest', 'Smallest', 'Newest', 'Best quality'],
    style: {
      height: 32
    }
  }))), /*#__PURE__*/React.createElement("p", {
    style: {
      display: 'flex',
      alignItems: 'center',
      gap: 6,
      fontSize: 12,
      color: 'var(--muted-foreground)'
    }
  }, /*#__PURE__*/React.createElement(Chip, {
    differs: true
  }, "2160p"), " claimed value that differs from your ", /*#__PURE__*/React.createElement("strong", {
    style: {
      color: 'var(--foreground)',
      fontWeight: 500
    }
  }, copies[base].label), " \xB7 ", /*#__PURE__*/React.createElement(Chip, {
    unknown: true
  }, "unknown"), " the title doesn't say"), /*#__PURE__*/React.createElement("div", {
    className: "ui-card",
    style: {
      overflow: 'hidden'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gridTemplateColumns: 'minmax(0,1fr) 5.5rem 4.5rem 6.5rem 6.5rem',
      gap: '0 0.75rem',
      padding: '0.5rem 0.875rem',
      fontSize: 12,
      fontWeight: 500,
      color: 'var(--muted-foreground)',
      borderBottom: '1px solid var(--border)',
      background: 'var(--muted)'
    }
  }, /*#__PURE__*/React.createElement("span", null, "Release \xB7 claimed"), /*#__PURE__*/React.createElement("span", {
    style: {
      textAlign: 'right'
    }
  }, "Size"), /*#__PURE__*/React.createElement("span", {
    style: {
      textAlign: 'right'
    }
  }, "Seeders"), /*#__PURE__*/React.createElement("span", null, "Published"), /*#__PURE__*/React.createElement("span", {
    style: {
      textAlign: 'right'
    }
  }, "Actions")), s.releases.map((r, idx) => {
    const k = P_KEYS(r.claimed);
    const sel = open && open.id === r.id;
    return /*#__PURE__*/React.createElement("div", {
      key: r.id,
      onClick: () => setOpen(r),
      style: {
        display: 'grid',
        gridTemplateColumns: 'minmax(0,1fr) 5.5rem 4.5rem 6.5rem 6.5rem',
        gap: '0 0.75rem',
        alignItems: 'center',
        padding: '0.625rem 0.875rem',
        borderTop: idx ? '1px solid var(--border)' : 0,
        cursor: 'pointer',
        background: sel ? 'var(--tint-selected)' : undefined
      }
    }, /*#__PURE__*/React.createElement("div", {
      style: {
        display: 'grid',
        gap: 5,
        minWidth: 0
      }
    }, /*#__PURE__*/React.createElement("span", {
      style: {
        display: 'flex',
        gap: 6,
        alignItems: 'baseline',
        minWidth: 0
      }
    }, /*#__PURE__*/React.createElement("span", {
      style: {
        fontWeight: 600,
        overflow: 'hidden',
        textOverflow: 'ellipsis',
        whiteSpace: 'nowrap'
      },
      title: r.title
    }, r.title)), /*#__PURE__*/React.createElement("span", {
      style: {
        display: 'flex',
        flexWrap: 'wrap',
        gap: 4,
        alignItems: 'center'
      }
    }, r.claimed.medium ? /*#__PURE__*/React.createElement(Chip, null, r.claimed.medium) : null, ['Resolution', 'Codec', 'HDR', 'Audio', 'Subtitles'].map(key => /*#__PURE__*/React.createElement(Chip, {
      key: key,
      unknown: !k[key],
      differs: k[key] && baseKeys[key] !== k[key]
    }, k[key] || (key === 'HDR' ? 'HDR unknown' : key + ' unknown'))), r.match ? /*#__PURE__*/React.createElement("span", {
      style: {
        display: 'inline-flex',
        alignItems: 'center',
        gap: 3,
        fontSize: 12,
        marginLeft: 4,
        color: r.match.evidence === 'TitleOnly' ? 'var(--foreground)' : 'var(--muted-foreground)'
      }
    }, /*#__PURE__*/React.createElement(Icon, {
      name: r.match.evidence === 'TitleOnly' ? 'CircleHelp' : 'LibraryBig',
      size: 12,
      style: {
        color: r.match.evidence === 'TitleOnly' ? 'var(--status-attention)' : undefined
      }
    }), r.match.evidence === 'TitleOnly' ? 'Possible match — check it' : r.match.evidence === 'Identifier' ? 'In library · by id' : 'In library · by title and year') : null)), /*#__PURE__*/React.createElement("span", {
      style: {
        textAlign: 'right',
        fontVariantNumeric: 'tabular-nums'
      }
    }, r.size || '—'), /*#__PURE__*/React.createElement("span", {
      style: {
        textAlign: 'right',
        fontVariantNumeric: 'tabular-nums',
        color: r.seeders == null ? 'var(--muted-foreground)' : undefined
      }
    }, r.seeders ?? 'Unknown'), /*#__PURE__*/React.createElement("span", {
      style: {
        color: 'var(--muted-foreground)',
        fontSize: 12
      }
    }, r.published || 'Unknown'), /*#__PURE__*/React.createElement("span", {
      style: {
        display: 'flex',
        justifyContent: 'flex-end',
        gap: 4
      },
      onClick: e => e.stopPropagation()
    }, /*#__PURE__*/React.createElement(Button, {
      variant: "ghost",
      size: "icon",
      icon: "Info",
      title: "Details",
      onClick: () => setOpen(r)
    }), /*#__PURE__*/React.createElement(Button, {
      variant: "ghost",
      size: "icon",
      icon: "ExternalLink",
      title: "Open source"
    }), /*#__PURE__*/React.createElement(Button, {
      variant: "outline",
      size: "icon",
      icon: "Download",
      title: "Sending a release to a download client comes in a later update.",
      disabled: true
    })));
  })), /*#__PURE__*/React.createElement(ListPager, {
    page: 1,
    pages: 2,
    pageSize: 10,
    total: 16
  }))), open ? /*#__PURE__*/React.createElement(ReleaseDrawer, {
    release: open,
    copy: copies[base],
    work: work,
    onClose: () => setOpen(null)
  }) : null);
}
function ReleaseDrawer({
  release,
  copy,
  work,
  onClose
}) {
  const {
    Button,
    Icon
  } = window.MAM_DS;
  const c = release.claimed,
    m = copy.file.media || {};
  const rows = [['Size', release.size || 'Unknown', copy.file.size], ['Resolution', c.resolution ? c.resolution + 'p' : 'Unknown', m.resolution], ['Made from', c.medium || 'Unknown', '—'], ['Video codec', c.codec || 'Unknown', m.codec], ['HDR', c.ranges.length ? c.ranges.join(', ') : 'Unknown', m.range], ['Audio', c.audio.length ? c.audio.join(', ') : 'Unknown', (m.audio || []).join('\n')], ['Subtitles', c.subs.length ? c.subs.join(', ') : 'Unknown', (m.subs || []).join('\n') || 'No subtitles']];
  return /*#__PURE__*/React.createElement(React.Fragment, null, /*#__PURE__*/React.createElement("div", {
    onClick: onClose,
    style: {
      position: 'fixed',
      inset: 0,
      background: 'rgb(0 0 0 / .25)',
      zIndex: 40
    }
  }), /*#__PURE__*/React.createElement("aside", {
    style: {
      position: 'fixed',
      top: 0,
      right: 0,
      bottom: 0,
      width: 'min(40rem, 92vw)',
      zIndex: 41,
      background: 'var(--card)',
      borderLeft: '1px solid var(--border)',
      boxShadow: 'var(--shadow-lg)',
      display: 'flex',
      flexDirection: 'column'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      padding: '1.25rem 1.5rem',
      borderBottom: '1px solid var(--border)',
      display: 'grid',
      gap: 6
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      gap: 12,
      alignItems: 'flex-start'
    }
  }, /*#__PURE__*/React.createElement("h2", {
    style: {
      fontSize: '1rem',
      fontWeight: 600,
      lineHeight: 1.35,
      overflowWrap: 'anywhere',
      flex: 1
    }
  }, release.title), /*#__PURE__*/React.createElement(Button, {
    variant: "ghost",
    size: "icon",
    icon: "X",
    title: "Close",
    onClick: onClose
  })), /*#__PURE__*/React.createElement("p", {
    style: {
      color: 'var(--muted-foreground)',
      fontSize: 12
    }
  }, release.source, " \xB7 ", release.seeders ?? 'Unknown', " seeders \xB7 ", release.peers ?? 'Unknown', " peers \xB7 Published ", release.published || 'unknown')), /*#__PURE__*/React.createElement("div", {
    style: {
      flex: 1,
      overflowY: 'auto',
      padding: '1rem 1.5rem',
      display: 'grid',
      alignContent: 'start',
      gap: '0.75rem'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gridTemplateColumns: '7rem 1fr 1fr',
      border: '1px solid var(--border)',
      borderRadius: 'var(--radius-lg)',
      overflow: 'hidden'
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      padding: '0.5rem 0.75rem',
      background: 'var(--muted)'
    }
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      padding: '0.5rem 0.75rem',
      background: 'var(--muted)',
      fontWeight: 600
    }
  }, "The listing claims"), /*#__PURE__*/React.createElement("span", {
    style: {
      padding: '0.5rem 0.75rem',
      background: 'var(--muted)',
      fontWeight: 600
    }
  }, "Your ", copy.label, " \xB7 verified"), rows.map(([k, a, b]) => /*#__PURE__*/React.createElement(React.Fragment, {
    key: k
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      padding: '0.5rem 0.75rem',
      borderTop: '1px solid var(--border)',
      color: 'var(--muted-foreground)'
    }
  }, k), /*#__PURE__*/React.createElement("span", {
    style: {
      padding: '0.5rem 0.75rem',
      borderTop: '1px solid var(--border)',
      color: a === 'Unknown' ? 'var(--muted-foreground)' : undefined
    }
  }, a), /*#__PURE__*/React.createElement("span", {
    style: {
      padding: '0.5rem 0.75rem',
      borderTop: '1px solid var(--border)',
      whiteSpace: 'pre-line'
    }
  }, b || 'Unknown')))), /*#__PURE__*/React.createElement("p", {
    style: {
      fontSize: 12,
      color: 'var(--muted-foreground)'
    }
  }, "The left column is read from the release title and isn't checked. The right column was read from your file by ffprobe.")), /*#__PURE__*/React.createElement("div", {
    style: {
      padding: '0.875rem 1.5rem',
      borderTop: '1px solid var(--border)',
      display: 'flex',
      gap: 8,
      justifyContent: 'flex-end'
    }
  }, /*#__PURE__*/React.createElement(Button, {
    variant: "outline",
    icon: "ExternalLink"
  }, "Open source"), /*#__PURE__*/React.createElement(Button, {
    icon: "Download",
    disabled: true,
    title: "Sending a release to a download client comes in a later update."
  }, "Download\u2026"))));
}
if (window.__MAM_KIT) Object.assign(window, {
  PSearch
});
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/proposal/PSearch.jsx", error: String((e && e.message) || e) }); }

// ui_kits/proposal/PSettings.jsx
try { (() => {
/* Proposed Integrations: rows grouped by the job each service does, test answers inline on the row. */
function PIntegrations() {
  const {
    Button,
    Badge,
    Icon,
    Input
  } = window.MAM_DS;
  const d = window.MAM_DATA;
  const [tested, setTested] = React.useState({});
  const groups = [['Media library', 'Read into the Library', 'LibraryBig'], ['Release source', 'Searched on the Search page', 'Search'], ['Download client', 'Receives downloads; its categories are the destinations', 'Download']];
  return /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gap: '1.25rem',
      maxWidth: '64rem'
    }
  }, /*#__PURE__*/React.createElement(PHeader, {
    title: "Integrations",
    lead: "The services this app works with. Credentials are stored encrypted and never shown again."
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      display: 'flex',
      gap: 8
    }
  }, /*#__PURE__*/React.createElement(Input, {
    type: "search",
    placeholder: "Search by name",
    style: {
      width: '14rem'
    }
  }), /*#__PURE__*/React.createElement(Button, {
    icon: "Plus"
  }, "Add integration"))), groups.map(([kind, hint, icon]) => /*#__PURE__*/React.createElement("section", {
    key: kind,
    style: {
      display: 'grid',
      gap: '0.5rem'
    }
  }, /*#__PURE__*/React.createElement("h2", {
    style: {
      display: 'flex',
      alignItems: 'center',
      gap: 6,
      fontSize: '0.875rem',
      fontWeight: 600
    }
  }, /*#__PURE__*/React.createElement(Icon, {
    name: icon,
    size: 16,
    style: {
      color: 'var(--muted-foreground)'
    }
  }), kind, /*#__PURE__*/React.createElement("span", {
    style: {
      fontWeight: 400,
      color: 'var(--muted-foreground)'
    }
  }, "\xB7 ", hint)), /*#__PURE__*/React.createElement("div", {
    className: "ui-card",
    style: {
      overflow: 'hidden'
    }
  }, d.integrations.filter(i => i.kind === kind).map((i, idx) => /*#__PURE__*/React.createElement("div", {
    key: i.name,
    style: {
      display: 'grid',
      gridTemplateColumns: 'minmax(0,1fr) minmax(0,1.3fr) auto',
      gap: '0.25rem 1rem',
      alignItems: 'center',
      padding: '0.75rem 0.875rem',
      borderTop: idx ? '1px solid var(--border)' : 0
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      display: 'grid',
      gap: 2
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      display: 'flex',
      gap: 6,
      alignItems: 'center'
    }
  }, /*#__PURE__*/React.createElement("strong", {
    style: {
      fontWeight: 500,
      color: i.enabled ? undefined : 'var(--muted-foreground)'
    }
  }, i.name), !i.enabled ? /*#__PURE__*/React.createElement(Badge, {
    variant: "outline"
  }, "Disabled") : null), /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: 12,
      color: 'var(--muted-foreground)'
    }
  }, i.service)), /*#__PURE__*/React.createElement("span", {
    style: {
      display: 'grid',
      gap: 2,
      minWidth: 0
    }
  }, /*#__PURE__*/React.createElement("span", {
    className: "mono",
    style: {
      fontSize: 12,
      color: 'var(--muted-foreground)',
      overflowWrap: 'anywhere'
    }
  }, i.url), tested[i.name] ? /*#__PURE__*/React.createElement("span", {
    className: "ui-status-line",
    "data-status": "ok",
    style: {
      fontSize: 12
    }
  }, /*#__PURE__*/React.createElement(Icon, {
    name: "CircleCheck",
    size: 12
  }), /*#__PURE__*/React.createElement("span", null, tested[i.name])) : null), /*#__PURE__*/React.createElement("span", {
    style: {
      display: 'flex',
      gap: 6
    }
  }, /*#__PURE__*/React.createElement(Button, {
    variant: "outline",
    size: "sm",
    icon: "PlugZap",
    onClick: () => setTested({
      ...tested,
      [i.name]: 'Connected just now.'
    })
  }, "Test"), /*#__PURE__*/React.createElement(Button, {
    variant: "outline",
    size: "sm",
    icon: "Pencil"
  }, "Edit"), /*#__PURE__*/React.createElement(Button, {
    variant: "danger",
    size: "icon",
    icon: "Trash2",
    title: "Remove"
  }))))))));
}
if (window.__MAM_KIT) Object.assign(window, {
  PIntegrations
});
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/proposal/PSettings.jsx", error: String((e && e.message) || e) }); }

// ui_kits/proposal/PShell.jsx
try { (() => {
/* Proposed shell: a left sidebar replaces the header bar + user menu. */
function SideItem({
  icon,
  label,
  active,
  onClick,
  badge
}) {
  const {
    Icon
  } = window.MAM_DS;
  return /*#__PURE__*/React.createElement("a", {
    href: "#",
    onClick: e => {
      e.preventDefault();
      onClick();
    },
    "aria-current": active ? 'page' : undefined,
    style: {
      display: 'flex',
      alignItems: 'center',
      gap: '0.5rem',
      padding: '0.4375rem 0.625rem',
      borderRadius: 'var(--radius-md)',
      fontSize: '0.875rem',
      fontWeight: 500,
      textDecoration: 'none',
      color: active ? 'var(--foreground)' : 'var(--muted-foreground)',
      background: active ? 'var(--tint-selected)' : 'transparent'
    }
  }, /*#__PURE__*/React.createElement(Icon, {
    name: icon,
    size: 16,
    style: {
      color: active ? 'var(--primary)' : undefined
    }
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      flex: 1
    }
  }, label), badge);
}
function Sidebar({
  page,
  onNav
}) {
  const {
    AppLogo,
    Icon,
    Button,
    ThemeSwitch
  } = window.MAM_DS;
  const d = window.MAM_DATA;
  const group = {
    fontSize: '0.75rem',
    fontWeight: 500,
    color: 'var(--subtle-foreground)',
    padding: '0 0.625rem',
    margin: '1rem 0 0.25rem'
  };
  return /*#__PURE__*/React.createElement("aside", {
    style: {
      width: 232,
      flexShrink: 0,
      position: 'sticky',
      top: 0,
      height: '100dvh',
      display: 'flex',
      flexDirection: 'column',
      background: 'var(--card)',
      borderRight: '1px solid var(--border)',
      padding: '0.875rem 0.75rem'
    }
  }, /*#__PURE__*/React.createElement("a", {
    href: "#",
    onClick: e => {
      e.preventDefault();
      onNav('search');
    },
    style: {
      display: 'flex',
      alignItems: 'center',
      gap: '0.5rem',
      padding: '0.25rem 0.375rem 0.75rem',
      textDecoration: 'none',
      color: 'var(--foreground)'
    }
  }, /*#__PURE__*/React.createElement(AppLogo, {
    size: 24
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: '0.875rem',
      fontWeight: 600,
      lineHeight: 1.2
    }
  }, "Media Acquisition", /*#__PURE__*/React.createElement("br", null), "Manager")), /*#__PURE__*/React.createElement("nav", {
    style: {
      display: 'grid',
      gap: 2
    }
  }, /*#__PURE__*/React.createElement(SideItem, {
    icon: "Search",
    label: "Search",
    active: page === 'search',
    onClick: () => onNav('search')
  }), /*#__PURE__*/React.createElement(SideItem, {
    icon: "LibraryBig",
    label: "Library",
    active: page === 'library',
    onClick: () => onNav('library')
  }), /*#__PURE__*/React.createElement("p", {
    style: group
  }, "Settings"), /*#__PURE__*/React.createElement(SideItem, {
    icon: "Plug",
    label: "Integrations",
    active: page === 'integrations',
    onClick: () => onNav('integrations')
  }), /*#__PURE__*/React.createElement(SideItem, {
    icon: "FolderInput",
    label: "Destinations",
    active: page === 'destinations',
    onClick: () => onNav('destinations')
  }), /*#__PURE__*/React.createElement(SideItem, {
    icon: "FolderOpen",
    label: "Media folders",
    active: page === 'folders',
    onClick: () => onNav('integrations')
  }), /*#__PURE__*/React.createElement(SideItem, {
    icon: "Users",
    label: "Users",
    active: page === 'users',
    onClick: () => onNav('integrations')
  })), /*#__PURE__*/React.createElement("div", {
    style: {
      marginTop: 'auto',
      display: 'grid',
      gap: '0.5rem',
      borderTop: '1px solid var(--border)',
      paddingTop: '0.75rem'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gap: 4,
      padding: '0 0.25rem'
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: '0.75rem',
      color: 'var(--subtle-foreground)',
      padding: '0 0.375rem'
    }
  }, "Theme"), /*#__PURE__*/React.createElement(ThemeSwitch, null)), /*#__PURE__*/React.createElement("p", {
    style: {
      display: 'flex',
      alignItems: 'center',
      gap: '0.5rem',
      padding: '0 0.625rem',
      fontSize: '0.75rem',
      color: 'var(--muted-foreground)'
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      width: 8,
      height: 8,
      borderRadius: 9999,
      background: 'var(--status-up)'
    }
  }), "Ready", /*#__PURE__*/React.createElement("span", {
    className: "mono",
    style: {
      marginLeft: 'auto'
    }
  }, "v", d.version)), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      alignItems: 'center',
      gap: '0.5rem',
      padding: '0.25rem 0.25rem 0 0.625rem'
    }
  }, /*#__PURE__*/React.createElement(Icon, {
    name: "CircleUser",
    size: 16
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      flex: 1,
      fontWeight: 500,
      overflow: 'hidden',
      textOverflow: 'ellipsis'
    }
  }, d.user.userName), /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: '0.75rem',
      color: 'var(--muted-foreground)'
    }
  }, "Owner"), /*#__PURE__*/React.createElement(Button, {
    variant: "ghost",
    size: "icon",
    icon: "LogOut",
    title: "Sign out"
  }))));
}
function PHeader({
  title,
  lead,
  children
}) {
  return /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      flexWrap: 'wrap',
      alignItems: 'flex-end',
      justifyContent: 'space-between',
      gap: '0.75rem 1.5rem'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gap: '0.25rem',
      minWidth: 0,
      maxWidth: '44rem'
    }
  }, /*#__PURE__*/React.createElement("h1", {
    style: {
      fontSize: 'var(--text-2xl)',
      fontWeight: 600,
      lineHeight: 1.25
    }
  }, title), lead ? /*#__PURE__*/React.createElement("p", {
    style: {
      fontSize: '0.875rem',
      color: 'var(--muted-foreground)',
      textWrap: 'pretty'
    }
  }, lead) : null), children);
}
if (window.__MAM_KIT) Object.assign(window, {
  Sidebar,
  PHeader
});
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/proposal/PShell.jsx", error: String((e && e.message) || e) }); }

__ds_ns.AppLogo = __ds_scope.AppLogo;

__ds_ns.Badge = __ds_scope.Badge;

__ds_ns.Banner = __ds_scope.Banner;

__ds_ns.StatusLine = __ds_scope.StatusLine;

__ds_ns.Button = __ds_scope.Button;

__ds_ns.Card = __ds_scope.Card;

__ds_ns.Dialog = __ds_scope.Dialog;

__ds_ns.ErrorState = __ds_scope.ErrorState;

__ds_ns.Icon = __ds_scope.Icon;

__ds_ns.Input = __ds_scope.Input;

__ds_ns.Field = __ds_scope.Field;

__ds_ns.ListPager = __ds_scope.ListPager;

__ds_ns.Menu = __ds_scope.Menu;

__ds_ns.Select = __ds_scope.Select;

__ds_ns.ThemeSwitch = __ds_scope.ThemeSwitch;

__ds_ns.ChipRow = __ds_scope.ChipRow;

__ds_ns.Facts = __ds_scope.Facts;

__ds_ns.MatchBadges = __ds_scope.MatchBadges;

__ds_ns.ReleaseRow = __ds_scope.ReleaseRow;

__ds_ns.WorkCard = __ds_scope.WorkCard;

})();
