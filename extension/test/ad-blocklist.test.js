import test from 'node:test';
import assert from 'node:assert/strict';
import { isAdCdnUrl } from '../lib/ad-blocklist.js';
import { classify } from '../lib/classify.js';

test('isAdCdnUrl identifies known ad and tracker CDNs', () => {
  assert.equal(isAdCdnUrl('https://pagead2.googlesyndication.com/pagead/show_ads.js'), true);
  assert.equal(isAdCdnUrl('https://ad.doubleclick.net/ddm/ad/N123.mp4'), true);
  assert.equal(isAdCdnUrl('https://googleads.g.doubleclick.net/pagead/ads?video.mp4'), true);
  assert.equal(isAdCdnUrl('https://imasdk.googleapis.com/js/core/bridge3.html'), true);
  assert.equal(isAdCdnUrl('https://connect.facebook.net/en_US/fbevents.js'), true);
  assert.equal(isAdCdnUrl('https://static.criteo.net/criteo.com/delivery/ad.mp4'), true);
  assert.equal(isAdCdnUrl('https://ib.adnxs.com/seg?video=test.mp4'), true);
  assert.equal(isAdCdnUrl('https://contextual.media.net/ad.mp4'), true);
  assert.equal(isAdCdnUrl('https://cdn.taboola.com/libtrc/ad.mp4'), true);
  assert.equal(isAdCdnUrl('https://widgets.outbrain.com/ad.m3u8'), true);
  assert.equal(isAdCdnUrl('https://bat.bing.com/action/0'), true);
  assert.equal(isAdCdnUrl('https://beacon.krxd.net/optout_check'), true);
  assert.equal(isAdCdnUrl('https://sb.scorecardresearch.com/beacon.js'), true);
});

test('isAdCdnUrl returns false for legitimate media and streaming CDNs', () => {
  assert.equal(isAdCdnUrl('https://cdn.example.com/videos/master.m3u8'), false);
  assert.equal(isAdCdnUrl('https://manifest.googlevideo.com/api/manifest/hls_variant/playlist.m3u8'), false);
  assert.equal(isAdCdnUrl('https://cf-vod.kaltura.com/hls/p/123/master.m3u8'), false);
  assert.equal(isAdCdnUrl('https://stream.akamaized.net/live/master.mpd'), false);
  assert.equal(isAdCdnUrl('https://fastly.example.com/movie.mp4'), false);
  assert.equal(isAdCdnUrl(''), false);
  assert.equal(isAdCdnUrl(null), false);
  assert.equal(isAdCdnUrl(undefined), false);
});

test('isAdCdnUrl is case-insensitive', () => {
  assert.equal(isAdCdnUrl('HTTPS://PAGEAD2.GOOGLESYNDICATION.COM/AD.MP4'), true);
  assert.equal(isAdCdnUrl('https://AD.DoubleClick.Net/video.mp4'), true);
});

test('classify silently drops ad URLs even if they have media or manifest extensions', () => {
  assert.equal(classify('https://ad.doubleclick.net/video/preroll.mp4', 'video/mp4', 200, 'media'), null);
  assert.equal(classify('https://imasdk.googleapis.com/vast/ad.m3u8', 'application/x-mpegURL', 200, 'xmlhttprequest'), null);
  assert.equal(classify('https://cdn.taboola.com/v/ad_video.mp4', 'video/mp4', 200, 'media'), null);
  assert.equal(classify('https://widgets.outbrain.com/video/ad.mpd', 'application/dash+xml', 200, 'xmlhttprequest'), null);
});
