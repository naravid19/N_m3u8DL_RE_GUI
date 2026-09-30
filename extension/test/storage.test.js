import { test, beforeEach } from 'node:test';
import assert from 'node:assert/strict';
import { installFakeChrome } from './helpers/fake-chrome.js';

const fake = installFakeChrome();
const {
  addStream,
  getTabStreams,
  getRecentStreams,
  clearTab,
  clearAll,
  getCachedVariants,
  setCachedVariants,
  dismissStreams,
  dismissMany,
  getDismissed,
  getCachedUpdateResult,
  setCachedUpdateResult
} = await import('../lib/storage.js');

const TAB = 7;

const stream = (url, kind) => ({
  url, kind, referer: null, userAgent: null, cookie: null,
  origin: null, tabId: TAB, timestamp: Date.now()
});

const s = (url, kind = 'HLS') => ({
  url, kind, confidence: 'high', referer: null, userAgent: null,
  cookie: null, origin: null, sizeBytes: null, tabId: TAB, timestamp: Date.now()
});

beforeEach(() => fake.reset());

test('stores a manifest', async () => {
  await addStream(TAB, stream('https://cdn.example.com/master.m3u8', 'HLS'));

  const list = await getTabStreams(TAB);
  assert.equal(list.length, 1);
});

test('drops media once the tab has a manifest', async () => {
  await addStream(TAB, stream('https://cdn.example.com/master.m3u8', 'HLS'));
  await addStream(TAB, stream('https://cdn.example.com/seg-00001.mp4', 'Media'));
  await addStream(TAB, stream('https://cdn.example.com/seg-00002.mp4', 'Media'));

  const list = await getTabStreams(TAB);
  assert.equal(list.length, 1);
  assert.equal(list[0].kind, 'HLS');
});

test('purges media already stored when a manifest arrives late', async () => {
  await addStream(TAB, stream('https://cdn.example.com/seg-00001.mp4', 'Media'));
  await addStream(TAB, stream('https://cdn.example.com/seg-00002.mp4', 'Media'));
  await addStream(TAB, stream('https://cdn.example.com/master.m3u8', 'HLS'));

  const list = await getTabStreams(TAB);
  assert.equal(list.length, 1);
  assert.equal(list[0].kind, 'HLS');
});

test('purges media from recent_streams when a manifest arrives late (M3)', async () => {
  await addStream(TAB, stream('https://cdn.example.com/seg-00001.mp4', 'Media'));
  await addStream(TAB, stream('https://cdn.example.com/master.m3u8', 'HLS'));

  const recent = await getRecentStreams();
  assert.equal(recent.length, 1);
  assert.equal(recent[0].kind, 'HLS');
});

test('keeps multiple manifests — the user may need to choose', async () => {
  await addStream(TAB, stream('https://cdn.example.com/master.m3u8', 'HLS'));
  await addStream(TAB, stream('https://cdn.example.com/audio.mpd', 'DASH'));

  assert.equal((await getTabStreams(TAB)).length, 2);
});

test('keeps media when the tab has no manifest at all', async () => {
  await addStream(TAB, stream('https://cdn.example.com/movie.mp4', 'Media'));

  const list = await getTabStreams(TAB);
  assert.equal(list.length, 1);
  assert.equal(list[0].kind, 'Media');
});

test('an Abyss entry does not suppress media', async () => {
  // Abyss is a player page, not a manifest — it says nothing about segments.
  await addStream(TAB, stream('https://abysscdn.com/?v=abc', 'Abyss'));
  await addStream(TAB, stream('https://cdn.example.com/movie.mp4', 'Media'));

  assert.equal((await getTabStreams(TAB)).length, 2);
});

test('deduplicates an identical URL', async () => {
  await addStream(TAB, stream('https://cdn.example.com/master.m3u8', 'HLS'));
  await addStream(TAB, stream('https://cdn.example.com/master.m3u8', 'HLS'));

  assert.equal((await getTabStreams(TAB)).length, 1);
});

test('concurrent writes do not lose entries', async () => {
  // The F2 regression guard: without serialization these clobber each other.
  await Promise.all(
    Array.from({ length: 20 }, (_, i) =>
      addStream(TAB, stream(`https://cdn.example.com/movie-${i}.mp4`, 'Media'))
    )
  );

  assert.equal((await getTabStreams(TAB)).length, 20);
});

test('caps a tab at 25 entries', async () => {
  for (let i = 0; i < 40; i++) {
    await addStream(TAB, stream(`https://cdn.example.com/movie-${i}.mp4`, 'Media'));
  }

  assert.equal((await getTabStreams(TAB)).length, 25);
});

test('returns the tab count so the badge is accurate', async () => {
  assert.equal(await addStream(TAB, stream('https://cdn.example.com/a.mp4', 'Media')), 1);
  assert.equal(await addStream(TAB, stream('https://cdn.example.com/b.mp4', 'Media')), 2);
  // A duplicate must not inflate the badge.
  assert.equal(await addStream(TAB, stream('https://cdn.example.com/b.mp4', 'Media')), 2);
});

