/**
 * N_m3u8DL-RE Companion — Popup logic
 */

function toCurl(stream) {
  const q = (s) => `'${String(s).replace(/'/g, `'\\''`)}'`;
  const parts = [`curl ${q(stream.url)}`];
  if (stream.referer)   parts.push(`-H ${q('Referer: ' + stream.referer)}`);
  if (stream.userAgent) parts.push(`-H ${q('User-Agent: ' + stream.userAgent)}`);
  if (stream.cookie)    parts.push(`-H ${q('Cookie: ' + stream.cookie)}`);
  return parts.join(' \\\n  ');
}

let toastTimer = null;
function showToast(message) {
  const toast = document.getElementById('toast');
  if (!toast) return;
  toast.textContent = message;
  toast.style.display = 'block';

  if (toastTimer) clearTimeout(toastTimer);
  toastTimer = setTimeout(() => {
    toast.style.display = 'none';
  }, 3500);
}

async function initPopup() {
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  if (!tab || tab.id === undefined) return;

  const key = `streams:${tab.id}`;
  const stored = await chrome.storage.session.get(key);
  const streams = stored[key] || [];

  const countBadge = document.getElementById('stream-count');
  const emptyState = document.getElementById('empty-state');
  const streamList = document.getElementById('stream-list');

  countBadge.textContent = String(streams.length);

  if (streams.length === 0) {
    emptyState.style.display = 'block';
    streamList.style.display = 'none';
    return;
  }

  emptyState.style.display = 'none';
  streamList.style.display = 'flex';
  streamList.innerHTML = '';

  // Sort manifests before media
  const sorted = [...streams].sort((a, b) => {
    const aRank = a.kind === 'Media' ? 1 : 0;
    const bRank = b.kind === 'Media' ? 1 : 0;
    return aRank - bRank;
  });

  for (const item of sorted) {
    const card = document.createElement('div');
    card.className = 'stream-card';

    const meta = document.createElement('div');
    meta.className = 'stream-meta';

    const kindSpan = document.createElement('span');
    kindSpan.className = 'stream-kind';
    kindSpan.textContent = item.kind;
    meta.appendChild(kindSpan);

    const urlDiv = document.createElement('div');
    urlDiv.className = 'stream-url';
    urlDiv.textContent = item.url;
    urlDiv.title = item.url;

    const actions = document.createElement('div');
    actions.className = 'actions';

    const copyCurlBtn = document.createElement('button');
    copyCurlBtn.className = 'btn';
    copyCurlBtn.textContent = '📋 Copy as cURL';
    copyCurlBtn.addEventListener('click', async () => {
      const curlCmd = toCurl(item);
      await navigator.clipboard.writeText(curlCmd);
      showToast('Copied cURL! Open GUI & click "Paste from browser"');
    });

    const copyUrlBtn = document.createElement('button');
    copyUrlBtn.className = 'btn btn-secondary';
    copyUrlBtn.textContent = 'Copy URL';
    copyUrlBtn.addEventListener('click', async () => {
      await navigator.clipboard.writeText(item.url);
      showToast('Copied URL to clipboard');
    });

    actions.appendChild(copyCurlBtn);
    actions.appendChild(copyUrlBtn);

    card.appendChild(meta);
    card.appendChild(urlDiv);
    card.appendChild(actions);
    streamList.appendChild(card);
  }
}

document.addEventListener('DOMContentLoaded', initPopup);
