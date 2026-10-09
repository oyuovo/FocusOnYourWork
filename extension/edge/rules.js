(function (root) {
  'use strict';
  function canonicalUrl(raw) {
    try {
      const url = new URL(raw);
      return url.protocol === 'https:' || url.protocol === 'http:' ? url.href : null;
    } catch { return null; }
  }
  function message(type, instance, win, tab) {
    return {
      Type: type, Instance: instance, TabId: tab?.id ?? -1,
      Url: canonicalUrl(tab?.url) ?? '',
      Title: type === 'select' ? String(tab?.title ?? '').slice(0, 120) : '',
      Focused: !!win?.focused && win.state !== 'minimized' && tab?.active === true
    };
  }
  const api = { canonicalUrl, message };
  if (typeof module !== 'undefined') module.exports = api;
  else root.FocusRules = api;
})(globalThis);
