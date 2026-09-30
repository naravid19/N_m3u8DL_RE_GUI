import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { getExtensionVersion } from '../lib/version.js';

test('getExtensionVersion reads from chrome.runtime.getManifest() when available', () => {
  const origChrome = globalThis.chrome;
  try {
    globalThis.chrome = {
      runtime: {
        getManifest: () => ({ version: '1.3.0' })
      }
    };
    assert.equal(getExtensionVersion(), '1.3.0');
  } finally {
    globalThis.chrome = origChrome;
  }
});

test('getExtensionVersion returns empty string when chrome runtime is unavailable', () => {
  const origChrome = globalThis.chrome;
  try {
    globalThis.chrome = undefined;
    assert.equal(getExtensionVersion(), '');
  } finally {
    globalThis.chrome = origChrome;
  }
});

test('manifest.json and package.json version stay synchronized', () => {
  const manifest = JSON.parse(readFileSync(resolve('extension/manifest.json'), 'utf8'));
  const pkg = JSON.parse(readFileSync(resolve('extension/package.json'), 'utf8'));

  assert.equal(manifest.version, '1.3.0');
  assert.equal(pkg.version, '1.3.0');
  assert.equal(manifest.version, pkg.version);
});

