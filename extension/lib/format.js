/**
 * Pure display helpers for formatting stream items, file sizes, and URLs in the popup.
 * No chrome.* access so it runs directly in node --test.
 */

/**
 * Resolves the true size of a response.
 *
 * On a 206 the content-length header is the length of the returned range, not
 * of the file — a 1.2 GB video fetched in 5 MB chunks reports 5 MB. The total
 * after the slash in Content-Range is the figure worth showing.
 */
export function totalSizeFrom(contentLength, contentRange, status) {
  if (status === 206) {
    const total = /\/(\d+)\s*$/.exec(contentRange || '');
    if (total) {
      return { sizeBytes: Number.parseInt(total[1], 10), isPartial: false };
    }
    const range = toByteCount(contentLength);
    return { sizeBytes: range, isPartial: range !== null };
  }

  return { sizeBytes: toByteCount(contentLength), isPartial: false };
}

function toByteCount(value) {
  if (value === null || value === undefined) return null;
  const parsed = Number.parseInt(value, 10);
  return Number.isFinite(parsed) && parsed >= 0 ? parsed : null;
}

/**
 * Format raw byte counts into human-readable strings (B, KB, MB, GB).
 */
export function formatBytes(bytes, isPartial = false) {
  if (bytes === null || bytes === undefined || typeof bytes !== 'number' || !Number.isFinite(bytes) || bytes < 0) {
    return '';
  }

  const prefix = isPartial ? '~' : '';

  if (bytes < 1024) {
    return `${prefix}${bytes} B`;
  }
  if (bytes < 1024 * 1024) {
    return `${prefix}${(bytes / 1024).toFixed(1)} KB`;
  }
  if (bytes < 1024 * 1024 * 1024) {
    return `${prefix}${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }
  return `${prefix}${(bytes / (1024 * 1024 * 1024)).toFixed(1)} GB`;
}

/**
 * Format Unix timestamps into human-readable relative time strings.
 */
export function formatRelativeTime(ts) {
  if (!ts) return '';
  const sec = Math.floor((Date.now() - ts) / 1000);
  if (sec < 10) return 'just now';
  if (sec < 60) return `${sec}s ago`;
  const min = Math.floor(sec / 60);
  if (min < 60) return `${min}m ago`;
  return `${Math.floor(min / 60)}h ago`;
}

/**
 * Formats bandwidth (bits per second) into a clean Mbps / kbps string.
 */
export function formatBitrate(bps) {
  if (!bps || typeof bps !== 'number' || bps <= 0) return '';
  if (bps >= 1000000) {
    const mbps = bps / 1000000;
    return `${mbps >= 10 ? Math.round(mbps) : mbps.toFixed(1)} Mbps`;
  }
  const kbps = Math.round(bps / 1000);
  return `${kbps} kbps`;
}

/**
 * Formats duration in seconds into mm:ss or hh:mm:ss string.
 */
export function formatDuration(seconds) {
  if (!seconds || typeof seconds !== 'number' || seconds <= 0) return '';
  const totalSec = Math.floor(seconds);
  const h = Math.floor(totalSec / 3600);
  const m = Math.floor((totalSec % 3600) / 60);
  const s = totalSec % 60;
  const mm = String(m).padStart(2, '0');
  const ss = String(s).padStart(2, '0');
  return h > 0 ? `${h}:${mm}:${ss}` : `${m}:${ss}`;
}

/**
 * Elides long URLs preserving the head (origin/scheme) and tail (filename/extension).
 */
export function elideUrl(url, max = 40) {
  if (!url || typeof url !== 'string') return '';
  if (url.length <= max) return url;

  const headLen = Math.floor((max - 1) * 0.6);
  const tailLen = max - 1 - headLen;

  return `${url.slice(0, headLen)}…${url.slice(-tailLen)}`;
}

/**
 * Produces a human-readable role explanation for a stream item.
 */
export function describeRole(item) {
  if (!item) return '';
  if (item.confidence === 'low') {
    return 'Might not be a video';
  }
  switch (item.kind) {
    case 'HLS':
    case 'DASH':
    case 'MSS':
      return 'Video + audio · every quality';
    case 'Media':
      return 'Video only · one quality';
    case 'Audio':
      return 'Audio only';
    case 'Abyss':
      return 'Player page · full video';
    default:
      return 'Stream';
  }
}

/**
 * Produces a concise descriptor for a stream item leading with its role.
 */
export function describeStream(item) {
  if (!item) return '';

  const role = describeRole(item);
  const size = formatBytes(item.sizeBytes, Boolean(item.isPartial));

  return [role, size].filter(Boolean).join(' · ');
}

/**
 * Splits a raw stream URL into logical components:
 * - filename: the final path segment (e.g. "chunk.m3u8"), without query parameters.
 * - queryParams: the query string if present (e.g. "?token=abc"), else "".
 * - hostAndPath: origin and pathname combined.
 */
export function splitUrl(rawUrl) {
  try {
    const u = new URL(rawUrl);
    const pathParts = u.pathname.split('/').filter(Boolean);
    const filename = pathParts.length > 0 ? pathParts[pathParts.length - 1] : u.hostname;
    const hostAndPath = `${u.origin}${u.pathname}`;
    return {
      filename,
      queryParams: u.search || '',
      hostAndPath
    };
  } catch {
    return { filename: rawUrl || '', queryParams: '', hostAndPath: '' };
  }
}


