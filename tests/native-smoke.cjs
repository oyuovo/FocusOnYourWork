// Run against the desktop with FOCUS_ISLAND_DATA_DIR set to an isolated test directory.
const { spawn } = require('node:child_process');
const assert = require('node:assert/strict');
const path = require('node:path');
const exe = path.resolve(__dirname, '../artifacts/app/FocusOnYourWork.NativeHost.exe');
const child = spawn(exe, [], { stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true });
let buffer = Buffer.alloc(0);
let waiting;
child.stdout.on('data', chunk => {
  buffer = Buffer.concat([buffer, chunk]);
  while (buffer.length >= 4) {
    const size = buffer.readUInt32LE(0);
    if (buffer.length < 4 + size) return;
    const message = JSON.parse(buffer.subarray(4, 4 + size).toString('utf8'));
    buffer = buffer.subarray(4 + size);
    const resolve = waiting; waiting = null;
    if (resolve) resolve(message);
  }
});
function request(message) {
  return new Promise((resolve, reject) => {
    const timeout = setTimeout(() => reject(new Error('Native reply timed out')), 6000);
    waiting = value => { clearTimeout(timeout); resolve(value); };
    const body = Buffer.from(JSON.stringify(message), 'utf8');
    const header = Buffer.alloc(4); header.writeUInt32LE(body.length);
    child.stdin.write(Buffer.concat([header, body]));
  });
}
(async () => {
  try {
    const ping = await request({ Type: 'ping', Instance: 'native-smoke' });
    assert.equal(ping.Ok, true, ping.Message);
    const invalid = await request({ Type: 'select', Instance: 'native-smoke', TabId: 2, Url: 'edge://settings', Title: '受限页面', Focused: true });
    assert.equal(invalid.Ok, false);
    const selected = await request({ Type: 'select', Instance: 'native-smoke', TabId: 2, Url: 'https://example.com/read?q=中文', Title: '阅读测试 · 中文', Focused: true });
    assert.equal(selected.Ok, true, selected.Message);
    assert.equal(selected.Selected, true);
    const status = await request({ Type: 'status', Instance: 'native-smoke', TabId: 2, Url: 'https://example.com/read?q=中文', Focused: false });
    assert.equal(status.Ok, true);
    console.log('PASS native stdin/stdout framing -> current-user pipe -> real desktop -> reply (4 assertions)');
  } finally { child.stdin.end(); }
})().catch(error => { console.error(error); process.exitCode = 1; child.kill(); });
