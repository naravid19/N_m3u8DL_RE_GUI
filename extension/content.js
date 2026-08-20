/**
 * N_m3u8DL-RE Companion — Content Script (All Frames)
 *
 * Runs on top page and inside player iframes.
 * Detects HTML5 video elements, sources, and media playback events.
 */

(function () {
  const seenUrls = new Set();

  function reportStream(url, kindHint) {
    if (!url || typeof url !== 'string') return;
    if (url.startsWith('blob:') || url.startsWith('data:') || url.startsWith('javascript:')) return;
    if (!url.startsWith('http://') && !url.startsWith('https://')) return;
    // Filter out synthetic player URLs that use hash fragments for internal routing (e.g. Hydrax/Abysscdn #mp4/...)
    if (url.includes('#mp4/') || url.includes('#hls/') || url.includes('#chunk') || url.includes('maxChunkSize=')) return;

    if (seenUrls.has(url)) return;
    seenUrls.add(url);

    try {
      chrome.runtime.sendMessage({
        type: 'MEDIA_ELEMENT_DETECTED',
        url: url,
        referer: window.location.href,
        kindHint: kindHint || null
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

    // Check nested <source> tags
    if (el.querySelectorAll) {
      const sources = el.querySelectorAll('source');
      for (const s of sources) {
        if (s.src) reportStream(s.src, s.type);
      }
    }
  }

  function scanAllMedia() {
    document.querySelectorAll('video, audio, source').forEach(checkMediaElement);
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
})();
