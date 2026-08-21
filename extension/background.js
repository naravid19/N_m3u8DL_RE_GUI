/**
 * N-RE Stream Bridge — Background Service Worker (Manifest V3)
 *
 * Two capture channels feed one storage layer: webRequest for network traffic,
 * and messages from the content script for DOM media elements.
 */

import { classify } from './lib/classify.js';
import { addStream, clearTab } from './lib/storage.js';
import { totalSizeFrom } from './lib/format.js';

const MAX_INFLIGHT = 300;
const INFLIGHT_TTL_MS = 120000;

// requestUrl -> { referer, userAgent, cookie, origin, at }
// Deliberately in-memory and deliberately lossy: writing this to storage on
// every request on the internet would cost far more than the rare miss when
// the service worker restarts between the two listeners.
const inFlightHeaders = new Map();

/**
 * Bounds the cache. A TTL sweep alone cannot help when 300+ requests are in
 * flight inside the TTL window, so a size backstop follows it. Map iterates in
 * insertion order, so the front is always the oldest entry.
 */
function pruneInFlight() {
  if (inFlightHeaders.size <= MAX_INFLIGHT) return;

  const cutoff = Date.now() - INFLIGHT_TTL_MS;
  for (const [key, value] of inFlightHeaders) {
    if (value.at < cutoff) inFlightHeaders.delete(key);
  }

  while (inFlightHeaders.size > MAX_INFLIGHT) {
    const oldest = inFlightHeaders.keys().next();
    if (oldest.done) break;
    inFlightHeaders.delete(oldest.value);
  }
}

function updateBadge(tabId, count) {
  if (!tabId || tabId <= 0) return;
  chrome.action.setBadgeBackgroundColor({ color: '#5865F2', tabId });
  chrome.action.setBadgeText({ tabId, text: count > 0 ? String(count) : '' });
}

async function register(tabId, streamData) {
  try {
    const count = await addStream(tabId, {
      url: streamData.url,
      kind: streamData.kind,
      confidence: streamData.confidence || 'high',
      sizeBytes: streamData.sizeBytes ?? null,
      isPartial: Boolean(streamData.isPartial),
      referer: streamData.referer || null,
      userAgent: streamData.userAgent || null,
      cookie: streamData.cookie || null,
      origin: streamData.origin || null,
      tabId: tabId && tabId > 0 ? tabId : null,
      timestamp: Date.now()
    });
    updateBadge(tabId, count);
  } catch (err) {
    console.error('[N_m3u8DL-RE] Error storing stream:', err);
  }
}

// 1. Capture outgoing headers so a detected stream can be replayed.
chrome.webRequest.onSendHeaders.addListener(
  (details) => {
    if (!details.requestHeaders) return;

    let referer = null;
    let userAgent = null;
    let cookie = null;
    let origin = null;

    for (const header of details.requestHeaders) {
      const name = header.name.toLowerCase();
      if (name === 'referer') referer = header.value;
      else if (name === 'user-agent') userAgent = header.value;
      else if (name === 'cookie') cookie = header.value;
      else if (name === 'origin') origin = header.value;
    }

    inFlightHeaders.set(details.url, { referer, userAgent, cookie, origin, at: Date.now() });
    pruneInFlight();
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
    if (details.responseHeaders) {
      for (const header of details.responseHeaders) {
        const name = header.name.toLowerCase();
        if (name === 'content-type') contentType = header.value;
        else if (name === 'content-length') contentLength = header.value;
        else if (name === 'content-range') contentRange = header.value;
      }
    }

    const result = classify(details.url, contentType, details.statusCode, details.type);
    if (!result) return;

    const headers = inFlightHeaders.get(details.url) || {};
    const { sizeBytes, isPartial } = totalSizeFrom(contentLength, contentRange, details.statusCode);

    register(details.tabId, {
      url: details.url,
      kind: result.kind,
      confidence: result.confidence,
      sizeBytes,
      isPartial,
      referer: headers.referer || (details.initiator ? details.initiator + '/' : null),
      userAgent: headers.userAgent || navigator.userAgent,
      cookie: headers.cookie || null,
      origin: headers.origin || null
    });
  },
  { urls: ['<all_urls>'] },
  ['responseHeaders', 'extraHeaders']
);

// 3. DOM media elements reported by the content script. Status 0 means "found
//    in the page", not "server returned 0".
chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message && message.type === 'MEDIA_ELEMENT_DETECTED') {
    const tabId = sender.tab ? sender.tab.id : null;
    const result = classify(message.url, null, 0, 'media');
    if (!result) {
      sendResponse({ ok: false });
      return true;
    }

    register(tabId, {
      url: message.url,
      kind: result.kind,
      confidence: result.confidence,
      sizeBytes: null,
      referer: message.referer || (sender.tab ? sender.tab.url : null),
      userAgent: navigator.userAgent
    });

    sendResponse({ ok: true });
    return true;
  }

  if (message && message.type === 'CLEAR_STREAMS' && message.tabId) {
    clearTab(message.tabId).then(() => {
      updateBadge(message.tabId, 0);
      sendResponse({ ok: true });
    });
    return true;
  }
});

// Per-tab origin, so a hash or query change during playback is not mistaken
// for a navigation. v1.0.1 cleared on any URL change and wiped streams the
// moment a player rewrote the hash.
const tabOrigins = new Map();

function originOf(url) {
  try {
    return new URL(url).origin;
  } catch {
    return null;
  }
}

chrome.tabs.onUpdated.addListener((tabId, changeInfo) => {
  if (!changeInfo.url) return;

  const nextOrigin = originOf(changeInfo.url);
  if (!nextOrigin) return;

  const previousOrigin = tabOrigins.get(tabId);
  tabOrigins.set(tabId, nextOrigin);

  if (previousOrigin && previousOrigin !== nextOrigin) {
    clearTab(tabId);
    updateBadge(tabId, 0);
  }
});

// 4. Best-effort cleanup. The popup sweeps whatever this misses.
chrome.tabs.onRemoved.addListener((tabId) => {
  tabOrigins.delete(tabId);
  clearTab(tabId);
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
