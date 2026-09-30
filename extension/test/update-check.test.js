import { test } from 'node:test';
import assert from 'node:assert/strict';
import { compareVersions, checkSuiteUpdate } from '../lib/update-check.js';

test('compareVersions handles numeric segment comparisons correctly', () => {
  assert.equal(compareVersions('1.3.0', '1.3.0'), 0);
  assert.equal(compareVersions('v1.3.0', '1.3.0'), 0);
  assert.equal(compareVersions('1.4.0', '1.3.0'), 1);
  assert.equal(compareVersions('2.0.0', '1.3.0'), 1);
  assert.equal(compareVersions('1.3.10', '1.3.9'), 1);
  assert.equal(compareVersions('1.2.9', '1.3.0'), -1);
  assert.equal(compareVersions('1.3.0', '1.3.1'), -1);
});

const respondingWith = (finalUrl) => async () => ({ ok: true, url: finalUrl });

test('a newer suite release is offered', async () => {
  const result = await checkSuiteUpdate('2.1.5', {
    fetchFn: respondingWith('https://github.com/naravid19/N_m3u8DL_RE_GUI/releases/tag/v2.2.0')
  });

  assert.equal(result.status, 'update-available');
  assert.equal(result.latestVersion, 'v2.2.0');
});

test('the current suite release is up to date', async () => {
  const result = await checkSuiteUpdate('2.1.5', {
    fetchFn: respondingWith('https://github.com/naravid19/N_m3u8DL_RE_GUI/releases/tag/v2.1.5')
  });

  assert.equal(result.status, 'up-to-date');
});

test('the extension version is never what gets compared', async () => {
  // S1: comparing the extension's 1.3.0 against a v2.x.x suite tag reported an
  // update forever, and no extension release could ever clear it.
  const result = await checkSuiteUpdate('2.1.5', {
    fetchFn: respondingWith('https://github.com/naravid19/N_m3u8DL_RE_GUI/releases/tag/v2.1.5')
  });

  assert.notEqual(result.status, 'update-available');
});

test('the final URL is read, not a Location header', async () => {
  // S2: redirect:"manual" yields an opaque-redirect response whose headers are
  // empty by design, so the header could never be read in a browser.
  const result = await checkSuiteUpdate('2.1.5', {
    fetchFn: async () => ({
      ok: true,
      url: 'https://github.com/naravid19/N_m3u8DL_RE_GUI/releases/tag/v2.2.0',
      headers: { get: () => null }
    })
  });

  assert.equal(result.status, 'update-available');
});

test('an older published tag is not offered as an update', async () => {
  const result = await checkSuiteUpdate('2.1.5', {
    fetchFn: respondingWith('https://github.com/naravid19/N_m3u8DL_RE_GUI/releases/tag/v2.1.4')
  });

  assert.equal(result.status, 'up-to-date');
});

test('patch numbers compare numerically', async () => {
  const result = await checkSuiteUpdate('2.1.9', {
    fetchFn: respondingWith('https://github.com/naravid19/N_m3u8DL_RE_GUI/releases/tag/v2.1.10')
  });

  assert.equal(result.status, 'update-available');
});

test('an unknown suite version is reported, not guessed', async () => {
  const result = await checkSuiteUpdate(null, { fetchFn: respondingWith('.../tag/v2.2.0') });

  assert.equal(result.status, 'unknown-version');
});

test('a network failure is distinguishable from being up to date', async () => {
  // S5: the C# checker was corrected for exactly this conflation.
  const result = await checkSuiteUpdate('2.1.5', {
    fetchFn: async () => { throw new TypeError('offline'); }
  });

  assert.equal(result.status, 'check-failed');
});

test('a final URL with no recognisable tag is a failure', async () => {
  const result = await checkSuiteUpdate('2.1.5', {
    fetchFn: respondingWith('https://github.com/naravid19/N_m3u8DL_RE_GUI/releases')
  });

  assert.equal(result.status, 'check-failed');
});

test('a two-component tag is a failure, not a quiet up-to-date', async () => {
  const result = await checkSuiteUpdate('2.1.5', {
    fetchFn: respondingWith('https://github.com/naravid19/N_m3u8DL_RE_GUI/releases/tag/v2.2')
  });

  assert.equal(result.status, 'check-failed');
});

