/**
 * The one place that fetches a manifest. On demand only — the user clicks
 * "Qualities" on a row. Nothing here runs automatically, because probing every
 * detected stream would mean an unprompted request to every CDN the user's
 * browser touched.
 */

import { parseHlsMaster, parseDashManifest } from './manifest.js';
import { parseMssManifest } from './mss.js';
import { getCachedVariants, setCachedVariants } from './storage.js';
import { selectParser, describeProbeFailure } from './probe-policy.js';

const TIMEOUT_MS = 8000;
const MAX_BYTES = 2 * 1024 * 1024; // 2 MB limit

/**
 * Fetches the manifest from inside the capturing page.
 *
 * fetch() from an extension context cannot set Referer, Origin, Cookie or
 * User-Agent — they are forbidden header names and the browser drops them
 * silently. Rather than try to forge them, the request runs where they are
 * already true: the page that fetched the same manifest moments ago. It
 * inherits that page's origin, referrer policy and first-party cookies.
 */
async function fetchFromPage(tabId, url, timeoutMs) {
  try {
    if (typeof chrome === 'undefined' || !chrome?.scripting?.executeScript) {
      return { ok: false, status: 0, error: 'Scripting API unavailable' };
    }

    // ponytail: MAIN world is required, not preferred — since Chrome 85 an
    // ISOLATED-world fetch uses the extension's own origin and would not
    // inherit the page's referrer or first-party cookies, which is the entire
    // reason for injecting. The cost is that this runs as page script, so a
    // page that overrides window.fetch could feed a fabricated manifest or
    // detect the probe. Accepted; do not "fix" this to ISOLATED.
    const [injection] = await chrome.scripting.executeScript({
      target: { tabId },
      world: 'MAIN',
      args: [url, timeoutMs, MAX_BYTES],
      func: async (target, limit, maxBytes) => {
        const controller = new AbortController();
        const timer = setTimeout(() => controller.abort(), limit);
        try {
          const response = await fetch(target, {
            credentials: 'include',
            signal: controller.signal
          });
          if (!response.ok) return { ok: false, status: response.status };

          const declared = Number.parseInt(response.headers.get('content-length') || '', 10);
          if (Number.isFinite(declared) && declared > maxBytes) {
            return { ok: false, status: response.status, error: 'TooLarge' };
          }

          if (!response.body || typeof response.body.getReader !== 'function') {
            const text = await response.text();
            if (text.length > maxBytes) return { ok: false, status: response.status, error: 'TooLarge' };
            return { ok: true, status: response.status, text };
          }

          const reader = response.body.getReader();
          const chunks = [];
          let totalBytes = 0;
          while (true) {
            const { done, value } = await reader.read();
            if (done) break;
            totalBytes += (value.byteLength || value.length || 0);
            if (totalBytes > maxBytes) {
              try { await reader.cancel(); } catch {}
              return { ok: false, status: response.status, error: 'TooLarge' };
            }
            chunks.push(value);
          }
          const decoder = new TextDecoder('utf-8');
          let text = '';
          for (const chunk of chunks) {
            text += decoder.decode(chunk, { stream: true });
          }
          text += decoder.decode();
          return { ok: true, status: response.status, text };
        } catch (err) {
          const msg = String(err && (err.name || err.message));
          return { ok: false, status: 0, error: msg.includes('TooLarge') ? 'TooLarge' : msg };
        } finally {
          clearTimeout(timer);
        }
      }
    });

    return injection?.result ?? { ok: false, status: 0, error: 'Injection failed' };
  } catch (err) {
    return { ok: false, status: 0, error: err.message || 'Script injection error' };
  }
}

/**
 * Direct fetch fallback when the tab is gone or unavailable.
 */
