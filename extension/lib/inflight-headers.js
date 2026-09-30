/**
 * Pure pieces of the onSendHeaders -> onHeadersReceived correlation cache.
 * Extracted from background.js so the bookkeeping (M3's requestId-only
 * keying, the TTL+size prune) is testable under `node --test` without
 * mocking the whole chrome.webRequest API.
 */

/** Extracts the four named headers plus the full ordered list from one
 *  onSendHeaders event's requestHeaders array. */
export function buildHeaderPayload(requestHeaders, now = Date.now()) {
  let referer = null;
  let userAgent = null;
  let cookie = null;
  let origin = null;
  const headersArray = [];

  for (const header of requestHeaders || []) {
    if (!header || typeof header.name !== 'string') continue;
    const name = header.name.toLowerCase();
    if (name === 'referer') referer = header.value;
    else if (name === 'user-agent') userAgent = header.value;
    else if (name === 'cookie') cookie = header.value;
    else if (name === 'origin') origin = header.value;
    headersArray.push({ name: header.name, value: header.value });
  }

  return { referer, userAgent, cookie, origin, headersArray, at: now };
}

/**
 * Bounds an in-flight header cache in place: a TTL sweep first, then an
 * insertion-order eviction backstop for when more than maxInflight requests
 * are genuinely in flight inside the TTL window. Returns the same map.
 */
export function pruneInflight(map, { maxInflight, ttlMs, now = Date.now() } = {}) {
  if (map.size <= maxInflight) return map;

  const cutoff = now - ttlMs;
  for (const [key, value] of map) {
    if (value.at < cutoff) map.delete(key);
  }

  while (map.size > maxInflight) {
    const oldest = map.keys().next();
    if (oldest.done) break;
    map.delete(oldest.value);
  }

  return map;
}
