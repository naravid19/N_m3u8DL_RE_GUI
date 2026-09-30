import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { ICON_NAMES, hasIcon } from '../lib/icons.js';

const html = readFileSync(new URL('../popup/popup.html', import.meta.url), 'utf8');
const js = readFileSync(new URL('../popup/popup.js', import.meta.url), 'utf8');

test('every icon referenced in the markup exists', () => {
  const referenced = [...html.matchAll(/data-icon="([^"]+)"/g)].map((m) => m[1]);
  assert.ok(referenced.length > 0, 'popup.html should declare icons via data-icon');
  for (const name of referenced) {
    assert.ok(hasIcon(name), `popup.html references an icon that does not exist: ${name}`);
  }
});

test('every icon requested from popup.js exists', () => {
  const direct = [...js.matchAll(/\bicon\(\s*'([^']+)'/g)].map((m) => m[1]);
  const fromSetBtn = [...js.matchAll(/\bsetButtonLabel\(\s*[^,]+,\s*'([^']+)'/g)].map((m) => m[1]);
  const fromCopy = [...js.matchAll(/\bcopyWithFeedback\(\s*[^,]+,\s*[^,]+,\s*'([^']+)'/g)].map((m) => m[1]);
  const referenced = [...new Set([...direct, ...fromSetBtn, ...fromCopy])].filter((n) => n !== 'null');

  assert.ok(referenced.length >= 6, 'popup.js should reference all core icons');
  for (const name of referenced) {
    assert.ok(hasIcon(name), `popup.js requests an icon that does not exist: ${name}`);
  }
});

test('an unknown icon degrades instead of throwing', () => {
  // A missing icon must never be able to take the whole popup down.
  assert.equal(hasIcon('no-such-icon'), false);
});

test('every icon carries at least one path', () => {
  assert.ok(ICON_NAMES.length >= 11);
});
