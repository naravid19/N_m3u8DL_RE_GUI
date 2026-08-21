import { test, beforeEach } from 'node:test';
import assert from 'node:assert/strict';
import { installFakeChrome } from './helpers/fake-chrome.js';

const fake = installFakeChrome();
const { addStream, getTabStreams } = await import('../lib/storage.js');

const TAB = 7;

const stream = (url, kind) => ({
  url, kind, referer: null, userAgent: null, cookie: null,
  origin: null, tabId: TAB, timestamp: Date.now()
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
