import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { functionBody } from './helpers/source.js';

// popup.js needs a live DOM, so these read the source; see popup-cta.test.js.
const popup = readFileSync(new URL('../popup/popup.js', import.meta.url), 'utf8');
const bodyOf = (name) => functionBody(popup, name);

test('Retry probes again instead of replaying the cached failure', () => {
  // probeVariants caches a failure for 30 s, so a plain re-probe answered Retry
  // with the very error it was retrying.
  assert.match(
    bodyOf('renderQualitiesPanel'),
    /retryBtn\.addEventListener\('click', \(\) => loadQualities\([^;]*\{ fresh: true \}\)\)/
  );
  assert.match(bodyOf('loadQualities'), /probeVariants\(item, tabId, \{ fresh \}\)/);
});

test('a probe that finishes after a re-render paints the panel now on screen', () => {
  // A detection can re-render the list while a probe (up to 8 s) is in flight. The
  // panel the probe started with is then detached, and the new one for the same
  // stream kept showing the loading skeleton until some unrelated re-render.
  // loadQualities never paints a panel it captured before the await; paintQualities
  // looks up whichever panel is on screen for the stream now.
  assert.doesNotMatch(bodyOf('loadQualities'), /renderQualitiesPanel\(/);
  assert.match(bodyOf('loadQualities'), /paintQualities\(item\)/);
  assert.match(bodyOf('paintQualities'), /qualitiesPanels\.get\(item\.url\)/);
  assert.match(bodyOf('paintQualities'), /\.isConnected/);
  assert.match(popup, /qualitiesPanels\.set\(item\.url, qualitiesPanel\)/);
});