async function fetchDirect(url, timeoutMs) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);
  try {
    const response = await fetch(url, {
      credentials: 'include',
      signal: controller.signal
    });
    if (!response.ok) return { ok: false, status: response.status };

    const declared = Number.parseInt(response.headers.get('content-length') || '', 10);
    if (Number.isFinite(declared) && declared > MAX_BYTES) {
      return { ok: false, status: response.status, error: 'TooLarge' };
    }

    if (!response.body || typeof response.body.getReader !== 'function') {
      const text = await response.text();
      if (text.length > MAX_BYTES) return { ok: false, status: response.status, error: 'TooLarge' };
      return { ok: true, status: response.status, text };
    }

    const reader = response.body.getReader();
    const chunks = [];
    let totalBytes = 0;
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;
      totalBytes += (value.byteLength || value.length || 0);
      if (totalBytes > MAX_BYTES) {
        try { await reader.cancel(); } catch {}
        return { ok: false, status: response.status, error: 'TooLarge' };
      }
      chunks.push(value);
    }
    const decoder = new TextDecoder('utf-8');
    let text = '';
    for (const chunk of chunks) {
      text += decoder.decode(chunk, { stream: true });
    }
    text += decoder.decode();
    return { ok: true, status: response.status, text };
  } catch (err) {
    if (err && err.name === 'AbortError') return { ok: false, status: 0, error: 'Timeout' };
    return { ok: false, status: 0, error: String(err && err.message) };
  } finally {
    clearTimeout(timer);
  }
}

/**
 * Fetches and parses variants for a given manifest stream on demand.
 * Probes from the page context (inheriting auth/cookies) with direct fallback,
 * caching results in session storage (P9). { fresh: true } skips the cache read,
 * so a Retry is not answered by the failure it is retrying.
 * Returns { variants: Variant[], error: string|null }.
 */
export async function probeVariants(stream, tabId = null, options = {}) {
  const fetchPage = options.fetchFromPage ?? fetchFromPage;
  const fetchDirect_ = options.fetchDirect ?? fetchDirect;

  if (!stream || !stream.url) {
    return { variants: [], error: 'Invalid stream' };
  }

  // Check session cache first (P9)
  if (!options.fresh) {
    try {
      const cached = await getCachedVariants(stream.url);
      if (cached) {
        return cached;
      }
    } catch {
      // ignore cache lookup error in unit tests / standalone contexts
    }
  }

  let fetchResult = null;
  const targetTabId = tabId || stream.tabId || null;

  if (targetTabId && (options.fetchFromPage || (typeof chrome !== 'undefined' && chrome?.scripting?.executeScript))) {
    fetchResult = await fetchPage(targetTabId, stream.url, TIMEOUT_MS);
  }

  // Fall back if tab was closed, inaccessible, or no tabId
  if (!fetchResult || !fetchResult.ok) {
    const directResult = await fetchDirect_(stream.url, TIMEOUT_MS);
    if (directResult.ok) {
      fetchResult = directResult;
    } else {
      const errorMsg = describeProbeFailure({
        hadTab: Boolean(targetTabId),
        pageResult: fetchResult,
        directResult
      });

      const failResult = { variants: [], error: errorMsg };
      try {
        await setCachedVariants(stream.url, failResult);
      } catch {}
      return failResult;
    }
  }

  const text = fetchResult.text;
  if (!text || typeof text !== 'string') {
    const emptyResult = { variants: [], error: 'Empty manifest response' };
    try {
      await setCachedVariants(stream.url, emptyResult);
    } catch {}
    return emptyResult;
  }

  // Size guard: accurate byte length (P8) backstop
  const byteLength = typeof TextEncoder !== 'undefined' ? new TextEncoder().encode(text).byteLength : text.length;
  if (byteLength > MAX_BYTES) {
    const tooLargeResult = { variants: [], error: 'Manifest exceeds maximum size limit (2 MB)' };
    try {
      await setCachedVariants(stream.url, tooLargeResult);
    } catch {}
    return tooLargeResult;
  }

  const parser = selectParser(stream.kind, text);
  let variants = [];

  if (parser === 'hls') {
    variants = parseHlsMaster(text, stream.url);
  } else if (parser === 'dash') {
    variants = parseDashManifest(text, stream.url);
  } else if (parser === 'mss') {
    variants = parseMssManifest(text, stream.url);
  }

  const successResult = { variants, error: null };
  try {
    await setCachedVariants(stream.url, successResult);
  } catch {}
  return successResult;
}
