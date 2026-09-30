import { test, beforeEach } from 'node:test';
import assert from 'node:assert/strict';
import { installFakeChrome } from './helpers/fake-chrome.js';

const fake = installFakeChrome();
const { probeVariants } = await import('../lib/probe.js');

const MASTER = '#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=1,RESOLUTION=1920x1080\na.m3u8\n';
const stream = { url: 'https://cdn.example.com/master.m3u8', kind: 'HLS', tabId: 7 };

const ok = (text) => async () => ({ ok: true, status: 200, text });
const fail = (status) => async () => ({ ok: false, status });
const never = () => async () => { throw new Error('should not have been called'); };

beforeEach(() => fake.reset());

test('parses variants from a successful page fetch', async () => {
  const result = await probeVariants(stream, 7, { fetchFromPage: ok(MASTER), fetchDirect: never() });

  assert.equal(result.error, null);
  assert.equal(result.variants.length, 1);
  assert.equal(result.variants[0].height, 1080);
});

test('the page fetch is preferred and the fallback is not attempted', async () => {
  await probeVariants(stream, 7, { fetchFromPage: ok(MASTER), fetchDirect: never() });
});

test('falls back to a direct fetch when the page fetch fails', async () => {
  const result = await probeVariants(stream, 7, { fetchFromPage: fail(403), fetchDirect: ok(MASTER) });

  assert.equal(result.error, null);
  assert.equal(result.variants.length, 1);
});

test('goes straight to the direct fetch when there is no tab', async () => {
  const result = await probeVariants(
    { ...stream, tabId: null }, null,
    { fetchFromPage: never(), fetchDirect: ok(MASTER) }
  );

  assert.equal(result.variants.length, 1);
});

test('reports an error when both paths fail', async () => {
  const result = await probeVariants(stream, 7, { fetchFromPage: fail(403), fetchDirect: fail(403) });

  assert.equal(result.variants.length, 0);
  assert.ok(result.error);
});

test('a success is served from cache on the second call', async () => {
  await probeVariants(stream, 7, { fetchFromPage: ok(MASTER), fetchDirect: never() });
  const second = await probeVariants(stream, 7, { fetchFromPage: never(), fetchDirect: never() });

  assert.equal(second.variants.length, 1);
});

test('a failure is cached too, so a dead host is not retried on every expand', async () => {
  await probeVariants(stream, 7, { fetchFromPage: fail(500), fetchDirect: fail(500) });
  const second = await probeVariants(stream, 7, { fetchFromPage: never(), fetchDirect: never() });

  assert.ok(second.error);
});

test('an oversize body is refused', async () => {
  const huge = 'x'.repeat(3 * 1024 * 1024);
  const result = await probeVariants(stream, 7, { fetchFromPage: ok(huge), fetchDirect: never() });

  assert.match(result.error, /size/i);
});

test('an empty body is reported rather than parsed', async () => {
  const result = await probeVariants(stream, 7, { fetchFromPage: ok(''), fetchDirect: never() });

  assert.equal(result.variants.length, 0);
  assert.ok(result.error);
});

test('a stream with no URL is rejected before any fetch', async () => {
  const result = await probeVariants({ kind: 'HLS' }, 7, { fetchFromPage: never(), fetchDirect: never() });

  assert.ok(result.error);
});

test('content is sniffed when the kind was a low-confidence guess', async () => {
  const result = await probeVariants(
    { url: 'https://cdn.example.com/get?id=1', kind: 'Media', tabId: 7 }, 7,
    { fetchFromPage: ok(MASTER), fetchDirect: never() }
  );

  assert.equal(result.variants.length, 1);
});

test('two different URLs do not share a cache entry', async () => {
  await probeVariants(stream, 7, { fetchFromPage: ok(MASTER), fetchDirect: never() });
  const other = await probeVariants(
    { ...stream, url: 'https://cdn.example.com/other.m3u8' }, 7,
    { fetchFromPage: ok(MASTER), fetchDirect: never() }
  );

  assert.equal(other.variants.length, 1);
});
