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

test('F4 fixed: a query string containing .mp4 is not media', () => {
  assert.equal(
    classify('https://cdn.example.com/track?u=https%3A%2F%2Fy.example.com%2Fa.mp4', null, 200, 'image'),
    null
  );
});

test('F5 fixed: a host containing .ts does not suppress a real stream', () => {
  assert.equal(classify('https://sports.ts.example.com/video.mp4', null, 200, 'media'), 'Media');
});

test('F5 fixed: a directory named assets.ts does not suppress a manifest', () => {
  assert.equal(classify('https://cdn.example.com/assets.ts/master.m3u8', null, 200, 'xmlhttprequest'), 'HLS');
});

test('F6 fixed: a 403 is not offered as a stream', () => {
  assert.equal(classify('https://cdn.example.com/video/movie.mp4', null, 403, 'media'), null);
});

test('F6 fixed: a 404 manifest is not offered as a stream', () => {
  assert.equal(classify('https://cdn.example.com/hls/master.m3u8', null, 404, 'xmlhttprequest'), null);
});

test('F6: 206 partial content is accepted', () => {
  assert.equal(classify('https://cdn.example.com/video/movie.mp4', null, 206, 'media'), 'Media');
});

test('query strings do not change classification of a valid manifest', () => {
  assert.equal(classify('https://cdn.example.com/master.m3u8?token=abc.ts.def', null, 200, 'xmlhttprequest'), 'HLS');
});

test('a status of 0 (DOM-reported, no response) is still accepted', () => {
  // content.js reports elements it found in the page; there is no HTTP status.
  assert.equal(classify('https://cdn.example.com/video/movie.mp4', null, 0, 'media'), 'Media');
});
