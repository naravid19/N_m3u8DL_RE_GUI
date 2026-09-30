import { test } from 'node:test';
import assert from 'node:assert/strict';

/**
 * A named import that does not exist on the target module throws at module
 * link time, before any of the importing module's own code ever runs. For
 * an extension entry point this is silent and total: popup.js linking
 * against a `formatDuration`/`formatBitrate` export format.js never defined
 * meant the entire popup script never executed — no init(), no listeners,
 * no render — leaving the popup permanently on its static "Waiting for
 * video stream..." placeholder regardless of what storage held. These tests
 * exist to catch exactly that class of bug: does the entry point link at
 * all, independent of what it does once running.
 */

test('popup.js links without a missing-export error', async () => {
  const previousDocument = globalThis.document;
  // Enough for the one top-level call in popup.js:
  // document.addEventListener('DOMContentLoaded', init) — init itself is
  // registered, not invoked, so no further DOM surface is needed here.
  globalThis.document = { addEventListener() {} };
  try {
    await assert.doesNotReject(import('../popup/popup.js?load-check=popup'));
  } finally {
    globalThis.document = previousDocument;
  }
});

test('background.js links without a missing-export error', async () => {
  const previousChrome = globalThis.chrome;
  globalThis.chrome = {
    webRequest: {
      onSendHeaders: { addListener() {} },
      onHeadersReceived: { addListener() {} }
    },
    runtime: {
      onMessage: { addListener() {} },
      onInstalled: { addListener() {} },
      onSuspend: { addListener() {} }
    },
    tabs: {
      onRemoved: { addListener() {} },
      onUpdated: { addListener() {} }
    },
    storage: {
      onChanged: { addListener() {} },
      local: { remove() {}, get(_keys, cb) { cb({}); } }
    },
    action: {
      setBadgeBackgroundColor() {},
      setBadgeText() {}
    }
  };
  try {
    await assert.doesNotReject(import('../background.js?load-check=background'));
  } finally {
    globalThis.chrome = previousChrome;
  }
});
