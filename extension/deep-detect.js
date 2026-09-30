/**
 * N-RE Stream Bridge — Deep In-Page Detector (MAIN World)
 *
 * Runs inside the page execution context (MAIN world).
 * Hooks JSON.parse to uncover manifest URLs delivered inside JSON API
 * responses that never appear as a network request of their own.
 *
 * Cleanroom implementation of public Web API inspection techniques.
 */

(function installDeepDetector() {
  if (typeof window === 'undefined' || window.__NRE_DEEP_DETECTOR__) return;
  window.__NRE_DEEP_DETECTOR__ = true;

  const MANIFEST_SUFFIXES = ['.m3u8', '.mpd', '.ism', '.isml'];
  const MAX_DEPTH = 8;
  /** Whole-walk ceiling. Depth alone bounds nothing: a flat array of 100k
   *  strings sits at depth 1, and this runs on the page's main thread for
   *  every JSON.parse the page makes. */
  const MAX_NODES = 5000;
  /** A manifest URL is a URL. Anything longer is prose, minified source, or a
   *  data: blob, and scanning it cannot pay off. */
  const MAX_STRING = 2048;
  const MAX_SEEN = 512;

  const seenUrls = new Set();

  function rememberUrl(url) {
    if (seenUrls.has(url)) return false;
    // Insertion order: the first key is the oldest. Same policy content.js
    // already uses -- these two caches should not behave differently.
    if (seenUrls.size >= MAX_SEEN) {
      seenUrls.delete(seenUrls.values().next().value);
    }
    seenUrls.add(url);
    return true;
  }

  try {
    window.__NRE_DEEP_DETECTOR_STATE__ = {
      seenSize: () => seenUrls.size
    };
  } catch {}

  /**
   * MAIN world has no extension messaging API -- chrome.runtime.sendMessage
   * without an extension id needs externally_connectable, which this
   * extension does not declare, so that call only ever threw into a catch.
   * content.js listens for exactly this message on the window.
   */
  function postDetection(payload) {
    try {
      if (typeof window !== 'undefined' && window.postMessage) {
        // Target origin '*' is intentional: the detector runs with all_frames: true,
        // which includes sandboxed or about:blank player frames where window.location.origin
        // is opaque and serialises to "null". Restricting targetOrigin breaks detection
        // in opaque-origin frames, while '*' is safe here because the payload originates
        // from the page's own memory and content.js checks event.source === window.
        window.postMessage({ source: 'NRE_DEEP_DETECTOR', payload }, '*');
      }
    } catch {}
  }

  function looksLikeManifestUrl(str) {
    if (!str || typeof str !== 'string' || str.length < 8 || str.length > MAX_STRING) return null;
    if (!str.startsWith('http://') && !str.startsWith('https://') && !str.startsWith('//')) return null;
    const clean = str.toLowerCase().split('?')[0].split('#')[0];
    for (const ext of MANIFEST_SUFFIXES) {
      if (clean.endsWith(ext)) return ext.slice(1);
    }
    return null;
  }

  function scanValue(val, depth, budget) {
    if (depth > MAX_DEPTH || val === null || val === undefined) return;
    if (budget.nodes++ > MAX_NODES) return;

    if (typeof val === 'string') {
      if (val.length > MAX_STRING) return;
      const ext = looksLikeManifestUrl(val);
      if (ext && rememberUrl(val)) {
        const resolvedUrl = val.startsWith('//') ? window.location.protocol + val : val;
        postDetection({
          type: 'DEEP_MANIFEST_DETECTED',
          url: resolvedUrl,
          ext,
          referer: window.location.href
        });
      }
    } else if (Array.isArray(val)) {
      for (let i = 0; i < val.length; i++) {
        if (budget.nodes > MAX_NODES) return;
        scanValue(val[i], depth + 1, budget);
      }
    } else if (typeof val === 'object') {
      for (const k in val) {
        if (budget.nodes > MAX_NODES) return;
        if (Object.prototype.hasOwnProperty.call(val, k)) {
          scanValue(val[k], depth + 1, budget);
        }
      }
    }
  }

  // 1. Hook JSON.parse
  if (typeof JSON !== 'undefined' && JSON.parse) {
    const origJsonParse = JSON.parse;
    JSON.parse = function () {
      const result = origJsonParse.apply(this, arguments);
      try {
        scanValue(result, 0, { nodes: 0 });
      } catch {}
      return result;
    };
    try {
      Object.defineProperty(JSON.parse, 'toString', {
        value: function () { return origJsonParse.toString(); },
        configurable: true,
        writable: true
      });
    } catch {}
  }
})();
