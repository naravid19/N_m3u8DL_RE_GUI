import { test } from 'node:test';
import assert from 'node:assert/strict';
import { resolveImpersonate } from '../lib/impersonate.js';

test('resolveImpersonate maps Chrome versions correctly', () => {
  assert.equal(resolveImpersonate('Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36'), 'chrome131');
  assert.equal(resolveImpersonate('Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36'), 'chrome120');
  assert.equal(resolveImpersonate('Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/100.0.0.0 Safari/537.36'), 'chrome');
});

test('resolveImpersonate maps Edge and Safari correctly', () => {
  assert.equal(resolveImpersonate('Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 Edg/101.0.1210.47'), 'edge101');
  assert.equal(resolveImpersonate('Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.2 Safari/605.1.15'), 'safari17_0');
});

test('resolveImpersonate falls back to chrome on unknown or empty input', () => {
  assert.equal(resolveImpersonate(''), 'chrome');
  assert.equal(resolveImpersonate(null), 'chrome');
  assert.equal(resolveImpersonate('CustomBot/1.0'), 'chrome');
});
