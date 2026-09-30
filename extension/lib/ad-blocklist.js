/**
 * Ad and tracker CDN blocklist.
 *
 * Silently drops requests from well-known ad networks, tracking pixels,
 * and interstitial video ad providers before classification.
 *
 * Matches against the parsed URL hostname, anchored to domain boundaries.
 */

const AD_CDN_DOMAINS = [
  // Google Ads / DoubleClick
  'doubleclick.net',
  'googleadservices.com',
  'googlesyndication.com',
  'imasdk.googleapis.com',

  // Meta / Facebook
  'connect.facebook.net',

  // Ad Networks & Exchanges
  'criteo.com',
  'criteo.net',
  'adnxs.com',
  'media.net',
  'taboola.com',
  'outbrain.com',
  'innovid.com',
  'yieldmo.com',
  'rubiconproject.com',
  'pubmatic.com',
  'openx.net',
  'casalemedia.com',

  // Tracking Beacons & Telemetry
  'bat.bing.com',
  'krxd.net',
  'beacon.krxd.net',
  'sb.scorecardresearch.com',
  'scorecardresearch.com',
  'quantserve.com',
  'moatads.com'
];

/**
 * Returns true if the URL belongs to a known ad/tracker CDN or service.
 * @param {string} url
 * @returns {boolean}
 */
export function isAdCdnUrl(url) {
  if (!url || typeof url !== 'string') return false;
  let host;
  try {
    host = new URL(url).hostname.toLowerCase();
  } catch {
    // Not a parseable absolute URL -- we cannot identify a host, so we must not
    // guess. Blocking on a substring match here is what caused legitimate
    // streams to vanish silently.
    return false;
  }
  return AD_CDN_DOMAINS.some((d) => host === d || host.endsWith('.' + d));
}

