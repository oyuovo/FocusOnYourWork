const test = require('node:test');
const assert = require('node:assert/strict');
const rules = require('../extension/edge/rules.js');

test('only HTTP(S) can be selected', () => {
  assert.equal(rules.canonicalUrl('edge://extensions'), null);
  assert.equal(rules.canonicalUrl('file:///private.txt'), null);
  assert.equal(rules.canonicalUrl('not a URL'), null);
  assert.equal(rules.canonicalUrl('https://example.com'), 'https://example.com/');
});
test('URL identity preserves query and hash routes', () => {
  assert.notEqual(rules.canonicalUrl('https://example.com/?q=a'), rules.canonicalUrl('https://example.com/?q=b'));
  assert.notEqual(rules.canonicalUrl('https://example.com/#article'), rules.canonicalUrl('https://example.com/#video'));
});
test('status requires active tab and focused visible window', () => {
  const win = { focused: true, state: 'normal' };
  const tab = { id: 7, active: true, url: 'https://example.com/read', title: 'Article' };
  assert.equal(rules.message('status', 'instance', win, tab).Focused, true);
  assert.equal(rules.message('status', 'instance', { ...win, focused: false }, tab).Focused, false);
  assert.equal(rules.message('status', 'instance', { ...win, state: 'minimized' }, tab).Focused, false);
  assert.equal(rules.message('status', 'instance', win, { ...tab, active: false }).Focused, false);
});
test('status never sends page title; selection is bounded', () => {
  const tab = { id: 1, active: true, url: 'https://example.com', title: 'x'.repeat(200) };
  assert.equal(rules.message('status', 'i', { focused: true }, tab).Title, '');
  assert.equal(rules.message('select', 'i', { focused: true }, tab).Title.length, 120);
});
test('missing tabs produce a safe non-target status', () => {
  const msg = rules.message('status', 'i', null, undefined);
  assert.equal(msg.TabId, -1);
  assert.equal(msg.Focused, false);
  assert.equal(msg.Url, '');
});
test('manifest grants no page content or network host permissions', () => {
  const manifest = require('../extension/edge/manifest.json');
  assert.equal(manifest.manifest_version, 3);
  assert.deepEqual(manifest.permissions.sort(), ['nativeMessaging', 'tabs']);
  assert.equal(manifest.host_permissions, undefined);
  assert.equal(manifest.content_scripts, undefined);
});
