/**
 * Decides which captured headers are worth re-sending.
 * Mirrors N_m3u8DL_RE_GUI.Core.Capture.HeaderPolicy and tested against a shared fixture.
 */

const DROPPED = new Set([
  // Transport-level
  'accept-encoding', 'content-length', 'host', 'connection',
  'te', 'trailer', 'transfer-encoding', 'expect', 'keep-alive',
  // Navigation hints
  'priority', 'dnt', 'upgrade-insecure-requests', 'cache-control', 'pragma',
  // Ranged requests
  'range'
]);

export function shouldForward(name) {
  if (!name || typeof name !== 'string') return false;
  const trimmed = name.trim();
  if (!trimmed) return false;

  // HTTP/2 pseudo-headers (:status, :path, etc.)
  if (trimmed.startsWith(':')) return false;

  // Browser fetch metadata
  if (trimmed.toLowerCase().startsWith('sec-')) return false;

  return !DROPPED.has(trimmed.toLowerCase());
}
