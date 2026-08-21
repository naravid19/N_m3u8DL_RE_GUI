/**
 * N_m3u8DL-RE Companion — Popup Logic
 */

import { getTabStreams, getRecentStreams, sweepOrphanTabs, clearTab } from '../lib/storage.js';

let activeTabId = null;
let currentView = 'current'; // 'current' | 'all'
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

function toCurl(stream) {
  const q = (s) => `'${String(s).replace(/'/g, `'\\''`)}'`;
  const parts = [`curl ${q(stream.url)}`];

  if (stream.referer)   parts.push(`-H ${q('Referer: ' + stream.referer)}`);
  if (stream.userAgent) parts.push(`-H ${q('User-Agent: ' + stream.userAgent)}`);
  if (stream.cookie)    parts.push(`-H ${q('Cookie: ' + stream.cookie)}`);
  if (stream.origin)    parts.push(`-H ${q('Origin: ' + stream.origin)}`);

  return parts.join(' \\\n  ');
}

function formatRelativeTime(ts) {
  if (!ts) return '';
  const sec = Math.floor((Date.now() - ts) / 1000);
  if (sec < 10) return 'just now';
  if (sec < 60) return `${sec}s ago`;
  const min = Math.floor(sec / 60);
  if (min < 60) return `${min}m ago`;
  return `${Math.floor(min / 60)}h ago`;
}

async function renderStreams() {
  const countBadge = document.getElementById('stream-count');
  const emptyState = document.getElementById('empty-state');
  const streamList = document.getElementById('stream-list');

  let streams = [];

  if (currentView === 'current' && activeTabId) {
    streams = await getTabStreams(activeTabId);
    // If current tab is empty, but we have global recent streams, hint or show count
    if (streams.length === 0) {
      const recent = await getRecentStreams();
      if (recent.length > 0) {
        countBadge.textContent = '0';
        emptyState.querySelector('.empty-hint').innerHTML =
          `No stream on this tab yet. Found <strong>${recent.length}</strong> stream(s) on other tabs. Click <strong>"All Recent"</strong> above.`;
      }
    }
  } else {
    streams = await getRecentStreams();
  }

  countBadge.textContent = String(streams.length);

  if (streams.length === 0) {
    emptyState.style.display = 'block';
    streamList.style.display = 'none';
    return;
  }

  emptyState.style.display = 'none';
  streamList.style.display = 'flex';
  streamList.innerHTML = '';

  // Sort: Manifests (HLS, DASH, Abyss) first then Media
  const sorted = [...streams].sort((a, b) => {
    const aRank = (a.kind === 'HLS' || a.kind === 'DASH' || a.kind === 'Abyss') ? 0 : 1;
    const bRank = (b.kind === 'HLS' || b.kind === 'DASH' || b.kind === 'Abyss') ? 0 : 1;
    return aRank - bRank;
  });

  for (const item of sorted) {
    const card = document.createElement('div');
    card.className = 'stream-card';

    const meta = document.createElement('div');
    meta.className = 'stream-meta';

    const kindSpan = document.createElement('span');
    kindSpan.className = 'stream-kind';
    if (item.kind === 'Abyss') {
      kindSpan.classList.add('kind-abyss');
      kindSpan.textContent = '🎬 Abyss / Hydrax';
    } else {
      kindSpan.textContent = item.kind;
    }
    meta.appendChild(kindSpan);

    if (item.timestamp) {
      const timeSpan = document.createElement('span');
      timeSpan.className = 'stream-time';
      timeSpan.textContent = formatRelativeTime(item.timestamp);
      meta.appendChild(timeSpan);
    }

    const urlDiv = document.createElement('div');
    urlDiv.className = 'stream-url';
    urlDiv.textContent = item.url;
    urlDiv.title = item.url;

    const actions = document.createElement('div');
    actions.className = 'actions';

    const copyCurlBtn = document.createElement('button');
    copyCurlBtn.className = 'btn';
    copyCurlBtn.innerHTML = '📋 Copy as cURL';
    copyCurlBtn.addEventListener('click', async () => {
      const curlCmd = toCurl(item);
      await navigator.clipboard.writeText(curlCmd);
      showToast('Copied cURL! Switch to GUI & click "Paste from browser"');
    });

    const copyUrlBtn = document.createElement('button');
    copyUrlBtn.className = 'btn btn-secondary';
    copyUrlBtn.textContent = 'Copy URL';
    copyUrlBtn.addEventListener('click', async () => {
      await navigator.clipboard.writeText(item.url);
      showToast('Copied raw URL to clipboard');
    });

    actions.appendChild(copyCurlBtn);
    actions.appendChild(copyUrlBtn);

    card.appendChild(meta);
    card.appendChild(urlDiv);
    card.appendChild(actions);
    streamList.appendChild(card);
  }
}

async function sweepOnOpen() {
  try {
    const tabs = await chrome.tabs.query({});
    const removed = await sweepOrphanTabs(tabs.map((t) => t.id));
    if (removed > 0) {
      console.debug(`[N_m3u8DL-RE] Cleared ${removed} orphaned tab entries.`);
    }
  } catch (err) {
    // A sweep failure must never stop the list from rendering.
    console.debug('[N_m3u8DL-RE] Sweep skipped:', err);
  }
}

async function init() {
  try {
    const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
    if (tab && tab.id) {
      activeTabId = tab.id;
    }
  } catch (err) {
    console.error('Could not get active tab', err);
  }

  // Tab switcher
  const tabCurrent = document.getElementById('tab-current');
  const tabAll = document.getElementById('tab-all');

  tabCurrent.addEventListener('click', () => {
    currentView = 'current';
    tabCurrent.classList.add('active');
    tabAll.classList.remove('active');
    renderStreams();
  });

  tabAll.addEventListener('click', () => {
    currentView = 'all';
    tabAll.classList.add('active');
    tabCurrent.classList.remove('active');
    renderStreams();
  });

  // Action buttons
  document.getElementById('btn-refresh').addEventListener('click', () => {
    renderStreams();
    showToast('Refreshed stream list');
  });

  document.getElementById('btn-clear').addEventListener('click', async () => {
    if (activeTabId) {
      await clearTab(activeTabId);
    }
    await chrome.storage.session.remove('recent_streams');
    renderStreams();
    showToast('Cleared stream list');
  });

  // Live storage change listener: auto-update popup if streams detected in real time!
  chrome.storage.onChanged.addListener(() => {
    renderStreams();
  });

  // Render immediately for fast UI
  renderStreams();

  // Sweep orphaned tab keys in background after first render
  sweepOnOpen();
}

document.addEventListener('DOMContentLoaded', init);
