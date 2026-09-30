/**
 * User-Agent patterns mapped to the curl_cffi fingerprints the GUI offers.
 *
 * An ES module rather than a JSON import: `with { type: 'json' }` is Chrome
 * 123+, and needing it for one static table pushed the whole extension's
 * browser floor up by twelve versions for no functional gain.
 *
 * Edge is matched before Chrome because an Edge User-Agent contains both
 * tokens; reordering these silently downgrades every Edge user to a Chrome
 * fingerprint. The profiles within each entry run highest-version-first, and
 * the first whose minVersion is met wins.
 */
export const PROFILES = [
  {
    pattern: 'Edg/(\\d+)',
    profiles: [
      { minVersion: 100, impersonate: 'edge101' },
      { minVersion: 0, impersonate: 'chrome' }
    ]
  },
  {
    pattern: 'Chrome/(\\d+)',
    profiles: [
      { minVersion: 130, impersonate: 'chrome131' },
      { minVersion: 110, impersonate: 'chrome120' },
      { minVersion: 0, impersonate: 'chrome' }
    ]
  },
  {
    pattern: 'Version/(\\d+).*Safari',
    profiles: [
      { minVersion: 17, impersonate: 'safari17_0' },
      { minVersion: 0, impersonate: 'chrome' }
    ]
  }
];
