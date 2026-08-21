import { test } from 'node:test';
import assert from 'node:assert/strict';
import { classify } from '../lib/classify.js';

test('recognises an HLS manifest', () => {
  assert.equal(classify('https://cdn.example.com/hls/master.m3u8', null, 200, 'xmlhttprequest'), 'HLS');
});

test('recognises a DASH manifest', () => {
  assert.equal(classify('https://cdn.example.com/dash/manifest.mpd', null, 200, 'xmlhttprequest'), 'DASH');
});

test('recognises progressive media', () => {
  assert.equal(classify('https://cdn.example.com/video/movie.mp4', null, 200, 'media'), 'Media');
});

test('rejects HLS segments', () => {
  assert.equal(classify('https://cdn.example.com/hls/seg_00001.ts', 'video/mp2t', 200, 'media'), null);
});

test('rejects non-http schemes', () => {
  assert.equal(classify('blob:https://example.com/abc', null, 200, 'media'), null);
});

test('falls back to content type when the path has no extension', () => {
  assert.equal(classify('https://cdn.example.com/manifest?id=42', 'application/dash+xml', 200, 'xmlhttprequest'), 'DASH');
});

// --- Known defects, pinned so Task 2 can flip them deliberately ---

test('KNOWN BUG F4: a query string containing .mp4 is misread as media', () => {
  assert.equal(
    classify('https://cdn.example.com/track?u=https%3A%2F%2Fy.example.com%2Fa.mp4', null, 200, 'image'),
    'Media'
  );
});

test('KNOWN BUG F5: a host containing .ts suppresses a real stream', () => {
  assert.equal(classify('https://sports.ts.example.com/video.mp4', null, 200, 'media'), null);
});

test('KNOWN BUG F6: a 403 is classified as a usable stream', () => {
  assert.equal(classify('https://cdn.example.com/video/movie.mp4', null, 403, 'media'), 'Media');
});
