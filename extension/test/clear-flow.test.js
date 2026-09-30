import { test, beforeEach } from 'node:test';
import assert from 'node:assert/strict';
import { installFakeChrome } from './helpers/fake-chrome.js';

const fake = installFakeChrome();
const { addStream, getTabStreams, getRecentStreams, dismissStreams, clearAll, clearTab, clearTabView, getDismissed } = await import('../lib/storage.js');

const TAB = 7;
const s = (url, kind = 'HLS') => ({
  url, kind, confidence: 'high', referer: null, userAgent: null,
  cookie: null, origin: null, sizeBytes: null, tabId: TAB, timestamp: Date.now()
});

beforeEach(() => fake.reset());

test('the reported bug: a still-playing page does not refill a cleared list', async () => {
  const playing = s('https://cdn.example.com/master.m3u8');
  await addStream(TAB, playing);

  // What the popup's Clear button does.
  const visible = await getTabStreams(TAB);
  await dismissStreams(TAB, visible.map((x) => x.url));
  await clearAll();

  // What the page does a few milliseconds later.
  await addStream(TAB, playing);
  await addStream(TAB, playing);

  assert.deepEqual(await getTabStreams(TAB), []);
});

test('clearing does not hide a stream the user has not seen', async () => {
  await addStream(TAB, s('https://cdn.example.com/old.m3u8'));

  const visible = await getTabStreams(TAB);
  await dismissStreams(TAB, visible.map((x) => x.url));
  await clearAll();

  await addStream(TAB, s('https://cdn.example.com/fresh.m3u8'));

  const list = await getTabStreams(TAB);
  assert.equal(list.length, 1);
  assert.equal(list[0].url, 'https://cdn.example.com/fresh.m3u8');
});

test('clearing one tab leaves another tab alone', async () => {
  const url = 'https://cdn.example.com/master.m3u8';
  await addStream(TAB, s(url));
  await dismissStreams(TAB, [url]);

  await addStream(9, { ...s(url), tabId: 9 });

  assert.equal((await getTabStreams(9)).length, 1);
});

test('clearing an empty list is harmless', async () => {
  await dismissStreams(TAB, []);
  await clearAll();

  assert.deepEqual(await getTabStreams(TAB), []);
});

test('a cleared then re-detected stream reappears after the tab is reset', async () => {
  const url = 'https://cdn.example.com/master.m3u8';
  await addStream(TAB, s(url));
  await dismissStreams(TAB, [url]);
  await clearTab(TAB); // clearTab drops dismissals as well as lists

  await addStream(TAB, s(url));

  assert.equal((await getTabStreams(TAB)).length, 1);
});

test('a dismissed stream stays dismissed across clearAll', async () => {
  // clearAll wipes the lists and the variant cache. It must NOT wipe
  // dismissals, or Clear stops holding the moment it finishes.
  const url = 'https://cdn.example.com/master.m3u8';
  await addStream(TAB, s(url));
  await dismissStreams(TAB, [url]);
  await clearAll();

  await addStream(TAB, s(url));

  assert.deepEqual(await getTabStreams(TAB), []);
});

test('clearAll does not remove dismissed keys', async () => {
  await dismissStreams(TAB, ['https://cdn.example.com/a.m3u8']);

  await clearAll();

  assert.equal((await getDismissed(TAB)).size, 1);
});

test('clearing the current tab leaves other tabs alone', async () => {
  await addStream(7, { ...s('https://a/1.m3u8'), tabId: 7 });
  await addStream(9, { ...s('https://a/2.m3u8'), tabId: 9 });

  await clearTabView(7);

  assert.deepEqual(await getTabStreams(7), []);
  assert.equal((await getTabStreams(9)).length, 1);
});

test('clearing All Recent empties every list', async () => {
  await addStream(7, { ...s('https://a/1.m3u8'), tabId: 7 });
  await addStream(9, { ...s('https://a/2.m3u8'), tabId: 9 });

  await clearAll();

  assert.deepEqual(await getRecentStreams(), []);
});
