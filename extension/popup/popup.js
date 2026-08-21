/**
 * N_m3u8DL-RE Companion — Popup Logic
 */

import { getTabStreams, getRecentStreams, sweepOrphanTabs, clearTab } from '../lib/storage.js';
import { formatBytes, formatRelativeTime, elideUrl, describeStream } from '../lib/format.js';
import { toCurl } from '../lib/curl.js';

let activeTabId = null;
let currentView = 'current'; // 'current' | 'all'
let toastTimer = null;
let renderTimer = null;
let renderGeneration = 0;
let filterQuery = '';

const KIND_RANK = { HLS: 0, DASH: 0, MSS: 0, Abyss: 1, Media: 2 };

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

async function copyWithFeedback(button, text, originalLabel, successMessage) {
  try {
    await navigator.clipboard.writeText(text);
  } catch (err) {
    showToast('Could not write to clipboard. Select the URL and copy it manually.');
    console.debug('[N_m3u8DL-RE] Clipboard write failed:', err);
    return;
  }

  button.textContent = '✓ Copied';
  button.classList.add('is-copied');
  setTimeout(() => {
    button.textContent = originalLabel;
    button.classList.remove('is-copied');
  }, 1500);

  showToast(successMessage);
}

async function loadStreams() {
  if (currentView === 'current' && activeTabId) {
    const currentStreams = await getTabStreams(activeTabId);
    let otherCount = 0;
    if (currentStreams.length === 0) {
      const recent = await getRecentStreams();
      otherCount = recent.length;
    }
    return { streams: currentStreams, otherCount };
  } else {
    const recent = await getRecentStreams();
    return { streams: recent, otherCount: 0 };
  }
}

function getKindClass(kind) {
  switch ((kind || '').toLowerCase()) {
    case 'hls': return 'kind-hls';
    case 'dash': return 'kind-dash';
    case 'mss': return 'kind-mss';
    case 'abyss': return 'kind-abyss';
    default: return 'kind-media';
  }
}

