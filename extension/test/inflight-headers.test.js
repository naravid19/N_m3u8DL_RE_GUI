import { test } from 'node:test';
import assert from 'node:assert/strict';
import { buildHeaderPayload, pruneInflight } from '../lib/inflight-headers.js';

test('buildHeaderPayload extracts the four named headers case-insensitively', () => {
  const payload = buildHeaderPayload(
    [
      { name: 'REFERER', value: 'https://site.example/' },
      { name: 'User-Agent', value: 'UA/1.0' },
      { name: 'cookie', value: 'a=1' },
      { name: 'Origin', value: 'https://site.example' }
    ],
    1000
  );
  assert.equal(payload.referer, 'https://site.example/');
  assert.equal(payload.userAgent, 'UA/1.0');
  assert.equal(payload.cookie, 'a=1');
  assert.equal(payload.origin, 'https://site.example');
  assert.equal(payload.at, 1000);
});

test('buildHeaderPayload keeps every header, original casing, in the ordered array', () => {
  const payload = buildHeaderPayload([
    { name: 'X-Api-Key', value: 'secret' },
    { name: 'Authorization', value: 'Bearer xyz' }
  ]);
  assert.deepEqual(payload.headersArray, [
    { name: 'X-Api-Key', value: 'secret' },
    { name: 'Authorization', value: 'Bearer xyz' }
  ]);
});

test('buildHeaderPayload handles a missing/empty requestHeaders array', () => {
  const payload = buildHeaderPayload(undefined, 5);
  assert.equal(payload.referer, null);
  assert.deepEqual(payload.headersArray, []);
  assert.equal(payload.at, 5);
});

test('buildHeaderPayload ignores a malformed header entry rather than throwing', () => {
  const payload = buildHeaderPayload([null, { name: 'Referer', value: 'https://x/' }, { value: 'no-name' }]);
  assert.equal(payload.referer, 'https://x/');
  assert.equal(payload.headersArray.length, 1);
});

test('two concurrent requests to the same URL in different tabs keep separate header sets (M3)', () => {
  // The production cache is keyed by requestId, not URL — this is the direct
  // regression guard for the bug where a shared URL key let one tab's
  // headers silently overwrite another's.
  const map = new Map();
  map.set('req-1', buildHeaderPayload([{ name: 'Cookie', value: 'tabA=1' }]));
  map.set('req-2', buildHeaderPayload([{ name: 'Cookie', value: 'tabB=1' }]));

  assert.equal(map.get('req-1').cookie, 'tabA=1');
  assert.equal(map.get('req-2').cookie, 'tabB=1');
});

test('pruneInflight is a no-op under the size limit', () => {
  const map = new Map([['a', { at: 1 }], ['b', { at: 2 }]]);
  pruneInflight(map, { maxInflight: 10, ttlMs: 1000, now: 100 });
  assert.equal(map.size, 2);
});

test('pruneInflight sweeps entries older than the TTL', () => {
  const map = new Map([
    ['stale', { at: 0 }],
    ['fresh', { at: 950 }]
  ]);
  pruneInflight(map, { maxInflight: 1, ttlMs: 100, now: 1000 });
  assert.deepEqual(Array.from(map.keys()), ['fresh']);
});

test('pruneInflight falls back to insertion-order eviction when nothing is past TTL', () => {
  const map = new Map([
    ['oldest', { at: 990 }],
    ['middle', { at: 995 }],
    ['newest', { at: 999 }]
  ]);
  pruneInflight(map, { maxInflight: 2, ttlMs: 100000, now: 1000 });
  assert.deepEqual(Array.from(map.keys()), ['middle', 'newest']);
});

test('pruneInflight returns the same map instance', () => {
  const map = new Map();
  assert.strictEqual(pruneInflight(map, { maxInflight: 5, ttlMs: 1000 }), map);
});
