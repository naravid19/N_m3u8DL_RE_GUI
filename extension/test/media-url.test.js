import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

function loadMediaUrlModule() {
  const code = fs.readFileSync(path.resolve(__dirname, '../lib/media-url.js'), 'utf-8');
  const context = { globalThis: {} };
  vm.createContext(context);
  vm.runInContext(code, context);
  return context.globalThis.NRE_MEDIA_URL;
}

test('media-url shouldReportUrl accepts http and https URLs', () => {
  const { shouldReportUrl } = loadMediaUrlModule();
  assert.equal(shouldReportUrl('https://example.com/video.mp4'), true);
  assert.equal(shouldReportUrl('http://example.com/stream.m3u8'), true);
  assert.equal(shouldReportUrl('https://example.com/manifest.mpd?query=1'), true);
});

test('media-url shouldReportUrl rejects non-string and empty inputs', () => {
  const { shouldReportUrl } = loadMediaUrlModule();
  assert.equal(shouldReportUrl(null), false);
  assert.equal(shouldReportUrl(undefined), false);
  assert.equal(shouldReportUrl(''), false);
  assert.equal(shouldReportUrl(123), false);
});

test('media-url shouldReportUrl rejects blob, data, and javascript schemes', () => {
  const { shouldReportUrl } = loadMediaUrlModule();
  assert.equal(shouldReportUrl('blob:https://example.com/uuid-123'), false);
  assert.equal(shouldReportUrl('data:video/mp4;base64,...'), false);
  assert.equal(shouldReportUrl('javascript:void(0)'), false);
});

test('media-url shouldReportUrl rejects synthetic internal fragment URLs', () => {
  const { shouldReportUrl } = loadMediaUrlModule();
  assert.equal(shouldReportUrl('https://hydrax.net/play#mp4/123'), false);
  assert.equal(shouldReportUrl('https://abysscdn.com/watch#hls/456'), false);
  assert.equal(shouldReportUrl('https://cdn.example.com/stream#chunk_0'), false);
  assert.equal(shouldReportUrl('https://cdn.example.com/stream?maxChunkSize=1000'), false);
});
