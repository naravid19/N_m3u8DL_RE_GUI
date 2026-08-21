/**
 * N-RE Stream Bridge — Popup Logic
 */

import { getTabStreams, getRecentStreams, sweepOrphanTabs, clearTab, clearAll } from '../lib/storage.js';
import { formatBytes, formatRelativeTime, elideUrl, describeStream } from '../lib/format.js';
import { toCurl, toBatchList } from '../lib/curl.js';
import { probeVariants } from '../lib/probe.js';

let activeTabId = null;
let currentView = 'current'; // 'current' | 'all'
let toastTimer = null;
let renderTimer = null;
let renderGeneration = 0;
let filterQuery = '';

const selectedUrls = new Set();
const variantsCache = new Map(); // url -> { variants, error, loading }
const selectedQualityMap = new Map(); // url -> selectVideo directive string
const expandedQualities = new Set(); // set of urls currently open

const KIND_RANK = { HLS: 0, DASH: 0, MSS: 0, Abyss: 1, Media: 2, Audio: 2 };

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
    console.debug('[N-RE Stream Bridge] Clipboard write failed:', err);
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
    case 'audio': return 'kind-audio';
    default: return 'kind-media';
  }
}

function splitUrl(rawUrl) {
  try {
    const u = new URL(rawUrl);
    const pathParts = u.pathname.split('/').filter(Boolean);
    const filename = pathParts.length > 0 ? pathParts[pathParts.length - 1] : u.hostname;
    const hostAndPath = `${u.origin}${u.pathname}`;
    return { filename: `${filename}${u.search ? ' ' + u.search : ''}`, hostAndPath };
  } catch {
    return { filename: rawUrl, hostAndPath: '' };
  }
}

function getOriginHeader(item) {
  if (item.referer) {
    try {
      return new URL(item.referer).hostname;
    } catch {
      return item.referer;
    }
  }
  try {
    return new URL(item.url).hostname;
  } catch {
    return 'Other streams';
  }
}

function updateBulkBar(visibleStreams) {
  const bulkBar = document.getElementById('bulk-bar');
  const countLabel = document.getElementById('selected-count');
  const selectAll = document.getElementById('select-all-checkbox');

  // Prune URLs that no longer exist
  const visibleUrlSet = new Set(visibleStreams.map((s) => s.url));
  for (const u of selectedUrls) {
    if (!visibleUrlSet.has(u)) selectedUrls.delete(u);
  }

  if (selectedUrls.size > 0) {
    bulkBar.hidden = false;
    countLabel.textContent = `${selectedUrls.size} selected`;
    selectAll.checked = visibleStreams.length > 0 && selectedUrls.size === visibleStreams.length;
  } else {
    bulkBar.hidden = true;
    selectAll.checked = false;
  }
}

