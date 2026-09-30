/**
 * N-RE Stream Bridge — Background Service Worker (Manifest V3)
 *
 * Two capture channels feed one storage layer: webRequest for network traffic,
 * and messages from the content script for DOM media elements.
 */

import { classify, extractDispositionFilename } from './lib/classify.js';
import { addStream, clearTab } from './lib/storage.js';
import { totalSizeFrom } from './lib/format.js';
import { buildHeaderPayload, pruneInflight } from './lib/inflight-headers.js';

const MAX_INFLIGHT = 300;
const INFLIGHT_TTL_MS = 120000;

// requestId -> { referer, userAgent, cookie, origin, headersArray, at }
// Deliberately in-memory and deliberately lossy: writing this to storage on
// every request on the internet would cost far more than the rare miss when
// the service worker restarts between the two listeners. Keyed by requestId,
// not URL — two tabs requesting the same manifest at once must not overwrite
// each other's headers (M3).
const inFlightHeaders = new Map();

function updateBadge(tabId, count) {
  if (!tabId || tabId <= 0) return;
  chrome.action.setBadgeBackgroundColor({ color: '#5865F2', tabId });
  chrome.action.setBadgeText({ tabId, text: count > 0 ? String(count) : '' });
}

/**
 * The badge is derived from storage, not pushed to.
 *
 * The popup mutates storage directly and has no channel to this worker, so a
 * pushed badge went stale on every clear — the icon kept its old count while
 * the list was empty. Reading the change event covers add, clear, tab-clear
 * and navigation with one path.
 */
chrome.storage.onChanged.addListener((changes, areaName) => {
  if (areaName !== 'session') return;

  for (const [key, change] of Object.entries(changes)) {
    if (!key.startsWith('tab_')) continue;

    const tabId = Number.parseInt(key.slice('tab_'.length), 10);
    if (!Number.isFinite(tabId)) continue;

    updateBadge(tabId, Array.isArray(change.newValue) ? change.newValue.length : 0);
  }
});

const tabOrigins = new Map();
const tabPages = new Map();

const pendingStreams = [];
let flushTimer = null;

async function flushPending() {
  if (flushTimer) {
    clearTimeout(flushTimer);
    flushTimer = null;
  }
  if (pendingStreams.length === 0) return;
  const batch = pendingStreams.splice(0, pendingStreams.length);
  for (const item of batch) {
    try {
      await addStream(item.tabId, item.payload);
    } catch (err) {
      console.error('[N-RE Stream Bridge] Error storing stream:', err);
    }
  }
}

// Queued detections live only in memory. Best effort: write them before MV3 suspends
// the worker -- Chrome does not promise async work started here will finish.
// ponytail: a worker killed without firing onSuspend still loses the batch; write
// each detection straight to storage if that is ever actually observed.
chrome.runtime.onSuspend.addListener(flushPending);

function queueStream(tabId, streamData) {
  const payload = {
    url: streamData.url,
    kind: streamData.kind,
    confidence: streamData.confidence || 'high',
    sizeBytes: streamData.sizeBytes ?? null,
    isPartial: Boolean(streamData.isPartial),
    referer: streamData.referer || null,
    userAgent: streamData.userAgent || null,
    cookie: streamData.cookie || null,
    origin: streamData.origin || null,
    headers: Array.isArray(streamData.headers) ? streamData.headers : [],
    pageUrl: streamData.pageUrl || (tabId ? tabPages.get(tabId) : null) || null,
    pageTitle: streamData.pageTitle || null,
    isCloudflare: Boolean(streamData.isCloudflare),
    tabId: tabId && tabId > 0 ? tabId : null,
    timestamp: Date.now()
  };

  // High-priority targets (manifests/abyss) flush immediately so the user
  // sees them on popup open without delay.
  if (payload.kind === 'HLS' || payload.kind === 'DASH' || payload.kind === 'MSS' || payload.kind === 'Abyss') {
    pendingStreams.push({ tabId, payload });
    flushPending();
    return;
  }

  pendingStreams.push({ tabId, payload });
  if (pendingStreams.length >= 5) {
    flushPending();
  } else if (!flushTimer) {
    // Armed once and never restarted. Resetting it on every arrival turned a
    // capped delay into a debounce that a page fetching media steadily could
    // push back for as long as it kept fetching, so nothing reached the popup
    // until five had piled up. Manifests skip this path entirely; Media does
    // not, which is why an .mp4 page was the one that looked stuck.
    flushTimer = setTimeout(flushPending, 1500);
  }
}

// 1. Capture outgoing headers so a detected stream can be replayed.
chrome.webRequest.onSendHeaders.addListener(
  (details) => {
    if (!details.requestHeaders) return;

    // requestId is unique per request and, unlike the URL, cannot collide
    // between two tabs requesting the same manifest at once (M3) — Chrome
    // guarantees it is present on every webRequest event, so there is no
    // fallback key worth keeping a second, cross-tab-collidable copy for.
    inFlightHeaders.set(details.requestId, buildHeaderPayload(details.requestHeaders));
    pruneInflight(inFlightHeaders, { maxInflight: MAX_INFLIGHT, ttlMs: INFLIGHT_TTL_MS });
  },
  { urls: ['<all_urls>'] },
  ['requestHeaders', 'extraHeaders']
);

