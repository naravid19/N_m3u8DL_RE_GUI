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
 * Produces a concise descriptor for a stream item (e.g. "HLS", "Media · 1.0 GB", "HLS · guess").
 */
export function describeStream(item) {
  if (!item) return '';

  const parts = [item.kind || 'Media'];

  if (item.confidence === 'low') {
    parts.push('guess');
  }

  const size = formatBytes(item.sizeBytes, Boolean(item.isPartial));
  if (size) {
    parts.push(size);
  }

  return parts.join(' · ');
}
