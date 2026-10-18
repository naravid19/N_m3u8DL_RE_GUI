import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { FIREFOX_EXTENSION_ID, toFirefoxManifest } from '../scripts/firefox-manifest.mjs';

const manifestUrl = new URL('../manifest.json', import.meta.url);
const chromeManifest = JSON.parse(readFileSync(manifestUrl, 'utf8'));

test('drops Chrome-only keys', () => {
  const result = toFirefoxManifest(chromeManifest);
  assert.equal('key' in result, false);
  assert.equal('incognito' in result, false);
  assert.equal('minimum_chrome_version' in result, false);
  assert.equal('service_worker' in (result.background || {}), false);
});

test('uses background.scripts with the same file, module type', () => {
  const result = toFirefoxManifest(chromeManifest);
  assert.deepEqual(result.background, {
    scripts: [chromeManifest.background.service_worker],
    type: 'module'
  });
});

test('declares gecko id, strict_min_version 128.0, data_collection_permissions none', () => {
  const result = toFirefoxManifest(chromeManifest);
  assert.deepEqual(result.browser_specific_settings, {
    gecko: {
      id: FIREFOX_EXTENSION_ID,
      strict_min_version: '128.0',
      data_collection_permissions: { required: ['none'] }
    }
  });
});

test('keeps permissions, host_permissions, content_scripts, action, icons, version unchanged', () => {
  const result = toFirefoxManifest(chromeManifest);
  assert.deepEqual(result.permissions, chromeManifest.permissions);
  assert.deepEqual(result.host_permissions, chromeManifest.host_permissions);
  assert.deepEqual(result.content_scripts, chromeManifest.content_scripts);
  assert.deepEqual(result.action, chromeManifest.action);
  assert.deepEqual(result.icons, chromeManifest.icons);
  assert.equal(result.version, chromeManifest.version);
  assert.equal(result.name, chromeManifest.name);
  assert.equal(result.description, chromeManifest.description);
});

test('does not mutate its input', () => {
  const input = JSON.parse(JSON.stringify(chromeManifest));
  const before = JSON.stringify(input);
  toFirefoxManifest(input);
  const after = JSON.stringify(input);
  assert.equal(before, after);
});

test('gecko id matches the C# constant', () => {
  const csPath = new URL('../../N_m3u8DL_RE_GUI.Core/NativeBridge/NativeHostManifest.cs', import.meta.url);
  const csSource = readFileSync(csPath, 'utf8');
  const match = csSource.match(/public const string FirefoxExtensionId = "([^"]+)";/);
  assert.ok(match, 'FirefoxExtensionId constant must exist in NativeHostManifest.cs');
  assert.equal(FIREFOX_EXTENSION_ID, match[1]);
});

test('the Firefox build leaves tool caches and dev files out of the add-on', async () => {
  const { mkdtempSync, mkdirSync, writeFileSync, existsSync } = await import('node:fs');
  const { tmpdir } = await import('node:os');
  const { join } = await import('node:path');
  const { buildFirefoxDist } = await import('../scripts/firefox-manifest.mjs');

  const dir = mkdtempSync(join(tmpdir(), 'nre-ext-'));
  writeFileSync(join(dir, 'manifest.json'), JSON.stringify(chromeManifest));
  writeFileSync(join(dir, 'background.js'), '');
  for (const skipped of ['.impeccable', 'test', 'scripts']) {
    mkdirSync(join(dir, skipped));
    writeFileSync(join(dir, skipped, 'x.json'), '{}');
  }

  buildFirefoxDist(dir);

  assert.ok(existsSync(join(dir, 'dist-firefox', 'background.js')));
  for (const skipped of ['.impeccable', 'test', 'scripts']) {
    assert.equal(existsSync(join(dir, 'dist-firefox', skipped)), false, `${skipped} leaked into dist-firefox`);
  }
});
