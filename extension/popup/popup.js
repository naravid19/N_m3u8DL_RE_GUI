/**
 * N-RE Stream Bridge — Popup Logic
 */

import { getTabStreams, getRecentStreams, clearTab, clearAll, clearTabView, dismissMany, undismissMany, getCachedUpdateResult, setCachedUpdateResult } from '../lib/storage.js';
import { formatBytes, formatRelativeTime, elideUrl, describeStream, splitUrl } from '../lib/format.js';
import { KIND_TITLES } from '../lib/classify.js';
import { toCurl, toBatchList, findRefererMismatch } from '../lib/curl.js';
import { probeVariants } from '../lib/probe.js';
import { rankStreams, groupByOrigin, reconcileSelection, matchesFilter } from '../lib/list-policy.js';
import { getExtensionVersion } from '../lib/version.js';
import { getSuiteVersion } from '../lib/suite-version.js';
import { getMergedCookies, applyMergedCookie } from '../lib/cookies.js';
import { checkSuiteUpdate } from '../lib/update-check.js';
import { sendToGui, isGuiAvailable } from '../lib/native.js';
import { icon, hydrateIcons } from '../lib/icons.js';

let activeTabId = null;
let activeTabTitle = '';
let activeTabUrl = '';
let currentView = 'current'; // 'current' | 'all'
let toastTimer = null;
let renderTimer = null;
let renderGeneration = 0;
let filterQuery = '';
let guiAvailable = null; // null = not probed yet, true | false
let guiAvailablePromise = null;

function checkGuiAvailable() {
  if (guiAvailablePromise) return guiAvailablePromise;
  guiAvailablePromise = isGuiAvailable().then((available) => {
    guiAvailable = available;
    return available;
  });
  return guiAvailablePromise;
}

const selectedUrls = new Set();
const variantsCache = new Map(); // url -> { variants, error, loading }
const selectedQualityMap = new Map(); // url -> selectVideo directive string
const expandedQualities = new Set(); // set of urls currently open
const qualitiesPanels = new Map(); // url -> the qualities panel currently rendered for it

function setButtonLabel(button, iconName, text) {
  const svg = iconName ? icon(iconName) : null;
  button.replaceChildren(...(svg ? [svg] : []), document.createTextNode(text));
}

function showToast(message, options = {}) {
  const toast = document.getElementById('toast');
  const toastText = document.getElementById('toast-text') || toast;
  const toastAction = document.getElementById('toast-action');
  if (!toast) return;

  if (toastText !== toast) {
    toastText.textContent = message;
  } else {
    toast.textContent = message;
  }

  if (toastAction) {
    if (options.actionLabel && options.onAction) {
      toastAction.textContent = options.actionLabel;
      toastAction.setAttribute('aria-label', options.actionLabel);
      toastAction.hidden = false;
      toastAction.onclick = (e) => {
        e.stopPropagation();
        options.onAction();
        hideToast();
      };
    } else {
      toastAction.hidden = true;
      toastAction.onclick = null;
    }
  }

  toast.style.display = 'flex';
  if (toastTimer) clearTimeout(toastTimer);
  toastTimer = setTimeout(() => {
    hideToast();
  }, options.duration || 3500);
}

function hideToast() {
  const toast = document.getElementById('toast');
  if (toast) toast.style.display = 'none';
  if (toastTimer) {
    clearTimeout(toastTimer);
    toastTimer = null;
  }
}

async function copyWithFeedback(button, text, originalIcon, originalLabel, successMessage) {
  try {
    await navigator.clipboard.writeText(text);
  } catch (err) {
    showToast('Could not write to clipboard. Select the URL and copy it manually.');
    console.debug('[N-RE Stream Bridge] Clipboard write failed:', err);
    return;
  }

  setButtonLabel(button, 'check', 'Copied');
  button.classList.add('is-copied');
  setTimeout(() => {
    setButtonLabel(button, originalIcon, originalLabel);
    button.classList.remove('is-copied');
  }, 1500);

  showToast(successMessage);
}

