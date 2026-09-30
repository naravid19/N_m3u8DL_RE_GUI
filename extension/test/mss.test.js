import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { parseMssManifest } from '../lib/mss.js';

const fixture = (name) =>
  readFileSync(join(import.meta.dirname, 'fixtures', name), 'utf8');

const MSS_URL = 'https://cdn.example.com/live/manifest.ism/Manifest';

test('parseMssManifest separates video from audio stream indexes', () => {
  const variants = parseMssManifest(fixture('manifest.ism'), MSS_URL);

  assert.equal(variants.filter((v) => v.kind === 'video').length, 2);
  assert.equal(variants.filter((v) => v.kind === 'audio').length, 1);
});

test('parseMssManifest reads dimensions, bitrate, and codecs', () => {
  const [top] = parseMssManifest(fixture('manifest.ism'), MSS_URL);

  assert.equal(top.width, 1920);
  assert.equal(top.height, 1080);
  assert.equal(top.bandwidth, 5000000);
  assert.equal(top.codecs, 'H264');
  assert.equal(top.label, '1080p · 5.0 Mbps');
});

test('parseMssManifest sorts variants descending by height then bitrate', () => {
  const variants = parseMssManifest(fixture('manifest.ism'), MSS_URL);
  const videoHeights = variants.filter((v) => v.kind === 'video').map((v) => v.height);

  assert.deepEqual(videoHeights, [1080, 720]);
});

test('parseMssManifest sets url: null for MSS variants', () => {
  const variants = parseMssManifest(fixture('manifest.ism'), MSS_URL);

  assert.ok(variants.every((v) => v.url === null));
});

test('parseMssManifest returns empty for non-MSS content', () => {
  assert.deepEqual(parseMssManifest('<html><body>404 Not Found</body></html>', MSS_URL), []);
  assert.deepEqual(parseMssManifest('', MSS_URL), []);
  assert.deepEqual(parseMssManifest(null, MSS_URL), []);
});
