import { test } from 'node:test';
import assert from 'node:assert/strict';
import { getMergedCookies, applyMergedCookie } from '../lib/cookies.js';

test('getMergedCookies merges store cookies and captured header, with header winning on collision', async () => {
  const fakeApi = {
    getAll: async ({ url }) => [
      { name: 'cf_clearance', value: 'from_store' },
      { name: 'store_only', value: 'val1' }
    ]
  };

  const res = await getMergedCookies(
    'https://example.com/stream.m3u8',
    'https://example.com/stream.m3u8',
    'cf_clearance=from_header; header_only=val2',
    fakeApi
  );
  assert.match(res, /cf_clearance=from_header/);
  assert.match(res, /store_only=val1/);
  assert.match(res, /header_only=val2/);
  assert.doesNotMatch(res, /cf_clearance=from_store/);
});

test('getMergedCookies handles empty inputs gracefully', async () => {
  const res = await getMergedCookies('', '', null, null);
  assert.equal(res, '');
});

test('getMergedCookies fetches the page domain separately from the stream/CDN domain (N6)', async () => {
  const calls = [];
  const fakeApi = {
    getAll: async ({ url }) => {
      calls.push(url);
      if (url === 'https://site.example/watch/123') {
        return [{ name: 'cf_clearance', value: 'page_domain_value' }];
      }
      if (url === 'https://cdn.example.net/seg.m3u8') {
        return [{ name: 'cdn_auth', value: 'cdn_domain_value' }];
      }
      return [];
    }
  };

  const res = await getMergedCookies(
    'https://cdn.example.net/seg.m3u8',
    'https://site.example/watch/123',
    null,
    fakeApi
  );

  assert.deepEqual(calls.sort(), ['https://cdn.example.net/seg.m3u8', 'https://site.example/watch/123'].sort());
  assert.match(res, /cf_clearance=page_domain_value/);
  assert.match(res, /cdn_auth=cdn_domain_value/);
});

test('getMergedCookies does not double-fetch when stream and page URL are identical', async () => {
  let callCount = 0;
  const fakeApi = {
    getAll: async () => {
      callCount++;
      return [{ name: 'x', value: '1' }];
    }
  };

  await getMergedCookies('https://same.example/a', 'https://same.example/a', null, fakeApi);
  assert.equal(callCount, 1);
});

test('getMergedCookies swallows a cookies API failure and still returns the captured header', async () => {
  const fakeApi = { getAll: async () => { throw new Error('permission denied'); } };
  const res = await getMergedCookies('https://x.example/a', 'https://y.example/b', 'sid=abc', fakeApi);
  assert.equal(res, 'sid=abc');
});

test('getMergedCookies returns empty string when nothing is available', async () => {
  const fakeApi = { getAll: async () => [] };
  const res = await getMergedCookies('https://x.example/a', 'https://x.example/a', null, fakeApi);
  assert.equal(res, '');
});

test('applyMergedCookie upserts a Cookie header when stream.headers is an array (fixes N5)', () => {
  const stream = {
    url: 'https://cdn.example.net/seg.m3u8',
    headers: [
      { name: 'Referer', value: 'https://site.example/' },
      { name: 'Cookie', value: 'stale=1' }
    ]
  };

  const out = applyMergedCookie(stream, 'cf_clearance=fresh; session=abc');
  const cookieHeader = out.headers.find((h) => h.name === 'Cookie');
  assert.equal(cookieHeader.value, 'cf_clearance=fresh; session=abc');
  assert.equal(out.headers.filter((h) => h.name === 'Cookie').length, 1);
  assert.equal(out.headers.find((h) => h.name === 'Referer').value, 'https://site.example/');
});

test('applyMergedCookie adds a Cookie header when none existed', () => {
  const stream = { url: 'https://cdn.example.net/seg.m3u8', headers: [{ name: 'Referer', value: 'https://x/' }] };
  const out = applyMergedCookie(stream, 'a=1');
  assert.deepEqual(
    out.headers.find((h) => h.name === 'Cookie'),
    { name: 'Cookie', value: 'a=1' }
  );
});

test('applyMergedCookie falls back to the flat .cookie field when headers is not an array', () => {
  const stream = { url: 'https://cdn.example.net/seg.m3u8', cookie: 'old=1' };
  const out = applyMergedCookie(stream, 'new=2');
  assert.equal(out.cookie, 'new=2');
  assert.equal(out.headers, undefined);
});

test('applyMergedCookie is a no-op when mergedCookie is empty', () => {
  const stream = { url: 'https://x/', headers: [{ name: 'Cookie', value: 'keep=1' }] };
  const out = applyMergedCookie(stream, '');
  assert.strictEqual(out, stream);
});

test('applyMergedCookie is a no-op for a null stream', () => {
  assert.strictEqual(applyMergedCookie(null, 'a=1'), null);
});
