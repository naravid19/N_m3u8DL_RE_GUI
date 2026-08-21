/**
 * Pure stream classification. No chrome.* access, so this module imports
 * cleanly under `node --test`.
 */

/** Media segments. Matched on the pathname's extension only — a substring
 *  search hits hostnames and query tokens and silently drops real streams. */
const SEGMENT_EXTENSIONS = new Set([
  '.ts', '.m4s', '.aac', '.mp3', '.vtt', '.cmfv', '.cmfa', '.cmft', '.init', '.key'
]);

const MANIFEST_EXTENSIONS = new Map([
  ['.m3u8', 'HLS'],
  ['.m3u', 'HLS'],
  ['.mpd', 'DASH']
]);

const MEDIA_EXTENSIONS = new Set(['.mp4', '.webm', '.mkv', '.mov', '.flv']);

/** Statuses that mean the server actually served the thing. 0 is the
 *  content-script path, where no HTTP exchange was observed. */
const USABLE_STATUSES = new Set([0, 200, 206]);

/**
 * Splits a URL into the parts classification cares about.
 * Returns null for anything that is not an absolute http(s) URL.
 */
export function parseUrlParts(url) {
  if (!url || typeof url !== 'string') return null;

  let parsed;
  try {
    parsed = new URL(url);
  } catch {
    return null;
  }

  if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') return null;

  const path = parsed.pathname.toLowerCase();
  const lastDot = path.lastIndexOf('.');
  const lastSlash = path.lastIndexOf('/');

  return {
    href: parsed.href,
    path,
    // A dot before the final slash belongs to a directory, not the file.
    ext: lastDot > lastSlash ? path.slice(lastDot) : '',
    host: parsed.hostname.toLowerCase()
  };
}

export function classify(url, mimeType, status, type) {
  const parts = parseUrlParts(url);
  if (!parts) return null;

  const lowerUrl = url.toLowerCase();

  // Synthetic player routing URLs. Only ever reachable from the content-script
  // path — webRequest strips fragments before the listener sees them.
  if (lowerUrl.includes('#mp4/') || lowerUrl.includes('#hls/') || lowerUrl.includes('maxchunksize=')) {
    return null;
  }

  // --- Abyss / Hydrax player pages. Unchanged from v1.0.1 by design. ---
  if (
    lowerUrl.includes('abysscdn.com/?v=') ||
    lowerUrl.includes('playhydrax.com/?v=') ||
    lowerUrl.includes('zplayer.io/?v=') ||
    lowerUrl.includes('abyss.to/?v=') ||
    lowerUrl.includes('short.ink/')
  ) {
    return 'Abyss';
  }
  // --- end unchanged block ---

  if (!USABLE_STATUSES.has(status)) return null;

  const mime = (mimeType || '').toLowerCase();

  // Manifests win outright: a playlist served from a directory called
  // "assets.ts" is still a playlist.
  const manifestKind = MANIFEST_EXTENSIONS.get(parts.ext);
  if (manifestKind) return manifestKind;

  if (mime.includes('mpegurl')) return 'HLS';
  if (mime.includes('dash+xml')) return 'DASH';

  // Segments are excluded only after manifests have had their chance.
  if (SEGMENT_EXTENSIONS.has(parts.ext)) return null;

  if (MEDIA_EXTENSIONS.has(parts.ext)) return 'Media';
  if (mime.startsWith('video/')) return 'Media';
  if (type === 'media') return 'Media';

  return null;
}
