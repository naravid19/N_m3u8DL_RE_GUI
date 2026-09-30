/**
 * N-RE Stream Bridge — Content Script (All Frames)
 *
 * Runs on top page and inside player iframes.
 * Detects HTML5 video elements, sources, and media playback events.
 */

(function () {
  const MAX_SEEN = 200;
  const seenUrls = new Set();

  function rememberUrl(url) {
    if (seenUrls.has(url)) return false;
    // Set iterates in insertion order, so the first key is the oldest.
    if (seenUrls.size >= MAX_SEEN) {
      seenUrls.delete(seenUrls.values().next().value);
    }
    seenUrls.add(url);
    return true;
  }

  function shouldReport(url) {
    if (typeof globalThis !== 'undefined' && globalThis.NRE_MEDIA_URL?.shouldReportUrl) {
      return globalThis.NRE_MEDIA_URL.shouldReportUrl(url);
    }
    if (!url || typeof url !== 'string') return false;
    if (url.startsWith('blob:') || url.startsWith('data:') || url.startsWith('javascript:')) return false;
    if (!url.startsWith('http://') && !url.startsWith('https://')) return false;
    if (url.includes('#mp4/') || url.includes('#hls/') || url.includes('#chunk') || url.includes('maxChunkSize=')) return false;
    return true;
  }

  function sanitizeWindowsFilename(title) {
    if (!title || typeof title !== 'string') return '';
    let safe = title.replace(/[\\/:*?"<>|\x00-\x1f]/g, '_');
    safe = safe.replace(/[\u200B\u200C\u200D\uFEFF]/g, '');
    safe = safe.replace(/^[.\s]+|[.\s]+$/g, '');
    return safe || '';
  }

  function getPageTitle() {
    try {
      const ogTitle = document.querySelector('meta[property="og:title"]')?.content;
      const twTitle = document.querySelector('meta[name="twitter:title"]')?.content;
      const raw = ogTitle || twTitle || document.title || '';
      return sanitizeWindowsFilename(raw);
    } catch {
      return '';
    }
  }

  function reportStream(url, kindHint) {
    if (!shouldReport(url)) return;

    if (!rememberUrl(url)) return;

    try {
      chrome.runtime.sendMessage({
        type: 'MEDIA_ELEMENT_DETECTED',
        url: url,
        referer: window.location.href,
        kindHint: kindHint || null,
        pageTitle: getPageTitle() || null
      });
    } catch {
      // Extension context invalidated or inactive
    }
  }

  function checkMediaElement(el) {
    if (!el) return;

    // Check src attribute
    if (el.src) {
      reportStream(el.src);
    }
    // Check currentSrc property (often populated on play)
    if (el.currentSrc) {
      reportStream(el.currentSrc);
    }

    // Check custom data attributes commonly used by video players (hls.js, video.js, plyr, etc.)
    if (el.getAttribute) {
      const candidates = [
        'data-src', 'data-url', 'data-video-url', 'data-hls', 'data-dash',
        'data-stream', 'data-mpd', 'data-m3u8', 'data-file', 'data-source'
      ];
      for (const attr of candidates) {
        const val = el.getAttribute(attr);
        if (val && typeof val === 'string') {
          reportStream(val);
        }
      }
    }

    // Check nested <source> tags
    if (el.querySelectorAll) {
      const sources = el.querySelectorAll('source');
      for (const s of sources) {
        if (s.src) reportStream(s.src, s.type);
        if (s.getAttribute) {
          const sVal = s.getAttribute('data-src') || s.getAttribute('src');
          if (sVal) reportStream(sVal, s.type);
        }
      }
    }
  }

  // Abyss/Hydrax player pages, whether embedded in an iframe or loaded as the frame itself.
  const ABYSS_PLAYER_MARKERS = ['abysscdn.com/?v=', 'playhydrax.com/?v=', 'zplayer.io/?v=', 'abyss.to/?v=', 'short.ink/'];

  function isAbyssPlayerUrl(url) {
    const lower = url.toLowerCase();
    return ABYSS_PLAYER_MARKERS.some((marker) => lower.includes(marker));
  }

  function scanAllMedia() {
    // 1. Scan HTML5 video/audio elements
    document.querySelectorAll('video, audio, source').forEach(checkMediaElement);

    // 2. Scan iframes for embedded players (e.g. Abyss, Hydrax, Marimo, TonyTonyChopper)
    document.querySelectorAll('iframe').forEach((iframe) => {
      try {
        const src = iframe.src || iframe.getAttribute('data-src') || iframe.getAttribute('src');
        if (src && typeof src === 'string') {
          if (isAbyssPlayerUrl(src)) {
            reportStream(src, 'Abyss');
          }
        }
      } catch {
        // Cross-origin iframe security
      }
    });

    // 3. If the current frame itself is an Abyss player page
    try {
      const currentUrl = window.location.href;
      if (isAbyssPlayerUrl(currentUrl)) {
        reportStream(currentUrl, 'Abyss');
      }
    } catch {}
  }

  // 1. Initial scan on load
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', scanAllMedia);
  } else {
    scanAllMedia();
  }

  // 2. Event listeners for dynamically playing videos
  document.addEventListener('play', (e) => checkMediaElement(e.target), true);
  document.addEventListener('loadstart', (e) => checkMediaElement(e.target), true);
  document.addEventListener('loadeddata', (e) => checkMediaElement(e.target), true);
  document.addEventListener('canplay', (e) => checkMediaElement(e.target), true);

  // 3. MutationObserver for video elements added dynamically (e.g. by JS players)
  const observer = new MutationObserver((mutations) => {
    for (const m of mutations) {
      for (const node of m.addedNodes) {
        if (node.nodeType === 1) {
          if (node.tagName === 'VIDEO' || node.tagName === 'AUDIO' || node.tagName === 'SOURCE') {
            checkMediaElement(node);
          } else if (node.querySelectorAll) {
            node.querySelectorAll('video, audio, source').forEach(checkMediaElement);
          }
        }
      }
    }
  });

  if (document.documentElement) {
    observer.observe(document.documentElement, { childList: true, subtree: true });
  }

  // 4. Relay deep detector messages from MAIN world
  window.addEventListener('message', (event) => {
    if (event.source !== window || !event.data || event.data.source !== 'NRE_DEEP_DETECTOR') return;
    const payload = event.data.payload;
    if (!payload || !payload.type) return;

    if (payload.type === 'DEEP_MANIFEST_DETECTED') {
      if (payload.url && !rememberUrl(payload.url)) return;
      if (!payload.pageTitle) {
        payload.pageTitle = getPageTitle() || null;
      }
    }

    try {
      chrome.runtime.sendMessage(payload);
    } catch {
      // Extension context invalidated
    }
  });
})();