test('clearAll removes every tab list and the recent list', async () => {
  await addStream(TAB, stream('https://cdn.example.com/a.m3u8', 'HLS'));
  await addStream(9, stream('https://cdn.example.com/b.m3u8', 'HLS'));

  await clearAll();

  assert.deepEqual(await getTabStreams(TAB), []);
  assert.deepEqual(await getTabStreams(9), []);
  assert.deepEqual(await getRecentStreams(), []);
});

test('purges Audio entries when a manifest arrives late', async () => {
  await addStream(TAB, stream('https://cdn.example.com/audio-seg.aac', 'Audio'));
  await addStream(TAB, stream('https://cdn.example.com/master.m3u8', 'HLS'));

  const list = await getTabStreams(TAB);
  assert.equal(list.length, 1);
  assert.equal(list[0].kind, 'HLS');
});

test('a clear issued mid-detection is not undone by it', async () => {
  // Without serialization the in-flight addStream's set() lands after the
  // remove() and the list reappears.
  const pending = addStream(TAB, stream('https://cdn.example.com/late.m3u8', 'HLS'));
  const cleared = clearAll();
  await Promise.all([pending, cleared]);

  assert.deepEqual(await getRecentStreams(), []);
});

test('getCachedVariants returns stored variants for a URL within TTL', async () => {
  const url = 'https://cdn.example.com/m.m3u8';
  const data = { variants: [{ height: 1080, bandwidth: 5000000 }], error: null };
  const now = 1000000;

  await setCachedVariants(url, data, now);

  const cached = await getCachedVariants(url, now + 5000);
  assert.deepEqual(cached, data);
});

test('getCachedVariants returns null for an expired entry', async () => {
  const url = 'https://cdn.example.com/m.m3u8';
  const data = { variants: [{ height: 1080 }], error: null };
  const now = 1000000;

  await setCachedVariants(url, data, now);

  // 1 hour + 1 second later
  const cached = await getCachedVariants(url, now + 3600000 + 1000);
  assert.equal(cached, null);
});

test('getCachedVariants treats failed probes with shorter TTL', async () => {
  const url = 'https://cdn.example.com/fail.m3u8';
  const data = { variants: [], error: 'HTTP 403' };
  const now = 1000000;

  await setCachedVariants(url, data, now);

  // 10 seconds later: still cached
  assert.deepEqual(await getCachedVariants(url, now + 10000), data);

  // 35 seconds later: expired
  assert.equal(await getCachedVariants(url, now + 35000), null);
});

test('clearAll removes cached variants as well', async () => {
  const url = 'https://cdn.example.com/m.m3u8';
  await setCachedVariants(url, { variants: [{ height: 720 }], error: null });

  await clearAll();

  assert.equal(await getCachedVariants(url), null);
});

test('a dismissed URL is not re-added', async () => {
  const item = stream('https://cdn.example.com/master.m3u8', 'HLS');
  await addStream(TAB, item);
  await dismissStreams(TAB, [item.url]);

  await addStream(TAB, item);

  assert.deepEqual(await getTabStreams(TAB), []);
});

test('a URL that was never dismissed still appears after a clear', async () => {
  // The failure mode worse than the bug: Clear must not hide something new.
  await addStream(TAB, stream('https://cdn.example.com/old.m3u8', 'HLS'));
  await dismissStreams(TAB, ['https://cdn.example.com/old.m3u8']);

  await addStream(TAB, stream('https://cdn.example.com/new.m3u8', 'HLS'));

  const list = await getTabStreams(TAB);
  assert.equal(list.length, 1);
  assert.equal(list[0].url, 'https://cdn.example.com/new.m3u8');
});

test('dismissal is scoped to its tab', async () => {
  const url = 'https://cdn.example.com/master.m3u8';
  await dismissStreams(TAB, [url]);

  await addStream(9, stream(url, 'HLS'));

  assert.equal((await getTabStreams(9)).length, 1);
});

test('a dismissed URL is also kept out of the recent list', async () => {
  const item = stream('https://cdn.example.com/master.m3u8', 'HLS');
  await dismissStreams(TAB, [item.url]);

  await addStream(TAB, item);

  assert.deepEqual(await getRecentStreams(), []);
});

test('dismissing several URLs at once', async () => {
  await dismissStreams(TAB, ['https://a/1.m3u8', 'https://a/2.m3u8']);

  await addStream(TAB, stream('https://a/1.m3u8', 'HLS'));
  await addStream(TAB, stream('https://a/2.m3u8', 'HLS'));

  assert.deepEqual(await getTabStreams(TAB), []);
});

test('dismissing is additive, not replacing', async () => {
  await dismissStreams(TAB, ['https://a/1.m3u8']);
  await dismissStreams(TAB, ['https://a/2.m3u8']);

  assert.equal((await getDismissed(TAB)).size, 2);
});

