/**
 * The one place that fetches a manifest. On demand only — the user clicks
 * "Qualities" on a row. Nothing here runs automatically, because probing every
 * detected stream would mean an unprompted request to every CDN the user's
 * browser touched.
 */

import { parseHlsMaster, parseDashManifest } from './manifest.js';

const TIMEOUT_MS = 8000;
const MAX_BYTES = 2 * 1024 * 1024; // 2 MB limit

/**
 * Fetches and parses variants for a given manifest stream on demand.
 * Replays captured headers (Referer, Cookie, etc.) to satisfy CDN auth.
 * Returns { variants: Variant[], error: string|null }.
 */
export async function probeVariants(stream) {
  if (!stream || !stream.url) {
    return { variants: [], error: 'Invalid stream' };
  }

  const headers = {};
  if (stream.referer) headers.Referer = stream.referer;
  if (stream.userAgent) headers['User-Agent'] = stream.userAgent;
  if (stream.cookie) headers.Cookie = stream.cookie;
  if (stream.origin) headers.Origin = stream.origin;

  const controller = new AbortController();
  const timeoutId = setTimeout(() => controller.abort(), TIMEOUT_MS);

  try {
    const response = await fetch(stream.url, {
      method: 'GET',
      headers,
      signal: controller.signal
    });

    if (!response.ok) {
      return { variants: [], error: `Server returned HTTP ${response.status}` };
    }

    const contentLength = response.headers.get('content-length');
    if (contentLength && Number.parseInt(contentLength, 10) > MAX_BYTES) {
      return { variants: [], error: 'Manifest exceeds maximum size limit (2 MB)' };
    }

    const text = await response.text();
    if (text.length > MAX_BYTES) {
      return { variants: [], error: 'Manifest exceeds maximum size limit (2 MB)' };
    }

    let variants = [];
    const kind = (stream.kind || '').toUpperCase();

    if (kind === 'HLS') {
      variants = parseHlsMaster(text, stream.url);
    } else if (kind === 'DASH' || kind === 'MSS') {
      variants = parseDashManifest(text, stream.url);
    } else {
      // Sniff content
      if (text.includes('#EXTM3U')) {
        variants = parseHlsMaster(text, stream.url);
      } else if (/<MPD/i.test(text)) {
        variants = parseDashManifest(text, stream.url);
      }
    }

    return { variants, error: null };
  } catch (err) {
    if (err && err.name === 'AbortError') {
      return { variants: [], error: 'Request timed out (8s)' };
    }
    return { variants: [], error: err.message || 'Could not fetch manifest' };
  } finally {
    clearTimeout(timeoutId);
  }
}
