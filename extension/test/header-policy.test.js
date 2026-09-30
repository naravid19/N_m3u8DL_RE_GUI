import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { shouldForward } from '../lib/header-policy.js';

test('header-policy matches shared fixture cases', () => {
  const cases = JSON.parse(readFileSync(new URL('./fixtures/header-policy-cases.json', import.meta.url), 'utf8'));

  for (const [header, expected] of Object.entries(cases)) {
    assert.equal(shouldForward(header), expected, 'Failed for header: ' + header);
  }
});
