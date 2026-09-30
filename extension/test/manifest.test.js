import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { parseHlsMaster, parseDashManifest, parseAttributeList, describeVariant } from '../lib/manifest.js';

const fixture = (name) =>
  readFileSync(join(import.meta.dirname, 'fixtures', name), 'utf8');

const MASTER_URL = 'https://cdn.example.com/hls/master.m3u8';

test('parseAttributeList keeps a quoted value containing a comma intact', () => {
  const attrs = parseAttributeList('BANDWIDTH=5000000,CODECS="avc1.640028,mp4a.40.2",AUDIO="aud"');

  assert.equal(attrs.BANDWIDTH, '5000000');
  assert.equal(attrs.CODECS, 'avc1.640028,mp4a.40.2');
  assert.equal(attrs.AUDIO, 'aud');
});

test('parseAttributeList tolerates an empty list', () => {
  assert.deepEqual(parseAttributeList(''), {});
});

test('parseHlsMaster returns one entry per variant, highest first', () => {
  const variants = parseHlsMaster(fixture('master.m3u8'), MASTER_URL);

  assert.equal(variants.length, 3);
  assert.deepEqual(variants.map((v) => v.height), [1080, 720, 360]);
});

test('parseHlsMaster resolves relative variant URLs against the master', () => {
  const [first] = parseHlsMaster(fixture('master.m3u8'), MASTER_URL);

  assert.equal(first.url, 'https://cdn.example.com/hls/1080p/index.m3u8');
});

test('parseHlsMaster carries bandwidth and codecs', () => {
  const [first] = parseHlsMaster(fixture('master.m3u8'), MASTER_URL);

  assert.equal(first.bandwidth, 5000000);
  assert.equal(first.codecs, 'avc1.640028,mp4a.40.2');
  assert.equal(first.kind, 'video');
});

test('parseHlsMaster returns nothing for a media playlist', () => {
  // A variant playlist has no EXT-X-STREAM-INF. Offering "qualities" for one
  // would be offering a choice that does not exist.
  assert.deepEqual(parseHlsMaster(fixture('media.m3u8'), MASTER_URL), []);
});

test('parseHlsMaster survives a variant with no RESOLUTION', () => {
  const text = '#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=800000\naudio-only.m3u8\n';
  const [only] = parseHlsMaster(text, MASTER_URL);

  assert.equal(only.height, null);
  assert.equal(only.bandwidth, 800000);
});

test('parseHlsMaster skips a STREAM-INF with no URI line', () => {
  const text = '#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=800000\n';

  assert.deepEqual(parseHlsMaster(text, MASTER_URL), []);
});

test('an orphaned STREAM-INF does not steal the next variant URL', () => {
  // Picking 1080p must not silently download 720p.
  const text = [
    '#EXTM3U',
    '#EXT-X-STREAM-INF:BANDWIDTH=5000000,RESOLUTION=1920x1080',
    '#EXT-X-STREAM-INF:BANDWIDTH=2500000,RESOLUTION=1280x720',
    '720p/index.m3u8'
  ].join('\n');

  const variants = parseHlsMaster(text, MASTER_URL);

  assert.equal(variants.length, 1);
  assert.equal(variants[0].height, 720);
  assert.equal(variants[0].url, 'https://cdn.example.com/hls/720p/index.m3u8');
});

test('a comment between a STREAM-INF and its URI is skipped', () => {
  const text = [
    '#EXTM3U',
    '#EXT-X-STREAM-INF:BANDWIDTH=1,RESOLUTION=640x360',
    '# a note from the packager',
    '360p/index.m3u8'
  ].join('\n');

  assert.equal(parseHlsMaster(text, MASTER_URL)[0].url, 'https://cdn.example.com/hls/360p/index.m3u8');
});

test('parseHlsMaster handles CRLF line endings', () => {
  const text = '#EXTM3U\r\n#EXT-X-STREAM-INF:BANDWIDTH=1,RESOLUTION=640x360\r\na.m3u8\r\n';
  const [only] = parseHlsMaster(text, MASTER_URL);

  assert.equal(only.height, 360);
});

test('parseDashManifest separates video from audio adaptation sets', () => {
  const variants = parseDashManifest(fixture('manifest.mpd'), 'https://cdn.example.com/d/manifest.mpd');

  assert.equal(variants.filter((v) => v.kind === 'video').length, 2);
  assert.equal(variants.filter((v) => v.kind === 'audio').length, 1);
});

test('parseDashManifest reads dimensions and bandwidth', () => {
  const [top] = parseDashManifest(fixture('manifest.mpd'), 'https://cdn.example.com/d/manifest.mpd');

  assert.equal(top.width, 1920);
  assert.equal(top.height, 1080);
  assert.equal(top.bandwidth, 5000000);
});

test('parseDashManifest returns nothing for content that is not an MPD', () => {
  assert.deepEqual(parseDashManifest('<html><body>404</body></html>', 'https://x/y.mpd'), []);
});

test('a Representation inherits width and height from its AdaptationSet', () => {
  const mpd = `<MPD><Period>
    <AdaptationSet mimeType="video/mp4" width="1920" height="1080">
      <Representation id="v0" bandwidth="5000000"/>
    </AdaptationSet></Period></MPD>`;

  const [only] = parseDashManifest(mpd, 'https://cdn.example.com/d/m.mpd');

  assert.equal(only.height, 1080);
  assert.equal(only.width, 1920);
});

test('a Representation overrides an inherited dimension', () => {
  const mpd = `<MPD><Period>
    <AdaptationSet mimeType="video/mp4" width="1920" height="1080">
      <Representation id="v0" bandwidth="5000000"/>
      <Representation id="v1" width="1280" height="720" bandwidth="2500000"/>
    </AdaptationSet></Period></MPD>`;

  assert.deepEqual(parseDashManifest(mpd, 'https://x/m.mpd').map((v) => v.height), [1080, 720]);
});

test('maxWidth and maxHeight are used when width and height are absent', () => {
  const mpd = `<MPD><Period>
    <AdaptationSet mimeType="video/mp4" maxWidth="1920" maxHeight="1080">
      <Representation id="v0" bandwidth="5000000"/>
    </AdaptationSet></Period></MPD>`;

  assert.equal(parseDashManifest(mpd, 'https://x/m.mpd')[0].height, 1080);
});

test('a DASH variant carries no per-variant URL', () => {
  // Unlike HLS there is no separate playlist per quality; the download target
  // is the manifest plus a selector. Duplicating the manifest URL on every row
  // would read as though each had its own.
  const variants = parseDashManifest(fixture('manifest.mpd'), 'https://x/m.mpd');

  assert.ok(variants.every((v) => v.url === null));
});

test('describeVariant leads with the height when there is one', () => {
  assert.equal(
    describeVariant({ kind: 'video', width: 1920, height: 1080, bandwidth: 5000000 }),
    '1080p · 5.0 Mbps'
  );
});

test('describeVariant falls back to bandwidth alone', () => {
  assert.equal(
    describeVariant({ kind: 'audio', width: null, height: null, bandwidth: 128000 }),
    'audio · 128 kbps'
  );
});

test('describeVariant tolerates a variant with neither', () => {
  assert.equal(describeVariant({ kind: 'video', width: null, height: null, bandwidth: null }), 'video');
});
