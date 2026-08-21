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
  ['.mpd', 'DASH'],
  ['.ism', 'MSS'],
  ['.isml', 'MSS']
]);

const MEDIA_EXTENSIONS = new Set([
  '.mp4', '.m4v', '.webm', '.mkv', '.mov', '.flv', '.ogv', '.3gp'
]);

/** Format hints a CDN may put in the query when the path carries no extension. */
const QUERY_HINTS = new Map([
  ['m3u8', 'HLS'],
  ['hls', 'HLS'],
  ['mpd', 'DASH'],
  ['dash', 'DASH'],
  ['ism', 'MSS'],
  ['mss', 'MSS']
]);

const high = (kind) => ({ kind, confidence: 'high' });
const low = (kind) => ({ kind, confidence: 'low' });

function classifyByMime(mime) {
  if (mime.includes('mpegurl')) return 'HLS';
  if (mime.includes('dash+xml') || mime.includes('dash.mpd')) return 'DASH';
  if (mime.includes('sstr+xml')) return 'MSS';
  return null;
}

/**
 * Last resort for CDNs that pass the real filename or a format flag through the
 * query string. Deliberately restricted to manifest kinds: promoting a media
 * extension found in a query is the F4 bug, where a tracking pixel carrying an
 * encoded .mp4 was reported as a downloadable video.
 */
function classifyByQuery(searchParams) {
  if (!searchParams) return null;
  for (const value of searchParams.values()) {
    const lower = value.toLowerCase();

    for (const [ext, kind] of MANIFEST_EXTENSIONS) {
      if (lower.endsWith(ext)) return kind;
    }

    const hint = QUERY_HINTS.get(lower);
    if (hint) return hint;
  }
  return null;
}

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
    host: parsed.hostname.toLowerCase(),
    search: parsed.searchParams
  };
}

/** Statuses that mean the server actually served the thing. 0 is the
 *  content-script path, where no HTTP exchange was observed. */
const USABLE_STATUSES = new Set([0, 200, 206]);

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
    return high('Abyss');
  }
  // --- end unchanged block ---

  if (!USABLE_STATUSES.has(status)) return null;

  const mime = (mimeType || '').toLowerCase();

  // Manifests win outright: a playlist served from a directory called
  // "assets.ts" is still a playlist.
  const byExtension = MANIFEST_EXTENSIONS.get(parts.ext);
  if (byExtension) return high(byExtension);

  const byMime = classifyByMime(mime);
  if (byMime) return high(byMime);

  // MSS commonly has no extension at all — the path just ends in /Manifest.
  if (/\/manifest$/.test(parts.path)) return high('MSS');

  // Segments are excluded only after manifests have had every chance.
  if (SEGMENT_EXTENSIONS.has(parts.ext)) return null;

  if (MEDIA_EXTENSIONS.has(parts.ext)) return high('Media');
  if (mime.startsWith('video/')) return high('Media');

  // Below here we are guessing. Manifest kinds only.
  const byQuery = classifyByQuery(parts.search);
  if (byQuery) return low(byQuery);

  if (type === 'media') return low('Media');

  return null;
}
