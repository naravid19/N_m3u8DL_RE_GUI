import { PROFILES as profiles } from './impersonate-profiles.js';

/**
 * Resolves browser userAgent to curl_cffi impersonate fingerprint.
 * Defaults to 'chrome' if unmatched.
 */
export function resolveImpersonate(userAgent) {
  if (!userAgent || typeof userAgent !== 'string') {
    return 'chrome';
  }

  for (const entry of profiles) {
    const regex = new RegExp(entry.pattern, 'i');
    const match = userAgent.match(regex);
    if (match && match[1]) {
      const ver = parseInt(match[1], 10);
      if (!isNaN(ver)) {
        for (const p of entry.profiles) {
          if (ver >= p.minVersion) {
            return p.impersonate;
          }
        }
      }
    }
  }

  return 'chrome';
}
