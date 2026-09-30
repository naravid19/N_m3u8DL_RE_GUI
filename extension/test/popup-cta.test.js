import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const popup = readFileSync(new URL('../popup/popup.js', import.meta.url), 'utf8');

/**
 * popup.js needs a live DOM and the chrome.* APIs, and this extension ships with no
 * test DOM (package.json declares no dependencies), so these read the source the way
 * platform-compat.test.js and popup-css.test.js already do for code they cannot run.
 */

test('the Primary CTA promotion is gated on a node the offline branch actually attaches', () => {
  // guiAvailable is null on every fresh popup open -- the popup document is recreated
  // each time -- so every card takes the offline-baseline branch. That branch calls
  // applyGuiAvailability(false), which appends only copyCurlBtn; downloadBtn is never
  // put in the DOM. Gating the later native-host result on downloadBtn.isConnected
  // therefore tests a node that is false by construction, and Download silently stays
  // demoted to "Copy as cURL" even while the GUI is running -- which is the whole
  // point of the dynamic-CTA feature.
  assert.doesNotMatch(
    popup,
    /downloadBtn\.isConnected/,
    'downloadBtn is not attached in the offline baseline branch, so .isConnected is always false there'
  );
});

test('the native-host probe still promotes the card when the GUI is reachable', () => {
  // Guards the fix from being "solved" by deleting the promotion outright.
  const promotion = popup.match(/checkGuiAvailable\(\)\.then\([\s\S]{0,900}?\}\);/);
  assert.ok(promotion, 'the deferred native-host probe is gone');
  assert.match(promotion[0], /applyGuiAvailability\(true\)/);
  assert.match(promotion[0], /\bcard\.isConnected\b/);
});
