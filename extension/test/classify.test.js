import { test } from 'node:test';
import assert from 'node:assert/strict';
import { classify, extractDispositionFilename, KIND_TITLES } from '../lib/classify.js';

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

// --- Query parameter allowlist & MSS path guard (M8, M9) ---

test('a format hint is read only from a parameter that could carry one', () => {
  assert.deepEqual(
    classify('https://cdn.example.com/p?type=m3u8', null, 200, 'xmlhttprequest'),
    { kind: 'HLS', confidence: 'low' }
  );
});

test('an unrelated parameter whose value happens to read as a format is ignored', () => {
  assert.equal(classify('https://cdn.example.com/p?theme=dash', null, 200, 'document'), null);
});

test('a filename parameter still resolves', () => {
  assert.deepEqual(
    classify('https://cdn.example.com/get?file=/hls/master.m3u8', null, 200, 'xmlhttprequest'),
    { kind: 'HLS', confidence: 'low' }
  );
});

test('a JSON API ending in /manifest is not Smooth Streaming', () => {
  assert.equal(
    classify('https://api.example.com/v1/manifest', 'application/json', 200, 'xmlhttprequest'),
    null
  );
});

test('a real MSS manifest is still detected', () => {
  assert.deepEqual(
    classify('https://cdn.example.com/video/Manifest', 'text/xml', 200, 'xmlhttprequest'),
    { kind: 'MSS', confidence: 'high' }
  );
});

// --- Audio-only stream detection (M10) ---

test('detects a standalone audio file by extension', () => {
  assert.deepEqual(
    classify('https://cdn.example.com/track.m4a', 'audio/mp4', 200, 'media'),
    { kind: 'Audio', confidence: 'high' }
  );
});

for (const ext of ['.opus', '.flac', '.wav', '.m4a']) {
  test(`recognises ${ext} as audio`, () => {
    assert.deepEqual(
      classify(`https://cdn.example.com/track${ext}`, null, 200, 'media'),
      { kind: 'Audio', confidence: 'high' }
    );
  });
}

test('an .aac served as audio with an explicit audio type is a file', () => {
  assert.deepEqual(
    classify('https://cdn.example.com/podcast.aac', 'audio/aac', 200, 'media'),
    { kind: 'Audio', confidence: 'high' }
  );
});

test('an .mp3 served as audio with an explicit audio type is a file', () => {
  assert.deepEqual(
    classify('https://cdn.example.com/song.mp3', 'audio/mpeg', 200, 'media'),
    { kind: 'Audio', confidence: 'high' }
  );
});

test('an .aac with no content type stays a segment', () => {
  // HLS audio segments use this extension; without a mime there is no way to
  // tell them apart, and a false segment is noisier than a missed file.
  assert.equal(classify('https://cdn.example.com/hls/seg-1.aac', null, 200, 'media'), null);
});

test('every kind the classifier can return has a title expansion', () => {
  // A badge with no tooltip is an acronym with no way in.
  for (const kind of ['HLS', 'DASH', 'MSS', 'Media', 'Audio', 'Abyss']) {
    assert.ok(KIND_TITLES[kind], `no KIND_TITLES entry for ${kind}`);
  }
});

// --- Stream detection coverage hardening ---

test('accepts HTTP 304 Not Modified stream responses', () => {
  assert.deepEqual(
    classify('https://cdn.example.com/hls/master.m3u8', null, 304, 'xmlhttprequest'),
    { kind: 'HLS', confidence: 'high' }
  );
});

test('accepts HTTP 302 / 307 Redirect stream responses', () => {
  assert.deepEqual(
    classify('https://cdn.example.com/stream.mpd', null, 302, 'xmlhttprequest'),
    { kind: 'DASH', confidence: 'high' }
  );
  assert.deepEqual(
    classify('https://cdn.example.com/video.mp4', null, 307, 'media'),
    { kind: 'Media', confidence: 'high' }
  );
});