async function loadStreams() {
  if (currentView === 'current') {
    const currentStreams = activeTabId ? await getTabStreams(activeTabId) : [];
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

function updateBulkBar(visibleStreams = []) {
  const bulkBar = document.getElementById('bulk-bar');
  const countLabel = document.getElementById('selected-count');
  const selectAll = document.getElementById('select-all-checkbox');
  const bulkWarning = document.getElementById('bulk-warning');

  // Prune URLs that no longer exist
  const reconciled = reconcileSelection(selectedUrls, visibleStreams);
  selectedUrls.clear();
  for (const u of reconciled) selectedUrls.add(u);

  const count = selectedUrls.size;
  const total = visibleStreams.length;

  if (count > 0) {
    bulkBar.hidden = false;
    countLabel.textContent = `${count} selected`;

    const selectedStreams = visibleStreams.filter((s) => selectedUrls.has(s.url));
    const mismatch = findRefererMismatch(selectedStreams);

    if (mismatch.mismatched && bulkWarning) {
      bulkWarning.hidden = false;
      const countPart = `${mismatch.offCount} of ${selectedStreams.length} selected`;
      bulkWarning.innerHTML = `<strong>${countPart} are from another site.</strong> Their headers will not apply, and those downloads will likely fail. Copy one site at a time.`;
    } else if (bulkWarning) {
      bulkWarning.hidden = true;
      bulkWarning.textContent = '';
    }
  } else {
    bulkBar.hidden = true;
    countLabel.textContent = '0 selected';
    if (bulkWarning) {
      bulkWarning.hidden = true;
      bulkWarning.textContent = '';
    }
  }

  if (selectAll) {
    selectAll.checked = total > 0 && count === total;
    selectAll.indeterminate = count > 0 && count < total;
    selectAll.setAttribute(
      'aria-checked',
      count === total && total > 0 ? 'true' : (count > 0 ? 'mixed' : 'false')
    );
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
    displayed = streams.filter((s) => matchesFilter(s, filterQuery));
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
  const ranked = rankStreams(displayed);

  // Group by page domain if viewing All Recent
  if (currentView === 'all' && !filterQuery) {
    const groups = groupByOrigin(ranked);

    for (const group of groups) {
      const groupWrapper = document.createElement('div');
      groupWrapper.className = 'page-group';
      groupWrapper.setAttribute('role', 'group');

      let headerText = 'Other streams';
      if (group.origin) {
        try {
          headerText = new URL(group.origin).hostname || group.origin;
        } catch {
          headerText = group.origin;
        }
      }
      groupWrapper.setAttribute('aria-label', headerText);

      const groupHeading = document.createElement('div');
      groupHeading.className = 'page-group-header';
      groupHeading.textContent = `${headerText} (${group.items.length})`;
      groupWrapper.appendChild(groupHeading);

      group.items.forEach((item, index) => {
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

/** The payload both Copy as cURL and Download send. */
async function buildCurlFor(item) {
  const chosenQuality = selectedQualityMap.get(item.url) || null;
  const rawTitle = item.pageTitle || activeTabTitle || '';
  const cleanTitle = rawTitle.replace(/[#*?<>|"\\/:]/g, ' ').replace(/\s+/g, ' ').trim();
  const options = {};
  if (chosenQuality) options.selectVideo = chosenQuality;
  if (cleanTitle) options.saveName = cleanTitle;

  const effectivePageUrl = item.pageUrl || activeTabUrl || null;
  const mergedCookie = await getMergedCookies(item.url, effectivePageUrl, item.cookie);
  const streamForCurl = applyMergedCookie({ ...item, pageUrl: effectivePageUrl }, mergedCookie);

  return toCurl(streamForCurl, options);
}

function createStreamCard(item, index, totalCount, allDisplayed) {
  const card = document.createElement('div');
  card.className = 'stream-card';
  card.tabIndex = 0; // roving keyboard focus
  card.setAttribute('role', 'option');
  card.setAttribute('aria-selected', selectedUrls.has(item.url) ? 'true' : 'false');

  // Position 0 of the *rendered* set. Filtering is when a user most needs the
  // ranking, and a lone stream still benefits from confirmation of its kind.
  if (index === 0) {
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
      card.setAttribute('aria-selected', 'true');
    } else {
      selectedUrls.delete(item.url);
      card.classList.remove('is-selected');
      card.setAttribute('aria-selected', 'false');
    }
    updateBulkBar(allDisplayed);
  });
  metaLeft.appendChild(checkbox);

  const kindSpan = document.createElement('span');
  kindSpan.className = `stream-kind ${getKindClass(item.kind)}`;
  kindSpan.textContent = item.kind === 'Abyss' ? 'Abyss / Hydrax' : item.kind;
  const expansion = KIND_TITLES[item.kind];
  if (expansion) kindSpan.title = expansion;
  metaLeft.appendChild(kindSpan);

  if (item.confidence === 'low') {
    const guessBadge = document.createElement('span');
    guessBadge.className = 'badge-guess';
    guessBadge.textContent = 'guess';
    metaLeft.appendChild(guessBadge);
  }

  const descText = describeStream(item);
  if (descText) {
    const descSpan = document.createElement('span');
    descSpan.className = 'stream-desc';
    descSpan.textContent = `· ${descText}`;
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
  const { filename, queryParams, hostAndPath } = splitUrl(item.url);

  const urlBox = document.createElement('div');
  urlBox.className = 'stream-url-box';
  urlBox.title = item.url;

  const fnDiv = document.createElement('div');
  fnDiv.className = 'url-filename';
  fnDiv.textContent = filename;

  if (queryParams) {
    const qSpan = document.createElement('span');
    qSpan.className = 'url-query';
    qSpan.textContent = ` ${queryParams.length > 38 ? queryParams.slice(0, 35) + '…' : queryParams}`;
    qSpan.title = item.url;
    fnDiv.appendChild(qSpan);
  }

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
    qualitiesPanels.set(item.url, qualitiesPanel);
  }

  // --- Action Buttons ---
  const actions = document.createElement('div');
  actions.className = 'actions';

  // The GUI is the whole point of this extension, so handing a stream
  // straight to it leads. It only appears when the GUI is actually
  // installed — Copy as cURL remains the path when it is not.
  const downloadBtn = document.createElement('button');
  downloadBtn.className = 'btn';
  setButtonLabel(downloadBtn, 'download', 'Download');
  downloadBtn.hidden = true;
  downloadBtn.title = 'Send this stream to N_m3u8DL-RE GUI and start downloading';
  downloadBtn.setAttribute('aria-label', `Download this ${item.kind} stream in N_m3u8DL-RE GUI`);
  downloadBtn.addEventListener('click', async () => {
    downloadBtn.disabled = true;
    setButtonLabel(downloadBtn, null, 'Sending…');

    const result = await sendToGui(await buildCurlFor(item));

    setButtonLabel(downloadBtn, 'download', 'Download');
    downloadBtn.disabled = false;

    if (result.ok) {
      showToast('Sent to GUI — the download starts there.');
    } else if (result.reason === 'unavailable') {
      // Re-render rather than patch this one card's buttons: every visible
      // card shares the same guiAvailable flag, and createStreamCard is
      // already the one place that decides each card's button state from it.
      guiAvailable = false;
      renderStreams();
      showToast('N_m3u8DL-RE GUI is not available. Use Copy as cURL instead.');
    } else {
      showToast(`GUI refused the stream: ${result.reason}`);
    }
  });

  const copyCurlBtn = document.createElement('button');
  copyCurlBtn.className = 'btn';
  setButtonLabel(copyCurlBtn, 'clipboard', 'Copy as cURL');
  copyCurlBtn.setAttribute('aria-label', `Copy cURL command for ${item.kind} stream`);
  copyCurlBtn.addEventListener('click', async () => {
    const curlCmd = await buildCurlFor(item);
    await copyWithFeedback(copyCurlBtn, curlCmd, 'clipboard', 'Copy as cURL', 'Copied cURL! Switch to GUI & click "Paste from browser"');
  });

  const copyUrlBtn = document.createElement('button');
  copyUrlBtn.className = 'btn btn-secondary';
  setButtonLabel(copyUrlBtn, 'link', 'URL only');
  copyUrlBtn.title = 'Copies the address without the Referer, Cookie or User-Agent headers. Most sites reject it.';
  copyUrlBtn.setAttribute('aria-label', `Copy the ${item.kind} URL without headers`);
  copyUrlBtn.addEventListener('click', async () => {
    await copyWithFeedback(copyUrlBtn, item.url, 'link', 'URL only', 'Copied the URL only — no headers.');
  });

  let qualBtn = null;
  if (isManifestKind) {
    qualBtn = document.createElement('button');
    qualBtn.className = 'btn btn-secondary btn-qualities';
    setButtonLabel(qualBtn, expandedQualities.has(item.url) ? 'chevronDown' : 'chevronRight', 'Qualities');
    qualBtn.setAttribute('aria-label', `Inspect quality renditions for ${item.kind} stream`);

    qualBtn.addEventListener('click', async () => {
      if (expandedQualities.has(item.url)) {
        expandedQualities.delete(item.url);
        setButtonLabel(qualBtn, 'chevronRight', 'Qualities');
        if (qualitiesPanel) qualitiesPanel.hidden = true;
      } else {
        expandedQualities.add(item.url);
        setButtonLabel(qualBtn, 'chevronDown', 'Qualities');
        if (qualitiesPanel) {
          qualitiesPanel.hidden = false;
          if (!variantsCache.has(item.url)) {
            await loadQualities(item, qualBtn);
          }
        }
      }
    });
  }

  const primaryRow = document.createElement('div');
  primaryRow.className = 'actions-primary';

  const secondaryRow = document.createElement('div');
  secondaryRow.className = 'actions-secondary';

  actions.append(primaryRow, secondaryRow);

  // Exactly one accent button per card. When the GUI is reachable, Download
  // takes the accent as the full-width primary CTA, and Copy as cURL steps back
  // to the outline treatment in the secondary toolbar; when it is not,
  // Copy as cURL is promoted to the primary CTA in Row 1.
  function applyGuiAvailability(available) {
    downloadBtn.hidden = !available;
    primaryRow.textContent = '';
    secondaryRow.textContent = '';

    if (available) {
      downloadBtn.className = 'btn';
      copyCurlBtn.className = 'btn btn-secondary';
      primaryRow.appendChild(downloadBtn);
      secondaryRow.appendChild(copyCurlBtn);
    } else {
      copyCurlBtn.className = 'btn';
      primaryRow.appendChild(copyCurlBtn);
    }
    secondaryRow.appendChild(copyUrlBtn);
    if (qualBtn) secondaryRow.appendChild(qualBtn);
  }

  // Render immediately with synchronous baseline (offline layout) to eliminate blank flash and CLS.
  // Promote to primary Download if native host is detected online.
  if (guiAvailable === true) {
    applyGuiAvailability(true);
  } else if (guiAvailable === false) {
    applyGuiAvailability(false);
  } else {
    applyGuiAvailability(false);
    checkGuiAvailable().then((available) => {
      // card, not downloadBtn: the offline baseline above appends only copyCurlBtn,
      // so downloadBtn is not in the DOM yet and its .isConnected is false by
      // construction -- the promotion could never fire on a fresh popup open. The
      // guard is here to skip a card that was re-rendered away while the native
      // host was being probed, and card is the node that answers that.
      if (available && card.isConnected) {
        applyGuiAvailability(true);
      }
    });
  }

  // Card keyboard shortcuts
  card.addEventListener('keydown', async (e) => {
    // These shortcuts belong to the card itself. Without this the events bubble
    // from whichever button holds focus, so Enter on "URL only" copies the cURL
    // command and Space on any button toggles selection instead of activating it.
    if (e.target !== card) return;

    if (e.key === ' ') {
      e.preventDefault();
      checkbox.checked = !checkbox.checked;
      checkbox.dispatchEvent(new Event('change'));
    } else if (e.key === 'Enter') {
      e.preventDefault();
      if (guiAvailable && !downloadBtn.hidden) {
        downloadBtn.click();
      } else {
        copyCurlBtn.click();
      }
    }
  });

  card.appendChild(meta);
  card.appendChild(urlBox);
  if (qualitiesPanel) card.appendChild(qualitiesPanel);
  card.appendChild(actions);

  return card;
}

async function loadQualities(item, button, { fresh = false } = {}) {
  variantsCache.set(item.url, { variants: [], error: null, loading: true });
  paintQualities(item);
  if (button) button.disabled = true;

  const tabId = item.tabId || activeTabId || null;
  const result = await probeVariants(item, tabId, { fresh });
  variantsCache.set(item.url, {
    variants: result.variants,
    error: result.error,
    loading: false
  });

  if (button) button.disabled = false;
  paintQualities(item);
}

/**
 * Paints the qualities panel on screen for this stream, if any. The list can
 * re-render while a probe runs, which detaches the panel the probe started with.
 */
function paintQualities(item) {
  const panel = qualitiesPanels.get(item.url);
  if (panel?.isConnected) renderQualitiesPanel(panel, item);
}

function renderQualitiesPanel(panel, item) {
  panel.textContent = '';
  const cached = variantsCache.get(item.url);

  if (!cached || cached.loading) {
    const skeleton = document.createElement('div');
    skeleton.className = 'qualities-skeleton';
    skeleton.setAttribute('role', 'status');
    skeleton.setAttribute('aria-label', 'Reading manifest renditions');

    for (let i = 0; i < 3; i++) {
      const row = document.createElement('div');
      row.className = 'skeleton-row';
      const radio = document.createElement('div');
      radio.className = 'skeleton-radio';
      const bar = document.createElement('div');
      bar.className = 'skeleton-bar';
      row.append(radio, bar);
      skeleton.appendChild(row);
    }

    const status = document.createElement('div');
    status.className = 'qualities-status';
    status.textContent = 'Reading manifest…';
    skeleton.appendChild(status);

    panel.appendChild(skeleton);
    return;
  }

  if (cached.error) {
    const status = document.createElement('div');
    status.className = 'qualities-status error';
    status.textContent = `Could not read manifest (${cached.error})`;

    const retryBtn = document.createElement('button');
    retryBtn.className = 'btn-retry';
    retryBtn.textContent = 'Retry';
    retryBtn.addEventListener('click', () => loadQualities(item, null, { fresh: true }));
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
    'Best Available (Default)',
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

function refreshTimestamps() {
  document.querySelectorAll('.stream-time[data-timestamp]').forEach((el) => {
    const ts = Number.parseInt(el.dataset.timestamp, 10);
    if (Number.isFinite(ts)) el.textContent = formatRelativeTime(ts);
  });
}

async function init() {
  hydrateIcons();
  try {
    let [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
    if (!tab) {
      const tabs = await chrome.tabs.query({ active: true, lastFocusedWindow: true });
      tab = tabs && tabs.length > 0 ? tabs[0] : null;
    }
    if (!tab) {
      const tabs = await chrome.tabs.query({ active: true });
      tab = tabs && tabs.length > 0 ? tabs[0] : null;
    }
    if (tab) {
      if (tab.id) activeTabId = tab.id;
      if (tab.title) activeTabTitle = tab.title;
      if (tab.url) activeTabUrl = tab.url;
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
    const { streams } = await loadStreams();

    selectedUrls.clear();
    variantsCache.clear();
    expandedQualities.clear();

    // clearAll wipes the lists and the variant cache but deliberately leaves
    // dismissals alone — they are what stops a still-playing page refilling
    // the list a few milliseconds later.
    const clearedItems = streams.map((s) => ({
      url: s.url,
      tabId: s.tabId || (currentView === 'current' ? activeTabId : null)
    }));

    if (clearedItems.length > 0) {
      await dismissMany(clearedItems);
    }

    if (currentView === 'current' && activeTabId) {
      await clearTabView(activeTabId);
    } else {
      await clearAll();
    }

    renderStreams();
    showToast(clearedItems.length > 0 ? `Cleared ${clearedItems.length} stream(s)` : 'Nothing to clear', {
      actionLabel: clearedItems.length > 0 ? 'Undo' : null,
      onAction: async () => {
        await undismissMany(clearedItems);
        renderStreams();
        showToast('Restored detection for cleared streams');
      }
    });
  });

  // Bulk bar actions
  const selectAll = document.getElementById('select-all-checkbox');
  selectAll.addEventListener('change', async () => {
    const data = await loadStreams();
    const visible = data.streams.filter((s) => matchesFilter(s, filterQuery));

    if (selectedUrls.size > 0) {
      selectedUrls.clear();
    } else {
      visible.forEach((s) => selectedUrls.add(s.url));
    }
    renderStreams();
  });

  const btnBulkCopy = document.getElementById('btn-bulk-copy');
  btnBulkCopy.addEventListener('click', async () => {
    const data = await loadStreams();
    const selectedStreams = data.streams.filter((s) => selectedUrls.has(s.url));
    if (selectedStreams.length === 0) return;

    const listPayload = toBatchList(selectedStreams);
    await copyWithFeedback(
      btnBulkCopy,
      listPayload,
      'clipboard',
      'Copy as list',
      `Copied ${selectedStreams.length} URLs as batch list! Paste in GUI.`
    );
  });

  // Filter input handler with debouncing
  const filterInput = document.getElementById('stream-filter');
  let filterTimer = null;
  filterInput.addEventListener('input', (e) => {
    filterQuery = e.target.value.trim();
    if (filterTimer) clearTimeout(filterTimer);
    filterTimer = setTimeout(renderStreams, 150);
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

  // Initialize version display and check for updates
  initVersionAndUpdates();
}

async function initVersionAndUpdates() {
  const extVersion = getExtensionVersion();
  const versionSpan = document.getElementById('ext-version');
  if (versionSpan && extVersion) {
    versionSpan.textContent = `v${extVersion}`;
  }

  try {
    let updateResult = await getCachedUpdateResult();
    if (!updateResult) {
      const suiteVersion = await getSuiteVersion();
      if (!suiteVersion) {
        console.debug('Suite version is unavailable; skipping update check');
        return;
      }

      updateResult = await checkSuiteUpdate(suiteVersion);
      await setCachedUpdateResult(updateResult);
    }

    if (updateResult && updateResult.status === 'update-available') {
      const badge = document.getElementById('ext-update-badge');
      if (badge) {
        badge.textContent = `N_m3u8DL-RE GUI ${updateResult.latestVersion} available`;
        const extIcon = icon('external', 11);
        if (extIcon) badge.appendChild(extIcon);
        badge.href = updateResult.releaseUrl || 'https://github.com/naravid19/N_m3u8DL_RE_GUI/releases/latest';
        badge.hidden = false;
      }
    }
  } catch (err) {
    console.debug('Failed to run update check in popup', err);
  }
}

document.addEventListener('DOMContentLoaded', init);
