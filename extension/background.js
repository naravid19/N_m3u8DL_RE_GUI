/**
 * N_m3u8DL-RE Companion — Background Service Worker (Manifest V3)
 *
 * Captures video streams from network traffic and DOM media elements across tabs & iframes.
 * Stores state safely in chrome.storage.local.
 */

const SEGMENT_EXTENSIONS = [
  '.ts', '.m4s', '.aac', '.mp3', '.vtt', '.cmfv', '.cmfa', '.cmft', '.init', '.key'
];

// Ephemeral in-flight headers cache: requestUrl -> { referer, userAgent, cookie, origin }
const inFlightHeaders = new Map();

/**
 * Classify a stream using robust URL and Content-Type inspection.
 */
function classify(url, mimeType, status, type) {
  if (!url || typeof url !== 'string') return null;
  if (!url.startsWith('http://') && !url.startsWith('https://')) return null;

  const lowerUrl = url.toLowerCase();
  if (lowerUrl.includes('#mp4/') || lowerUrl.includes('#hls/') || lowerUrl.includes('maxchunksize=')) {
    return null;
  }

  // 1. Exclude segments (unless it's an m3u8 / mpd playlist)
  const isManifest = lowerUrl.includes('.m3u8') || lowerUrl.includes('.m3u') || lowerUrl.includes('.mpd');
  if (!isManifest) {
    for (const ext of SEGMENT_EXTENSIONS) {
      if (lowerUrl.includes(ext)) return null;
    }
  }

  // 2. HLS Manifests
  if (lowerUrl.includes('.m3u8') || lowerUrl.includes('.m3u')) {
    return 'HLS';
  }

  // 3. DASH Manifests
  if (lowerUrl.includes('.mpd')) {
    return 'DASH';
  }

  // 4. MIME Type matches
  const mime = (mimeType || '').toLowerCase();
  if (mime.includes('mpegurl') || mime.includes('application/x-mpegurl') || mime.includes('audio/x-mpegurl')) {
    return 'HLS';
  }
  if (mime.includes('dash+xml')) {
    return 'DASH';
  }

  // 5. Progressive Video Streams
  if (
    lowerUrl.includes('.mp4') ||
    lowerUrl.includes('.webm') ||
    lowerUrl.includes('.mkv') ||
    lowerUrl.includes('.mov') ||
    lowerUrl.includes('.flv')
  ) {
    return 'Media';
  }

  if (mime.startsWith('video/') || type === 'media') {
    return 'Media';
  }

  return null;
}

/**
 * Register a detected stream in storage.
 */
async function registerStream(tabId, streamData) {
  const effectiveTabId = tabId && tabId > 0 ? tabId : null;
  const now = Date.now();

  const streamItem = {
    url: streamData.url,
    kind: streamData.kind,
    referer: streamData.referer || null,
    userAgent: streamData.userAgent || null,
    cookie: streamData.cookie || null,
    origin: streamData.origin || null,
    tabId: effectiveTabId,
    timestamp: now
  };

  try {
    const data = await chrome.storage.local.get(null);

    // 1. Per-tab storage
    if (effectiveTabId) {
      const tabKey = `tab_${effectiveTabId}`;
      const tabList = data[tabKey] || [];

      if (!tabList.some((s) => s.url === streamItem.url)) {
        tabList.unshift(streamItem);
        // Keep max 25 per tab
        if (tabList.length > 25) tabList.length = 25;
        await chrome.storage.local.set({ [tabKey]: tabList });
        updateBadge(effectiveTabId, tabList.length);
      }
    }

    // 2. Global recent streams ring-buffer (max 30)
    const recent = data.recent_streams || [];
    if (!recent.some((s) => s.url === streamItem.url)) {
      recent.unshift(streamItem);
      if (recent.length > 30) recent.length = 30;
      await chrome.storage.local.set({ recent_streams: recent });
    }
  } catch (err) {
    console.error('[N_m3u8DL-RE] Error storing stream:', err);
  }
}

function updateBadge(tabId, count) {
  if (!tabId || tabId <= 0) return;
  chrome.action.setBadgeBackgroundColor({ color: '#5865F2', tabId });
  chrome.action.setBadgeText({
    tabId,
    text: count > 0 ? String(count) : ''
  });
}

// 1. Intercept outgoing request headers
chrome.webRequest.onSendHeaders.addListener(
  (details) => {
    if (!details.requestHeaders) return;

    let referer = null;
    let userAgent = null;
    let cookie = null;
    let origin = null;

    for (const h of details.requestHeaders) {
      const name = h.name.toLowerCase();
      if (name === 'referer') referer = h.value;
      else if (name === 'user-agent') userAgent = h.value;
      else if (name === 'cookie') cookie = h.value;
      else if (name === 'origin') origin = h.value;
    }

    inFlightHeaders.set(details.url, { referer, userAgent, cookie, origin, at: Date.now() });

    // Prune stale cache
    if (inFlightHeaders.size > 300) {
      const cutoff = Date.now() - 120000;
      for (const [key, val] of inFlightHeaders.entries()) {
        if (val.at < cutoff) inFlightHeaders.delete(key);
      }
    }
  },
  { urls: ['<all_urls>'] },
  ['requestHeaders', 'extraHeaders']
);

// 2. Intercept responses and classify stream
chrome.webRequest.onHeadersReceived.addListener(
  (details) => {
    let contentType = null;
    if (details.responseHeaders) {
      for (const h of details.responseHeaders) {
        if (h.name.toLowerCase() === 'content-type') {
          contentType = h.value;
          break;
        }
      }
    }

    const kind = classify(details.url, contentType, details.statusCode, details.type);
    if (!kind) return;

    const headers = inFlightHeaders.get(details.url) || {};

    registerStream(details.tabId, {
      url: details.url,
      kind: kind,
      referer: headers.referer || (details.initiator ? details.initiator + '/' : null),
      userAgent: headers.userAgent || navigator.userAgent,
      cookie: headers.cookie || null,
      origin: headers.origin || null
    });
  },
  { urls: ['<all_urls>'] },
  ['responseHeaders', 'extraHeaders']
);

// 3. Listen to messages from content scripts
chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message && message.type === 'MEDIA_ELEMENT_DETECTED') {
    const tabId = sender.tab ? sender.tab.id : null;
    const kind = classify(message.url, null, 200, 'media') || 'Media';

    registerStream(tabId, {
      url: message.url,
      kind: kind,
      referer: message.referer || (sender.tab ? sender.tab.url : null),
      userAgent: navigator.userAgent
    });

    sendResponse({ ok: true });
    return true;
  }

  if (message && message.type === 'CLEAR_STREAMS') {
    const tabId = message.tabId;
    if (tabId) {
      chrome.storage.local.remove(`tab_${tabId}`, () => {
        updateBadge(tabId, 0);
        sendResponse({ ok: true });
      });
      return true;
    }
  }
});

// 4. Tab cleanup only when tab is actually closed
chrome.tabs.onRemoved.addListener((tabId) => {
  chrome.storage.local.remove(`tab_${tabId}`);
});
