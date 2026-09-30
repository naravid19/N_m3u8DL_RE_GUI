import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { functionBody } from './helpers/source.js';

// popup.js needs a live DOM, so this reads the source; see popup-cta.test.js.
const popup = readFileSync(new URL('../popup/popup.js', import.meta.url), 'utf8');

test('the save name uses the title captured with the stream', () => {
  // content.js captures og:title / document.title with each stream as pageTitle, but
  // the popup read item.title, which nothing sets -- so a stream listed from an older
  // or other tab was named after whichever tab happened to be active.
  const body = functionBody(popup, 'buildCurlFor');

  assert.match(body, /item\.pageTitle/);
  assert.doesNotMatch(body, /item\.title\b/);
});
