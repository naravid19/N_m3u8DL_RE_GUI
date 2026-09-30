import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const background = readFileSync(new URL('../background.js', import.meta.url), 'utf8');

// background.js runs as a service worker and touches chrome.* at module scope,
// so it cannot be imported here. This is the same source-read guard style the
// repo already uses for files it cannot load (platform-compat, design-system).
test('the content-script detection dispatch cannot be reached by a prototype key', () => {
  // Any page can reach this dispatch: it posts
  //   { source: 'NRE_DEEP_DETECTOR', payload: { type: <anything>, url } }
  // to its own window, content.js accepts it (event.source === window is true
  // for a page posting to itself) and forwards payload verbatim via
  // chrome.runtime.sendMessage.
  //
  // With a plain object, DETECTION_SOURCES['constructor'] -- and __proto__,
  // toString, valueOf, hasOwnProperty -- all resolve to Object.prototype
  // members and are TRUTHY, so the page enters a branch meant only for the two
  // real detection messages. A Map has no prototype chain to walk into.
  assert.match(
    background,
    /const DETECTION_SOURCES = new Map\(/,
    'DETECTION_SOURCES must be a Map: a plain object lets a page reach this branch with a key like "constructor"'
  );
  assert.doesNotMatch(
    background,
    /DETECTION_SOURCES\s*\[/,
    'DETECTION_SOURCES must not be indexed with brackets -- that is the prototype-reachable lookup this guard exists to prevent'
  );
});

test('queued detections are flushed when the worker is about to be suspended', () => {
  // Media and Audio detections wait in memory for up to 1.5 s before reaching storage,
  // and nothing in flight keeps an MV3 worker alive, so a suspend in that gap dropped
  // them. onSuspend is the last point they can still be written.
  assert.match(background, /chrome\.runtime\.onSuspend\??\.addListener\(flushPending\)/);
});