// 2. Classify responses.
chrome.webRequest.onHeadersReceived.addListener(
  (details) => {
    let contentType = null;
    let contentLength = null;
    let contentRange = null;
    let contentDisposition = null;
    let isCloudflare = false;

    if (details.responseHeaders) {
      for (const header of details.responseHeaders) {
        const name = header.name.toLowerCase();
        if (name === 'content-type') contentType = header.value;
        else if (name === 'content-length') contentLength = header.value;
        else if (name === 'content-range') contentRange = header.value;
        else if (name === 'content-disposition') contentDisposition = header.value;
        else if (name === 'cf-ray') isCloudflare = true;
        else if (name === 'server' && header.value && header.value.toLowerCase().includes('cloudflare')) isCloudflare = true;
      }
    }

    const dispositionFilename = extractDispositionFilename(contentDisposition);
    const result = classify(details.url, contentType, details.statusCode, details.type, dispositionFilename);
    if (!result) return;

    const headers = inFlightHeaders.get(details.requestId) || {};
    const { sizeBytes, isPartial } = totalSizeFrom(contentLength, contentRange, details.statusCode);

    queueStream(details.tabId, {
      url: details.url,
      kind: result.kind,
      confidence: result.confidence,
      sizeBytes,
      isPartial,
      referer: headers.referer || (details.initiator ? details.initiator + '/' : null),
      userAgent: headers.userAgent || navigator.userAgent,
      cookie: headers.cookie || null,
      origin: headers.origin || null,
      headers: headers.headersArray || [],
      pageUrl: details.tabId ? tabPages.get(details.tabId) : null,
      isCloudflare
    });
  },
  { urls: ['<all_urls>'] },
  ['responseHeaders', 'extraHeaders']
);

// 3. Messages reported by content scripts (DOM elements, deep detector).
// A Map, not an object literal: message.type arrives from a content script and
// is therefore page-influenced, and a plain object would resolve 'constructor',
// '__proto__', 'toString', 'valueOf' and 'hasOwnProperty' to truthy
// Object.prototype members -- letting any page enter this branch with a key
// that is not a detection message at all.
const DETECTION_SOURCES = new Map([
  ['MEDIA_ELEMENT_DETECTED', { confidence: 0, resourceType: 'media' }],
  ['DEEP_MANIFEST_DETECTED', { confidence: 200, resourceType: 'xmlhttprequest' }]
]);

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (!message || typeof message.type !== 'string') return;

  const tabId = sender.tab ? sender.tab.id : null;

  const source = DETECTION_SOURCES.get(message.type);
  if (source) {
    const result = classify(message.url, null, source.confidence, source.resourceType);
    if (!result) {
      sendResponse({ ok: false });
      return true;
    }

    if (sender.tab && sender.tab.url) {
      tabPages.set(tabId, sender.tab.url);
    }

    queueStream(tabId, {
      url: message.url,
      kind: result.kind,
      confidence: result.confidence,
      sizeBytes: null,
      referer: message.referer || (sender.tab ? sender.tab.url : null),
      userAgent: navigator.userAgent,
      pageUrl: sender.tab ? sender.tab.url : null,
      pageTitle: message.pageTitle || null
    });

    sendResponse({ ok: true });
    return true;
  }
});

function originOf(url) {
  try {
    return new URL(url).origin;
  } catch {
    return null;
  }
}

chrome.tabs.onUpdated.addListener((tabId, changeInfo) => {
  if (!changeInfo.url) return;

  tabPages.set(tabId, changeInfo.url);
  const nextOrigin = originOf(changeInfo.url);
  if (!nextOrigin) return;

  const previousOrigin = tabOrigins.get(tabId);
  tabOrigins.set(tabId, nextOrigin);

  if (previousOrigin && previousOrigin !== nextOrigin) {
    clearTab(tabId);
    updateBadge(tabId, 0);
  }
});

// 4. Best-effort cleanup.
chrome.tabs.onRemoved.addListener((tabId) => {
  tabOrigins.delete(tabId);
  tabPages.delete(tabId);
  clearTab(tabId);
  updateBadge(tabId, 0);
});

// One-shot: 1.0.1 wrote captured cookies to storage.local, which persists on
// disk across restarts. Clear anything it left behind.
chrome.runtime.onInstalled.addListener(() => {
  chrome.storage.local.remove(['recent_streams']);
  chrome.storage.local.get(null, (all) => {
    if (!all) return;
    const stale = Object.keys(all).filter((key) => key.startsWith('tab_'));
    if (stale.length > 0) chrome.storage.local.remove(stale);
  });
});