test('the dismissed set is bounded', async () => {
  const many = Array.from({ length: 300 }, (_, i) => `https://a/${i}.m3u8`);
  await dismissStreams(TAB, many);

  assert.ok((await getDismissed(TAB)).size <= 200);
});

test('the newest dismissals survive the bound', async () => {
  // Dropping the oldest is right: an old URL is unlikely to be re-requested,
  // and if it is, showing it again is a smaller harm than hiding a fresh one.
  await dismissStreams(TAB, Array.from({ length: 200 }, (_, i) => `https://a/${i}.m3u8`));
  await dismissStreams(TAB, ['https://a/newest.m3u8']);

  assert.ok((await getDismissed(TAB)).has('https://a/newest.m3u8'));
});

test('dismissStreams with an empty list is a no-op', async () => {
  await dismissStreams(TAB, []);

  assert.equal((await getDismissed(TAB)).size, 0);
});

test('getDismissed returns an empty set for an unknown tab', async () => {
  assert.equal((await getDismissed(4242)).size, 0);
});

test('clearTab also drops that tab dismissals', async () => {
  await dismissStreams(TAB, ['https://a/1.m3u8']);

  await clearTab(TAB);

  assert.equal((await getDismissed(TAB)).size, 0);
});

test('dismissMany records each URL against its own tab', async () => {
  await dismissMany([
    { url: 'https://a/1.m3u8', tabId: 7 },
    { url: 'https://a/2.m3u8', tabId: 9 }
  ]);

  assert.ok((await getDismissed(7)).has('https://a/1.m3u8'));
  assert.ok((await getDismissed(9)).has('https://a/2.m3u8'));
});

test('dismissMany does not cross-contaminate tabs', async () => {
  await dismissMany([{ url: 'https://a/1.m3u8', tabId: 7 }]);

  assert.equal((await getDismissed(9)).size, 0);
});

test('clearing All Recent stops every source tab refilling it', async () => {
  // The reported failure: recent_streams holds items from many tabs, and
  // dismissing them all under the active tab left every other tab free to
  // re-add its own.
  await addStream(7, { ...s('https://a/1.m3u8'), tabId: 7 });
  await addStream(9, { ...s('https://a/2.m3u8'), tabId: 9 });

  const recent = await getRecentStreams();
  await dismissMany(recent.map((x) => ({ url: x.url, tabId: x.tabId })));

  await addStream(7, { ...s('https://a/1.m3u8'), tabId: 7 });
  await addStream(9, { ...s('https://a/2.m3u8'), tabId: 9 });

  assert.deepEqual(await getRecentStreams(), []);
});

test('dismissMany groups so one tab is written once', async () => {
  await dismissMany([
    { url: 'https://a/1.m3u8', tabId: 7 },
    { url: 'https://a/2.m3u8', tabId: 7 },
    { url: 'https://a/3.m3u8', tabId: 7 }
  ]);

  assert.equal((await getDismissed(7)).size, 3);
});

test('dismissMany with an empty list is a no-op', async () => {
  await dismissMany([]);
});

test('a stream with no tab can be dismissed', async () => {
  const url = 'https://cdn.example.com/orphan.m3u8';
  await addStream(-1, { ...s(url), tabId: null });
  await dismissStreams(null, [url]);

  await addStream(-1, { ...s(url), tabId: null });

  assert.deepEqual(await getRecentStreams(), []);
});

test('a no-tab dismissal does not suppress the same URL on a real tab', async () => {
  const url = 'https://cdn.example.com/shared.m3u8';
  await dismissStreams(null, [url]);

  await addStream(7, { ...s(url), tabId: 7 });

  assert.equal((await getTabStreams(7)).length, 1);
});

test('clearAll leaves the no-tab dismissal in place', async () => {
  await dismissStreams(null, ['https://cdn.example.com/orphan.m3u8']);

  await clearAll();

  assert.equal((await getDismissed(null)).size, 1);
});

test('a cached result is reused within the TTL', async () => {
  await setCachedUpdateResult({ status: 'up-to-date', latestVersion: 'v2.1.5' });

  assert.equal((await getCachedUpdateResult())?.status, 'up-to-date');
});

test('a cached result expires after a day', async () => {
  const twentyFiveHoursAgo = Date.now() - 25 * 60 * 60 * 1000;
  await setCachedUpdateResult({ status: 'up-to-date' }, twentyFiveHoursAgo);

  assert.equal(await getCachedUpdateResult(), null);
});

test('a failed check is cached only briefly so a blip is not sticky', async () => {
  const tenMinutesAgo = Date.now() - 10 * 60 * 1000;
  await setCachedUpdateResult({ status: 'check-failed' }, tenMinutesAgo);

  assert.equal(await getCachedUpdateResult(), null);
});

test('an empty cache reports nothing rather than throwing', async () => {
  assert.equal(await getCachedUpdateResult(), null);
});
