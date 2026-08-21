import { test } from 'node:test';
import assert from 'node:assert/strict';
import { classify } from '../lib/classify.js';

test('recognises an HLS manifest', () => {
  assert.deepEqual(classify('https://cdn.example.com/hls/master.m3u8', null, 200, 'xmlhttprequest'), { kind: 'HLS', confidence: 'high' });
});

test('recognises a DASH manifest', () => {
  assert.deepEqual(classify('https://cdn.example.com/dash/manifest.mpd', null, 200, 'xmlhttprequest'), { kind: 'DASH', confidence: 'high' });
});

test('recognises progressive media', () => {
  assert.deepEqual(classify('https://cdn.example.com/video/movie.mp4', null, 200, 'media'), { kind: 'Media', confidence: 'high' });
});

test('rejects HLS segments', () => {
  assert.equal(classify('https://cdn.example.com/hls/seg_00001.ts', 'video/mp2t', 200, 'media'), null);
});

test('rejects non-http schemes', () => {
  assert.equal(classify('blob:https://example.com/abc', null, 200, 'media'), null);
});

test('falls back to content type when the path has no extension', () => {
  assert.deepEqual(classify('https://cdn.example.com/manifest?id=42', 'application/dash+xml', 200, 'xmlhttprequest'), { kind: 'DASH', confidence: 'high' });
});

test('F4 fixed: a query string containing .mp4 is not media', () => {
  assert.equal(
    classify('https://cdn.example.com/track?u=https%3A%2F%2Fy.example.com%2Fa.mp4', null, 200, 'image'),
    null
  );
});

test('F5 fixed: a host containing .ts does not suppress a real stream', () => {
  assert.deepEqual(classify('https://sports.ts.example.com/video.mp4', null, 200, 'media'), { kind: 'Media', confidence: 'high' });
});

test('F5 fixed: a directory named assets.ts does not suppress a manifest', () => {
  assert.deepEqual(classify('https://cdn.example.com/assets.ts/master.m3u8', null, 200, 'xmlhttprequest'), { kind: 'HLS', confidence: 'high' });
});

test('F6 fixed: a 403 is not offered as a stream', () => {
  assert.equal(classify('https://cdn.example.com/video/movie.mp4', null, 403, 'media'), null);
});

test('F6 fixed: a 404 manifest is not offered as a stream', () => {
  assert.equal(classify('https://cdn.example.com/hls/master.m3u8', null, 404, 'xmlhttprequest'), null);
});

test('F6: 206 partial content is accepted', () => {
  assert.deepEqual(classify('https://cdn.example.com/video/movie.mp4', null, 206, 'media'), { kind: 'Media', confidence: 'high' });
});

test('query strings do not change classification of a valid manifest', () => {
  assert.deepEqual(classify('https://cdn.example.com/master.m3u8?token=abc.ts.def', null, 200, 'xmlhttprequest'), { kind: 'HLS', confidence: 'high' });
});

test('a status of 0 (DOM-reported, no response) is still accepted', () => {
  // content.js reports elements it found in the page; there is no HTTP status.
  assert.deepEqual(classify('https://cdn.example.com/video/movie.mp4', null, 0, 'media'), { kind: 'Media', confidence: 'high' });
});

// --- Smooth Streaming (C2) ---

test('detects MSS by the .ism extension', () => {
  assert.deepEqual(
    classify('https://cdn.example.com/video.ism/Manifest', null, 200, 'xmlhttprequest'),
    { kind: 'MSS', confidence: 'high' }
  );
});

test('detects MSS by its content type', () => {
  assert.deepEqual(
    classify('https://cdn.example.com/stream/Manifest', 'application/vnd.ms-sstr+xml', 200, 'xmlhttprequest'),
    { kind: 'MSS', confidence: 'high' }
  );
});

test('detects the alternate DASH content type', () => {
  assert.deepEqual(
    classify('https://cdn.example.com/stream/x', 'video/vnd.mpeg.dash.mpd', 200, 'xmlhttprequest'),
    { kind: 'DASH', confidence: 'high' }
  );
});

// --- Additional progressive extensions ---

for (const ext of ['.m4v', '.ogv', '.3gp', '.webm', '.mkv', '.mov', '.flv']) {
  test(`recognises ${ext} as progressive media`, () => {
    assert.deepEqual(
      classify(`https://cdn.example.com/movie${ext}`, null, 200, 'media'),
      { kind: 'Media', confidence: 'high' }
    );
  });
}

// --- Low-confidence query tier ---

test('finds a manifest extension hiding in a query value, at low confidence', () => {
  assert.deepEqual(
    classify('https://cdn.example.com/get?file=/hls/master.m3u8', null, 200, 'xmlhttprequest'),
    { kind: 'HLS', confidence: 'low' }
  );
});

test('reads a format hint from the query, at low confidence', () => {
  assert.deepEqual(
    classify('https://cdn.example.com/playlist?type=m3u8', null, 200, 'xmlhttprequest'),
    { kind: 'HLS', confidence: 'low' }
  );
});

test('the query tier never promotes a media extension', () => {
  // This is exactly the F4 regression. A .mp4 in a query is still not media.
  assert.equal(
    classify('https://cdn.example.com/track?u=https%3A%2F%2Fy.example.com%2Fa.mp4', null, 200, 'image'),
    null
  );
});

test('a path match always outranks a conflicting query hint', () => {
  assert.deepEqual(
    classify('https://cdn.example.com/master.m3u8?type=mpd', null, 200, 'xmlhttprequest'),
    { kind: 'HLS', confidence: 'high' }
  );
});

// --- Regressions from the hardening pass must still hold ---

test('F5 still fixed: a host containing .ts does not suppress a real stream', () => {
  assert.deepEqual(
    classify('https://sports.ts.example.com/video.mp4', null, 200, 'media'),
    { kind: 'Media', confidence: 'high' }
  );
});

test('F6 still fixed: a 403 is not offered as a stream', () => {
  assert.equal(classify('https://cdn.example.com/video/movie.mp4', null, 403, 'media'), null);
});