function paint({ streams, otherCount }) {
  const countBadge = document.getElementById('stream-count');
  const emptyState = document.getElementById('empty-state');
  const streamList = document.getElementById('stream-list');
  const filterBar = document.getElementById('filter-bar');
  const filterInput = document.getElementById('stream-filter');
  const hintDefault = document.getElementById('hint-default');
  const hintOtherTabs = document.getElementById('hint-other-tabs');
  const otherTabCount = document.getElementById('other-tab-count');
  const noMatches = document.getElementById('no-matches');
  const noMatchesTerm = document.getElementById('no-matches-term');

  countBadge.textContent = String(streams.length);

  // Keep the filter bar visible whenever a filter is active or streams count >= 5
  const showFilter = streams.length >= 5 || filterQuery.length > 0;
  filterBar.hidden = !showFilter;
  if (!showFilter) {
    filterQuery = '';
    filterInput.value = '';
  }

  // Filter streams by search query if set
  let displayed = streams;
  if (filterQuery) {
    const q = filterQuery.toLowerCase();
    displayed = streams.filter((s) => (s.url && s.url.toLowerCase().includes(q)) || (s.kind && s.kind.toLowerCase().includes(q)));
  }

  updateBulkBar(displayed);

  if (streams.length === 0) {
    emptyState.style.display = 'block';
    streamList.style.display = 'none';
    streamList.textContent = '';
    noMatches.hidden = true;

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

  if (displayed.length === 0) {
    streamList.style.display = 'none';
    streamList.textContent = '';
    noMatches.hidden = false;
    noMatchesTerm.textContent = filterQuery;
    return;
  }

  noMatches.hidden = true;
  streamList.style.display = 'flex';
  streamList.textContent = ''; // clear without innerHTML

  // Rank: manifests first, high confidence first, then newest first
  const ranked = [...displayed].sort((a, b) =>
    (KIND_RANK[a.kind] ?? 3) - (KIND_RANK[b.kind] ?? 3) ||
    (a.confidence === 'low') - (b.confidence === 'low') ||
    (b.timestamp || 0) - (a.timestamp || 0)
  );

  // Group by page domain if viewing All Recent
  if (currentView === 'all' && !filterQuery) {
    const groups = new Map();
    for (const item of ranked) {
      const origin = getOriginHeader(item);
      if (!groups.has(origin)) groups.set(origin, []);
      groups.get(origin).push(item);
    }

    for (const [origin, groupItems] of groups) {
      const groupWrapper = document.createElement('div');
      groupWrapper.className = 'page-group';

      const groupHeading = document.createElement('div');
      groupHeading.className = 'page-group-header';
      groupHeading.textContent = `🌐 ${origin} (${groupItems.length})`;
      groupWrapper.appendChild(groupHeading);

      groupItems.forEach((item, index) => {
        const card = createStreamCard(item, index, ranked.length, displayed);
        groupWrapper.appendChild(card);
      });

      streamList.appendChild(groupWrapper);
    }
  } else {
    ranked.forEach((item, index) => {
      const card = createStreamCard(item, index, ranked.length, displayed);
      streamList.appendChild(card);
    });
  }
}

function createStreamCard(item, index, totalCount, allDisplayed) {
  const card = document.createElement('div');
  card.className = 'stream-card';
  card.tabIndex = 0; // roving keyboard focus

  if (index === 0 && !filterQuery && totalCount > 1) {
    card.classList.add('is-primary');
  }
  if (selectedUrls.has(item.url)) {
    card.classList.add('is-selected');
  }

  // --- Meta Header ---
  const meta = document.createElement('div');
  meta.className = 'stream-meta';

  const metaLeft = document.createElement('div');
  metaLeft.className = 'meta-left';

  // Checkbox for multi-select
  const checkbox = document.createElement('input');
  checkbox.type = 'checkbox';
  checkbox.className = 'stream-checkbox';
  checkbox.checked = selectedUrls.has(item.url);
  checkbox.setAttribute('aria-label', `Select ${item.kind} stream`);
  checkbox.addEventListener('change', (e) => {
    e.stopPropagation();
    if (checkbox.checked) {
      selectedUrls.add(item.url);
      card.classList.add('is-selected');
    } else {
      selectedUrls.delete(item.url);
      card.classList.remove('is-selected');
    }
    updateBulkBar(allDisplayed);
  });
  metaLeft.appendChild(checkbox);

  const kindSpan = document.createElement('span');
  kindSpan.className = `stream-kind ${getKindClass(item.kind)}`;
  kindSpan.textContent = item.kind === 'Abyss' ? '🎬 Abyss / Hydrax' : item.kind;
  metaLeft.appendChild(kindSpan);

  if (index === 0 && !filterQuery && totalCount > 1) {
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
    timeSpan.dataset.timestamp = String(item.timestamp);
    timeSpan.textContent = formatRelativeTime(item.timestamp);
    meta.appendChild(timeSpan);
  }

  // --- Two-Line URL Display ---
  const { filename, hostAndPath } = splitUrl(item.url);

  const urlBox = document.createElement('div');
  urlBox.className = 'stream-url-box';
  urlBox.title = item.url;

  const fnDiv = document.createElement('div');
  fnDiv.className = 'url-filename';
  fnDiv.textContent = filename;

  const hostDiv = document.createElement('div');
  hostDiv.className = 'url-hostpath';
  hostDiv.textContent = elideUrl(hostAndPath || item.url, 58);

  urlBox.appendChild(fnDiv);
  if (hostDiv.textContent) urlBox.appendChild(hostDiv);

  // --- Qualities Disclosure Panel ---
  const isManifestKind = ['HLS', 'DASH', 'MSS'].includes(item.kind);
  let qualitiesPanel = null;

  if (isManifestKind) {
    qualitiesPanel = document.createElement('div');
    qualitiesPanel.className = 'qualities-panel';
    qualitiesPanel.hidden = !expandedQualities.has(item.url);
    renderQualitiesPanel(qualitiesPanel, item);
  }

  // --- Action Buttons ---
  const actions = document.createElement('div');
  actions.className = 'actions';

  const copyCurlBtn = document.createElement('button');
  copyCurlBtn.className = 'btn';
  copyCurlBtn.textContent = '📋 Copy as cURL';
  copyCurlBtn.setAttribute('aria-label', `Copy cURL command for ${item.kind} stream`);
  copyCurlBtn.addEventListener('click', async () => {
    const chosenQuality = selectedQualityMap.get(item.url) || null;
    const curlCmd = toCurl(item, chosenQuality ? { selectVideo: chosenQuality } : {});
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

  if (isManifestKind) {
    const qualBtn = document.createElement('button');
    qualBtn.className = 'btn btn-secondary btn-qualities';
    qualBtn.textContent = expandedQualities.has(item.url) ? '▾ Qualities' : '▸ Qualities';
    qualBtn.setAttribute('aria-label', `Inspect quality renditions for ${item.kind} stream`);

    qualBtn.addEventListener('click', async () => {
      if (expandedQualities.has(item.url)) {
        expandedQualities.delete(item.url);
        qualBtn.textContent = '▸ Qualities';
        if (qualitiesPanel) qualitiesPanel.hidden = true;
      } else {
        expandedQualities.add(item.url);
        qualBtn.textContent = '▾ Qualities';
        if (qualitiesPanel) {
          qualitiesPanel.hidden = false;
          if (!variantsCache.has(item.url)) {
            await loadQualities(item, qualitiesPanel, qualBtn);
          }
        }
      }
    });

    actions.appendChild(qualBtn);
  }

  // Card keyboard shortcuts
  card.addEventListener('keydown', async (e) => {
    if (e.key === ' ') {
      e.preventDefault();
      checkbox.checked = !checkbox.checked;
      checkbox.dispatchEvent(new Event('change'));
    } else if (e.key === 'Enter') {
      e.preventDefault();
      copyCurlBtn.click();
    }
  });

  card.appendChild(meta);
  card.appendChild(urlBox);
  if (qualitiesPanel) card.appendChild(qualitiesPanel);
  card.appendChild(actions);

  return card;
}

async function loadQualities(item, panel, button) {
  variantsCache.set(item.url, { variants: [], error: null, loading: true });
  renderQualitiesPanel(panel, item);
  if (button) button.disabled = true;

  const result = await probeVariants(item);
  variantsCache.set(item.url, {
    variants: result.variants,
    error: result.error,
    loading: false
  });

  if (button) button.disabled = false;
  renderQualitiesPanel(panel, item);
}

function renderQualitiesPanel(panel, item) {
  panel.textContent = '';
  const cached = variantsCache.get(item.url);

  if (!cached || cached.loading) {
    const status = document.createElement('div');
    status.className = 'qualities-status';
    status.textContent = '⋯ Reading manifest...';
    panel.appendChild(status);
    return;
  }

  if (cached.error) {
    const status = document.createElement('div');
    status.className = 'qualities-status error';
    status.textContent = `Could not read manifest (${cached.error})`;

    const retryBtn = document.createElement('button');
    retryBtn.className = 'btn-retry';
    retryBtn.textContent = 'Retry';
    retryBtn.addEventListener('click', () => loadQualities(item, panel, null));
    status.appendChild(retryBtn);

    panel.appendChild(status);
    return;
  }

  if (!cached.variants || cached.variants.length === 0) {
    const status = document.createElement('div');
    status.className = 'qualities-status';
    status.textContent = 'Single quality — nothing to choose';
    panel.appendChild(status);
    return;
  }

  const list = document.createElement('div');
  list.className = 'qualities-list';

  // "Best available" default option
  const currentSelection = selectedQualityMap.get(item.url) || 'best';

  const defaultOption = createQualityOption(
    item.url,
    'best',
    '⭐ Best Available (Default)',
    currentSelection === 'best',
    (val) => selectedQualityMap.set(item.url, val)
  );
  list.appendChild(defaultOption);

  for (const v of cached.variants) {
    if (v.kind !== 'video') continue;
    const selector = v.height ? `res="${v.height}*"` : (v.bandwidth ? `for=best` : 'best');
    const label = v.label || (v.height ? `${v.height}p` : 'Video stream');

    const opt = createQualityOption(
      item.url,
      selector,
      label,
      currentSelection === selector,
      (val) => selectedQualityMap.set(item.url, val)
    );
    list.appendChild(opt);
  }

  panel.appendChild(list);
}

function createQualityOption(streamUrl, value, labelText, isChecked, onChange) {
  const label = document.createElement('label');
  label.className = 'quality-option';

  const radio = document.createElement('input');
  radio.type = 'radio';
  radio.name = `qualities-${streamUrl}`;
  radio.value = value;
  radio.checked = isChecked;

  radio.addEventListener('change', () => {
    if (radio.checked) onChange(value);
  });

  const text = document.createElement('span');
  text.textContent = labelText;

  label.appendChild(radio);
  label.appendChild(text);
  return label;
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
      console.debug(`[N-RE Stream Bridge] Cleared ${removed} orphaned tab entries.`);
    }
  } catch (err) {
    console.debug('[N-RE Stream Bridge] Sweep skipped:', err);
  }
}

function refreshTimestamps() {
  document.querySelectorAll('.stream-time[data-timestamp]').forEach((el) => {
    const ts = Number.parseInt(el.dataset.timestamp, 10);
    if (Number.isFinite(ts)) el.textContent = formatRelativeTime(ts);
  });
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

  // Switch to all recent button inside empty state
  const btnSwitchAll = document.getElementById('btn-switch-all');
  if (btnSwitchAll) {
    btnSwitchAll.addEventListener('click', () => {
      tabAll.click();
    });
  }

  // Action buttons
  document.getElementById('btn-refresh').addEventListener('click', () => {
    renderStreams();
    showToast('Refreshed stream list');
  });

  document.getElementById('btn-clear').addEventListener('click', async () => {
    selectedUrls.clear();
    variantsCache.clear();
    expandedQualities.clear();
    await clearAll();
    renderStreams();
    showToast('Cleared stream list');
  });

  // Bulk bar actions
  const selectAll = document.getElementById('select-all-checkbox');
  selectAll.addEventListener('change', async () => {
    const data = await loadStreams();
    const visible = filterQuery
      ? data.streams.filter((s) => (s.url && s.url.toLowerCase().includes(filterQuery.toLowerCase())) || (s.kind && s.kind.toLowerCase().includes(filterQuery.toLowerCase())))
      : data.streams;

    if (selectAll.checked) {
      visible.forEach((s) => selectedUrls.add(s.url));
    } else {
      selectedUrls.clear();
    }
    renderStreams();
  });

  document.getElementById('btn-bulk-copy').addEventListener('click', async () => {
    const data = await loadStreams();
    const selectedStreams = data.streams.filter((s) => selectedUrls.has(s.url));
    if (selectedStreams.length === 0) return;

    const listPayload = toBatchList(selectedStreams);
    await copyWithFeedback(
      document.getElementById('btn-bulk-copy'),
      listPayload,
      '📋 Copy as list',
      `Copied ${selectedStreams.length} URLs as batch list! Paste in GUI.`
    );
  });

  document.getElementById('btn-bulk-clear').addEventListener('click', () => {
    selectedUrls.clear();
    renderStreams();
  });

  // Filter input handler
  const filterInput = document.getElementById('stream-filter');
  filterInput.addEventListener('input', (e) => {
    filterQuery = e.target.value.trim();
    renderStreams();
  });

  // Live storage change listener with debouncing (C3)
  chrome.storage.onChanged.addListener((changes, areaName) => {
    if (areaName !== 'session') return;
    if (renderTimer) clearTimeout(renderTimer);
    renderTimer = setTimeout(renderStreams, 150);
  });

  // Live relative timestamp refresher
  const ticker = setInterval(refreshTimestamps, 30000);
  window.addEventListener('pagehide', () => clearInterval(ticker));

  // Render immediately for fast UI
  renderStreams();

  // Sweep orphaned tab keys in background after first render
  sweepOnOpen();
}

document.addEventListener('DOMContentLoaded', init);