test('detects manifests with subpaths after .m3u8 or .mpd extension', () => {
  assert.deepEqual(
    classify('https://cdn.example.com/hls/master.m3u8/index', null, 200, 'xmlhttprequest'),
    { kind: 'HLS', confidence: 'high' }
  );
  assert.deepEqual(
    classify('https://cdn.example.com/dash/video.mpd/manifest', null, 200, 'xmlhttprequest'),
    { kind: 'DASH', confidence: 'high' }
  );
});

test('detects manifests inside complex query parameters with extra tokens', () => {
  assert.deepEqual(
    classify('https://cdn.example.com/play?source=https%3A%2F%2Fsite.com%2Fvideo.m3u8%3Ftoken%3D123', null, 200, 'xmlhttprequest'),
    { kind: 'HLS', confidence: 'low' }
  );
  assert.deepEqual(
    classify('https://cdn.example.com/api?video=https://example.com/manifest.mpd&auth=abc', null, 200, 'xmlhttprequest'),
    { kind: 'DASH', confidence: 'low' }
  );
});

test('detects surrit.com master playlists and quality variants', () => {
  assert.deepEqual(
    classify('https://surrit.com/f4a44b42-8c05-4e77-a172-792d7c81606f/playlist.m3u8', 'application/vnd.apple.mpegurl', 200, 'xmlhttprequest'),
    { kind: 'HLS', confidence: 'high' }
  );
  assert.deepEqual(
    classify('https://surrit.com/f4a44b42-8c05-4e77-a172-792d7c81606f/1080p/video.m3u8', 'application/vnd.apple.mpegurl', 200, 'xmlhttprequest'),
    { kind: 'HLS', confidence: 'high' }
  );
});

test('classifies the container formats that were previously dropped', () => {
  for (const ext of ['avi', 'wmv', 'asf', 'divx', 'f4v', 'mpeg', 'mpg']) {
    const result = classify(`https://cdn.example.test/movie.${ext}`, null, 200, 'other');
    assert.equal(result?.kind, 'Media', `.${ext} should classify as Media`);
    assert.equal(result?.confidence, 'high');
  }
});

test('classifies audio-only containers as Audio, not Media', () => {
  for (const ext of ['weba', 'wma']) {
    const result = classify(`https://cdn.example.test/track.${ext}`, null, 200, 'other');
    assert.equal(result?.kind, 'Audio', `.${ext} should classify as Audio`);
  }
});

test('still ignores extensions that would flood the list', () => {
  // .json is every API response on the page; .srt has no path through the GUI.
  for (const ext of ['json', 'srt']) {
    assert.equal(classify(`https://api.example.test/data.${ext}`, null, 200, 'xmlhttprequest'), null);
  }
});

test('extractDispositionFilename parses standard and UTF-8 encoded filenames', () => {
  assert.equal(extractDispositionFilename('attachment; filename="video_sample.mp4"'), 'video_sample.mp4');
  assert.equal(extractDispositionFilename('attachment; filename=track.m4a'), 'track.m4a');
  assert.equal(extractDispositionFilename("attachment; filename*=UTF-8''%e6%b5%8b%e8%af%95.mp4"), '测试.mp4');
  assert.equal(extractDispositionFilename('inline'), null);
  assert.equal(extractDispositionFilename(null), null);
});

test('classify uses Content-Disposition filename when URL has no extension', () => {
  const result = classify(
    'https://cdn.example.test/download/stream?id=123',
    null,
    200,
    'other',
    'attachment; filename="movie.mp4"'
  );
  assert.deepEqual(result, { kind: 'Media', confidence: 'high' });

  const audioResult = classify(
    'https://cdn.example.test/get_audio?id=999',
    null,
    200,
    'other',
    'attachment; filename="song.flac"'
  );
  assert.deepEqual(audioResult, { kind: 'Audio', confidence: 'high' });
});

test('classify path extension still takes priority over Content-Disposition', () => {
  const result = classify(
    'https://cdn.example.test/hls/master.m3u8',
    null,
    200,
    'xmlhttprequest',
    'attachment; filename="fallback.mp4"'
  );
  assert.deepEqual(result, { kind: 'HLS', confidence: 'high' });
});
