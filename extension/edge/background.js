'use strict';
importScripts('rules.js');
const hostName = 'cn.focusisland.desktop';
const instance = crypto.randomUUID();
let port = null;
let pending = null;
let sequence = Promise.resolve();
let tracking = false;
let sampling = false;
let wake = null;

function connect() {
  if (port) return;
  const opened = chrome.runtime.connectNative(hostName);
  port = opened;
  opened.onMessage.addListener(reply => {
    if (port !== opened) return;
    tracking = reply.Tracking === true;
    const resolve = pending; pending = null;
    if (resolve) resolve(reply);
  });
  opened.onDisconnect.addListener(() => {
    const error = chrome.runtime.lastError;
    if (port !== opened) return;
    port = null; tracking = false;
    const lastReply = { Ok: false, Message: error ? '连接不可用。请确认已运行注册脚本，并打开专注宗门。' : '连接已断开，请重新选择页面。' };
    const resolve = pending; pending = null;
    if (resolve) resolve(lastReply);
    // User opens the popup to reconnect; do not respawn a missing host in a loop.
    clearTimeout(wake);
  });
}
function exchange(message) {
  const job = sequence.then(() => new Promise(resolve => {
    try {
      connect();
      const timeout = setTimeout(() => {
        const old = port; port = null; tracking = false;
        pending = null;
        if (old) old.disconnect();
        resolve({ Ok: false, Message: '连接超时，请重新打开扩展重试。' });
      }, 4500);
      pending = reply => { clearTimeout(timeout); resolve(reply); };
      port.postMessage(message);
    } catch {
      const done = pending; pending = null;
      const old = port; port = null; tracking = false;
      if (old) old.disconnect();
      if (done) done({ Ok: false, Message: '无法连接，请先完成 Edge 注册步骤。' });
      else resolve({ Ok: false, Message: '无法连接本地客户端。' });
    }
  }));
  sequence = job.catch(() => {});
  return job;
}
async function snapshot(type) {
  const win = await chrome.windows.getLastFocused();
  const tabs = await chrome.tabs.query({ active: true, windowId: win.id });
  return FocusRules.message(type, instance, win, tabs[0]);
}
async function pulse() {
  if (sampling) return;
  sampling = true; clearTimeout(wake);
  try {
    const wasTracking = tracking;
    const message = tracking ? await snapshot('status') : { Type: 'ping', Instance: instance };
    const reply = await exchange(message);
    if (!wasTracking && reply.Tracking) await exchange(await snapshot('status'));
  } catch {
    if (port) await exchange({ Type: 'status', Instance: instance, Error: 'snapshot-unavailable' });
  } finally {
    sampling = false;
    if (port) wake = setTimeout(pulse, tracking ? 750 : 3000);
  }
}
function changed() { if (tracking) void pulse(); }
chrome.tabs.onActivated.addListener(changed);
chrome.tabs.onUpdated.addListener((_id, change) => { if (change.url || change.status === 'complete') changed(); });
chrome.tabs.onRemoved.addListener(changed);
chrome.windows.onFocusChanged.addListener(changed);
chrome.runtime.onMessage.addListener((message, _sender, reply) => {
  if (message.action === 'select') {
    (async () => {
      const page = await snapshot('select');
      if (!page.Url) return { Ok: false, Message: '请打开普通 HTTP/HTTPS 网页后再选择。' };
      const result = await exchange(page);
      void pulse();
      return result.Ok ? { ...result, Message: '页面已选好。回到专注宗门开始计时。' } : result;
    })().then(reply).catch(() => reply({ Ok: false, Message: '无法读取当前标签，请稍后重试。' }));
    return true;
  }
  if (message.action === 'status') {
    exchange({ Type: 'ping', Instance: instance }).then(value => { reply(value); void pulse(); });
    return true;
  }
  return false;
});
