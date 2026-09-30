import { test } from 'node:test';
import assert from 'node:assert/strict';
import { PROFILES } from '../lib/impersonate-profiles.js';
import { resolveImpersonate } from '../lib/impersonate.js';

test('PROFILES patterns are all valid regular expressions', () => {
  for (const entry of PROFILES) {
    assert.ok(entry.pattern, 'profile entry must have a pattern');
    assert.doesNotThrow(() => new RegExp(entry.pattern), `Pattern ${entry.pattern} must be valid RegExp`);
    assert.ok(Array.isArray(entry.profiles) && entry.profiles.length > 0, 'profiles must be a non-empty array');
  }
});

test('PROFILES have minVersion sorted in descending order within each entry', () => {
  for (const entry of PROFILES) {
    const versions = entry.profiles.map((p) => p.minVersion);
    for (let i = 1; i < versions.length; i++) {
      assert.ok(
        versions[i - 1] > versions[i],
        `minVersion in ${entry.pattern} must be strictly descending: ${versions[i - 1]} vs ${versions[i]}`
      );
    }
    assert.equal(versions[versions.length - 1], 0, `last profile in ${entry.pattern} should be fallback (minVersion 0)`);
  }
});

test('Edge pattern is defined before Chrome pattern so Edge UAs are not misclassified as Chrome', () => {
  const edgeIdx = PROFILES.findIndex((p) => p.pattern.includes('Edg'));
  const chromeIdx = PROFILES.findIndex((p) => p.pattern.includes('Chrome'));
  assert.ok(edgeIdx >= 0, 'Edge pattern must exist');
  assert.ok(chromeIdx >= 0, 'Chrome pattern must exist');
  assert.ok(edgeIdx < chromeIdx, 'Edge pattern must come before Chrome pattern');
});

test('resolveImpersonate maps real user agents correctly', () => {
  const edgeUA = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36 Edg/130.0.0.0';
  assert.equal(resolveImpersonate(edgeUA), 'edge101');

  const chrome131UA = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36';
  assert.equal(resolveImpersonate(chrome131UA), 'chrome131');

  const chrome120UA = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36';
  assert.equal(resolveImpersonate(chrome120UA), 'chrome120');

  const safariUA = 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.4 Safari/605.1.15';
  assert.equal(resolveImpersonate(safariUA), 'safari17_0');

  assert.equal(resolveImpersonate(null), 'chrome');
  assert.equal(resolveImpersonate(''), 'chrome');
});
