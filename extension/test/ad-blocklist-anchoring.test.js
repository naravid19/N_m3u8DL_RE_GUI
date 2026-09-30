import test from 'node:test';
import assert from 'node:assert/strict';
import { isAdCdnUrl } from '../lib/ad-blocklist.js';

// The bug this file exists for: matching with String.includes() over the whole
// URL drops legitimate streams whose host merely ENDS WITH an ad domain, and
// drops innocent URLs that carry an ad domain in a query parameter. The failure
// is silent -- classify() returns null and the stream never reaches the popup --
// so nothing but a test will ever catch a regression here.
test('does not block hosts that merely end with an ad domain', () => {
  assert.equal(isAdCdnUrl('https://socialmedia.net/master.m3u8'), false);
  assert.equal(isAdCdnUrl('https://mymedia.net/video/index.m3u8'), false);
  assert.equal(isAdCdnUrl('https://notdoubleclick.net/stream.m3u8'), false);
});

test('does not block on an ad domain appearing in the query string', () => {
  assert.equal(isAdCdnUrl('https://example.com/master.m3u8?ref=openx.net/x'), false);
  assert.equal(isAdCdnUrl('https://cdn.example.com/v.m3u8?next=https://taboola.com/a'), false);
});

test('still blocks the real thing, exact host and subdomains alike', () => {
  assert.equal(isAdCdnUrl('https://media.net/ads.js'), true);
  assert.equal(isAdCdnUrl('https://cdn.media.net/ads.js'), true);
  assert.equal(isAdCdnUrl('https://securepubads.g.doubleclick.net/gampad/ads'), true);
  assert.equal(isAdCdnUrl('https://sb.scorecardresearch.com/beacon.js'), true);
});

test('malformed input is not blocked and does not throw', () => {
  assert.equal(isAdCdnUrl('not a url'), false);
  assert.equal(isAdCdnUrl(''), false);
  assert.equal(isAdCdnUrl(null), false);
  assert.equal(isAdCdnUrl(undefined), false);
});
