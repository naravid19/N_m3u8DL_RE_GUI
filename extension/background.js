/**
 * N_m3u8DL-RE Companion — Background Service Worker (Manifest V3)
 *
 * Observes browser network requests in real-time, extracts stream manifests/media,
 * and maintains detected stream state in chrome.storage.session across service worker restarts.
 */

const SEGMENT_EXTENSIONS = new Set([
  '.ts', '.m4s', '.aac', '.mp3', '.vtt', '.cmfv', '.cmfa', '.cmft', '.init', '.key'
]);

// Ephemeral in-flight headers cache: requestUrl -> { referer, userAgent, cookie }
const pendingHeaders = new Map();

/**
 * Classify a stream using the exact same rules as HarStreamExtractor.Classify in Core.
 */
function classify(url, mimeType, status) {
  let pathname = '';
  try {
    const parsed = new URL(url);
    if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') {
      return null;
    }
    pathname = parsed.pathname.toLowerCase();
  } catch {
    return null;
  }

  // Segment extensions are excluded even when MIME is video/*
  const dotIndex = pathname.lastIndexOf('.');
  const ext = dotIndex !== -1 ? pathname.substring(dotIndex) : '';
  if (SEGMENT_EXTENSIONS.has(ext)) {
    return null;
  }

  if (pathname.endsWith('.m3u8') || pathname.endsWith('.m3u')) {
    return 'HLS';
  }

  if (pathname.endsWith('.mpd')) {
    return 'DASH';
  }

  const mime = (mimeType || '').toLowerCase();
  if (mime.includes('mpegurl')) {
    return 'HLS';
  }
  if (mime.includes('dash+xml')) {
    return 'DASH';
  }

  if (status === 200 || status === 206) {
    if (pathname.endsWith('.mp4') || pathname.endsWith('.webm') || pathname.endsWith('.mkv')) {
      return 'Media';
    }
    if (mime.startsWith('video/')) {
      return 'Media';
    }
  }

  return null;
}

// 1. Capture outgoing request headers
chrome.webRequest.onSendHeaders.addListener(
  (details) => {
    if (!details.requestHeaders || details.tabId < 0) return;

    let referer = null;
    let userAgent = null;
    let cookie = null;

    for (const h of details.requestHeaders) {
      const name = h.name.toLowerCase();
      if (name === 'referer') referer = h.value;
      else if (name === 'user-agent') userAgent = h.value;
      else if (name === 'cookie') cookie = h.value;
    }

    pendingHeaders.set(details.url, { referer, userAgent, cookie, at: Date.now() });

    // Prune stale pending entries (> 2 minutes)
    if (pendingHeaders.size > 200) {
      const now = Date.now();
      for (const [key, val] of pendingHeaders.entries()) {
        if (now - val.at > 120000) {
          pendingHeaders.delete(key);
        }
      }
    }
  },
  { urls: ['<all_urls>'] },
  ['requestHeaders', 'extraHeaders']
);

// 2. Classify response and record stream candidates in session storage
chrome.webRequest.onHeadersReceived.addListener(
  async (details) => {
    if (details.tabId < 0) return;

    let contentType = null;
    if (details.responseHeaders) {
      for (const h of details.responseHeaders) {
        if (h.name.toLowerCase() === 'content-type') {
          contentType = h.value;
          break;
        }
      }
    }

    const kind = classify(details.url, contentType, details.statusCode);
    if (!kind) return;

    const headers = pendingHeaders.get(details.url) || {};
    const key = `streams:${details.tabId}`;

    try {
      const stored = await chrome.storage.session.get(key);
      const list = stored[key] || [];

      // Deduplicate by exact URL, preserving the first occurrence
      if (!list.some((s) => s.url === details.url)) {
        list.push({
          url: details.url,
          kind: kind,
          referer: headers.referer || null,
          userAgent: headers.userAgent || null,
          cookie: headers.cookie || null,
          at: Date.now()
        });

        await chrome.storage.session.set({ [key]: list });
        updateBadge(details.tabId, list.length);
      }
    } catch (err) {
      console.error('Failed to update session streams', err);
    }
  },
  { urls: ['<all_urls>'] },
  ['responseHeaders']
);

function updateBadge(tabId, count) {
  if (tabId < 0) return;
  chrome.action.setBadgeBackgroundColor({ color: '#5865F2', tabId });
  chrome.action.setBadgeText({
    tabId,
    text: count > 0 ? String(count) : ''
  });
}

// 3. Tab lifecycle cleanup
chrome.tabs.onUpdated.addListener((tabId, changeInfo) => {
  if (changeInfo.url) {
    const key = `streams:${tabId}`;
    chrome.storage.session.remove(key);
    updateBadge(tabId, 0);
  }
});

chrome.tabs.onRemoved.addListener((tabId) => {
  const key = `streams:${tabId}`;
  chrome.storage.session.remove(key);
});
