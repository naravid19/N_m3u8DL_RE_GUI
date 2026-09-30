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