function paint({ streams, otherCount }) {
  const countBadge = document.getElementById('stream-count');
  const emptyState = document.getElementById('empty-state');
  const streamList = document.getElementById('stream-list');
  const filterBar = document.getElementById('filter-bar');
  const hintDefault = document.getElementById('hint-default');
  const hintOtherTabs = document.getElementById('hint-other-tabs');
  const otherTabCount = document.getElementById('other-tab-count');

  countBadge.textContent = String(streams.length);

  // Toggle filter bar visibility based on unfiltered item count
  if (streams.length >= 5) {
    filterBar.hidden = false;
  } else {
    filterBar.hidden = true;
    if (!filterQuery) {
      document.getElementById('stream-filter').value = '';
    }
  }

  // Filter streams by search query if set
  let displayed = streams;
  if (filterQuery) {
    const q = filterQuery.toLowerCase();
    displayed = streams.filter((s) => (s.url && s.url.toLowerCase().includes(q)) || (s.kind && s.kind.toLowerCase().includes(q)));
  }

  if (streams.length === 0) {
    emptyState.style.display = 'block';
    streamList.style.display = 'none';
    streamList.textContent = '';

    if (otherCount > 0 && currentView === 'current') {
      hintDefault.hidden = true;
      hintOtherTabs.hidden = false;
      otherTabCount.textContent = String(otherCount);
    } else {
      hintDefault.hidden = false;
      hintOtherTabs.hidden = true;
    }
    return;
  }

  emptyState.style.display = 'none';
  streamList.style.display = 'flex';
  streamList.textContent = ''; // clear without innerHTML (C8)

  // Rank: manifests first, high confidence first, then newest first
  const ranked = [...displayed].sort((a, b) =>
    (KIND_RANK[a.kind] ?? 3) - (KIND_RANK[b.kind] ?? 3) ||
    (a.confidence === 'low') - (b.confidence === 'low') ||
    (b.timestamp || 0) - (a.timestamp || 0)
  );

  ranked.forEach((item, index) => {
    const card = document.createElement('div');
    card.className = 'stream-card';
    if (index === 0 && !filterQuery && ranked.length > 1) {
      card.classList.add('is-primary');
    }

    const meta = document.createElement('div');
    meta.className = 'stream-meta';

    const metaLeft = document.createElement('div');
    metaLeft.className = 'meta-left';

    const kindSpan = document.createElement('span');
    kindSpan.className = `stream-kind ${getKindClass(item.kind)}`;
    kindSpan.textContent = item.kind === 'Abyss' ? '🎬 Abyss / Hydrax' : item.kind;
    metaLeft.appendChild(kindSpan);

    if (index === 0 && !filterQuery && ranked.length > 1) {
      const recBadge = document.createElement('span');
      recBadge.className = 'badge-recommended';
      recBadge.textContent = '⭐ Recommended';
      metaLeft.appendChild(recBadge);
    }

    const descText = describeStream(item);
    const descDetails = descText.split(' · ').slice(1);
    if (descDetails.length > 0) {
      const descSpan = document.createElement('span');
      descSpan.className = 'stream-desc';
      descSpan.textContent = `· ${descDetails.join(' · ')}`;
      metaLeft.appendChild(descSpan);
    }

    meta.appendChild(metaLeft);

    if (item.timestamp) {
      const timeSpan = document.createElement('span');
      timeSpan.className = 'stream-time';
      timeSpan.textContent = formatRelativeTime(item.timestamp);
      meta.appendChild(timeSpan);
    }

    const urlDiv = document.createElement('div');
    urlDiv.className = 'stream-url';
    urlDiv.textContent = elideUrl(item.url, 64);
    urlDiv.title = item.url;

    const actions = document.createElement('div');
    actions.className = 'actions';

    const copyCurlBtn = document.createElement('button');
    copyCurlBtn.className = 'btn';
    copyCurlBtn.textContent = '📋 Copy as cURL';
    copyCurlBtn.setAttribute('aria-label', `Copy cURL command for ${item.kind} stream`);
    copyCurlBtn.addEventListener('click', async () => {
      const curlCmd = toCurl(item);
      await copyWithFeedback(copyCurlBtn, curlCmd, '📋 Copy as cURL', 'Copied cURL! Switch to GUI & click "Paste from browser"');
    });

    const copyUrlBtn = document.createElement('button');
    copyUrlBtn.className = 'btn btn-secondary';
    copyUrlBtn.textContent = 'Copy URL';
    copyUrlBtn.setAttribute('aria-label', `Copy raw URL for ${item.kind} stream`);
    copyUrlBtn.addEventListener('click', async () => {
      await copyWithFeedback(copyUrlBtn, item.url, 'Copy URL', 'Copied raw URL to clipboard');
    });

    actions.appendChild(copyCurlBtn);
    actions.appendChild(copyUrlBtn);

    card.appendChild(meta);
    card.appendChild(urlDiv);
    card.appendChild(actions);
    streamList.appendChild(card);
  });
}

async function renderStreams() {
  const generation = ++renderGeneration;
  const data = await loadStreams();
  if (generation !== renderGeneration) return;
  paint(data);
}

async function sweepOnOpen() {
  try {
    const tabs = await chrome.tabs.query({});
    const removed = await sweepOrphanTabs(tabs.map((t) => t.id));
    if (removed > 0) {
      console.debug(`[N_m3u8DL-RE] Cleared ${removed} orphaned tab entries.`);
    }
  } catch (err) {
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
    tabCurrent.setAttribute('aria-selected', 'true');
    tabAll.classList.remove('active');
    tabAll.setAttribute('aria-selected', 'false');
    renderStreams();
  });

  tabAll.addEventListener('click', () => {
    currentView = 'all';
    tabAll.classList.add('active');
    tabAll.setAttribute('aria-selected', 'true');
    tabCurrent.classList.remove('active');
    tabCurrent.setAttribute('aria-selected', 'false');
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

  // Filter input handler
  const filterInput = document.getElementById('stream-filter');
  filterInput.addEventListener('input', (e) => {
    filterQuery = e.target.value.trim();
    renderStreams();
  });

  // Live storage change listener with debouncing (C3)
  chrome.storage.onChanged.addListener((changes, areaName) => {
    if (areaName !== 'session') return; // ignore local migration cleanup
    if (renderTimer) clearTimeout(renderTimer);
    renderTimer = setTimeout(renderStreams, 150);
  });

  // Render immediately for fast UI
  renderStreams();

  // Sweep orphaned tab keys in background after first render
  sweepOnOpen();
}

document.addEventListener('DOMContentLoaded', init);
