async function fetchDomainCookies(api, url, cookieMap) {
  if (!api || typeof api.getAll !== 'function' || !url) return;
  try {
    const storeCookies = await api.getAll({ url });
    if (Array.isArray(storeCookies)) {
      for (const c of storeCookies) {
        if (c && c.name && c.value !== undefined) {
          cookieMap.set(c.name, c.value);
        }
      }
    }
  } catch {
    // Best-effort
  }
}

/**
 * Merges captured Cookie header with the browser cookie store.
 * Format: name=value; name2=value2
 * Precedence (lowest to highest): stream/CDN domain store cookies, then page
 * domain store cookies, then the captured Cookie header. The page domain is
 * fetched separately from the stream domain because Cloudflare issues
 * cf_clearance against the PAGE's origin, not the CDN the manifest was
 * fetched from — merging only the stream domain (the pre-fix behaviour)
 * silently drops the one cookie this feature exists to carry (N6).
 */
export async function getMergedCookies(streamUrl, pageUrl, capturedCookieHeader, cookieStoreApi = null) {
  const cookieMap = new Map();
  const api = cookieStoreApi || (typeof chrome !== 'undefined' && chrome.cookies ? chrome.cookies : null);

  await fetchDomainCookies(api, streamUrl, cookieMap);
  if (pageUrl && pageUrl !== streamUrl) {
    await fetchDomainCookies(api, pageUrl, cookieMap);
  }

  // Overlay captured header — highest precedence, it is what the browser
  // actually sent on this exact request.
  if (capturedCookieHeader && typeof capturedCookieHeader === 'string') {
    const pairs = capturedCookieHeader.split(';');
    for (const pair of pairs) {
      const trimmed = pair.trim();
      if (!trimmed) continue;
      const eqIdx = trimmed.indexOf('=');
      if (eqIdx > 0) {
        const name = trimmed.slice(0, eqIdx).trim();
        const val = trimmed.slice(eqIdx + 1).trim();
        cookieMap.set(name, val);
      }
    }
  }

  if (cookieMap.size === 0) return '';

  return Array.from(cookieMap.entries())
    .map(([k, v]) => k + '=' + v)
    .join('; ');
}

/**
 * Applies a merged cookie value to a stream, upserting a Cookie entry into
 * its headers array when one exists (the shape toCurl actually reads) rather
 * than only setting .cookie, which toCurl ignores whenever headers is
 * present (N5). Falls back to .cookie for the legacy flat-field stream shape.
 */
export function applyMergedCookie(stream, mergedCookie) {
  if (!stream) return stream;
  if (!mergedCookie) return stream;

  if (Array.isArray(stream.headers)) {
    const headers = stream.headers.filter((h) => !(h && h.name && h.name.toLowerCase() === 'cookie'));
    headers.push({ name: 'Cookie', value: mergedCookie });
    return { ...stream, headers, cookie: mergedCookie };
  }

  return { ...stream, cookie: mergedCookie };
}
