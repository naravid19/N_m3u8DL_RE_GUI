import { test } from 'node:test';
import assert from 'node:assert/strict';
import { rankStreams, groupByOrigin, reconcileSelection, matchesFilter } from '../lib/list-policy.js';

const at = (n) => 1_700_000_000_000 + n * 1000;
const s = (url, kind, extra = {}) => ({ url, kind, confidence: 'high', timestamp: at(0), ...extra });

test('manifests rank above progressive media', () => {
  const ranked = rankStreams([s('u1', 'Media'), s('u2', 'HLS')]);

  assert.deepEqual(ranked.map((x) => x.kind), ['HLS', 'Media']);
});

test('the three manifest kinds rank equally', () => {
  const ranked = rankStreams([s('u1', 'MSS'), s('u2', 'HLS'), s('u3', 'DASH')]);

  assert.ok(ranked.every((x) => ['HLS', 'DASH', 'MSS'].includes(x.kind)));
});

test('Abyss ranks between manifests and media', () => {
  const ranked = rankStreams([s('u1', 'Media'), s('u2', 'Abyss'), s('u3', 'HLS')]);

  assert.deepEqual(ranked.map((x) => x.kind), ['HLS', 'Abyss', 'Media']);
});

test('a confident result outranks a guess of the same kind', () => {
  const ranked = rankStreams([
    s('u1', 'HLS', { confidence: 'low' }),
    s('u2', 'HLS', { confidence: 'high' })
  ]);

  assert.equal(ranked[0].url, 'u2');
});

test('newest wins when kind and confidence tie', () => {
  const ranked = rankStreams([
    s('older', 'HLS', { timestamp: at(0) }),
    s('newer', 'HLS', { timestamp: at(10) })
  ]);

  assert.equal(ranked[0].url, 'newer');
});

test('rankStreams does not mutate its input', () => {
  const input = [s('u1', 'Media'), s('u2', 'HLS')];
  const before = input.map((x) => x.url);

  rankStreams(input);

  assert.deepEqual(input.map((x) => x.url), before);
});

test('rankStreams tolerates an unknown kind and missing timestamps', () => {
  const ranked = rankStreams([s('u1', 'Mystery', { timestamp: undefined }), s('u2', 'HLS')]);

  assert.equal(ranked[0].kind, 'HLS');
  assert.equal(ranked.length, 2);
});

// --- grouping ---

test('groupByOrigin buckets by the referer origin', () => {
  const groups = groupByOrigin([
    s('u1', 'HLS', { referer: 'https://a.example.com/watch/1' }),
    s('u2', 'HLS', { referer: 'https://a.example.com/watch/2' }),
    s('u3', 'HLS', { referer: 'https://b.example.com/x' })
  ]);

  assert.equal(groups.length, 2);
  assert.equal(groups[0].items.length, 2);
});

test('groupByOrigin puts entries with no referer in one trailing bucket', () => {
  const groups = groupByOrigin([
    s('u1', 'HLS', { referer: 'https://a.example.com/x' }),
    s('u2', 'HLS', { referer: null })
  ]);

  assert.equal(groups.at(-1).origin, null);
});

test('groupByOrigin survives an unparseable referer', () => {
  const groups = groupByOrigin([s('u1', 'HLS', { referer: 'not a url' })]);

  assert.equal(groups.length, 1);
});

test('groupByOrigin returns nothing for an empty list', () => {
  assert.deepEqual(groupByOrigin([]), []);
});

// --- selection ---

test('reconcileSelection drops URLs that are no longer present', () => {
  const kept = reconcileSelection(new Set(['gone', 'here']), [s('here', 'HLS')]);

  assert.deepEqual([...kept], ['here']);
});

test('reconcileSelection keeps a selection that is entirely still present', () => {
  const kept = reconcileSelection(new Set(['a', 'b']), [s('a', 'HLS'), s('b', 'HLS')]);

  assert.equal(kept.size, 2);
});

test('reconcileSelection returns empty when nothing survives', () => {
  assert.equal(reconcileSelection(new Set(['x']), []).size, 0);
});

test('reconcileSelection does not mutate the set it was given', () => {
  const original = new Set(['gone', 'here']);

  reconcileSelection(original, [s('here', 'HLS')]);

  assert.equal(original.size, 2);
});

// --- filter ---

test('matchesFilter is case insensitive on the URL', () => {
  assert.ok(matchesFilter(s('https://cdn/MASTER.m3u8', 'HLS'), 'master'));
});

test('matchesFilter also matches the kind', () => {
  assert.ok(matchesFilter(s('https://cdn/a', 'DASH'), 'dash'));
});

test('an empty query matches everything', () => {
  assert.ok(matchesFilter(s('https://cdn/a', 'HLS'), ''));
});

test('a query matching neither field does not match', () => {
  assert.ok(!matchesFilter(s('https://cdn/a.m3u8', 'HLS'), 'zzz'));
});

test('ranking a filtered subset still puts a manifest first', () => {
  // The rendered set after filtering must be ranked, not merely sliced from
  // the ranked full set — the top-ranked stream may not survive the filter.
  const all = [
    s('https://cdn/master.m3u8', 'HLS'),
    s('https://cdn/chunk-720.m3u8', 'HLS'),
    s('https://cdn/chunk-720.mp4', 'Media')
  ];

  const filtered = all.filter((x) => matchesFilter(x, 'chunk'));
  const ranked = rankStreams(filtered);

  assert.equal(ranked.length, 2);
  assert.equal(ranked[0].kind, 'HLS');
});

test('matchesFilter is the single predicate — trimming behaves the same everywhere', () => {
  // Guards B3: popup.js filtered in two places, one inline. A query with
  // surrounding whitespace must not select a different set than it shows.
  const item = s('https://cdn/master.m3u8', 'HLS');

  assert.equal(matchesFilter(item, '  master  '), matchesFilter(item, 'master'));
});

test('matchesFilter treats an all-whitespace query as no filter', () => {
  assert.ok(matchesFilter(s('https://cdn/a.m3u8', 'HLS'), '   '));
});
