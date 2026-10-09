'use strict';
const status = document.getElementById('status');
const select = document.getElementById('select');
async function request(action) {
  try {
    const reply = await chrome.runtime.sendMessage({ action });
    status.textContent = reply?.Message || '请先打开专注岛桌面客户端。';
  } catch { status.textContent = '连接失败。请确认扩展已注册，并重试。'; }
}
select.addEventListener('click', async () => {
  select.disabled = true;
  await request('select');
  select.disabled = false;
});
void request('status');
