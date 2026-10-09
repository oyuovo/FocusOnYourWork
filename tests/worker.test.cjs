const test = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');
const rules = require('../extension/edge/rules.js');
const source = fs.readFileSync(path.join(__dirname, '../extension/edge/background.js'), 'utf8');
function event() {
  const listeners = [];
  return { listeners, addListener(fn) { listeners.push(fn); }, emit(...args) { for (const fn of listeners) fn(...args); } };
}
async function drain() { for (let i = 0; i < 6; i++) await new Promise(resolve => setImmediate(resolve)); }
function harness() {
  const h = { messages: [], ports: [], queries: 0, hostTracking: false, dropReplies: false,
    win: { id: 5, focused: true, state: 'normal' }, tab: { id: 9, active: true, url: 'https://example.com/read', title: '文章' }, timers: new Map() };
  let timerId = 0;
  const chrome = {
    runtime: { onMessage: event(), lastError: undefined, connectNative(name) {
      assert.equal(name, 'cn.focusisland.desktop');
      const port = { onMessage: event(), onDisconnect: event(), disconnected: false,
        postMessage(msg) {
          h.messages.push(JSON.parse(JSON.stringify(msg)));
          if (!h.dropReplies) queueMicrotask(() => port.onMessage.emit({ Ok: true, Tracking: h.hostTracking, Selected: msg.Type === 'select', Message: 'connected' }));
        },
        disconnect() { port.disconnected = true; port.onDisconnect.emit(); }
      };
      h.ports.push(port); return port;
    } },
    windows: { onFocusChanged: event(), async getLastFocused() { return { ...h.win }; } },
    tabs: { onActivated: event(), onUpdated: event(), onRemoved: event(), async query(options) {
      h.queries++; assert.equal(options.windowId, h.win.id); return [{ ...h.tab }];
    } }
  };
  h.chrome = chrome;
  const context = vm.createContext({ chrome, crypto: { randomUUID: () => 'worker-instance' }, URL,
    FocusRules: rules, importScripts(name) { assert.equal(name, 'rules.js'); },
    setTimeout(fn, delay) { const id = ++timerId; h.timers.set(id, { fn, delay }); return id; },
    clearTimeout(id) { h.timers.delete(id); } });
  vm.runInContext(source, context, { filename: 'background.js' });
  h.send = action => new Promise(resolve => {
    assert.equal(chrome.runtime.onMessage.listeners[0]({ action }, {}, resolve), true);
  });
  h.runTimer = async delay => {
    const entry = [...h.timers].find(([, timer]) => timer.delay === delay);
    assert.ok(entry, `expected a ${delay} ms timer`);
    h.timers.delete(entry[0]); entry[1].fn(); await drain();
  };
  return h;
}
test('idle connection sends pings without reading browsing state', async () => {
  const h = harness(); await h.send('status'); await drain();
  assert.ok(h.messages.length >= 1);
  assert.equal(h.queries, 0);
  assert.ok(h.messages.every(msg => msg.Type === 'ping' && !('Url' in msg)));
});
test('page selection carries Unicode title and exact URL', async () => {
  const h = harness(); h.tab.url = 'https://example.com/read?q=中文#chapter';
  const reply = await h.send('select'); await drain();
  assert.equal(reply.Ok, true);
  const selected = h.messages.find(msg => msg.Type === 'select');
  assert.equal(selected.TabId, 9); assert.equal(selected.Title, '文章');
  assert.equal(selected.Url, rules.canonicalUrl(h.tab.url));
});
test('restricted page selection never reaches native host', async () => {
  const h = harness(); h.tab.url = 'edge://extensions';
  const reply = await h.send('select');
  assert.equal(reply.Ok, false); assert.equal(h.ports.length, 0);
});
test('starting a desktop session switches from idle to fresh status', async () => {
  const h = harness(); await h.send('status'); await drain(); h.hostTracking = true;
  await h.runTimer(3000);
  assert.equal(h.messages.at(-1).Type, 'status');
  assert.equal(h.messages.at(-1).Focused, true);
  assert.ok([...h.timers.values()].some(timer => timer.delay === 750));
});
test('active tab changes are forwarded during tracking', async () => {
  const h = harness(); h.hostTracking = true; await h.send('status'); await drain();
  h.tab = { ...h.tab, id: 10, url: 'https://example.com/other' };
  h.chrome.tabs.onActivated.emit({ tabId: 10, windowId: 5 }); await drain();
  assert.equal(h.messages.at(-1).TabId, 10);
  assert.equal(h.messages.at(-1).Title, '');
});
test('ending a desktop session stops page reads', async () => {
  const h = harness(); h.hostTracking = true; await h.send('status'); await drain();
  h.hostTracking = false; await h.runTimer(750); const queries = h.queries;
  await h.runTimer(3000);
  assert.equal(h.queries, queries); assert.equal(h.messages.at(-1).Type, 'ping');
});
test('disconnect stops background reconnection loop', async () => {
  const h = harness(); await h.send('status'); await drain();
  h.chrome.runtime.lastError = { message: 'host disconnected' }; h.ports[0].disconnect(); await drain();
  assert.equal(h.timers.size, 0); assert.equal(h.ports.length, 1);
  await h.send('status'); await drain(); assert.equal(h.ports.length, 2);
});
test('late disconnect from an old port does not tear down the new connection', async () => {
  const h = harness(); await h.send('status'); await drain();
  const old = h.ports[0]; old.disconnect(); await h.send('status'); await drain();
  old.onDisconnect.emit(); await drain();
  await h.runTimer(3000);
  assert.equal(h.ports.length, 2); assert.equal(h.ports[1].disconnected, false);
});
test('native timeout resolves popup instead of hanging forever', async () => {
  const h = harness(); h.dropReplies = true;
  const replyPromise = h.send('status'); await drain(); await h.runTimer(4500);
  const reply = await replyPromise;
  assert.equal(reply.Ok, false); assert.match(reply.Message, /超时/);
});
