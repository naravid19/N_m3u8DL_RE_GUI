/**
 * Pure policy functions for manifest probing and error formatting.
 */

/**
 * Determines which manifest parser to use based on declared kind or body content.
 * Returns 'hls' | 'dash' | 'mss' | null.
 */
export function selectParser(kind, text) {
  const normKind = (kind || '').trim().toLowerCase();

  if (normKind === 'hls') return 'hls';
  if (normKind === 'dash') return 'dash';
  if (normKind === 'mss') return 'mss';

  if (!text || typeof text !== 'string') return null;

  // Strip leading BOM and whitespace
  const clean = text.replace(/^[\uFEFF\s]+/, '');

  if (clean.includes('#EXTM3U')) return 'hls';
  if (/<MPD/i.test(clean)) return 'dash';
  if (/<SmoothStreamingMedia/i.test(clean)) return 'mss';

  return null;
}

/**
 * Formats a user-facing error message describing why a manifest probe failed.
 */
export function describeProbeFailure({ hadTab, pageResult, directResult } = {}) {
  const hasPageRefusal = pageResult && pageResult.status === 403;
  const hasDirectRefusal = directResult && directResult.status === 403;

  if (hadTab && (hasPageRefusal || hasDirectRefusal)) {
    return 'The server refused this request even from the page — the link may have expired.';
  }

  if (!hadTab && hasDirectRefusal) {
    return 'Open the page again to read its qualities';
  }

  const isTimeout =
    (pageResult && (pageResult.error === 'AbortError' || pageResult.error === 'Timeout')) ||
    (directResult && (directResult.error === 'AbortError' || directResult.error === 'Timeout'));

  if (isTimeout) {
    return 'The manifest request timed out.';
  }

  const status = pageResult?.status || directResult?.status;
  if (status && status > 0) {
    return `Could not fetch manifest (HTTP ${status})`;
  }

  const errDetail = pageResult?.error || directResult?.error;
  if (errDetail) {
    return `Could not fetch manifest (${errDetail})`;
  }

  return 'Could not fetch manifest (Unknown error)';
}

// Widevine, PlayReady, FairPlay system IDs plus the HLS KEYFORMAT / skd:// spellings.
const DRM_SIGNATURE = new RegExp([
  'edef8ba9-79d6-4ace-a3c8-27dcd51d21ed',
  '9a04f079-9840-4286-ab92-e65be0885f95',
  '94ce86fb-07ff-4f43-adb8-93d2fa968ca2',
  'com\\.apple\\.streamingkeydelivery',
  'com\\.microsoft\\.playready',
  'skd://'
].join('|'), 'i');

/**
 * True when the manifest declares a DRM key system, i.e. a download cannot decrypt it.
 * HLS: only #EXT-X-KEY / #EXT-X-SESSION-KEY lines count, so a title or URL that
 * happens to contain a signature is not flagged. Plain AES-128 and SAMPLE-AES with
 * an ordinary key URI are not DRM; the engine can decrypt those with a key.
 * shortcut: a master playlist rarely carries key tags (they sit in the media
 * playlists, which the probe does not fetch); upgrade by probing one variant.
 */
export function detectDrm(text) {
  if (!text || typeof text !== 'string') return false;

  if (text.includes('#EXTM3U')) {
    return text
      .split(/\r?\n/)
      .some((line) => /^#EXT-X-(SESSION-)?KEY:/.test(line.trim()) && DRM_SIGNATURE.test(line));
  }

  return DRM_SIGNATURE.test(text);
}
